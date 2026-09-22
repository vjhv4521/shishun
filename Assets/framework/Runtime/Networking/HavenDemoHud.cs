using System.Collections;
using FishNet.Managing;
using Haven.Framework.Bootstrap;
using Haven.Framework.Core;
using Haven.Framework.Scenes;
using Haven.Framework.Services;
using Haven.Networking;
using UnityEngine;

namespace Haven.Framework.Demo
{
    public sealed class HavenDemoHud : MonoBehaviour
    {
        [SerializeField] private string npcId = "camp_guide";
        [SerializeField] private string playerMessage = "营地现在最缺什么？";

        private HavenNetworkSettings _settings;
        private string _status = "正在初始化联机服务…";
        private bool _busy;
        private bool _returningToMenu;
        private GUIStyle _boxStyle;

        private void Awake()
        {
            _settings = UnityEngine.Resources.Load<HavenNetworkSettings>(HavenNetworkSettings.DefaultResourceName) ?? HavenNetworkSettings.CreateRuntimeDefault();
        }

        private void OnGUI()
        {
#if UNITY_SERVER && !UNITY_EDITOR
            return;
#endif
            _boxStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15 };
            GUILayout.BeginArea(new Rect(16f, 16f, 500f, 535f), "Haven 局域网房间 / AIGC", _boxStyle);
            GUILayout.Space(8f);
            GUILayout.Label("局域网直连会话（无大厅列表）");
            GUILayout.Label($"协议：{_settings.TransportDisplayName} | 端口：{_settings.Port} | 上限：{_settings.MaximumPlayers} 人");

            var bootstrap = GameBootstrap.Instance;
            GUILayout.Label($"Framework：{(bootstrap ? bootstrap.State.ToString() : "Not created")}");
            if (!TryServices(out var network, out var host, out var llm))
            {
                GUILayout.Label(_status);
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"连接状态：{Localize(network.State)} | 当前玩家：{network.ConnectedPeerCount}");
            if (host != null && host.IsHosting)
            {
                GUILayout.Label($"房间地址：{host.ShareEndpoint}");
                if (host.ShareEndpoint.Host == "127.0.0.1")
                    GUILayout.Label("未检测到局域网 IPv4；请检查网卡或手动查询本机地址。");
                GUI.enabled = !_busy;
                if (GUILayout.Button("复制房间地址"))
                {
                    GUIUtility.systemCopyBuffer = host.ShareEndpoint.ToString();
                    _status = $"已复制：{host.ShareEndpoint}";
                }
            }
            else if (network.IsConnected)
            {
                GUILayout.Label($"已加入：{network.ConnectedEndpoint}");
            }
            else if (network.LastError != null)
            {
                GUILayout.Label($"连接失败：{Localize(network.LastError)}");
            }

            GUI.enabled = !_busy;
            if (network.IsConnected || network.State == NetworkState.Failed || network.State == NetworkState.Disconnected)
            {
                if (GUILayout.Button(network.IsConnected ? "退出房间并返回主菜单" : "返回主菜单"))
                    StartCoroutine(ReturnToMenu(network, host));
            }

            GUILayout.Space(10f);
            GUILayout.Label("NPC");
            npcId = GUILayout.TextField(npcId);
            GUILayout.Label("玩家消息");
            playerMessage = GUILayout.TextField(playerMessage);
            GUI.enabled = !_busy && network.IsConnected;
            if (GUILayout.Button("由服务器请求 DeepSeek 生成任务"))
                StartCoroutine(RequestQuest(llm));
            GUI.enabled = true;

            GUILayout.Space(8f);
            GUILayout.Label("WASD 移动；玩家生成、连接认证与移动结果均由服务器判定。", GUI.skin.label);
            GUILayout.TextArea(_status, GUILayout.MinHeight(78f));
            GUILayout.EndArea();
        }

        private IEnumerator ReturnToMenu(INetworkService network, INetworkHostService host)
        {
            if (_returningToMenu)
                yield break;
            _returningToMenu = true;
            _busy = true;
            _status = "正在退出房间并返回主菜单…";

            if (host != null && host.IsHosting)
                host.StopHost();
            else
                network.Disconnect();
            yield return null;

            var bootstrap = GameBootstrap.Instance;
            bootstrap?.PrepareForSceneTransition();
            var manager = FindAnyObjectByType<NetworkManager>();
            if (manager)
                Destroy(manager.gameObject);
            yield return null;

            DontDestroyOnLoad(gameObject);
            var transition = SceneTransitionService.LoadScene(_settings.MenuSceneName);
            yield return transition;
            if (!transition.Succeeded)
            {
                _status = transition.Error ?? $"无法加载主菜单场景：{_settings.MenuSceneName}";
                _busy = false;
                _returningToMenu = false;
                yield break;
            }

            yield return null;
            bootstrap?.RestartForCurrentScene();
            Destroy(gameObject);
        }

        private IEnumerator RequestQuest(ILLMService llm)
        {
            _busy = true;
            _status = "服务器正在校验请求并生成任务…";
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
                _status = $"[{quest.Source}] {quest.Dialogue}\n任务：{quest.QuestType} {quest.TargetId} x{quest.Count}\n奖励：{quest.RewardId}";
            }
            else
            {
                _status = result.Error?.ToString() ?? "AIGC 请求没有完成。";
            }
            _busy = false;
        }

        private static bool TryServices(out INetworkService network, out INetworkHostService host, out ILLMService llm)
        {
            network = null;
            host = null;
            llm = null;
            var context = GameBootstrap.Instance?.Context;
            if (context == null || !context.Services.TryResolve(out network) || !context.Services.TryResolve(out llm))
                return false;
            context.Services.TryResolve(out host);
            return true;
        }

        private static string Localize(NetworkState state)
        {
            switch (state)
            {
                case NetworkState.Idle:
                    return "空闲";
                case NetworkState.Starting:
                    return "正在启动";
                case NetworkState.Connecting:
                    return "正在连接/认证";
                case NetworkState.Connected:
                    return "已连接";
                case NetworkState.Failed:
                    return "失败";
                case NetworkState.Disconnected:
                    return "已断开";
                default:
                    return state.ToString();
            }
        }

        private static string Localize(FrameworkError error)
        {
            switch (error.Code)
            {
                case "NETWORK_PROTOCOL_MISMATCH":
                    return "客户端与房主版本不一致。";
                case "NETWORK_SERVER_FULL":
                    return "房间人数已满。";
                case "NETWORK_CONNECT_TIMEOUT":
                    return "连接超时，请检查地址、防火墙和房主状态。";
                case "NETWORK_CONNECTION_REJECTED":
                    return "房主拒绝了连接。";
                default:
                    return error.Message;
            }
        }
    }
}
