using System;
using System.Collections;
using System.Text;
using Haven.Framework.CampQuests;
using Haven.Framework.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Haven.Camp
{
    public sealed class CampQuestGatewayClient : ICampQuestGenerator
    {
        private UnityWebRequest _active;
        internal static string GatewayBaseUrl
        {
            get
            {
                var configured = Environment.GetEnvironmentVariable("HAVEN_CAMP_GATEWAY_PORT");
                return int.TryParse(configured, out var port) && port > 0 && port <= 65535
                    ? "http://127.0.0.1:" + port
                    : "http://127.0.0.1:5080";
            }
        }
        public IEnumerator Generate(CampQuestRequest payload, Action<FrameworkResult<CampQuestProposal>> completed)
        {
            using var request = new UnityWebRequest(GatewayBaseUrl + "/api/v1/camp-quests/propose", UnityWebRequest.kHttpVerbPOST);
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
                    completed(Failure("AI_GATEWAY_UNAVAILABLE"));
                    yield break;
                }
                CampQuestProposal proposal = null;
                try { proposal = JsonUtility.FromJson<CampQuestProposal>(request.downloadHandler.text); }
                catch (Exception) { /* Invalid output is reported below and handled by the deterministic fallback. */ }
                completed(proposal == null ? Failure("AI_INVALID_JSON") : FrameworkResult<CampQuestProposal>.Success(proposal));
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
        private static FrameworkResult<CampQuestProposal> Failure(string code) => FrameworkResult<CampQuestProposal>.Failure(
            new FrameworkError(code, "营地正在使用本地委托。", "CampGateway", true));
    }
}
