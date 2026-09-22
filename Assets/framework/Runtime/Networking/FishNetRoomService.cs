using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;

namespace Haven.Networking
{
    internal sealed class FishNetRoomService : IRoomService, IDisposable
    {
        private const string GameSceneName = "WorldGenMap";

        private sealed class PendingRequest
        {
            public bool Completed;
            public FrameworkResult<RoomSnapshot> Result;
        }

        private readonly FrameworkContext _context;
        private readonly NetworkManager _manager;
        private readonly HavenNetworkSettings _settings;
        private readonly MonoBehaviour _coroutineHost;
        private readonly NetworkObject _playerPrefab;
        private readonly Dictionary<string, PendingRequest> _pendingRequests = new Dictionary<string, PendingRequest>();
        private readonly HashSet<int> _pendingSceneMembers = new HashSet<int>();
        private readonly HashSet<int> _lateJoiningMembers = new HashSet<int>();
        private RoomSession _session;
        private Coroutine _loadTimeoutRoutine;
        private bool _disposed;

        public FishNetRoomService(
            FrameworkContext context,
            NetworkManager manager,
            HavenNetworkSettings settings,
            MonoBehaviour coroutineHost,
            NetworkObject playerPrefab)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _manager = manager ? manager : throw new ArgumentNullException(nameof(manager));
            _settings = settings ? settings : throw new ArgumentNullException(nameof(settings));
            _coroutineHost = coroutineHost ? coroutineHost : throw new ArgumentNullException(nameof(coroutineHost));
            _playerPrefab = playerPrefab;

            Current = RoomSnapshot.Empty;
            _manager.ServerManager.RegisterBroadcast<RoomCommandBroadcast>(OnServerRoomCommand);
            _manager.ClientManager.RegisterBroadcast<RoomResponseBroadcast>(OnClientRoomResponse);
            _manager.ClientManager.RegisterBroadcast<RoomSnapshotBroadcast>(OnClientRoomSnapshot);
            _manager.ClientManager.RegisterBroadcast<RoomLoadProgressBroadcast>(OnClientLoadProgress);
            _manager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            _manager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            _manager.SceneManager.OnClientPresenceChangeEnd += OnClientPresenceChangeEnd;
            _manager.SceneManager.OnLoadPercentChange += OnSceneLoadPercentChange;
        }

        public RoomSnapshot Current { get; private set; }

        public IEnumerator CreateRoom(string displayName, Action<FrameworkResult<RoomSnapshot>> completed)
        {
            return SendRequest(new RoomCommandBroadcast
            {
                Command = RoomCommand.Create,
                DisplayName = displayName
            }, completed);
        }

        public IEnumerator JoinRoom(string roomCode, string displayName, Action<FrameworkResult<RoomSnapshot>> completed)
        {
            return SendRequest(new RoomCommandBroadcast
            {
                Command = RoomCommand.Join,
                RoomCode = roomCode,
                DisplayName = displayName
            }, completed);
        }

        public IEnumerator SetReady(bool ready, Action<FrameworkResult<RoomSnapshot>> completed)
        {
            return SendRequest(new RoomCommandBroadcast
            {
                Command = RoomCommand.SetReady,
                Ready = ready
            }, completed);
        }

        public IEnumerator StartGame(Action<FrameworkResult<RoomSnapshot>> completed)
        {
            return SendRequest(new RoomCommandBroadcast { Command = RoomCommand.StartGame }, completed);
        }

        public IEnumerator LeaveRoom(Action<FrameworkResult<RoomSnapshot>> completed)
        {
            return SendRequest(new RoomCommandBroadcast { Command = RoomCommand.Leave }, completed);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_loadTimeoutRoutine != null && _coroutineHost)
                _coroutineHost.StopCoroutine(_loadTimeoutRoutine);
            _loadTimeoutRoutine = null;

            if (_manager)
            {
                _manager.ServerManager.UnregisterBroadcast<RoomCommandBroadcast>(OnServerRoomCommand);
                _manager.ClientManager.UnregisterBroadcast<RoomResponseBroadcast>(OnClientRoomResponse);
                _manager.ClientManager.UnregisterBroadcast<RoomSnapshotBroadcast>(OnClientRoomSnapshot);
                _manager.ClientManager.UnregisterBroadcast<RoomLoadProgressBroadcast>(OnClientLoadProgress);
                _manager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
                _manager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
                _manager.SceneManager.OnClientPresenceChangeEnd -= OnClientPresenceChangeEnd;
                _manager.SceneManager.OnLoadPercentChange -= OnSceneLoadPercentChange;
            }

            FailAllPending(RoomErrorCodes.NotConnected, "房间服务已停止。");
            SetCurrent(RoomSnapshot.Empty);
            _pendingSceneMembers.Clear();
            _lateJoiningMembers.Clear();
            _session = null;
        }

        internal bool IsInGameMember(int clientId)
        {
            return _session != null && _session.Phase == RoomPhase.InGame && _session.Contains(clientId);
        }

        internal int[] InGameMemberIds()
        {
            return _session != null && _session.Phase == RoomPhase.InGame
                ? _session.MemberIds()
                : Array.Empty<int>();
        }

        private IEnumerator SendRequest(RoomCommandBroadcast command, Action<FrameworkResult<RoomSnapshot>> completed)
        {
            if (_disposed || !_manager.ClientManager.Started)
            {
                completed?.Invoke(Failure(RoomErrorCodes.NotConnected, "尚未连接 Dedicated Server。", true));
                yield break;
            }

            command.RequestId = Guid.NewGuid().ToString("N");
            command.ProtocolVersion = _settings.ProtocolVersion;
            var pending = new PendingRequest();
            _pendingRequests.Add(command.RequestId, pending);
            _manager.ClientManager.Broadcast(command, Channel.Reliable);

            var deadline = Time.realtimeSinceStartup + _settings.RoomRequestTimeoutSeconds;
            while (!pending.Completed && Time.realtimeSinceStartup < deadline && _manager.ClientManager.Started)
                yield return null;

            _pendingRequests.Remove(command.RequestId);
            if (!pending.Completed)
                pending.Result = Failure(RoomErrorCodes.RequestTimeout, "房间请求超时，请重试。", true);
            completed?.Invoke(pending.Result);
        }

        private void OnServerRoomCommand(NetworkConnection connection, RoomCommandBroadcast command, Channel channel)
        {
            if (command.ProtocolVersion != _settings.ProtocolVersion)
            {
                SendFailure(connection, command.RequestId, RoomErrorCodes.ProtocolMismatch,
                    $"协议版本不一致：客户端 {command.ProtocolVersion}，服务器 {_settings.ProtocolVersion}。");
                return;
            }

            switch (command.Command)
            {
                case RoomCommand.Create:
                    HandleCreate(connection, command);
                    break;
                case RoomCommand.Join:
                    HandleJoin(connection, command);
                    break;
                case RoomCommand.SetReady:
                    HandleMutation(connection, command.RequestId, _session?.SetReady(connection.ClientId, command.Ready));
                    break;
                case RoomCommand.StartGame:
                    HandleStartGame(connection, command.RequestId);
                    break;
                case RoomCommand.Leave:
                    HandleLeave(connection, command.RequestId);
                    break;
                default:
                    SendFailure(connection, command.RequestId, RoomErrorCodes.NotFound, "未知房间命令。");
                    break;
            }
        }

        private void HandleCreate(NetworkConnection connection, RoomCommandBroadcast command)
        {
            if (_session != null && !_session.IsEmpty)
            {
                var code = _session.Contains(connection.ClientId) ? RoomErrorCodes.AlreadyJoined : RoomErrorCodes.InProgress;
                SendFailure(connection, command.RequestId, code, "当前服务器已经承载一个逻辑房间。");
                return;
            }

            var created = RoomSession.Create(GenerateRoomCode(), _settings.MaximumPlayers, _settings.MinimumPlayers,
                connection.ClientId, command.DisplayName);
            if (!created.Succeeded)
            {
                SendFailure(connection, command.RequestId, created.Error.Code, created.Error.Message);
                return;
            }

            _session = created.Value;
            var snapshot = _session.SnapshotFor(connection.ClientId);
            SendSuccess(connection, command.RequestId, snapshot);
            BroadcastSnapshots();
        }

        private void HandleJoin(NetworkConnection connection, RoomCommandBroadcast command)
        {
            if (_session == null || _session.IsEmpty ||
                !string.Equals(_session.RoomCode, (command.RoomCode ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                SendFailure(connection, command.RequestId, RoomErrorCodes.NotFound, "没有找到该房间。");
                return;
            }

            var joiningInGame = _session.Phase == RoomPhase.InGame;
            if (joiningInGame && (connection.FirstObject == null || !connection.FirstObject.IsSpawned))
            {
                SendFailure(connection, command.RequestId, RoomErrorCodes.LoadFailed, "玩家对象尚未就绪，请稍后重试。");
                return;
            }

            var result = _session.Join(connection.ClientId, command.DisplayName);
            if (!result.Succeeded)
            {
                SendFailure(connection, command.RequestId, result.Error.Code, result.Error.Message);
                return;
            }

            SendSuccess(connection, command.RequestId, result.Value);
            BroadcastSnapshots();
            if (!joiningInGame)
                return;

            _lateJoiningMembers.Add(connection.ClientId);
            var loadData = new SceneLoadData(GameSceneName)
            {
                MovedNetworkObjects = new[] { connection.FirstObject },
                ReplaceScenes = ReplaceOption.None,
                PreferredActiveScene = new PreferredScene(new SceneLookupData(GameSceneName)),
                Options = new LoadOptions { AutomaticallyUnload = true }
            };
            _manager.SceneManager.LoadConnectionScenes(new[] { connection }, loadData);
        }

        private void HandleMutation(NetworkConnection connection, string requestId, FrameworkResult<RoomSnapshot>? result)
        {
            if (!result.HasValue)
            {
                SendFailure(connection, requestId, RoomErrorCodes.NotFound, "当前没有房间。");
                return;
            }
            if (!result.Value.Succeeded)
            {
                SendFailure(connection, requestId, result.Value.Error.Code, result.Value.Error.Message);
                return;
            }

            SendSuccess(connection, requestId, result.Value.Value);
            BroadcastSnapshots();
        }

        private void HandleStartGame(NetworkConnection connection, string requestId)
        {
            if (_session == null)
            {
                SendFailure(connection, requestId, RoomErrorCodes.NotFound, "当前没有房间。");
                return;
            }

            var result = _session.BeginLoading(connection.ClientId);
            if (!result.Succeeded)
            {
                SendFailure(connection, requestId, result.Error.Code, result.Error.Message);
                return;
            }

            var connections = GetRoomConnections(out var movedObjects);
            if (connections.Length != _session.Count || movedObjects.Length != _session.Count)
            {
                _session.RollbackLoading();
                SendFailure(connection, requestId, RoomErrorCodes.LoadFailed, "玩家对象尚未就绪，请稍后重试。");
                BroadcastSnapshots();
                return;
            }

            _pendingSceneMembers.Clear();
            foreach (var memberId in _session.MemberIds())
                _pendingSceneMembers.Add(memberId);

            SendSuccess(connection, requestId, _session.SnapshotFor(connection.ClientId));
            BroadcastSnapshots();
            BroadcastLoadProgress(0f, "正在同步加载世界场景…");

            var loadData = new SceneLoadData(GameSceneName)
            {
                MovedNetworkObjects = movedObjects,
                ReplaceScenes = ReplaceOption.None,
                PreferredActiveScene = new PreferredScene(new SceneLookupData(GameSceneName)),
                Options = new LoadOptions { AutomaticallyUnload = true }
            };
            _manager.SceneManager.LoadConnectionScenes(connections, loadData);
            _loadTimeoutRoutine = _coroutineHost.StartCoroutine(WatchLoadTimeout());
        }

        private void HandleLeave(NetworkConnection connection, string requestId)
        {
            if (_session == null || !_session.Contains(connection.ClientId))
            {
                SendFailure(connection, requestId, RoomErrorCodes.NotFound, "你不在当前房间中。");
                return;
            }

            var previousPhase = _session.Phase;
            _session.Remove(connection.ClientId);
            _lateJoiningMembers.Remove(connection.ClientId);
            SendSuccess(connection, requestId, RoomSnapshot.Empty);

            if (previousPhase == RoomPhase.Loading)
            {
                _session.RollbackLoading();
                ReturnAllMembersToLobby(connection);
            }
            else if (previousPhase == RoomPhase.InGame)
            {
                ReturnConnectionToLobby(connection);
            }

            if (_session.IsEmpty)
                _session = null;
            else
                BroadcastSnapshots();
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped || _session == null || !_session.Contains(connection.ClientId))
                return;

            var wasLoading = _session.Phase == RoomPhase.Loading;
            _session.Remove(connection.ClientId);
            _pendingSceneMembers.Remove(connection.ClientId);
            _lateJoiningMembers.Remove(connection.ClientId);
            if (_session.IsEmpty)
            {
                StopLoadTimeout();
                _session = null;
                _pendingSceneMembers.Clear();
                return;
            }

            if (wasLoading)
            {
                _session.RollbackLoading();
                ReturnAllMembersToLobby(null);
            }
            BroadcastSnapshots();
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped)
                return;
            FailAllPending(RoomErrorCodes.NotConnected, "与服务器的连接已断开。");
            SetCurrent(RoomSnapshot.Empty);
        }

        private void OnClientPresenceChangeEnd(ClientPresenceChangeEventArgs args)
        {
            if (!args.Added || args.Scene.name != GameSceneName || _session == null)
                return;
            if (_session.Phase == RoomPhase.InGame)
            {
                if (!_lateJoiningMembers.Remove(args.Connection.ClientId))
                    return;
                PlacePlayer(args.Connection);
                BroadcastSnapshots();
                return;
            }
            if (_session.Phase != RoomPhase.Loading)
                return;
            if (!_pendingSceneMembers.Remove(args.Connection.ClientId))
                return;

            PlacePlayer(args.Connection);

            var loaded = _session.Count - _pendingSceneMembers.Count;
            BroadcastLoadProgress((float)loaded / _session.Count, $"已进入世界：{loaded}/{_session.Count}");
            if (_pendingSceneMembers.Count > 0)
                return;

            StopLoadTimeout();
            _session.CompleteLoading();
            BroadcastSnapshots();
        }

        private void PlacePlayer(NetworkConnection connection)
        {
            var player = connection.FirstObject;
            if (!player || _session == null)
                return;
            var index = Math.Max(0, Array.IndexOf(_session.MemberIds(), connection.ClientId));
            var angle = index * Mathf.PI * 2f / Math.Max(1, _session.Count);
            player.transform.SetPositionAndRotation(
                new Vector3(Mathf.Cos(angle) * 2f, 1f, Mathf.Sin(angle) * 2f),
                Quaternion.identity);
        }

        private void OnSceneLoadPercentChange(SceneLoadPercentEventArgs args)
        {
            if (_session == null || _session.Phase != RoomPhase.Loading)
                return;
            var loaded = _session.Count - _pendingSceneMembers.Count;
            BroadcastLoadProgress(args.Percent, $"正在加载世界… {args.Percent:P0}");
            if (loaded > 0)
                BroadcastLoadProgress(Mathf.Max(args.Percent, (float)loaded / _session.Count), $"已进入世界：{loaded}/{_session.Count}");
        }

        private IEnumerator WatchLoadTimeout()
        {
            var deadline = Time.realtimeSinceStartup + _settings.RoomLoadTimeoutSeconds;
            while (!_disposed && _session != null && _session.Phase == RoomPhase.Loading &&
                   _pendingSceneMembers.Count > 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            _loadTimeoutRoutine = null;
            if (_disposed || _session == null || _session.Phase != RoomPhase.Loading || _pendingSceneMembers.Count == 0)
                yield break;

            GameLog.Warning("Room", "WorldGenMap load timed out; rolling the room back to Lobby.", RoomErrorCodes.RequestTimeout);
            _session.RollbackLoading();
            ReturnAllMembersToLobby(null);
            BroadcastSnapshots();
            BroadcastLoadProgress(0f, "场景加载超时，已返回房间。");
        }

        private void ReturnAllMembersToLobby(NetworkConnection additionalConnection)
        {
            StopLoadTimeout();
            _pendingSceneMembers.Clear();
            if (_session != null)
            {
                foreach (var memberId in _session.MemberIds())
                {
                    if (_manager.ServerManager.Clients.TryGetValue(memberId, out var memberConnection))
                        ReturnConnectionToLobby(memberConnection);
                }
            }
            if (additionalConnection != null && additionalConnection.IsActive)
                ReturnConnectionToLobby(additionalConnection);
        }

        private void ReturnConnectionToLobby(NetworkConnection connection)
        {
            if (connection == null || !connection.IsActive)
                return;
            if (connection.FirstObject != null && connection.FirstObject.IsSpawned)
                _manager.ServerManager.Despawn(connection.FirstObject);
            _manager.SceneManager.UnloadConnectionScenes(connection, new SceneUnloadData(GameSceneName));
            _coroutineHost.StartCoroutine(RestoreLobbyPlayer(connection));
        }

        private IEnumerator RestoreLobbyPlayer(NetworkConnection connection)
        {
            yield return null;
            yield return null;
            if (_disposed || connection == null || !connection.IsActive || connection.FirstObject != null || !_playerPrefab)
                yield break;

            var player = _manager.GetPooledInstantiated(_playerPrefab, Vector3.zero, Quaternion.identity, true);
            _manager.ServerManager.Spawn(player, connection);
            _manager.SceneManager.AddOwnerToDefaultScene(player);
        }

        private NetworkConnection[] GetRoomConnections(out NetworkObject[] movedObjects)
        {
            var connections = new List<NetworkConnection>();
            var objects = new List<NetworkObject>();
            foreach (var memberId in _session.MemberIds())
            {
                if (!_manager.ServerManager.Clients.TryGetValue(memberId, out var connection) || !connection.IsActive)
                    continue;
                connections.Add(connection);
                if (connection.FirstObject != null && connection.FirstObject.IsSpawned)
                    objects.Add(connection.FirstObject);
            }
            movedObjects = objects.ToArray();
            return connections.ToArray();
        }

        private void BroadcastSnapshots()
        {
            if (_session == null)
                return;
            foreach (var memberId in _session.MemberIds())
            {
                if (_manager.ServerManager.Clients.TryGetValue(memberId, out var connection) && connection.IsActive)
                {
                    _manager.ServerManager.Broadcast(connection, new RoomSnapshotBroadcast
                    {
                        Snapshot = ToWire(_session.SnapshotFor(memberId))
                    });
                }
            }
        }

        private void BroadcastLoadProgress(float progress, string message)
        {
            if (_session == null)
                return;
            var data = new RoomLoadProgressBroadcast
            {
                Progress = Mathf.Clamp01(progress),
                LoadedMembers = _session.Count - _pendingSceneMembers.Count,
                TotalMembers = _session.Count,
                Message = message ?? string.Empty
            };
            foreach (var memberId in _session.MemberIds())
            {
                if (_manager.ServerManager.Clients.TryGetValue(memberId, out var connection) && connection.IsActive)
                    _manager.ServerManager.Broadcast(connection, data);
            }
        }

        private void SendSuccess(NetworkConnection connection, string requestId, RoomSnapshot snapshot)
        {
            _manager.ServerManager.Broadcast(connection, new RoomResponseBroadcast
            {
                RequestId = requestId,
                Succeeded = true,
                Snapshot = ToWire(snapshot)
            });
        }

        private void SendFailure(NetworkConnection connection, string requestId, string code, string message, bool retryable = false)
        {
            _manager.ServerManager.Broadcast(connection, new RoomResponseBroadcast
            {
                RequestId = requestId,
                Succeeded = false,
                ErrorCode = code,
                ErrorMessage = message,
                Retryable = retryable,
                Snapshot = ToWire(RoomSnapshot.Empty)
            });
        }

        private void OnClientRoomResponse(RoomResponseBroadcast response, Channel channel)
        {
            if (!_pendingRequests.TryGetValue(response.RequestId ?? string.Empty, out var pending))
                return;
            pending.Result = response.Succeeded
                ? FrameworkResult<RoomSnapshot>.Success(FromWire(response.Snapshot))
                : Failure(response.ErrorCode, response.ErrorMessage, response.Retryable);
            pending.Completed = true;
            if (response.Succeeded)
                SetCurrent(pending.Result.Value);
        }

        private void OnClientRoomSnapshot(RoomSnapshotBroadcast broadcast, Channel channel)
        {
            SetCurrent(FromWire(broadcast.Snapshot));
        }

        private void OnClientLoadProgress(RoomLoadProgressBroadcast broadcast, Channel channel)
        {
            _context.Events.Publish(new RoomGameLoadProgressChanged(
                broadcast.Progress, broadcast.LoadedMembers, broadcast.TotalMembers, broadcast.Message));
        }

        private void SetCurrent(RoomSnapshot snapshot)
        {
            Current = snapshot;
            _context.Events.Publish(new RoomSnapshotChanged(snapshot));
        }

        private void FailAllPending(string code, string message)
        {
            foreach (var pending in _pendingRequests.Values)
            {
                pending.Result = Failure(code, message, true);
                pending.Completed = true;
            }
            _pendingRequests.Clear();
        }

        private void StopLoadTimeout()
        {
            if (_loadTimeoutRoutine != null && _coroutineHost)
                _coroutineHost.StopCoroutine(_loadTimeoutRoutine);
            _loadTimeoutRoutine = null;
        }

        private static string GenerateRoomCode()
        {
            return UnityEngine.Random.Range(0, 1000000).ToString("D6");
        }

        private static RoomSnapshotWire ToWire(RoomSnapshot snapshot)
        {
            var source = snapshot.Members ?? Array.Empty<RoomMemberSnapshot>();
            var members = new RoomMemberWire[source.Length];
            for (var index = 0; index < source.Length; index++)
            {
                members[index] = new RoomMemberWire
                {
                    MemberId = source[index].MemberId,
                    DisplayName = source[index].DisplayName,
                    IsHost = source[index].IsHost,
                    IsReady = source[index].IsReady
                };
            }
            return new RoomSnapshotWire
            {
                RoomCode = snapshot.RoomCode ?? string.Empty,
                Phase = snapshot.Phase,
                MaximumPlayers = snapshot.MaximumPlayers,
                LocalMemberId = snapshot.LocalMemberId,
                Members = members
            };
        }

        private static RoomSnapshot FromWire(RoomSnapshotWire wire)
        {
            var source = wire.Members ?? Array.Empty<RoomMemberWire>();
            var members = new RoomMemberSnapshot[source.Length];
            for (var index = 0; index < source.Length; index++)
            {
                members[index] = new RoomMemberSnapshot
                {
                    MemberId = source[index].MemberId,
                    DisplayName = source[index].DisplayName ?? string.Empty,
                    IsHost = source[index].IsHost,
                    IsReady = source[index].IsReady
                };
            }
            return new RoomSnapshot
            {
                RoomCode = wire.RoomCode ?? string.Empty,
                Phase = wire.Phase,
                MaximumPlayers = wire.MaximumPlayers,
                LocalMemberId = wire.LocalMemberId,
                Members = members
            };
        }

        private static FrameworkResult<RoomSnapshot> Failure(string code, string message, bool retryable = false)
        {
            return FrameworkResult<RoomSnapshot>.Failure(new FrameworkError(
                string.IsNullOrEmpty(code) ? RoomErrorCodes.NotFound : code,
                message ?? "房间请求失败。", "Room", retryable));
        }
    }
}
