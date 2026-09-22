using System;
using System.Collections;
using Haven.Framework.Core;
using Haven.Framework.Demo;
using Haven.Framework.Resources;
using Haven.Framework.Services;
using Haven.Hotfix.Flow;
using UnityEngine;

namespace Haven.Hotfix.Lobby
{
    internal sealed class LobbyPresenter : IDisposable
    {
        private readonly HavenDemoHud _view;
        private readonly INetworkService _network;
        private readonly IRoomService _rooms;
        private readonly IGameFlowService _flow;
        private readonly IResourceService _resources;
        private readonly string _contentVersion;
        private readonly IDisposable _networkSubscription;
        private readonly IDisposable _roomSubscription;
        private readonly IDisposable _progressSubscription;
        private ResourceLease<Sprite> _badgeLease;
        private bool _busy;
        private bool _disposed;

        public LobbyPresenter(HavenDemoHud view, INetworkService network, IRoomService rooms,
            IGameFlowService flow, IEventBus events, IResourceService resources, string contentVersion)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _contentVersion = contentVersion ?? string.Empty;

            _view.ConnectRequested += OnConnectRequested;
            _view.DisconnectRequested += OnDisconnectRequested;
            _view.CreateRequested += OnCreateRequested;
            _view.JoinRequested += OnJoinRequested;
            _view.ReadyRequested += OnReadyRequested;
            _view.StartRequested += OnStartRequested;
            _view.LeaveRequested += OnLeaveRequested;

            _networkSubscription = events.Subscribe<NetworkStateChanged>(OnNetworkChanged);
            _roomSubscription = events.Subscribe<RoomSnapshotChanged>(OnRoomChanged);
            _progressSubscription = events.Subscribe<RoomGameLoadProgressChanged>(OnLoadProgress);

            _view.SetNetworkState(_network.State);
            _view.SetRoom(_rooms.Current);
            _view.SetStatus(_network.IsConnected ? "已连接服务器，可以创建或加入房间。" : "请输入服务器地址并连接。");
            if (_network.IsConnected)
                MoveTo(GameFlowState.Lobby);
        }

        public IEnumerator InitializeHotUpdateDemo()
        {
            FrameworkResult<ResourceLease<Sprite>> result = default;
            yield return _resources.LoadAsset<Sprite>("HotUpdateDemoBadge", value => result = value);
            if (_disposed)
            {
                if (result.Succeeded)
                    result.Value.Dispose();
                yield break;
            }

            if (result.Succeeded)
            {
                _badgeLease = result.Value;
                _view.SetHotUpdateDemo(_badgeLease.Asset, HotUpdateDemoRevision.Announcement, _contentVersion);
            }
            else
            {
                _view.SetHotUpdateDemo(null, $"营地公告 {HotUpdateDemoRevision.Id}：热更新逻辑已生效，但徽章资源加载失败。", _contentVersion);
                GameLog.Warning("Lobby", result.Error?.Message ?? "Hot-update badge failed to load.", "HOTUPDATE_BADGE_LOAD_FAILED");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _view.ConnectRequested -= OnConnectRequested;
            _view.DisconnectRequested -= OnDisconnectRequested;
            _view.CreateRequested -= OnCreateRequested;
            _view.JoinRequested -= OnJoinRequested;
            _view.ReadyRequested -= OnReadyRequested;
            _view.StartRequested -= OnStartRequested;
            _view.LeaveRequested -= OnLeaveRequested;
            _networkSubscription.Dispose();
            _roomSubscription.Dispose();
            _progressSubscription.Dispose();
            _badgeLease?.Dispose();
            _badgeLease = null;
            _view.ClearHotUpdateDemo();
        }

        private void OnConnectRequested()
        {
            Run(ConnectOnly());
        }

        private void OnDisconnectRequested()
        {
            _network.Disconnect();
            _view.SetStatus("已断开服务器连接。");
        }

        private void OnCreateRequested()
        {
            Run(ConnectThenRoomOperation(() => _rooms.CreateRoom(_view.DisplayName, CompleteRoomOperation), "正在创建房间…"));
        }

        private void OnJoinRequested()
        {
            Run(ConnectThenRoomOperation(() => _rooms.JoinRoom(_view.RoomCode, _view.DisplayName, CompleteRoomOperation), "正在加入房间…"));
        }

        private void OnReadyRequested(bool ready)
        {
            Run(RoomOperation(_rooms.SetReady(ready, CompleteRoomOperation), ready ? "正在准备…" : "正在取消准备…"));
        }

        private void OnStartRequested()
        {
            Run(RoomOperation(_rooms.StartGame(CompleteRoomOperation), "正在启动游戏…"));
        }

        private void OnLeaveRequested()
        {
            if (_flow.Current == GameFlowState.InGame)
                MoveTo(GameFlowState.ReturningToLobby);
            Run(RoomOperation(_rooms.LeaveRoom(CompleteRoomOperation), "正在返回大厅…"));
        }

        private IEnumerator ConnectOnly()
        {
            yield return EnsureConnected();
        }

        private IEnumerator ConnectThenRoomOperation(Func<IEnumerator> operation, string status)
        {
            yield return EnsureConnected();
            if (_network.IsConnected)
                yield return RoomOperation(operation(), status);
        }

        private IEnumerator EnsureConnected()
        {
            if (_network.IsConnected)
                yield break;

            SetBusy(true);
            _view.SetStatus($"正在连接 {_view.Host}:{_view.Port}…");
            FrameworkResult result = default;
            yield return _network.Connect(new NetworkEndpoint(_view.Host, _view.Port), value => result = value);
            SetBusy(false);
            if (result.Succeeded)
            {
                _view.SetStatus("服务器连接成功。");
                MoveTo(GameFlowState.Lobby);
            }
            else
            {
                _view.SetStatus(ToChinese(result.Error));
            }
        }

        private IEnumerator RoomOperation(IEnumerator operation, string status)
        {
            SetBusy(true);
            _view.SetStatus(status);
            yield return operation;
            SetBusy(false);
        }

        private void CompleteRoomOperation(FrameworkResult<RoomSnapshot> result)
        {
            if (result.Succeeded)
            {
                _view.SetStatus(StatusFor(result.Value));
                OnRoomChanged(new RoomSnapshotChanged(result.Value));
            }
            else
            {
                _view.SetStatus(ToChinese(result.Error));
                if (_flow.Current == GameFlowState.ReturningToLobby)
                    MoveTo(GameFlowState.InGame);
            }
        }

        private void OnNetworkChanged(NetworkStateChanged change)
        {
            _view.SetNetworkState(change.Current);
            if (change.Current == NetworkState.Connected)
            {
                MoveTo(GameFlowState.Lobby);
                return;
            }
            if (change.Current == NetworkState.Disconnected || change.Current == NetworkState.Failed)
            {
                _view.SetRoom(RoomSnapshot.Empty);
                _view.SetStatus(change.Current == NetworkState.Failed ? "服务器连接失败。" : "服务器连接已断开。");
                MoveTo(GameFlowState.MainMenu);
                SetBusy(false);
            }
        }

        private void OnRoomChanged(RoomSnapshotChanged change)
        {
            _view.SetRoom(change.Snapshot);
            switch (change.Snapshot.Phase)
            {
                case RoomPhase.Lobby:
                    if (_flow.Current == GameFlowState.LoadingGame)
                    {
                        _view.SetStatus("本次开局已取消，房间准备状态已重置。");
                        MoveTo(GameFlowState.Room);
                    }
                    else if (_flow.Current == GameFlowState.Lobby)
                        MoveTo(GameFlowState.Room);
                    else if (_flow.Current == GameFlowState.ReturningToLobby)
                        MoveTo(GameFlowState.Lobby);
                    break;
                case RoomPhase.Loading:
                    if (_flow.Current == GameFlowState.Lobby)
                        MoveTo(GameFlowState.Room);
                    MoveTo(GameFlowState.LoadingGame);
                    break;
                case RoomPhase.InGame:
                    if (_flow.Current == GameFlowState.Room)
                        MoveTo(GameFlowState.LoadingGame);
                    MoveTo(GameFlowState.InGame);
                    break;
                case RoomPhase.None:
                    if (_network.IsConnected && _flow.Current != GameFlowState.Lobby)
                        MoveTo(GameFlowState.Lobby);
                    break;
            }
        }

        private void OnLoadProgress(RoomGameLoadProgressChanged progress)
        {
            _view.SetLoadProgress(progress);
            if (!string.IsNullOrEmpty(progress.Message))
                _view.SetStatus(progress.Message);
        }

        private void Run(IEnumerator routine)
        {
            if (_busy || routine == null)
                return;
            _view.Run(routine);
        }

        private void SetBusy(bool value)
        {
            _busy = value;
            _view.SetBusy(value);
        }

        private void MoveTo(GameFlowState target)
        {
            if (_flow.Current != target)
                _flow.TryTransition(target);
        }

        private static string StatusFor(RoomSnapshot snapshot)
        {
            return snapshot.Phase switch
            {
                RoomPhase.Lobby => $"已进入房间 {snapshot.RoomCode}。",
                RoomPhase.Loading => "所有人已准备，正在加载 WorldGenMap。",
                RoomPhase.InGame => "所有成员已进入 WorldGenMap。",
                _ => "已返回大厅。"
            };
        }

        private static string ToChinese(FrameworkError error)
        {
            if (error == null)
                return "操作失败，请重试。";
            return error.Code switch
            {
                RoomErrorCodes.NotFound => "没有找到该房间，请检查房间码。",
                RoomErrorCodes.Full => "房间已满。",
                RoomErrorCodes.InProgress => "房间已经开始游戏。",
                RoomErrorCodes.NotHost => "只有房主可以开始游戏。",
                RoomErrorCodes.MinimumPlayers => "至少需要两名玩家才能开始。",
                RoomErrorCodes.NotReady => "仍有玩家尚未准备。",
                RoomErrorCodes.InvalidName => "昵称必须包含 1–16 个有效字符。",
                RoomErrorCodes.DuplicateName => "房间内已有相同昵称。",
                RoomErrorCodes.RequestTimeout => "请求超时，请检查网络后重试。",
                RoomErrorCodes.ProtocolMismatch => "客户端与服务器版本不一致，请重新构建并更新双方。",
                RoomErrorCodes.LoadFailed => "玩家或场景尚未就绪，请稍后重试。",
                _ => error.Message
            };
        }
    }
}
