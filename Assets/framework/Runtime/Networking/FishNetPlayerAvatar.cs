using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using Haven.Framework.Bootstrap;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;

namespace Haven.Networking
{
    [DisallowMultipleComponent]
    public sealed class FishNetPlayerAvatar : NetworkBehaviour, ISurvivalCommandRouter
    {
        private readonly Dictionary<string, Action<FrameworkResult<AiQuestResponse>>> _pending = new Dictionary<string, Action<FrameworkResult<AiQuestResponse>>>();
        private readonly Dictionary<string, float> _processedSurvivalRequests = new Dictionary<string, float>();
        private GatewayQuestClient _gateway;
        private HavenNetworkSettings _settings;
        private INetworkPlayerSimulation _simulation;
        private Vector3 _serverInput;
        private Vector3 _lastSentInput;
        private float _nextInputSendAt;
        private float _nextAiRequestAt;
        private float _nextSurvivalSnapshotAt;
        private float _nextPublicStateAt;
        private float _nextSurvivalCommandAt;
        private float _nextSnapshotRequestAt;
        private long _survivalRevision;
        private long _lastReceivedSurvivalRevision;
        private bool _executingSurvivalCommand;
        private bool _simulationRoleInitialized;
        private bool _simulationServerRole;
        private bool _simulationOwnerRole;
        private int _simulationPlayerId = -1;

        internal event Action<SurvivalSnapshot> SurvivalSnapshotReceived;
        internal event Action<SurvivalCommandCompleted> SurvivalCommandResultReceived;

        public bool ShouldRouteCommands => !_executingSurvivalCommand && IsOwner && IsClientStarted &&
                                           _simulation != null && _simulation.IsReady;
        public bool BlockLocalCommands => !_executingSurvivalCommand && IsOwner;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _settings = Resources.Load<HavenNetworkSettings>(HavenNetworkSettings.DefaultResourceName) ?? HavenNetworkSettings.CreateRuntimeDefault();
            if (IsServerStarted)
                _gateway = new GatewayQuestClient(_settings);
            EnsureSimulation();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            EnsureSimulation();
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

        public override void OnStopNetwork()
        {
            _simulation?.ShutdownNetworkRole();
            _simulation = null;
            _simulationRoleInitialized = false;
            _processedSurvivalRequests.Clear();
            _lastReceivedSurvivalRevision = 0;
            base.OnStopNetwork();
        }

        private void Update()
        {
            EnsureSimulation();
            if (IsOwner)
                SendLocalInput();
            if (IsServerStarted)
            {
                _simulation?.ApplyServerMovement(_serverInput);
                SendSurvivalState();
            }
        }

        public bool Submit(SurvivalCommand command)
        {
            if (!ShouldRouteCommands)
                return false;
            if (string.IsNullOrWhiteSpace(command.RequestId))
                command.RequestId = Guid.NewGuid().ToString("N");
            ServerSubmitSurvivalCommand(
                command.RequestId,
                (byte)command.Type,
                command.TargetUid ?? string.Empty,
                command.DataId ?? string.Empty,
                command.ActionId ?? string.Empty,
                (byte)command.SourceInventory,
                (byte)command.TargetInventory,
                command.SourceSlot,
                command.TargetSlot,
                command.Quantity,
                command.Position,
                command.Rotation,
                _settings.ProtocolVersion);
            return true;
        }

        internal bool RequestSurvivalRefresh()
        {
            if (!ShouldRouteCommands)
                return false;
            ServerRequestSurvivalSnapshot(_settings.ProtocolVersion);
            return true;
        }

        internal void ClearSurvivalClientState()
        {
            _lastReceivedSurvivalRevision = 0;
            SurvivalSnapshotReceived?.Invoke(SurvivalSnapshot.Empty);
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
            if (Time.unscaledTime < _nextInputSendAt)
                return;
            var movement = _simulation != null && _simulation.IsReady
                ? _simulation.CaptureWorldMovement() : Vector3.zero;
            if ((movement - _lastSentInput).sqrMagnitude > 0.0001f || movement.sqrMagnitude > 0f)
            {
                ServerSetInput(movement, _settings.ProtocolVersion);
                _lastSentInput = movement;
            }
            _nextInputSendAt = Time.unscaledTime + _settings.InputSendInterval;
        }

        [ServerRpc]
        private void ServerSetInput(Vector3 input, int protocolVersion)
        {
            if (protocolVersion != _settings.ProtocolVersion || !IsFinite(input))
            {
                _serverInput = Vector3.zero;
                return;
            }
            input.y = 0f;
            _serverInput = Vector3.ClampMagnitude(input, 1f);
        }

        [ServerRpc]
        private void ServerSubmitSurvivalCommand(
            string requestId,
            byte commandType,
            string targetUid,
            string dataId,
            string actionId,
            byte sourceInventory,
            byte targetInventory,
            int sourceSlot,
            int targetSlot,
            int quantity,
            Vector3 position,
            Quaternion rotation,
            int protocolVersion)
        {
            if (protocolVersion != _settings.ProtocolVersion)
            {
                TargetSurvivalCommandResult(Owner, requestId, false, GameplayErrorCodes.ProtocolMismatch,
                    "客户端与房主的玩法协议版本不一致。");
                return;
            }
            if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64 ||
                _processedSurvivalRequests.ContainsKey(requestId))
            {
                TargetSurvivalCommandResult(Owner, requestId, false, GameplayErrorCodes.DuplicateRequest,
                    "该操作已经处理，未重复结算。");
                return;
            }

            var maximumTargetLength = (SurvivalCommandType)commandType == SurvivalCommandType.QuestAction
                ? 8192 : 128;
            if (targetUid?.Length > maximumTargetLength || dataId?.Length > 128 || actionId?.Length > 32)
            {
                TargetSurvivalCommandResult(Owner, requestId, false, GameplayErrorCodes.InvalidRequest,
                    "请求字段长度超过允许范围。");
                return;
            }
            if (Time.unscaledTime < _nextSurvivalCommandAt)
            {
                TargetSurvivalCommandResult(Owner, requestId, false, GameplayErrorCodes.RateLimited,
                    "操作过快，请稍后重试。");
                return;
            }
            if (_processedSurvivalRequests.Count >= 65536)
            {
                TargetSurvivalCommandResult(Owner, requestId, false, GameplayErrorCodes.RateLimited,
                    "本局操作记录已达到上限，请重新创建房间。");
                return;
            }
            _nextSurvivalCommandAt = Time.unscaledTime + 0.05f;

            _processedSurvivalRequests[requestId] = Time.unscaledTime;
            EnsureSimulation();
            if (_simulation == null || !_simulation.IsReady)
            {
                TargetSurvivalCommandResult(Owner, requestId, false, GameplayErrorCodes.NotInGame,
                    "生存世界尚未准备完成。");
                return;
            }

            var command = new SurvivalCommand
            {
                RequestId = requestId,
                Type = (SurvivalCommandType)commandType,
                TargetUid = targetUid,
                DataId = dataId,
                ActionId = actionId,
                SourceInventory = (SurvivalInventoryKind)sourceInventory,
                TargetInventory = (SurvivalInventoryKind)targetInventory,
                SourceSlot = sourceSlot,
                TargetSlot = targetSlot,
                Quantity = quantity,
                Position = position,
                Rotation = rotation
            };
            _executingSurvivalCommand = true;
            var succeeded = false;
            var errorCode = GameplayErrorCodes.InvalidRequest;
            var message = "房主未能执行该操作。";
            try
            {
                succeeded = _simulation.ExecuteServerCommand(command, out errorCode, out message);
            }
            finally
            {
                _executingSurvivalCommand = false;
            }
            TargetSurvivalCommandResult(Owner, requestId, succeeded, errorCode, message);
            if (succeeded)
                SendSnapshotToOwner();
        }

        [ServerRpc]
        private void ServerRequestSurvivalSnapshot(int protocolVersion)
        {
            if (protocolVersion == _settings.ProtocolVersion && Time.unscaledTime >= _nextSnapshotRequestAt)
            {
                _nextSnapshotRequestAt = Time.unscaledTime + 0.25f;
                SendSnapshotToOwner();
            }
        }

        [TargetRpc]
        private void TargetSurvivalCommandResult(NetworkConnection connection, string requestId, bool succeeded,
            string errorCode, string message)
        {
            var result = new SurvivalCommandCompleted(requestId, succeeded, errorCode, message);
            SurvivalCommandResultReceived?.Invoke(result);
            GameBootstrap.Instance?.Context?.Events.Publish(result);
        }

        [TargetRpc]
        private void TargetSurvivalSnapshot(NetworkConnection connection, string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;
            var snapshot = JsonUtility.FromJson<SurvivalSnapshot>(json);
            if (!snapshot.HasState || snapshot.Revision <= _lastReceivedSurvivalRevision)
                return;
            _lastReceivedSurvivalRevision = snapshot.Revision;
            _simulation?.ApplySnapshot(snapshot);
            SurvivalSnapshotReceived?.Invoke(snapshot);
            GameBootstrap.Instance?.Context?.Events.Publish(new SurvivalSnapshotChanged(snapshot));
        }

        [ObserversRpc(BufferLast = true)]
        private void ObserversApplyPublicState(int playerId, Vector3 position, Quaternion rotation,
            bool isMoving, bool isBusy, bool isDead, float health, string equippedItemId)
        {
            if (IsServerStarted)
                return;
            _simulation?.ApplyPublicState(new SurvivalPublicPlayerSnapshot
            {
                PlayerId = playerId,
                Position = position,
                Rotation = rotation,
                IsMoving = isMoving,
                IsBusy = isBusy,
                IsDead = isDead,
                Health = health,
                EquippedItemId = equippedItemId
            });
        }

        private void SendSurvivalState()
        {
            if (_simulation == null || !_simulation.IsReady)
                return;
            if (Time.unscaledTime >= _nextPublicStateAt)
            {
                _nextPublicStateAt = Time.unscaledTime + _settings.InputSendInterval;
                var state = _simulation.CapturePublicState();
                ObserversApplyPublicState(state.PlayerId, state.Position, state.Rotation, state.IsMoving,
                    state.IsBusy, state.IsDead, state.Health, state.EquippedItemId ?? string.Empty);
            }
            if (Time.unscaledTime >= _nextSurvivalSnapshotAt)
            {
                _nextSurvivalSnapshotAt = Time.unscaledTime + 0.25f;
                SendSnapshotToOwner();
            }
        }

        private void SendSnapshotToOwner()
        {
            if (!IsServerStarted || Owner == null || !Owner.IsActive || _simulation == null || !_simulation.IsReady)
                return;
            var snapshot = _simulation.CaptureSnapshot(++_survivalRevision);
            TargetSurvivalSnapshot(Owner, JsonUtility.ToJson(snapshot));
        }

        private void EnsureSimulation()
        {
            if (_simulation == null)
            {
                foreach (var behaviour in GetComponents<MonoBehaviour>())
                {
                    if (behaviour is INetworkPlayerSimulation simulation)
                    {
                        _simulation = simulation;
                        break;
                    }
                }
            }
            if (_simulation == null)
                return;
            var serverRole = IsServerStarted;
            var ownerRole = IsOwner;
            var playerId = Owner != null ? Owner.ClientId : -1;
            if (_simulationRoleInitialized && serverRole == _simulationServerRole &&
                ownerRole == _simulationOwnerRole && playerId == _simulationPlayerId)
                return;
            _simulationServerRole = serverRole;
            _simulationOwnerRole = ownerRole;
            _simulationPlayerId = playerId;
            _simulationRoleInitialized = true;
            _simulation.InitializeNetworkRole(serverRole, ownerRole, playerId, this);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
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
