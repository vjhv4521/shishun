using System;
using System.Collections;
using System.Text;
using Haven.Framework.CampQuests;
using Haven.Framework.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Haven.Camp
{
    public sealed class CampNpcChatGatewayClient : ICampNpcChatGenerator
    {
        private UnityWebRequest _active;

        public IEnumerator Generate(CampNpcChatRequest payload, Action<FrameworkResult<CampNpcChatResponse>> completed)
        {
            using var request = new UnityWebRequest(CampQuestGatewayClient.GatewayBaseUrl + "/api/v1/camp-npc/chat", UnityWebRequest.kHttpVerbPOST);
            _active = request;
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 10;
            request.SetRequestHeader("Content-Type", "application/json");
            var token = Environment.GetEnvironmentVariable("HAVEN_GATEWAY_TOKEN");
            if (!string.IsNullOrWhiteSpace(token)) request.SetRequestHeader("X-Haven-Gateway-Token", token);
            try
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    completed(Failure("GATEWAY_UNAVAILABLE"));
                    yield break;
                }
                CampNpcChatResponse response = null;
                try { response = JsonUtility.FromJson<CampNpcChatResponse>(request.downloadHandler.text); }
                catch (Exception) { /* Invalid responses use the local dialogue in the hotfix service. */ }
                completed(response == null ? Failure("INVALID_JSON") : FrameworkResult<CampNpcChatResponse>.Success(response));
            }
            finally
            {
                if (ReferenceEquals(_active, request)) _active = null;
            }
        }

        public void Cancel()
        {
            var request = _active;
            _active = null;
            if (request == null) return;
            request.Abort();
            request.Dispose();
        }

        public void Dispose() { Cancel(); }

        private static FrameworkResult<CampNpcChatResponse> Failure(string code) =>
            FrameworkResult<CampNpcChatResponse>.Failure(new FrameworkError(code, "营地正在使用本地对白。", "CampChatGateway", true));
    }
}
