using System;
using System.Collections;
using System.Collections.Generic;
using Haven.Framework.Bootstrap;
using Haven.Framework.Core;
using Haven.Framework.HotUpdate;
using Haven.Framework.Services;
using UnityEngine;

namespace Haven.Framework.Demo
{
    /// <summary>AOT-only IMGUI view. Room behavior lives in the Hotfix LobbyPresenter.</summary>
    public sealed class HavenDemoHud : MonoBehaviour
    {
        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private ushort port = 7770;
        [SerializeField] private string displayName = "幸存者";
        [SerializeField] private string roomCode = string.Empty;

        private NetworkState _networkState = NetworkState.Disconnected;
        private RoomSnapshot _room = RoomSnapshot.Empty;
        private string _status = "正在启动 Framework…";
        private string _loadMessage = string.Empty;
        private float _loadProgress;
        private bool _busy;
        private GUIStyle _boxStyle;
        private GameBootstrap _observedBootstrap;
        private readonly HotUpdateUiModel _updateUi = new HotUpdateUiModel();
        private Sprite _hotUpdateBadge;
        private string _hotUpdateAnnouncement = string.Empty;
        private string _hotUpdateContentVersion = string.Empty;
        private ICoopGameplayService _gameplayService;
        private IDisposable _gameplaySubscription;
        private GameplaySnapshot _gameplay = GameplaySnapshot.Empty;
        private string _gameplayStatus = "正在等待服务端状态…";
        private bool _gameplayBusy;
        private bool _gameplayRefreshRequested;
        private long _visualRevision = -1;
        private readonly Dictionary<string, GameObject> _gameplayVisuals = new Dictionary<string, GameObject>();

        public event Action ConnectRequested;
        public event Action DisconnectRequested;
        public event Action CreateRequested;
        public event Action JoinRequested;
        public event Action<bool> ReadyRequested;
        public event Action StartRequested;
        public event Action LeaveRequested;

        public string Host => string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        public ushort Port => port == 0 ? (ushort)7770 : port;
        public string DisplayName => displayName;
        public string RoomCode => roomCode;

        public void SetDefaultEndpoint(string defaultHost, ushort defaultPort)
        {
            if (!string.IsNullOrWhiteSpace(defaultHost))
                host = defaultHost.Trim();
            if (defaultPort > 0)
                port = defaultPort;
        }

        public Coroutine Run(IEnumerator routine)
        {
            return routine == null ? null : StartCoroutine(routine);
        }

        public void SetNetworkState(NetworkState state)
        {
            _networkState = state;
        }

        public void SetRoom(RoomSnapshot snapshot)
        {
            _room = snapshot;
            if (snapshot.HasRoom)
                roomCode = snapshot.RoomCode;
            if (snapshot.Phase != RoomPhase.InGame)
            {
                _gameplay = GameplaySnapshot.Empty;
                _gameplayRefreshRequested = false;
                ClearGameplayVisuals();
            }
        }

        public void SetBusy(bool value)
        {
            _busy = value;
        }

        public void SetStatus(string value)
        {
            _status = value ?? string.Empty;
        }

        public void SetLoadProgress(RoomGameLoadProgressChanged progress)
        {
            _loadProgress = progress.Progress;
            _loadMessage = progress.Message;
        }

        public void SetHotUpdateDemo(Sprite badge, string announcement, string contentVersion)
        {
            _hotUpdateBadge = badge;
            _hotUpdateAnnouncement = announcement ?? string.Empty;
            _hotUpdateContentVersion = contentVersion ?? string.Empty;
        }

        public void ClearHotUpdateDemo()
        {
            _hotUpdateBadge = null;
            _hotUpdateAnnouncement = string.Empty;
            _hotUpdateContentVersion = string.Empty;
        }

        private void Update()
        {
            ObserveBootstrap(GameBootstrap.Instance);
            if (_room.Phase == RoomPhase.InGame)
            {
                ClearGameplayVisuals();
                return;
            }
            EnsureGameplayBinding();
            if (_room.Phase == RoomPhase.InGame && _gameplayService != null &&
                !_gameplay.HasState && !_gameplayBusy && !_gameplayRefreshRequested)
            {
                _gameplayRefreshRequested = true;
                RunGameplayRequest(callback => _gameplayService.Refresh(callback), "世界状态已同步。");
            }
            SyncGameplayVisuals();
        }

        private void OnDisable()
        {
            ObserveBootstrap(null);
            UnbindGameplay();
            ClearGameplayVisuals();
        }

        private void OnGUI()
        {
#if (UNITY_SERVER || HAVEN_SERVER_BUILD) && !UNITY_EDITOR
            return;
#endif
            ObserveBootstrap(GameBootstrap.Instance);
            if (ShouldDrawHotUpdateOverlay())
            {
                DrawHotUpdateOverlay();
                return;
            }

            if (_room.Phase == RoomPhase.InGame)
            {
                return;
            }

            _boxStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16 };
            GUILayout.BeginArea(new Rect(16f, 16f, 480f, 590f), "Haven 多人房间", _boxStyle);
            GUILayout.Space(8f);
            GUILayout.Label($"Framework: {(GameBootstrap.Instance ? GameBootstrap.Instance.State.ToString() : "未启动")}");
            GUILayout.Label($"网络: {_networkState}");
            DrawHotUpdateDemo();

            if (_networkState != NetworkState.Connected)
                DrawConnectionPage();
            else if (!_room.HasRoom)
                DrawLobbyPage();
            else
                DrawRoomPage();

            GUILayout.Space(10f);
            GUILayout.TextArea(_status, GUILayout.MinHeight(60f));
            if (_room.Phase == RoomPhase.Loading)
            {
                GUILayout.Space(6f);
                GUILayout.Label(string.IsNullOrEmpty(_loadMessage) ? "正在加载 WorldGenMap…" : _loadMessage);
                GUILayout.HorizontalSlider(_loadProgress, 0f, 1f);
            }
            GUILayout.EndArea();
        }

        private void ObserveBootstrap(GameBootstrap bootstrap)
        {
            if (_observedBootstrap == bootstrap)
                return;
            if (_observedBootstrap)
                _observedBootstrap.ProgressChanged -= OnHotUpdateProgress;
            _observedBootstrap = bootstrap;
            if (!_observedBootstrap)
                return;
            _observedBootstrap.ProgressChanged += OnHotUpdateProgress;
            OnHotUpdateProgress(_observedBootstrap.LastProgress);
        }

        private void EnsureGameplayBinding()
        {
            var context = GameBootstrap.Instance?.Context;
            if (context == null || !context.Services.TryResolve<ICoopGameplayService>(out var service))
            {
                if (_gameplayService != null)
                    UnbindGameplay();
                return;
            }
            if (ReferenceEquals(_gameplayService, service))
                return;
            UnbindGameplay();
            _gameplayService = service;
            _gameplay = service.Current;
            _gameplaySubscription = context.Events.Subscribe<GameplaySnapshotChanged>(OnGameplaySnapshotChanged);
        }

        private void UnbindGameplay()
        {
            _gameplaySubscription?.Dispose();
            _gameplaySubscription = null;
            _gameplayService = null;
            _gameplayBusy = false;
            _gameplayRefreshRequested = false;
            _gameplay = GameplaySnapshot.Empty;
        }

        private void OnGameplaySnapshotChanged(GameplaySnapshotChanged changed)
        {
            _gameplay = changed.Snapshot;
        }

        private void OnHotUpdateProgress(HotUpdateProgress progress)
        {
            _updateUi.Observe(progress, Time.realtimeSinceStartup);
        }

        private bool ShouldDrawHotUpdateOverlay()
        {
            if (!_observedBootstrap || _observedBootstrap.State != BootstrapState.Running)
                return true;
            return _updateUi.ShouldDraw(_observedBootstrap.State, Time.realtimeSinceStartup);
        }

        private void DrawHotUpdateOverlay()
        {
            _boxStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16 };
            GUILayout.BeginArea(new Rect(16f, 16f, 520f, 330f), "Haven 更新", _boxStyle);
            GUILayout.Space(10f);

            if (!_observedBootstrap)
            {
                GUILayout.Label("正在创建启动器…");
                GUILayout.EndArea();
                return;
            }

            var state = _observedBootstrap.State;
            if (state == BootstrapState.Running)
            {
                GUILayout.Label("更新完成，正在进入大厅…");
                DrawDownloadSummary();
                GUILayout.EndArea();
                return;
            }

            if (state == BootstrapState.Failed)
            {
                var error = _observedBootstrap.LastError;
                GUILayout.Label("更新失败");
                GUILayout.TextArea(ToChineseHotUpdateError(error), GUILayout.MinHeight(80f));
                if (error != null)
                    GUILayout.Label($"错误码：{error.Code}");
                DrawDownloadSummary();
                GUI.enabled = _observedBootstrap.CanRetry;
                if (GUILayout.Button(_observedBootstrap.CanRetry ? "重试下载" : "需要重新安装客户端", GUILayout.Height(38f)))
                {
                    _updateUi.ResetForRetry();
                    _observedBootstrap.Retry();
                }
                GUI.enabled = true;
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label(ToChineseStage(_updateUi.Progress.Stage));
            GUILayout.HorizontalSlider(_updateUi.Progress.NormalizedProgress, 0f, 1f);
            GUILayout.Label($"进度：{Mathf.RoundToInt(_updateUi.Progress.NormalizedProgress * 100f)}%");
            DrawDownloadSummary();
            GUILayout.EndArea();
        }

        private void DrawDownloadSummary()
        {
            if (!_updateUi.DownloadObserved)
            {
                if (_updateUi.Progress.Stage == HotUpdateStage.DownloadFiles && _updateUi.Progress.NormalizedProgress >= 1f)
                    GUILayout.Label("资源已是最新，无需下载。");
                return;
            }

            GUILayout.Label($"文件：{_updateUi.DownloadedFiles}/{_updateUi.TotalFiles}");
            GUILayout.Label($"数据：{FormatBytes(_updateUi.DownloadedBytes)}/{FormatBytes(_updateUi.TotalBytes)}");
        }

        private void DrawHotUpdateDemo()
        {
            if (!_hotUpdateBadge && string.IsNullOrEmpty(_hotUpdateAnnouncement))
                return;
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal(GUI.skin.box);
            if (_hotUpdateBadge)
                GUILayout.Label(_hotUpdateBadge.texture, GUILayout.Width(72f), GUILayout.Height(72f));
            GUILayout.BeginVertical();
            GUILayout.Label(string.IsNullOrEmpty(_hotUpdateAnnouncement) ? "热更新演示资源已加载" : _hotUpdateAnnouncement);
            if (!string.IsNullOrEmpty(_hotUpdateContentVersion))
                GUILayout.Label($"内容版本：{_hotUpdateContentVersion}");
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        internal static string ToChineseStage(HotUpdateStage stage)
        {
            return stage switch
            {
                HotUpdateStage.InitializeFramework => "正在初始化框架…",
                HotUpdateStage.InitializePackage => "正在初始化资源包…",
                HotUpdateStage.RequestVersion => "正在检查远端版本…",
                HotUpdateStage.LoadManifest => "正在读取资源清单…",
                HotUpdateStage.CreateDownloader => "发现更新，正在计算下载内容…",
                HotUpdateStage.DownloadFiles => "正在下载更新…",
                HotUpdateStage.LoadAotMetadata => "正在加载兼容元数据…",
                HotUpdateStage.LoadHotfixAssemblies => "正在加载热更新代码…",
                HotUpdateStage.StartHotfix => "正在启动热更新逻辑…",
                _ => "正在启动…"
            };
        }

        internal static string ToChineseHotUpdateError(FrameworkError error)
        {
            if (error == null)
                return "启动失败，请查看日志。";
            return error.Code switch
            {
                "HU_VERSION_FAILED" => "无法取得补丁版本。请确认补丁服务器已启动，然后重试。",
                "HU_MANIFEST_FAILED" => "补丁清单下载失败。请检查网络和服务器文件后重试。",
                "HU_DOWNLOAD_FAILED" => "补丁文件下载失败。恢复服务器或网络后可以继续重试。",
                "HU_INIT_FAILED" => "资源系统初始化失败。请检查客户端文件是否完整。",
                "BOOT_UPDATE_EXCEPTION" => "更新流程出现异常。请检查日志后重试。",
                _ => error.Retryable ? $"{error.Message}\n恢复网络或服务器后可以重试。" : $"{error.Message}\n此错误需要重新安装兼容客户端。"
            };
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{Math.Max(0, bytes)} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024f:0.0} KiB";
            return $"{bytes / (1024f * 1024f):0.0} MiB";
        }

        private void DrawConnectionPage()
        {
            GUILayout.Space(10f);
            GUILayout.Label("服务器地址");
            GUILayout.BeginHorizontal();
            host = GUILayout.TextField(host, GUILayout.Width(300f));
            var portText = GUILayout.TextField(port.ToString(), GUILayout.Width(100f));
            if (ushort.TryParse(portText, out var parsedPort) && parsedPort > 0)
                port = parsedPort;
            GUILayout.EndHorizontal();
            GUI.enabled = !_busy && _networkState != NetworkState.Connecting;
            if (GUILayout.Button("连接局域网服务器", GUILayout.Height(34f)))
                ConnectRequested?.Invoke();
            GUILayout.Space(8f);
            GUILayout.Label("也可以直接创建或加入（会先自动连接）");
            displayName = GUILayout.TextField(displayName, 16);
            if (GUILayout.Button("连接并创建房间"))
                CreateRequested?.Invoke();
            roomCode = GUILayout.TextField(roomCode, 6).Trim();
            if (GUILayout.Button("连接并加入房间"))
                JoinRequested?.Invoke();
            GUI.enabled = true;
        }

        private void DrawLobbyPage()
        {
            GUILayout.Space(10f);
            GUILayout.Label("玩家昵称（1–16 字符）");
            displayName = GUILayout.TextField(displayName, 16);
            GUI.enabled = !_busy;
            if (GUILayout.Button("创建房间", GUILayout.Height(34f)))
                CreateRequested?.Invoke();

            GUILayout.Space(8f);
            GUILayout.Label("6 位房间码");
            roomCode = GUILayout.TextField(roomCode, 6).Trim();
            if (GUILayout.Button("加入房间", GUILayout.Height(34f)))
                JoinRequested?.Invoke();
            if (GUILayout.Button("断开连接"))
                DisconnectRequested?.Invoke();
            GUI.enabled = true;
        }

        private void DrawRoomPage()
        {
            GUILayout.Space(10f);
            GUILayout.Label($"房间码：{_room.RoomCode}");
            GUILayout.Label($"成员：{_room.MemberCount}/{_room.MaximumPlayers}　阶段：{_room.Phase}");
            GUILayout.Space(6f);

            var localIsHost = false;
            var localIsReady = false;
            foreach (var member in _room.Members ?? Array.Empty<RoomMemberSnapshot>())
            {
                var suffix = member.IsHost ? " [房主]" : member.IsReady ? " [已准备]" : " [未准备]";
                GUILayout.Label($"• {member.DisplayName}{suffix}");
                if (member.MemberId == _room.LocalMemberId)
                {
                    localIsHost = member.IsHost;
                    localIsReady = member.IsReady;
                }
            }

            GUILayout.Space(8f);
            GUI.enabled = !_busy && _room.Phase == RoomPhase.Lobby;
            if (!localIsHost && GUILayout.Button(localIsReady ? "取消准备" : "准备", GUILayout.Height(34f)))
                ReadyRequested?.Invoke(!localIsReady);
            if (localIsHost && GUILayout.Button("开始游戏", GUILayout.Height(34f)))
                StartRequested?.Invoke();
            if (GUILayout.Button("退出房间"))
                LeaveRequested?.Invoke();
            GUI.enabled = true;
        }

        private void DrawInGamePanel()
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 310f, 115f), "多人游戏", GUI.skin.box);
            GUILayout.Label($"房间 {_room.RoomCode}　{_room.MemberCount}/{_room.MaximumPlayers}");
            GUI.enabled = !_busy;
            if (GUILayout.Button("返回大厅"))
                LeaveRequested?.Invoke();
            GUI.enabled = true;
            GUILayout.EndArea();
        }

        private void DrawGameplayPanel()
        {
            var width = 410f;
            GUILayout.BeginArea(new Rect(Mathf.Max(340f, Screen.width - width - 16f), 16f, width, 650f),
                "WP4 服务端权威玩法", GUI.skin.box);

            if (!_gameplay.TryGetLocalPlayer(out var player))
            {
                GUILayout.Label("正在等待服务端发送完整世界快照…");
                GUILayout.TextArea(_gameplayStatus, GUILayout.MinHeight(45f));
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"同步版本：{_gameplay.Revision}　玩家：{player.PlayerId}");
            GUILayout.Label($"背包　木材 {Quantity(player, GameplayItemIds.Wood)}　石材 {Quantity(player, GameplayItemIds.Stone)}　斧头 {Quantity(player, GameplayItemIds.Axe)}　金币 {Quantity(player, GameplayItemIds.Coin)}");
            var quest = player.Quest;
            GUILayout.Space(4f);
            GUILayout.Label("个人探索记录");
            GUILayout.Label($"采集木材 {quest.CollectedWood}　建造火堆 {quest.BuiltCampfires}　击败敌人 {quest.DefeatedEnemies}");
            var shared = _gameplay.SharedQuest;
            GUILayout.Label($"共享委托：木材 {shared.WoodContributed}/{shared.WoodRequired}　石材 {shared.StoneContributed}/{shared.StoneRequired}　贡献者 {shared.ContributorCount}/{shared.RequiredContributors}");
            GUILayout.Label(shared.Dialogue ?? string.Empty);
            GUI.enabled = !_gameplayBusy && shared.IsComplete && !shared.LocalRewardClaimed;
            if (GUILayout.Button(shared.LocalRewardClaimed ? "奖励已领取" : "领取共享奖励（每人 10 金币）"))
                RunGameplayRequest(callback => _gameplayService.ClaimQuestReward(callback), "任务奖励领取成功。");
            GUI.enabled = true;
            GUILayout.BeginHorizontal();
            GUI.enabled = !_gameplayBusy && Quantity(player, GameplayItemIds.Wood) > 0;
            if (GUILayout.Button("贡献 1 木"))
                RunGameplayRequest(callback => _gameplayService.Contribute(GameplayItemIds.Wood, 1, callback), "已贡献木材。");
            GUI.enabled = !_gameplayBusy && Quantity(player, GameplayItemIds.Stone) > 0;
            if (GUILayout.Button("贡献 1 石"))
                RunGameplayRequest(callback => _gameplayService.Contribute(GameplayItemIds.Stone, 1, callback), "已贡献石材。");
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("资源（靠近至 3.25 米后采集）");
            foreach (var node in _gameplay.ResourceNodes ?? Array.Empty<GameplayResourceNodeSnapshot>())
            {
                GUILayout.BeginHorizontal();
                var distance = HorizontalDistance(player.Position, node.Position);
                var state = node.Remaining > 0 ? $"{node.Remaining}/{node.Capacity}" : $"刷新 {node.RespawnRemainingSeconds:0.0}s";
                GUILayout.Label($"{node.ItemId}　{state}　{distance:0.0}m", GUILayout.Width(280f));
                GUI.enabled = !_gameplayBusy && node.Remaining > 0 && distance <= 3.25f;
                if (GUILayout.Button("采集", GUILayout.Width(80f)))
                {
                    var nodeId = node.NodeId;
                    RunGameplayRequest(callback => _gameplayService.Collect(nodeId, callback), $"已采集 {node.ItemId}。");
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6f);
            GUILayout.Label("建造（服务端扣除材料并校验位置）");
            GUI.enabled = !_gameplayBusy;
            if (GUILayout.Button("制作斧头（2 木 + 1 石）"))
                RunGameplayRequest(callback => _gameplayService.CraftAxe(callback), "斧头制作成功。");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("篝火（2 木 + 1 石）"))
            {
                var position = player.Position + new Vector3(2f, 0f, 0f);
                RunGameplayRequest(callback => _gameplayService.Build(GameplayBuildingTypes.Firepit, position, 0f, callback), "火堆建造成功。");
            }
            if (GUILayout.Button("木墙（3 木）"))
            {
                var position = player.Position + new Vector3(0f, 0f, 2f);
                RunGameplayRequest(callback => _gameplayService.Build(GameplayBuildingTypes.WallWood, position, 0f, callback), "木墙建造成功。");
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.Label($"当前建筑：{(_gameplay.Buildings?.Length ?? 0)}");

            GUILayout.Space(6f);
            GUILayout.Label("敌人（靠近至 3.5 米后攻击）");
            foreach (var enemy in _gameplay.Enemies ?? Array.Empty<GameplayEnemySnapshot>())
            {
                GUILayout.BeginHorizontal();
                var distance = HorizontalDistance(player.Position, enemy.Position);
                var state = enemy.IsAlive ? $"生命 {enemy.Health}/{enemy.MaximumHealth}" : $"复活 {enemy.RespawnRemainingSeconds:0.0}s";
                GUILayout.Label($"{enemy.EnemyId}　{state}　{distance:0.0}m", GUILayout.Width(280f));
                GUI.enabled = !_gameplayBusy && enemy.IsAlive && distance <= 3.5f;
                if (GUILayout.Button("攻击", GUILayout.Width(80f)))
                {
                    var enemyId = enemy.EnemyId;
                    RunGameplayRequest(callback => _gameplayService.Attack(enemyId, callback), "攻击已由服务端结算。");
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8f);
            GUI.enabled = !_gameplayBusy;
            if (GUILayout.Button("刷新完整状态"))
                RunGameplayRequest(callback => _gameplayService.Refresh(callback), "世界状态已刷新。");
            GUI.enabled = true;
            GUILayout.TextArea(_gameplayStatus, GUILayout.MinHeight(48f));
            GUILayout.EndArea();
        }

        private void RunGameplayRequest(
            Func<Action<FrameworkResult<GameplaySnapshot>>, IEnumerator> requestFactory,
            string successMessage)
        {
            if (_gameplayBusy || _gameplayService == null)
                return;
            _gameplayBusy = true;
            StartCoroutine(requestFactory(result =>
            {
                _gameplayBusy = false;
                if (result.Succeeded)
                {
                    _gameplay = result.Value;
                    _gameplayStatus = successMessage;
                }
                else
                {
                    _gameplayStatus = $"{result.Error.Message}\n错误码：{result.Error.Code}";
                }
            }));
        }

        private void SyncGameplayVisuals()
        {
            if (_room.Phase != RoomPhase.InGame || !_gameplay.HasState)
            {
                ClearGameplayVisuals();
                return;
            }
            if (_visualRevision == _gameplay.Revision)
                return;
            _visualRevision = _gameplay.Revision;
            var activeKeys = new HashSet<string>();

            foreach (var node in _gameplay.ResourceNodes ?? Array.Empty<GameplayResourceNodeSnapshot>())
            {
                var key = "resource:" + node.NodeId;
                activeKeys.Add(key);
                var marker = GetOrCreateMarker(key, PrimitiveType.Sphere,
                    node.ItemId == GameplayItemIds.Wood ? new Color(0.48f, 0.28f, 0.1f) : Color.gray);
                marker.transform.position = node.Position;
                marker.transform.localScale = Vector3.one * 1.1f;
                marker.SetActive(node.Remaining > 0);
            }
            foreach (var building in _gameplay.Buildings ?? Array.Empty<GameplayBuildingSnapshot>())
            {
                var key = "building:" + building.BuildingId;
                activeKeys.Add(key);
                var marker = GetOrCreateMarker(key,
                    building.BuildingType == GameplayBuildingTypes.Firepit ? PrimitiveType.Cylinder : PrimitiveType.Cube,
                    building.BuildingType == GameplayBuildingTypes.Firepit ? new Color(1f, 0.35f, 0.05f) : new Color(0.35f, 0.18f, 0.06f));
                marker.transform.SetPositionAndRotation(building.Position, Quaternion.Euler(0f, building.Yaw, 0f));
                marker.transform.localScale = building.BuildingType == GameplayBuildingTypes.Firepit
                    ? new Vector3(1.1f, 0.25f, 1.1f)
                    : new Vector3(2.5f, 1.5f, 0.3f);
                marker.SetActive(true);
            }
            foreach (var enemy in _gameplay.Enemies ?? Array.Empty<GameplayEnemySnapshot>())
            {
                var key = "enemy:" + enemy.EnemyId;
                activeKeys.Add(key);
                var marker = GetOrCreateMarker(key, PrimitiveType.Capsule, new Color(0.65f, 0.08f, 0.08f));
                marker.transform.position = enemy.Position;
                marker.transform.localScale = new Vector3(0.8f, 1.2f, 0.8f);
                marker.SetActive(enemy.IsAlive);
            }

            var stale = new List<string>();
            foreach (var pair in _gameplayVisuals)
            {
                if (!activeKeys.Contains(pair.Key))
                    stale.Add(pair.Key);
            }
            foreach (var key in stale)
            {
                if (_gameplayVisuals[key])
                    Destroy(_gameplayVisuals[key]);
                _gameplayVisuals.Remove(key);
            }
        }

        private GameObject GetOrCreateMarker(string key, PrimitiveType type, Color color)
        {
            if (_gameplayVisuals.TryGetValue(key, out var existing) && existing)
                return existing;
            var marker = GameObject.CreatePrimitive(type);
            marker.name = "[WP4] " + key;
            var collider = marker.GetComponent<Collider>();
            if (collider)
                Destroy(collider);
            var renderer = marker.GetComponent<Renderer>();
            if (renderer)
                renderer.material.color = color;
            _gameplayVisuals[key] = marker;
            return marker;
        }

        private void ClearGameplayVisuals()
        {
            foreach (var marker in _gameplayVisuals.Values)
            {
                if (marker)
                    Destroy(marker);
            }
            _gameplayVisuals.Clear();
            _visualRevision = -1;
        }

        private static int Quantity(GameplayPlayerSnapshot player, string itemId)
        {
            foreach (var item in player.Inventory ?? Array.Empty<GameplayInventoryItemSnapshot>())
            {
                if (string.Equals(item.ItemId, itemId, StringComparison.Ordinal))
                    return item.Quantity;
            }
            return 0;
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            left.y = 0f;
            right.y = 0f;
            return Vector3.Distance(left, right);
        }
    }
}
