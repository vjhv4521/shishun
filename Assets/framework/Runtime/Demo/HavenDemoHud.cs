using System.Collections;
using Haven.Framework.Bootstrap;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;

namespace Haven.Framework.Demo
{
    public sealed class HavenDemoHud : MonoBehaviour
    {
        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private ushort port = 7770;
        [SerializeField] private string npcId = "camp_guide";
        [SerializeField] private string playerMessage = "营地现在最缺什么？";

        private string _status = "Framework is starting...";
        private bool _busy;
        private GUIStyle _boxStyle;

        private void OnGUI()
        {
#if UNITY_SERVER && !UNITY_EDITOR
            return;
#endif
            _boxStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15 };
            GUILayout.BeginArea(new Rect(16f, 16f, 440f, 355f), "Haven 联机 / AIGC 垂直切片", _boxStyle);
            GUILayout.Space(8f);

            var bootstrap = GameBootstrap.Instance;
            GUILayout.Label($"Framework: {(bootstrap ? bootstrap.State.ToString() : "Not created")}");
            if (!TryServices(out var network, out var llm))
            {
                GUILayout.Label(_status);
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"Network: {network.State} | peers: {network.ConnectedPeerCount}");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Server", GUILayout.Width(55f));
            host = GUILayout.TextField(host, GUILayout.Width(240f));
            var portText = GUILayout.TextField(port.ToString(), GUILayout.Width(70f));
            if (ushort.TryParse(portText, out var parsedPort) && parsedPort > 0)
                port = parsedPort;
            GUILayout.EndHorizontal();

            GUI.enabled = !_busy;
            if (!network.IsConnected)
            {
                if (GUILayout.Button("连接 Dedicated Server"))
                    StartCoroutine(Connect(network));
            }
            else if (GUILayout.Button("断开连接"))
            {
                network.Disconnect();
                _status = "Disconnected.";
            }

            GUILayout.Space(8f);
            GUILayout.Label("NPC");
            npcId = GUILayout.TextField(npcId);
            GUILayout.Label("玩家消息");
            playerMessage = GUILayout.TextField(playerMessage);
            GUI.enabled = !_busy && network.IsConnected;
            if (GUILayout.Button("由服务器请求 DeepSeek 生成任务"))
                StartCoroutine(RequestQuest(llm));
            GUI.enabled = true;

            GUILayout.Space(8f);
            GUILayout.Label("WASD 移动；移动结果由服务器权威同步。", GUI.skin.label);
            GUILayout.TextArea(_status, GUILayout.MinHeight(78f));
            GUILayout.EndArea();
        }

        private IEnumerator Connect(INetworkService network)
        {
            _busy = true;
            _status = $"Connecting to {host}:{port}...";
            FrameworkResult result = default;
            var completed = false;
            yield return network.Connect(new NetworkEndpoint(host, port), value =>
            {
                result = value;
                completed = true;
            });
            _status = completed && result.Succeeded ? "Connected. Waiting for the player avatar..." : result.Error?.ToString() ?? "Connection did not complete.";
            _busy = false;
        }

        private IEnumerator RequestQuest(ILLMService llm)
        {
            _busy = true;
            _status = "Dedicated Server is validating the request and calling the AIGC gateway...";
            FrameworkResult<AiQuestResponse> result = default;
            var completed = false;
            yield return llm.RequestQuest(new AiQuestRequest(npcId, playerMessage), value =>
            {
                result = value;
                completed = true;
            });

            if (completed && result.Succeeded)
            {
                var quest = result.Value;
                _status = $"[{quest.Source}] {quest.Dialogue}\n任务: {quest.QuestType} {quest.TargetId} x{quest.Count}\n奖励: {quest.RewardId}";
            }
            else
            {
                _status = result.Error?.ToString() ?? "AIGC request did not complete.";
            }
            _busy = false;
        }

        private static bool TryServices(out INetworkService network, out ILLMService llm)
        {
            network = null;
            llm = null;
            var context = GameBootstrap.Instance?.Context;
            return context != null &&
                   context.Services.TryResolve(out network) &&
                   context.Services.TryResolve(out llm);
        }
    }
}
