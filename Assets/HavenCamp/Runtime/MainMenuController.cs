using System.Collections;
using Haven.Framework.Bootstrap;
using Haven.Framework.Core;
using Haven.Framework.Scenes;
using Haven.Framework.Services;
using Haven.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Haven.Camp.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        private enum LaunchMode
        {
            None,
            Host,
            Client
        }

        [Header("Pages")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject modePanel;
        [SerializeField] private GameObject multiplayerPanel;

        [Header("Controls")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button singlePlayerButton;
        [SerializeField] private Button multiplayerButton;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private Button joinRoomButton;
        [SerializeField] private Button modeBackButton;
        [SerializeField] private Button multiplayerBackButton;
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private TMP_Text statusLabel;

        [Header("Scenes")]
        [SerializeField] private string singlePlayerScene = "WorldGenMap";
        [SerializeField] private string multiplayerScene = "FrameworkDemo";
        [SerializeField, Min(5f)] private float frameworkStartupTimeout = 30f;

        private Button[] _actionButtons;
        private HavenNetworkSettings _networkSettings;
        private bool _busy;
        private bool _sceneLoaded;

        private void Awake()
        {
            _networkSettings = Resources.Load<HavenNetworkSettings>(HavenNetworkSettings.DefaultResourceName);
            if (_networkSettings)
            {
                singlePlayerScene = _networkSettings.SinglePlayerSceneName;
                multiplayerScene = _networkSettings.MultiplayerSceneName;
            }
            _actionButtons = new[]
            {
                startButton,
                quitButton,
                singlePlayerButton,
                multiplayerButton,
                createRoomButton,
                joinRoomButton,
                modeBackButton,
                multiplayerBackButton
            };

            startButton.onClick.AddListener(ShowModeSelection);
            quitButton.onClick.AddListener(QuitGame);
            singlePlayerButton.onClick.AddListener(StartSinglePlayer);
            multiplayerButton.onClick.AddListener(ShowMultiplayerSelection);
            createRoomButton.onClick.AddListener(CreateRoom);
            joinRoomButton.onClick.AddListener(JoinRoom);
            modeBackButton.onClick.AddListener(ShowMain);
            multiplayerBackButton.onClick.AddListener(ShowModeSelection);

            var defaultHost = _networkSettings ? _networkSettings.DefaultHost : "127.0.0.1";
            var defaultPort = _networkSettings ? _networkSettings.Port : (ushort)7770;
            addressInput.text = $"{defaultHost}:{defaultPort}";
            ShowMain();
        }

        private void OnDestroy()
        {
            if (startButton)
                startButton.onClick.RemoveListener(ShowModeSelection);
            if (quitButton)
                quitButton.onClick.RemoveListener(QuitGame);
            if (singlePlayerButton)
                singlePlayerButton.onClick.RemoveListener(StartSinglePlayer);
            if (multiplayerButton)
                multiplayerButton.onClick.RemoveListener(ShowMultiplayerSelection);
            if (createRoomButton)
                createRoomButton.onClick.RemoveListener(CreateRoom);
            if (joinRoomButton)
                joinRoomButton.onClick.RemoveListener(JoinRoom);
            if (modeBackButton)
                modeBackButton.onClick.RemoveListener(ShowMain);
            if (multiplayerBackButton)
                multiplayerBackButton.onClick.RemoveListener(ShowModeSelection);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (_busy || !mainPanel || !modePanel || !multiplayerPanel || keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;
            if (multiplayerPanel.activeSelf)
                ShowModeSelection();
            else if (modePanel.activeSelf)
                ShowMain();
        }

        private void ShowMain()
        {
            SetPage(mainPanel);
            SetStatus(string.Empty, false);
        }

        private void ShowModeSelection()
        {
            SetPage(modePanel);
            SetStatus("请选择游戏模式", false);
        }

        private void ShowMultiplayerSelection()
        {
            SetPage(multiplayerPanel);
            SetStatus("局域网直连（无大厅列表）：创建房间，或输入房主地址加入", false);
            addressInput.Select();
        }

        private void StartSinglePlayer()
        {
            if (!_busy)
                StartCoroutine(LoadGame(singlePlayerScene, LaunchMode.None, default));
        }

        private void CreateRoom()
        {
            if (_busy)
                return;
            var port = _networkSettings ? _networkSettings.Port : (ushort)7770;
            SetStatus("正在启动本机 Host；进入后会显示可分享的局域网地址", false);
            StartCoroutine(LoadGame(multiplayerScene, LaunchMode.Host, new NetworkEndpoint("127.0.0.1", port)));
        }

        private void JoinRoom()
        {
            if (_busy)
                return;
            var port = _networkSettings ? _networkSettings.Port : (ushort)7770;
            if (!NetworkEndpoint.TryParse(addressInput.text, port, out var endpoint))
            {
                SetStatus("地址格式无效，请输入 IP 或 IP:端口", true);
                addressInput.Select();
                return;
            }

            StartCoroutine(LoadGame(multiplayerScene, LaunchMode.Client, endpoint));
        }

        private IEnumerator LoadGame(string sceneName, LaunchMode launchMode, NetworkEndpoint endpoint)
        {
            SetBusy(true);
            SetStatus(launchMode == LaunchMode.Host ? "正在创建本机房间；进入后将显示可分享地址…" : launchMode == LaunchMode.Client ? "正在加入房间并校验版本…" : "正在进入游戏…", false);
            DontDestroyOnLoad(gameObject);

            var transition = SceneTransitionService.LoadScene(sceneName);
            yield return transition;
            if (!transition.Succeeded)
            {
                FailLaunch(transition.Error ?? $"无法加载场景：{sceneName}");
                yield break;
            }

            _sceneLoaded = true;
            yield return null;
            var bootstrap = GameBootstrap.Instance;
            if (!bootstrap)
            {
                FailLaunch("框架启动器不存在，无法进入游戏。");
                yield break;
            }

            bootstrap.RestartForCurrentScene();
            var deadline = Time.realtimeSinceStartup + frameworkStartupTimeout;
            while (bootstrap.State == BootstrapState.Starting && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (bootstrap.State != BootstrapState.Running)
            {
                FailLaunch(bootstrap.LastError?.Message ?? "框架启动超时，请查看日志。");
                yield break;
            }

            if (launchMode == LaunchMode.None)
            {
                Destroy(gameObject);
                yield break;
            }

            FrameworkResult result = default;
            var completed = false;
            if (launchMode == LaunchMode.Host)
            {
                if (!bootstrap.Context.Services.TryResolve<INetworkHostService>(out var hostService))
                {
                    FailLaunch("当前场景未安装房间服务。");
                    yield break;
                }
                yield return hostService.StartHost(endpoint, value =>
                {
                    result = value;
                    completed = true;
                });
            }
            else
            {
                if (!bootstrap.Context.Services.TryResolve<INetworkService>(out var networkService))
                {
                    FailLaunch("当前场景未安装联机服务。");
                    yield break;
                }
                yield return networkService.Connect(endpoint, value =>
                {
                    result = value;
                    completed = true;
                });
            }

            if (!completed || !result.Succeeded)
            {
                FailLaunch(result.Error?.Message ?? "连接没有完成，请在联机场景中重试。");
                yield break;
            }

            Destroy(gameObject);
        }

        private void SetPage(GameObject activePage)
        {
            mainPanel.SetActive(activePage == mainPanel);
            modePanel.SetActive(activePage == modePanel);
            multiplayerPanel.SetActive(activePage == multiplayerPanel);
        }

        private void SetBusy(bool value)
        {
            _busy = value;
            foreach (var button in _actionButtons)
            {
                if (button)
                    button.interactable = !value;
            }
            if (addressInput)
                addressInput.interactable = !value;
        }

        private void SetStatus(string message, bool isError)
        {
            statusLabel.text = message;
            statusLabel.color = isError ? new Color(1f, 0.48f, 0.38f) : new Color(0.82f, 0.91f, 0.82f);
            statusLabel.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        private void FailLaunch(string message)
        {
            Debug.LogError($"[Haven Menu] {message}");
            if (_sceneLoaded)
            {
                Destroy(gameObject);
                return;
            }

            SetBusy(false);
            SetStatus(message, true);
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
