using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;

namespace Haven.Networking
{
    internal sealed class FishNetGameplayService : ICoopGameplayService, IDisposable
    {
        private const float SnapshotIntervalSeconds = 0.2f;

        private sealed class PendingRequest
        {
            public bool Completed;
            public FrameworkResult<GameplaySnapshot> Result;
            public string Command;
        }

        private readonly FrameworkContext _context;
        private readonly NetworkManager _manager;
        private readonly HavenNetworkSettings _settings;
        private readonly FishNetRoomService _roomService;
        private readonly MonoBehaviour _coroutineHost;
        private readonly Dictionary<string, PendingRequest> _pendingRequests = new Dictionary<string, PendingRequest>();
        private AuthoritativeGameplaySession _session = new AuthoritativeGameplaySession();
        private float _nextSnapshotAt;
        private bool _serverSessionActive;
        private bool _questRequested;
        private bool _disposed;

        public FishNetGameplayService(
            FrameworkContext context,
            NetworkManager manager,
            HavenNetworkSettings settings,
            FishNetRoomService roomService,
            MonoBehaviour coroutineHost)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _manager = manager ? manager : throw new ArgumentNullException(nameof(manager));
            _settings = settings ? settings : throw new ArgumentNullException(nameof(settings));
            _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
            _coroutineHost = coroutineHost ? coroutineHost : throw new ArgumentNullException(nameof(coroutineHost));
            _session.ValidatePlacement = ValidatePlacement;

            Current = GameplaySnapshot.Empty;
            _manager.ServerManager.RegisterBroadcast<GameplayCommandBroadcast>(OnServerCommand);
            _manager.ClientManager.RegisterBroadcast<GameplayResponseBroadcast>(OnClientResponse);
            _manager.ClientManager.RegisterBroadcast<GameplaySnapshotBroadcast>(OnClientSnapshot);
            _manager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            _manager.ClientManager.OnClientConnectionState += OnClientConnectionState;
        }

        public GameplaySnapshot Current { get; private set; }

        public IEnumerator Refresh(Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            return SendRequest(new GameplayCommandBroadcast { Command = GameplayCommand.Refresh }, completed);
        }

        public IEnumerator Collect(string resourceNodeId, Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            return SendRequest(new GameplayCommandBroadcast
            {
                Command = GameplayCommand.Collect,
                TargetId = resourceNodeId
            }, completed);
        }

        public IEnumerator CraftAxe(Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            return SendRequest(new GameplayCommandBroadcast { Command = GameplayCommand.CraftAxe }, completed);
        }

        public IEnumerator Contribute(string itemId, int quantity, Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            return SendRequest(new GameplayCommandBroadcast
            {
                Command = GameplayCommand.Contribute,
                TargetId = itemId,
                Quantity = quantity
            }, completed);
        }

        public IEnumerator Build(string buildingType, Vector3 requestedPosition, float yaw,
            Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            return SendRequest(new GameplayCommandBroadcast
            {
                Command = GameplayCommand.Build,
                TargetId = buildingType,
                RequestedPosition = requestedPosition,
                Yaw = yaw
            }, completed);
        }

        public IEnumerator Attack(string enemyId, Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            return SendRequest(new GameplayCommandBroadcast
            {
                Command = GameplayCommand.Attack,
                TargetId = enemyId
            }, completed);
        }

        public IEnumerator ClaimQuestReward(Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            return SendRequest(new GameplayCommandBroadcast { Command = GameplayCommand.ClaimQuestReward }, completed);
        }

        public void Tick(float now, float deltaTime)
        {
            if (_disposed || !_manager.ServerManager.Started)
                return;

            var memberIds = _roomService.InGameMemberIds();
            if (memberIds.Length == 0)
            {
                if (_serverSessionActive)
                {
                    _session = new AuthoritativeGameplaySession();
                    _session.ValidatePlacement = ValidatePlacement;
                    _serverSessionActive = false;
                    _questRequested = false;
                    _nextSnapshotAt = 0f;
                }
                return;
            }

            _serverSessionActive = true;
            if (!_questRequested)
            {
                _questRequested = true;
                _coroutineHost.StartCoroutine(GenerateQuestPresentation());
            }
            var changed = false;
            var activeMembers = new HashSet<int>(memberIds);
            foreach (var existingPlayerId in _session.PlayerIds())
            {
                if (!activeMembers.Contains(existingPlayerId))
                    changed |= _session.RemovePlayer(existingPlayerId);
            }
            foreach (var memberId in memberIds)
            {
                if (!_manager.ServerManager.Clients.TryGetValue(memberId, out var connection) ||
                    !connection.IsActive || connection.FirstObject == null)
                    continue;
                changed |= _session.AddOrUpdatePlayer(memberId, connection.FirstObject.transform.position);
            }
            changed |= _session.Step(now, deltaTime);
            if (changed && now >= _nextSnapshotAt)
            {
                _nextSnapshotAt = now + SnapshotIntervalSeconds;
                BroadcastSnapshots(now);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_manager)
            {
                _manager.ServerManager.UnregisterBroadcast<GameplayCommandBroadcast>(OnServerCommand);
                _manager.ClientManager.UnregisterBroadcast<GameplayResponseBroadcast>(OnClientResponse);
                _manager.ClientManager.UnregisterBroadcast<GameplaySnapshotBroadcast>(OnClientSnapshot);
                _manager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
                _manager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            }
            FailAllPending(GameplayErrorCodes.NotConnected, "玩法同步服务已停止。");
            SetCurrent(GameplaySnapshot.Empty);
        }

        private IEnumerator SendRequest(
            GameplayCommandBroadcast command,
            Action<FrameworkResult<GameplaySnapshot>> completed)
        {
            if (_disposed || !_manager.ClientManager.Started)
            {
                completed?.Invoke(Failure(GameplayErrorCodes.NotConnected, "尚未连接服务器。", true));
                yield break;
            }
            if (_roomService.Current.Phase != RoomPhase.InGame)
            {
                completed?.Invoke(Failure(GameplayErrorCodes.NotInGame, "当前不在联机游戏中。", false));
                yield break;
            }

            command.RequestId = Guid.NewGuid().ToString("N");
            command.ProtocolVersion = _settings.ProtocolVersion;
            var pending = new PendingRequest { Command = command.Command.ToString() };
            _pendingRequests.Add(command.RequestId, pending);
            _manager.ClientManager.Broadcast(command, Channel.Reliable);

            var deadline = Time.realtimeSinceStartup + _settings.RoomRequestTimeoutSeconds;
            while (!pending.Completed && Time.realtimeSinceStartup < deadline && _manager.ClientManager.Started)
                yield return null;

            _pendingRequests.Remove(command.RequestId);
            if (!pending.Completed)
                pending.Result = Failure(GameplayErrorCodes.RequestTimeout, "玩法请求超时，请重试。", true);
            completed?.Invoke(pending.Result);
        }

        private void OnServerCommand(NetworkConnection connection, GameplayCommandBroadcast command, Channel channel)
        {
            if (command.ProtocolVersion != _settings.ProtocolVersion)
            {
                SendFailure(connection, command.RequestId, GameplayErrorCodes.ProtocolMismatch,
                    $"协议版本不一致：客户端 {command.ProtocolVersion}，服务器 {_settings.ProtocolVersion}。");
                return;
            }
            if (connection == null || !connection.IsActive || !connection.IsAuthenticated)
            {
                SendFailure(connection, command.RequestId, GameplayErrorCodes.NotConnected, "连接尚未通过认证。");
                return;
            }
            if (!_roomService.IsInGameMember(connection.ClientId) || connection.FirstObject == null)
            {
                SendFailure(connection, command.RequestId, GameplayErrorCodes.NotInGame, "当前连接不属于进行中的游戏。");
                return;
            }

            var now = Time.realtimeSinceStartup;
            var position = connection.FirstObject.transform.position;
            FrameworkResult<GameplaySnapshot> result;
            switch (command.Command)
            {
                case GameplayCommand.Refresh:
                    result = _session.Refresh(connection.ClientId, command.RequestId, position);
                    break;
                case GameplayCommand.Collect:
                    result = _session.Collect(connection.ClientId, command.RequestId, command.TargetId, position, now);
                    break;
                case GameplayCommand.Build:
                    result = _session.Build(connection.ClientId, command.RequestId, command.TargetId,
                        command.RequestedPosition, command.Yaw, position, now);
                    break;
                case GameplayCommand.Attack:
                    result = _session.Attack(connection.ClientId, command.RequestId, command.TargetId, position, now);
                    break;
                case GameplayCommand.ClaimQuestReward:
                    result = _session.ClaimReward(connection.ClientId, command.RequestId, position, now);
                    break;
                case GameplayCommand.CraftAxe:
                    result = _session.CraftAxe(connection.ClientId, command.RequestId, position, now);
                    break;
                case GameplayCommand.Contribute:
                    result = _session.Contribute(connection.ClientId, command.RequestId, command.TargetId,
                        command.Quantity, position, now);
                    break;
                default:
                    result = Failure(GameplayErrorCodes.InvalidRequest, "未知玩法命令。", false);
                    break;
            }

            if (!result.Succeeded)
            {
                SendFailure(connection, command.RequestId, result.Error.Code, result.Error.Message, result.Error.Retryable);
                return;
            }

            SendSuccess(connection, command.RequestId, result.Value);
            BroadcastSnapshots(now);
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;
            if (_session.RemovePlayer(connection.ClientId))
                BroadcastSnapshots(Time.realtimeSinceStartup);
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped)
                return;
            FailAllPending(GameplayErrorCodes.NotConnected, "与服务器的连接已断开。");
            SetCurrent(GameplaySnapshot.Empty);
        }

        private void BroadcastSnapshots(float now)
        {
            foreach (var memberId in _roomService.InGameMemberIds())
            {
                if (!_manager.ServerManager.Clients.TryGetValue(memberId, out var connection) || !connection.IsActive)
                    continue;
                _manager.ServerManager.Broadcast(connection, new GameplaySnapshotBroadcast
                {
                    Snapshot = ToWire(_session.SnapshotFor(memberId, now))
                });
            }
        }

        private IEnumerator GenerateQuestPresentation()
        {
            var activeSession = _session;
            var gateway = new GatewayQuestClient(_settings);
            FrameworkResult<AiQuestResponse> result = default;
            yield return gateway.Generate(Guid.NewGuid().ToString("N"), "camp_steward",
                "请为双人合作收集木材与石料的营地委托写一句简短说明。", value => result = value);
            if (_disposed || !ReferenceEquals(activeSession, _session) || !_manager.ServerManager.Started)
                yield break;
            if (result.Succeeded)
                _session.SetQuestPresentation(result.Value.Dialogue,
                    result.Value.Source == AiQuestSource.DeepSeek ? "deepseek" : "local-fallback");
            BroadcastSnapshots(Time.realtimeSinceStartup);
        }

        private static bool ValidatePlacement(Vector3 requested, string buildingType)
        {
            var hits = Physics.RaycastAll(requested + Vector3.up * 8f, Vector3.down, 18f,
                ~0, QueryTriggerInteraction.Ignore);
            var surfaceY = float.NegativeInfinity;
            foreach (var hit in hits)
            {
                if (hit.collider.GetComponentInParent<FishNetPlayerAvatar>() != null ||
                    Vector3.Angle(hit.normal, Vector3.up) > 30f ||
                    Mathf.Abs(hit.point.y - requested.y) > 2.5f ||
                    hit.point.y <= surfaceY)
                    continue;
                surfaceY = hit.point.y;
            }
            if (float.IsNegativeInfinity(surfaceY))
                return false;

            var halfExtents = buildingType == GameplayBuildingTypes.Firepit
                ? new Vector3(0.5f, 0.5f, 0.5f)
                : new Vector3(1.15f, 0.7f, 0.2f);
            var overlaps = Physics.OverlapBox(new Vector3(requested.x, surfaceY + halfExtents.y, requested.z),
                halfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            foreach (var collider in overlaps)
            {
                if (collider is TerrainCollider ||
                    collider.GetComponentInParent<FishNetPlayerAvatar>() != null ||
                    collider.bounds.max.y <= surfaceY + 0.15f)
                    continue;
                return false;
            }
            return true;
        }

        private void SendSuccess(NetworkConnection connection, string requestId, GameplaySnapshot snapshot)
        {
            _manager.ServerManager.Broadcast(connection, new GameplayResponseBroadcast
            {
                RequestId = requestId,
                Succeeded = true,
                Snapshot = ToWire(snapshot)
            });
        }

        private void SendFailure(
            NetworkConnection connection,
            string requestId,
            string code,
            string message,
            bool retryable = false)
        {
            if (connection == null || !connection.IsActive)
                return;
            _manager.ServerManager.Broadcast(connection, new GameplayResponseBroadcast
            {
                RequestId = requestId ?? string.Empty,
                Succeeded = false,
                ErrorCode = code,
                ErrorMessage = message,
                Retryable = retryable,
                Snapshot = ToWire(_session.SnapshotFor(connection.ClientId, Time.realtimeSinceStartup))
            });
        }

        private void OnClientResponse(GameplayResponseBroadcast response, Channel channel)
        {
            if (!_pendingRequests.TryGetValue(response.RequestId ?? string.Empty, out var pending))
                return;
            var snapshot = FromWire(response.Snapshot);
            pending.Result = response.Succeeded
                ? FrameworkResult<GameplaySnapshot>.Success(snapshot)
                : Failure(response.ErrorCode, response.ErrorMessage, response.Retryable);
            pending.Completed = true;
            SetCurrent(snapshot);
            _context.Events.Publish(new GameplayCommandCompleted(new GameplayCommandResult
            {
                RequestId = response.RequestId,
                Command = pending.Command,
                Succeeded = response.Succeeded,
                ErrorCode = response.ErrorCode,
                Message = response.ErrorMessage
            }));
        }

        private void OnClientSnapshot(GameplaySnapshotBroadcast broadcast, Channel channel)
        {
            SetCurrent(FromWire(broadcast.Snapshot));
        }

        private void SetCurrent(GameplaySnapshot snapshot)
        {
            Current = snapshot;
            _context.Events.Publish(new GameplaySnapshotChanged(snapshot));
            if (snapshot.TryGetLocalPlayer(out var player))
                _context.Events.Publish(new GameplayInventoryChanged(player.Inventory));
            _context.Events.Publish(new GameplaySharedQuestChanged(snapshot.SharedQuest));
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

        private static GameplaySnapshotWire ToWire(GameplaySnapshot snapshot)
        {
            var players = snapshot.Players ?? Array.Empty<GameplayPlayerSnapshot>();
            var playerWires = new GameplayPlayerWire[players.Length];
            for (var playerIndex = 0; playerIndex < players.Length; playerIndex++)
            {
                var inventory = players[playerIndex].Inventory ?? Array.Empty<GameplayInventoryItemSnapshot>();
                var inventoryWires = new GameplayInventoryItemWire[inventory.Length];
                for (var itemIndex = 0; itemIndex < inventory.Length; itemIndex++)
                {
                    inventoryWires[itemIndex] = new GameplayInventoryItemWire
                    {
                        ItemId = inventory[itemIndex].ItemId,
                        Quantity = inventory[itemIndex].Quantity
                    };
                }
                var quest = players[playerIndex].Quest;
                playerWires[playerIndex] = new GameplayPlayerWire
                {
                    PlayerId = players[playerIndex].PlayerId,
                    Position = players[playerIndex].Position,
                    Inventory = inventoryWires,
                    Quest = new GameplayQuestWire
                    {
                        QuestId = quest.QuestId,
                        CollectedWood = quest.CollectedWood,
                        RequiredWood = quest.RequiredWood,
                        BuiltCampfires = quest.BuiltCampfires,
                        RequiredCampfires = quest.RequiredCampfires,
                        DefeatedEnemies = quest.DefeatedEnemies,
                        RequiredEnemies = quest.RequiredEnemies,
                        RewardClaimed = quest.RewardClaimed
                    }
                };
            }

            var nodes = snapshot.ResourceNodes ?? Array.Empty<GameplayResourceNodeSnapshot>();
            var nodeWires = new GameplayResourceNodeWire[nodes.Length];
            for (var index = 0; index < nodes.Length; index++)
            {
                nodeWires[index] = new GameplayResourceNodeWire
                {
                    NodeId = nodes[index].NodeId,
                    ItemId = nodes[index].ItemId,
                    Position = nodes[index].Position,
                    Remaining = nodes[index].Remaining,
                    Capacity = nodes[index].Capacity,
                    RespawnRemainingSeconds = nodes[index].RespawnRemainingSeconds
                };
            }

            var buildings = snapshot.Buildings ?? Array.Empty<GameplayBuildingSnapshot>();
            var buildingWires = new GameplayBuildingWire[buildings.Length];
            for (var index = 0; index < buildings.Length; index++)
            {
                buildingWires[index] = new GameplayBuildingWire
                {
                    BuildingId = buildings[index].BuildingId,
                    BuildingType = buildings[index].BuildingType,
                    OwnerPlayerId = buildings[index].OwnerPlayerId,
                    Position = buildings[index].Position,
                    Yaw = buildings[index].Yaw
                };
            }

            var enemies = snapshot.Enemies ?? Array.Empty<GameplayEnemySnapshot>();
            var enemyWires = new GameplayEnemyWire[enemies.Length];
            for (var index = 0; index < enemies.Length; index++)
            {
                enemyWires[index] = new GameplayEnemyWire
                {
                    EnemyId = enemies[index].EnemyId,
                    Position = enemies[index].Position,
                    Health = enemies[index].Health,
                    MaximumHealth = enemies[index].MaximumHealth,
                    TargetPlayerId = enemies[index].TargetPlayerId,
                    IsAlive = enemies[index].IsAlive,
                    RespawnRemainingSeconds = enemies[index].RespawnRemainingSeconds
                };
            }

            return new GameplaySnapshotWire
            {
                Revision = snapshot.Revision,
                LocalPlayerId = snapshot.LocalPlayerId,
                Players = playerWires,
                ResourceNodes = nodeWires,
                Buildings = buildingWires,
                Enemies = enemyWires,
                SharedQuest = new GameplaySharedQuestWire
                {
                    QuestId = snapshot.SharedQuest.QuestId,
                    Dialogue = snapshot.SharedQuest.Dialogue,
                    Source = snapshot.SharedQuest.Source,
                    WoodContributed = snapshot.SharedQuest.WoodContributed,
                    WoodRequired = snapshot.SharedQuest.WoodRequired,
                    StoneContributed = snapshot.SharedQuest.StoneContributed,
                    StoneRequired = snapshot.SharedQuest.StoneRequired,
                    ContributorCount = snapshot.SharedQuest.ContributorCount,
                    RequiredContributors = snapshot.SharedQuest.RequiredContributors,
                    LocalRewardClaimed = snapshot.SharedQuest.LocalRewardClaimed
                }
            };
        }

        private static GameplaySnapshot FromWire(GameplaySnapshotWire wire)
        {
            var playerWires = wire.Players ?? Array.Empty<GameplayPlayerWire>();
            var players = new GameplayPlayerSnapshot[playerWires.Length];
            for (var playerIndex = 0; playerIndex < playerWires.Length; playerIndex++)
            {
                var inventoryWires = playerWires[playerIndex].Inventory ?? Array.Empty<GameplayInventoryItemWire>();
                var inventory = new GameplayInventoryItemSnapshot[inventoryWires.Length];
                for (var itemIndex = 0; itemIndex < inventoryWires.Length; itemIndex++)
                {
                    inventory[itemIndex] = new GameplayInventoryItemSnapshot
                    {
                        ItemId = inventoryWires[itemIndex].ItemId ?? string.Empty,
                        Quantity = inventoryWires[itemIndex].Quantity
                    };
                }
                var quest = playerWires[playerIndex].Quest;
                players[playerIndex] = new GameplayPlayerSnapshot
                {
                    PlayerId = playerWires[playerIndex].PlayerId,
                    Position = playerWires[playerIndex].Position,
                    Inventory = inventory,
                    Quest = new GameplayQuestSnapshot
                    {
                        QuestId = quest.QuestId ?? string.Empty,
                        CollectedWood = quest.CollectedWood,
                        RequiredWood = quest.RequiredWood,
                        BuiltCampfires = quest.BuiltCampfires,
                        RequiredCampfires = quest.RequiredCampfires,
                        DefeatedEnemies = quest.DefeatedEnemies,
                        RequiredEnemies = quest.RequiredEnemies,
                        RewardClaimed = quest.RewardClaimed
                    }
                };
            }

            var nodeWires = wire.ResourceNodes ?? Array.Empty<GameplayResourceNodeWire>();
            var nodes = new GameplayResourceNodeSnapshot[nodeWires.Length];
            for (var index = 0; index < nodeWires.Length; index++)
            {
                nodes[index] = new GameplayResourceNodeSnapshot
                {
                    NodeId = nodeWires[index].NodeId ?? string.Empty,
                    ItemId = nodeWires[index].ItemId ?? string.Empty,
                    Position = nodeWires[index].Position,
                    Remaining = nodeWires[index].Remaining,
                    Capacity = nodeWires[index].Capacity,
                    RespawnRemainingSeconds = nodeWires[index].RespawnRemainingSeconds
                };
            }

            var buildingWires = wire.Buildings ?? Array.Empty<GameplayBuildingWire>();
            var buildings = new GameplayBuildingSnapshot[buildingWires.Length];
            for (var index = 0; index < buildingWires.Length; index++)
            {
                buildings[index] = new GameplayBuildingSnapshot
                {
                    BuildingId = buildingWires[index].BuildingId ?? string.Empty,
                    BuildingType = buildingWires[index].BuildingType ?? string.Empty,
                    OwnerPlayerId = buildingWires[index].OwnerPlayerId,
                    Position = buildingWires[index].Position,
                    Yaw = buildingWires[index].Yaw
                };
            }

            var enemyWires = wire.Enemies ?? Array.Empty<GameplayEnemyWire>();
            var enemies = new GameplayEnemySnapshot[enemyWires.Length];
            for (var index = 0; index < enemyWires.Length; index++)
            {
                enemies[index] = new GameplayEnemySnapshot
                {
                    EnemyId = enemyWires[index].EnemyId ?? string.Empty,
                    Position = enemyWires[index].Position,
                    Health = enemyWires[index].Health,
                    MaximumHealth = enemyWires[index].MaximumHealth,
                    TargetPlayerId = enemyWires[index].TargetPlayerId,
                    IsAlive = enemyWires[index].IsAlive,
                    RespawnRemainingSeconds = enemyWires[index].RespawnRemainingSeconds
                };
            }

            return new GameplaySnapshot
            {
                Revision = wire.Revision,
                LocalPlayerId = wire.LocalPlayerId,
                Players = players,
                ResourceNodes = nodes,
                Buildings = buildings,
                Enemies = enemies,
                SharedQuest = new GameplaySharedQuestSnapshot
                {
                    QuestId = wire.SharedQuest.QuestId ?? string.Empty,
                    Dialogue = wire.SharedQuest.Dialogue ?? string.Empty,
                    Source = wire.SharedQuest.Source ?? string.Empty,
                    WoodContributed = wire.SharedQuest.WoodContributed,
                    WoodRequired = wire.SharedQuest.WoodRequired,
                    StoneContributed = wire.SharedQuest.StoneContributed,
                    StoneRequired = wire.SharedQuest.StoneRequired,
                    ContributorCount = wire.SharedQuest.ContributorCount,
                    RequiredContributors = wire.SharedQuest.RequiredContributors,
                    LocalRewardClaimed = wire.SharedQuest.LocalRewardClaimed
                }
            };
        }

        private static FrameworkResult<GameplaySnapshot> Failure(string code, string message, bool retryable)
        {
            return FrameworkResult<GameplaySnapshot>.Failure(new FrameworkError(
                string.IsNullOrEmpty(code) ? GameplayErrorCodes.InvalidRequest : code,
                message ?? "玩法请求失败。", "Gameplay", retryable));
        }
    }
}
