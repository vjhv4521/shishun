using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using Haven.Framework.Bootstrap;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Haven.Networking
{
    [DisallowMultipleComponent]
    public sealed class FishNetPlayerAvatar : NetworkBehaviour
    {
        private readonly Dictionary<string, Action<FrameworkResult<AiQuestResponse>>> _pending = new Dictionary<string, Action<FrameworkResult<AiQuestResponse>>>();
        private GatewayQuestClient _gateway;
        private HavenNetworkSettings _settings;
        private Vector2 _serverInput;
        private Vector2 _lastSentInput;
        private float _nextInputSendAt;
        private float _nextAiRequestAt;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _settings = Resources.Load<HavenNetworkSettings>(HavenNetworkSettings.DefaultResourceName) ?? HavenNetworkSettings.CreateRuntimeDefault();
            if (IsServerStarted)
                _gateway = new GatewayQuestClient(_settings);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner)
                return;
            if (TryGetNetworkService(out var service))
                service.AttachLocalPlayer(this);
        }

        public override void OnStopClient()
        {
            if (IsOwner && TryGetNetworkService(out var service))
                service.DetachLocalPlayer(this);
            FailPending("AI_CLIENT_STOPPED", "The network player stopped before the AIGC request completed.");
            base.OnStopClient();
        }

        private void Update()
        {
            if (IsOwner)
                SendLocalInput();
            if (IsServerStarted)
                ApplyServerMovement();
        }

        internal IEnumerator RequestQuest(AiQuestRequest request, Action<FrameworkResult<AiQuestResponse>> completed)
        {
            if (!IsOwner || !IsClientStarted)
            {
                completed?.Invoke(Failure("AI_PLAYER_NOT_READY", "The local network player is not ready.", true));
                yield break;
            }

            var requestId = Guid.NewGuid().ToString("N");
            var finished = false;
            FrameworkResult<AiQuestResponse> result = default;
            _pending.Add(requestId, value =>
            {
                result = value;
                finished = true;
            });

            ServerRequestQuest(
                requestId,
                Limit(request.NpcId, 64),
                Limit(request.PlayerMessage, 240),
                _settings.ProtocolVersion);

            var deadline = Time.realtimeSinceStartup + _settings.GatewayTimeoutSeconds + 5f;
            while (!finished && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!finished)
            {
                _pending.Remove(requestId);
                result = Failure("AI_REQUEST_TIMEOUT", "The Dedicated Server did not return an AIGC quest in time.", true);
            }
            completed?.Invoke(result);
        }

        private void SendLocalInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || Time.unscaledTime < _nextInputSendAt)
                return;

            var input = new Vector2(
                ReadAxis(keyboard.aKey, keyboard.dKey),
                ReadAxis(keyboard.sKey, keyboard.wKey));
            input = Vector2.ClampMagnitude(input, 1f);
            if ((input - _lastSentInput).sqrMagnitude > 0.0001f || input.sqrMagnitude > 0f)
            {
                ServerSetInput(input, _settings.ProtocolVersion);
                _lastSentInput = input;
            }
            _nextInputSendAt = Time.unscaledTime + _settings.InputSendInterval;
        }

        private void ApplyServerMovement()
        {
            var movement = new Vector3(_serverInput.x, 0f, _serverInput.y);
            if (movement.sqrMagnitude > 1f)
                movement.Normalize();
            transform.position += movement * (_settings.PlayerMoveSpeed * Time.deltaTime);
        }

        [ServerRpc]
        private void ServerSetInput(Vector2 input, int protocolVersion)
        {
            if (protocolVersion != _settings.ProtocolVersion)
            {
                _serverInput = Vector2.zero;
                return;
            }
            _serverInput = Vector2.ClampMagnitude(input, 1f);
        }

        [ServerRpc]
        private void ServerRequestQuest(string requestId, string npcId, string playerMessage, int protocolVersion)
        {
            if (protocolVersion != _settings.ProtocolVersion)
            {
                TargetQuestFailed(Owner, requestId, "NETWORK_PROTOCOL_MISMATCH", "Client and server protocol versions do not match.", false);
                return;
            }
            if (Time.unscaledTime < _nextAiRequestAt)
            {
                TargetQuestFailed(Owner, requestId, "AI_RATE_LIMITED", "Please wait before requesting another generated quest.", true);
                return;
            }
            if (string.IsNullOrWhiteSpace(npcId) || string.IsNullOrWhiteSpace(playerMessage))
            {
                TargetQuestFailed(Owner, requestId, "AI_INVALID_REQUEST", "NPC id and player message are required.", false);
                return;
            }

            _nextAiRequestAt = Time.unscaledTime + _settings.AiRequestCooldownSeconds;
            StartCoroutine(ProcessQuest(requestId, Limit(npcId, 64), Limit(playerMessage, 240), Owner));
        }

        private IEnumerator ProcessQuest(string requestId, string npcId, string playerMessage, NetworkConnection target)
        {
            FrameworkResult<AiQuestResponse> gatewayResult = default;
            var completed = false;
            yield return _gateway.Generate(requestId, npcId, playerMessage, value =>
            {
                gatewayResult = value;
                completed = true;
            });

            AiQuestResponse response;
            if (!completed || !gatewayResult.Succeeded)
            {
                response = QuestResponseValidator.CreateFallback(requestId);
                GameLog.Warning("AIGCServer", gatewayResult.Error?.Message ?? "AIGC gateway did not complete; local fallback was used.", "AI_FALLBACK_USED");
            }
            else
            {
                response = gatewayResult.Value;
            }

            TargetQuestCompleted(
                target,
                response.RequestId,
                response.Dialogue,
                response.QuestType,
                response.TargetId,
                response.Count,
                response.RewardId,
                (int)response.Source);
        }

        [TargetRpc]
        private void TargetQuestCompleted(NetworkConnection connection, string requestId, string dialogue, string questType, string targetId, int count, string rewardId, int source)
        {
            var response = new AiQuestResponse
            {
                RequestId = requestId,
                Dialogue = dialogue,
                QuestType = questType,
                TargetId = targetId,
                Count = count,
                RewardId = rewardId,
                Source = source == (int)AiQuestSource.LocalFallback ? AiQuestSource.LocalFallback : AiQuestSource.DeepSeek
            };
            var validated = QuestResponseValidator.Validate(response);
            if (_pending.TryGetValue(requestId, out var callback))
            {
                _pending.Remove(requestId);
                callback(validated);
            }
            if (validated.Succeeded)
                GameBootstrap.Instance?.Context?.Events.Publish(new AiQuestCompleted(validated.Value));
            else
                GameBootstrap.Instance?.Context?.Events.Publish(new AiQuestFailed(validated.Error));
        }

        [TargetRpc]
        private void TargetQuestFailed(NetworkConnection connection, string requestId, string code, string message, bool retryable)
        {
            var result = Failure(code, message, retryable);
            if (_pending.TryGetValue(requestId, out var callback))
            {
                _pending.Remove(requestId);
                callback(result);
            }
            GameBootstrap.Instance?.Context?.Events.Publish(new AiQuestFailed(result.Error));
        }

        private bool TryGetNetworkService(out FishNetNetworkService service)
        {
            service = null;
            var context = GameBootstrap.Instance?.Context;
            return context != null && context.Services.TryResolve<INetworkService>(out var abstraction) && (service = abstraction as FishNetNetworkService) != null;
        }

        private void FailPending(string code, string message)
        {
            if (_pending.Count == 0)
                return;
            var failure = Failure(code, message, true);
            foreach (var callback in _pending.Values)
                callback(failure);
            _pending.Clear();
        }

        private static float ReadAxis(KeyControl negative, KeyControl positive)
        {
            return (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
        }

        private static string Limit(string value, int maximumLength)
        {
            value = (value ?? string.Empty).Trim();
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        private static FrameworkResult<AiQuestResponse> Failure(string code, string message, bool retryable)
        {
            return FrameworkResult<AiQuestResponse>.Failure(new FrameworkError(code, message, "AIGCClient", retryable));
        }
    }
}
