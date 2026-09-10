using System;
using System.Collections;
using System.Text;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;
using UnityEngine.Networking;

namespace Haven.Networking
{
    internal sealed class GatewayQuestClient
    {
        private readonly HavenNetworkSettings _settings;

        public GatewayQuestClient(HavenNetworkSettings settings)
        {
            _settings = settings;
        }

        public IEnumerator Generate(string requestId, string npcId, string playerMessage, Action<FrameworkResult<AiQuestResponse>> completed)
        {
            var payload = new GatewayRequest
            {
                requestId = requestId,
                npcId = npcId,
                playerMessage = playerMessage,
                context = new GatewayContext
                {
                    day = 1,
                    allowedTargets = new[] { "Wood", "Stone" },
                    allowedRewards = new[] { "Food" }
                }
            };

            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            using var request = new UnityWebRequest($"{_settings.GatewayBaseUrl}/api/v1/quests/generate", UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = _settings.GatewayTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            var token = _settings.GatewayToken;
            if (!string.IsNullOrWhiteSpace(token))
                request.SetRequestHeader("X-Haven-Gateway-Token", token);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                completed?.Invoke(FrameworkResult<AiQuestResponse>.Failure(new FrameworkError(
                    "AI_GATEWAY_UNAVAILABLE",
                    $"AIGC gateway request failed with HTTP {request.responseCode}: {request.error}",
                    "AIGCGateway",
                    true)));
                yield break;
            }

            GatewayResponse response;
            try
            {
                response = JsonUtility.FromJson<GatewayResponse>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                completed?.Invoke(FrameworkResult<AiQuestResponse>.Failure(new FrameworkError(
                    "AI_GATEWAY_INVALID_JSON",
                    "AIGC gateway returned invalid JSON.",
                    "AIGCGateway",
                    false,
                    exception)));
                yield break;
            }

            var candidate = new AiQuestResponse
            {
                RequestId = response.requestId,
                Dialogue = response.dialogue,
                QuestType = response.questType,
                TargetId = response.targetId,
                Count = response.count,
                RewardId = response.rewardId,
                Source = response.source == "local-fallback" ? AiQuestSource.LocalFallback : AiQuestSource.DeepSeek
            };
            completed?.Invoke(QuestResponseValidator.Validate(candidate));
        }

        [Serializable]
        private sealed class GatewayRequest
        {
            public string requestId;
            public string npcId;
            public string playerMessage;
            public GatewayContext context;
        }

        [Serializable]
        private sealed class GatewayContext
        {
            public int day;
            public string[] allowedTargets;
            public string[] allowedRewards;
        }

        [Serializable]
        private sealed class GatewayResponse
        {
            public string requestId;
            public string dialogue;
            public string questType;
            public string targetId;
            public int count;
            public string rewardId;
            public string source;
        }
    }
}
