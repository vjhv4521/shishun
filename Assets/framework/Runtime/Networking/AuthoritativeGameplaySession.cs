using System;
using System.Collections.Generic;
using Haven.Framework.Core;
using Haven.Framework.Services;
using UnityEngine;

namespace Haven.Networking
{
    internal sealed class AuthoritativeGameplaySession
    {
        private const float CollectDistance = 3.25f;
        private const float BuildDistance = 5f;
        private const float AttackDistance = 3.5f;
        private const float BuildingSeparation = 1.5f;
        private const float WorldExtent = 30f;
        private const float CollectCooldown = 0.35f;
        private const float BuildCooldown = 0.5f;
        private const float AttackCooldown = 0.4f;
        private const float ClaimCooldown = 0.5f;
        private const float CraftCooldown = 0.5f;
        private const float ContributeCooldown = 0.2f;
        private const float ResourceRespawnSeconds = 12f;
        private const float EnemyRespawnSeconds = 8f;
        private const float EnemyMoveSpeed = 1.25f;
        private const int RequestHistoryCapacity = 65536;
        private const int QuestWoodRequired = 3;
        private const int QuestStoneRequired = 2;
        private const int RequiredContributors = 2;
        private const int QuestCoinReward = 10;
        private const int MaximumBuildings = 24;

        private sealed class RequestHistory
        {
            private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.Ordinal);

            public bool TryAdd(string requestId)
            {
                if (_ids.Count >= RequestHistoryCapacity)
                    return false;
                if (!_ids.Add(requestId))
                    return false;
                return true;
            }
        }

        private sealed class PlayerState
        {
            public int PlayerId;
            public Vector3 Position;
            public readonly Dictionary<string, int> Inventory = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly RequestHistory Requests = new RequestHistory();
            public int CollectedWood;
            public int BuiltCampfires;
            public int DefeatedEnemies;
            public bool RewardClaimed;
            public float NextCollectAt;
            public float NextBuildAt;
            public float NextAttackAt;
            public float NextClaimAt;
            public float NextCraftAt;
            public float NextContributeAt;
            public int ContributedWood;
            public int ContributedStone;
        }

        private sealed class ResourceNodeState
        {
            public string NodeId;
            public string ItemId;
            public Vector3 Position;
            public int Remaining;
            public int Capacity;
            public float RespawnAt;
        }

        private sealed class BuildingState
        {
            public string BuildingId;
            public string BuildingType;
            public int OwnerPlayerId;
            public Vector3 Position;
            public float Yaw;
        }

        private sealed class EnemyState
        {
            public string EnemyId;
            public Vector3 SpawnPosition;
            public Vector3 Position;
            public int Health;
            public int MaximumHealth;
            public int TargetPlayerId;
            public bool IsAlive;
            public float RespawnAt;
        }

        private readonly Dictionary<int, PlayerState> _players = new Dictionary<int, PlayerState>();
        private readonly List<ResourceNodeState> _resourceNodes = new List<ResourceNodeState>();
        private readonly List<BuildingState> _buildings = new List<BuildingState>();
        private readonly List<EnemyState> _enemies = new List<EnemyState>();
        private readonly HashSet<int> _contributors = new HashSet<int>();
        private int _nextBuildingId = 1;
        private long _revision = 1;
        private int _contributedWood;
        private int _contributedStone;
        private string _questDialogue = "营地需要木材和石料。请与队友合作贡献物资。";
        private string _questSource = "local-fallback";

        public Func<Vector3, string, bool> ValidatePlacement { get; set; }

        public void SetQuestPresentation(string dialogue, string source)
        {
            if (string.IsNullOrWhiteSpace(dialogue) || dialogue.Length > 200)
                return;
            _questDialogue = dialogue.Trim();
            _questSource = source == "deepseek" ? "deepseek" : "local-fallback";
            _revision++;
        }

        public AuthoritativeGameplaySession()
        {
            _resourceNodes.Add(CreateNode("wood_01", GameplayItemIds.Wood, new Vector3(3f, 1f, 0f), 8));
            _resourceNodes.Add(CreateNode("wood_02", GameplayItemIds.Wood, new Vector3(0f, 1f, 3f), 8));
            _resourceNodes.Add(CreateNode("stone_01", GameplayItemIds.Stone, new Vector3(-3f, 1f, 0f), 8));
            _enemies.Add(new EnemyState
            {
                EnemyId = "raider_01",
                SpawnPosition = new Vector3(0f, 1f, 5f),
                Position = new Vector3(0f, 1f, 5f),
                Health = 100,
                MaximumHealth = 100,
                TargetPlayerId = -1,
                IsAlive = true
            });
        }

        public bool AddOrUpdatePlayer(int playerId, Vector3 position)
        {
            if (!_players.TryGetValue(playerId, out var player))
            {
                player = new PlayerState { PlayerId = playerId, Position = position };
                _players.Add(playerId, player);
                _revision++;
                return true;
            }

            if ((player.Position - position).sqrMagnitude <= 0.0001f)
                return false;
            player.Position = position;
            _revision++;
            return true;
        }

        public bool RemovePlayer(int playerId)
        {
            if (!_players.Remove(playerId))
                return false;
            foreach (var enemy in _enemies)
            {
                if (enemy.TargetPlayerId == playerId)
                    enemy.TargetPlayerId = -1;
            }
            _revision++;
            return true;
        }

        public int[] PlayerIds()
        {
            var result = new int[_players.Count];
            _players.Keys.CopyTo(result, 0);
            return result;
        }

        public FrameworkResult<GameplaySnapshot> Refresh(int playerId, string requestId, Vector3 serverPosition)
        {
            var begin = BeginRequest(playerId, requestId, serverPosition, out var player);
            if (!begin.Succeeded)
                return FrameworkResult<GameplaySnapshot>.Failure(begin.Error);
            _revision++;
            return Success(playerId, 0f);
        }

        public FrameworkResult<GameplaySnapshot> Collect(
            int playerId, string requestId, string nodeId, Vector3 serverPosition, float now)
        {
            var begin = BeginRequest(playerId, requestId, serverPosition, out var player);
            if (!begin.Succeeded)
                return FrameworkResult<GameplaySnapshot>.Failure(begin.Error);
            if (now < player.NextCollectAt)
                return Failure(GameplayErrorCodes.RateLimited, "采集操作过快，请稍后重试。", true);

            var node = _resourceNodes.Find(value => string.Equals(value.NodeId, nodeId, StringComparison.Ordinal));
            if (node == null)
                return Failure(GameplayErrorCodes.NotFound, "资源节点不存在。", false);
            if (node.Remaining <= 0)
                return Failure(GameplayErrorCodes.Depleted, "资源节点尚未刷新。", true);
            if (HorizontalDistance(serverPosition, node.Position) > CollectDistance)
                return Failure(GameplayErrorCodes.OutOfRange, "距离资源节点太远。", true);

            player.NextCollectAt = now + CollectCooldown;
            node.Remaining--;
            if (node.Remaining == 0)
                node.RespawnAt = now + ResourceRespawnSeconds;
            AddItem(player, node.ItemId, 1);
            if (node.ItemId == GameplayItemIds.Wood)
                player.CollectedWood++;
            _revision++;
            return Success(playerId, now);
        }

        public FrameworkResult<GameplaySnapshot> CraftAxe(
            int playerId, string requestId, Vector3 serverPosition, float now)
        {
            var begin = BeginRequest(playerId, requestId, serverPosition, out var player);
            if (!begin.Succeeded)
                return FrameworkResult<GameplaySnapshot>.Failure(begin.Error);
            if (now < player.NextCraftAt)
                return Failure(GameplayErrorCodes.RateLimited, "制作操作过快。", true);
            if (GetItem(player, GameplayItemIds.Wood) < 2 || GetItem(player, GameplayItemIds.Stone) < 1)
                return Failure(GameplayErrorCodes.InsufficientItems, "制作斧头需要 2 木和 1 石。", true);

            player.NextCraftAt = now + CraftCooldown;
            AddItem(player, GameplayItemIds.Wood, -2);
            AddItem(player, GameplayItemIds.Stone, -1);
            AddItem(player, GameplayItemIds.Axe, 1);
            _revision++;
            return Success(playerId, now);
        }

        public FrameworkResult<GameplaySnapshot> Contribute(
            int playerId, string requestId, string itemId, int quantity, Vector3 serverPosition, float now)
        {
            var begin = BeginRequest(playerId, requestId, serverPosition, out var player);
            if (!begin.Succeeded)
                return FrameworkResult<GameplaySnapshot>.Failure(begin.Error);
            if (now < player.NextContributeAt)
                return Failure(GameplayErrorCodes.RateLimited, "贡献操作过快。", true);
            if (quantity < 1 || quantity > 10)
                return Failure(GameplayErrorCodes.InvalidQuantity, "单次贡献数量必须在 1 到 10 之间。", false);

            var remaining = itemId == GameplayItemIds.Wood
                ? QuestWoodRequired - _contributedWood
                : itemId == GameplayItemIds.Stone
                    ? QuestStoneRequired - _contributedStone
                    : -1;
            if (remaining <= 0 || quantity > remaining)
                return Failure(GameplayErrorCodes.InvalidQuantity, "该物资的委托需求已满或贡献数量超出剩余需求。", false);
            if (GetItem(player, itemId) < quantity)
                return Failure(GameplayErrorCodes.InsufficientItems, "背包物资不足。", true);
            var willFinishWood = _contributedWood + (itemId == GameplayItemIds.Wood ? quantity : 0) >= QuestWoodRequired;
            var willFinishStone = _contributedStone + (itemId == GameplayItemIds.Stone ? quantity : 0) >= QuestStoneRequired;
            var contributorCountAfter = _contributors.Count + (_contributors.Contains(playerId) ? 0 : 1);
            if (willFinishWood && willFinishStone && contributorCountAfter < RequiredContributors)
                return Failure(GameplayErrorCodes.QuestIncomplete, "共享委托需要另一名玩家共同贡献。", true);

            player.NextContributeAt = now + ContributeCooldown;
            AddItem(player, itemId, -quantity);
            _contributors.Add(playerId);
            if (itemId == GameplayItemIds.Wood)
            {
                player.ContributedWood += quantity;
                _contributedWood += quantity;
            }
            else
            {
                player.ContributedStone += quantity;
                _contributedStone += quantity;
            }
            _revision++;
            return Success(playerId, now);
        }

        public FrameworkResult<GameplaySnapshot> Build(
            int playerId, string requestId, string buildingType, Vector3 requestedPosition, float yaw,
            Vector3 serverPosition, float now)
        {
            var begin = BeginRequest(playerId, requestId, serverPosition, out var player);
            if (!begin.Succeeded)
                return FrameworkResult<GameplaySnapshot>.Failure(begin.Error);
            if (now < player.NextBuildAt)
                return Failure(GameplayErrorCodes.RateLimited, "建造操作过快，请稍后重试。", true);
            buildingType = (buildingType ?? string.Empty).Trim().ToLowerInvariant();
            if (!TryGetRecipe(buildingType, out var woodCost, out var stoneCost))
                return Failure(GameplayErrorCodes.InvalidRequest, "未知建筑类型。", false);
            if (!IsFinite(requestedPosition) || !IsFinite(yaw) ||
                HorizontalDistance(serverPosition, requestedPosition) > BuildDistance)
                return Failure(GameplayErrorCodes.OutOfRange, "建造位置距离玩家太远。", true);
            if (Mathf.Abs(requestedPosition.x) > WorldExtent || Mathf.Abs(requestedPosition.z) > WorldExtent)
                return Failure(GameplayErrorCodes.InvalidPlacement, "建造位置超出服务器允许范围。", false);
            if (_buildings.Count >= MaximumBuildings)
                return Failure(GameplayErrorCodes.InvalidPlacement, "本局建筑已达数量上限。", false);
            foreach (var existing in _buildings)
            {
                if (HorizontalDistance(existing.Position, requestedPosition) < BuildingSeparation)
                    return Failure(GameplayErrorCodes.InvalidPlacement, "该位置与已有建筑重叠。", true);
            }
            if (GetItem(player, GameplayItemIds.Wood) < woodCost || GetItem(player, GameplayItemIds.Stone) < stoneCost)
                return Failure(GameplayErrorCodes.InsufficientItems, "建造材料不足。", true);
            if (ValidatePlacement != null && !ValidatePlacement(requestedPosition, buildingType))
                return Failure(GameplayErrorCodes.InvalidPlacement, "地面坡度过大或建造区域被物体占据。", true);

            player.NextBuildAt = now + BuildCooldown;
            AddItem(player, GameplayItemIds.Wood, -woodCost);
            AddItem(player, GameplayItemIds.Stone, -stoneCost);
            _buildings.Add(new BuildingState
            {
                BuildingId = $"building_{_nextBuildingId++:D4}",
                BuildingType = buildingType,
                OwnerPlayerId = playerId,
                Position = requestedPosition,
                Yaw = Mathf.Repeat(yaw, 360f)
            });
            if (buildingType == GameplayBuildingTypes.Firepit)
                player.BuiltCampfires++;
            _revision++;
            return Success(playerId, now);
        }

        public FrameworkResult<GameplaySnapshot> Attack(
            int playerId, string requestId, string enemyId, Vector3 serverPosition, float now)
        {
            var begin = BeginRequest(playerId, requestId, serverPosition, out var player);
            if (!begin.Succeeded)
                return FrameworkResult<GameplaySnapshot>.Failure(begin.Error);
            if (now < player.NextAttackAt)
                return Failure(GameplayErrorCodes.RateLimited, "攻击操作过快，请稍后重试。", true);

            var enemy = _enemies.Find(value => string.Equals(value.EnemyId, enemyId, StringComparison.Ordinal));
            if (enemy == null)
                return Failure(GameplayErrorCodes.NotFound, "敌人不存在。", false);
            if (!enemy.IsAlive)
                return Failure(GameplayErrorCodes.EnemyDefeated, "敌人已经被击败。", true);
            if (HorizontalDistance(serverPosition, enemy.Position) > AttackDistance)
                return Failure(GameplayErrorCodes.OutOfRange, "距离敌人太远。", true);

            player.NextAttackAt = now + AttackCooldown;
            enemy.Health = Math.Max(0, enemy.Health - 25);
            if (enemy.Health == 0)
            {
                enemy.IsAlive = false;
                enemy.TargetPlayerId = -1;
                enemy.RespawnAt = now + EnemyRespawnSeconds;
                player.DefeatedEnemies++;
            }
            _revision++;
            return Success(playerId, now);
        }

        public FrameworkResult<GameplaySnapshot> ClaimReward(
            int playerId, string requestId, Vector3 serverPosition, float now)
        {
            var begin = BeginRequest(playerId, requestId, serverPosition, out var player);
            if (!begin.Succeeded)
                return FrameworkResult<GameplaySnapshot>.Failure(begin.Error);
            if (now < player.NextClaimAt)
                return Failure(GameplayErrorCodes.RateLimited, "领奖操作过快，请稍后重试。", true);
            if (!IsSharedQuestComplete())
                return Failure(GameplayErrorCodes.QuestIncomplete, "任务目标尚未全部完成。", true);
            if (!_contributors.Contains(playerId))
                return Failure(GameplayErrorCodes.QuestIncomplete, "只有参与贡献的玩家可以领取共享奖励。", false);
            if (player.RewardClaimed)
                return Failure(GameplayErrorCodes.RewardClaimed, "任务奖励已经领取。", false);

            player.NextClaimAt = now + ClaimCooldown;
            player.RewardClaimed = true;
            AddItem(player, GameplayItemIds.Coin, QuestCoinReward);
            _revision++;
            return Success(playerId, now);
        }

        public bool Step(float now, float deltaTime)
        {
            var changed = false;
            foreach (var node in _resourceNodes)
            {
                if (node.Remaining > 0 || node.RespawnAt <= 0f || now < node.RespawnAt)
                    continue;
                node.Remaining = node.Capacity;
                node.RespawnAt = 0f;
                changed = true;
            }

            foreach (var enemy in _enemies)
            {
                if (!enemy.IsAlive)
                {
                    if (enemy.RespawnAt <= 0f || now < enemy.RespawnAt)
                        continue;
                    enemy.IsAlive = true;
                    enemy.Health = enemy.MaximumHealth;
                    enemy.Position = enemy.SpawnPosition;
                    enemy.TargetPlayerId = -1;
                    enemy.RespawnAt = 0f;
                    changed = true;
                    continue;
                }

                var target = FindNearestPlayer(enemy.Position);
                var targetId = target?.PlayerId ?? -1;
                if (enemy.TargetPlayerId != targetId)
                {
                    enemy.TargetPlayerId = targetId;
                    changed = true;
                }
                if (target == null || deltaTime <= 0f)
                    continue;

                var offset = target.Position - enemy.Position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= 1.5f * 1.5f)
                    continue;
                var movement = Vector3.ClampMagnitude(offset, EnemyMoveSpeed * deltaTime);
                enemy.Position += movement;
                changed = true;
            }

            if (changed)
                _revision++;
            return changed;
        }

        public GameplaySnapshot SnapshotFor(int localPlayerId, float now)
        {
            var playerIds = new List<int>(_players.Keys);
            playerIds.Sort();
            var players = new GameplayPlayerSnapshot[playerIds.Count];
            for (var index = 0; index < playerIds.Count; index++)
            {
                var player = _players[playerIds[index]];
                players[index] = new GameplayPlayerSnapshot
                {
                    PlayerId = player.PlayerId,
                    Position = player.Position,
                    Inventory = player.PlayerId == localPlayerId
                        ? new[]
                        {
                            Item(GameplayItemIds.Wood, GetItem(player, GameplayItemIds.Wood)),
                            Item(GameplayItemIds.Stone, GetItem(player, GameplayItemIds.Stone)),
                            Item(GameplayItemIds.Axe, GetItem(player, GameplayItemIds.Axe)),
                            Item(GameplayItemIds.Coin, GetItem(player, GameplayItemIds.Coin))
                        }
                        : Array.Empty<GameplayInventoryItemSnapshot>(),
                    Quest = new GameplayQuestSnapshot
                    {
                        QuestId = "camp_rebuild_01",
                        CollectedWood = player.CollectedWood,
                        RequiredWood = QuestWoodRequired,
                        BuiltCampfires = player.BuiltCampfires,
                        RequiredCampfires = 1,
                        DefeatedEnemies = player.DefeatedEnemies,
                        RequiredEnemies = 1,
                        RewardClaimed = player.PlayerId == localPlayerId && player.RewardClaimed
                    }
                };
            }

            var nodes = new GameplayResourceNodeSnapshot[_resourceNodes.Count];
            for (var index = 0; index < nodes.Length; index++)
            {
                var node = _resourceNodes[index];
                nodes[index] = new GameplayResourceNodeSnapshot
                {
                    NodeId = node.NodeId,
                    ItemId = node.ItemId,
                    Position = node.Position,
                    Remaining = node.Remaining,
                    Capacity = node.Capacity,
                    RespawnRemainingSeconds = node.Remaining > 0 ? 0f : Math.Max(0f, node.RespawnAt - now)
                };
            }

            var buildings = new GameplayBuildingSnapshot[_buildings.Count];
            for (var index = 0; index < buildings.Length; index++)
            {
                var building = _buildings[index];
                buildings[index] = new GameplayBuildingSnapshot
                {
                    BuildingId = building.BuildingId,
                    BuildingType = building.BuildingType,
                    OwnerPlayerId = building.OwnerPlayerId,
                    Position = building.Position,
                    Yaw = building.Yaw
                };
            }

            var enemies = new GameplayEnemySnapshot[_enemies.Count];
            for (var index = 0; index < enemies.Length; index++)
            {
                var enemy = _enemies[index];
                enemies[index] = new GameplayEnemySnapshot
                {
                    EnemyId = enemy.EnemyId,
                    Position = enemy.Position,
                    Health = enemy.Health,
                    MaximumHealth = enemy.MaximumHealth,
                    TargetPlayerId = enemy.TargetPlayerId,
                    IsAlive = enemy.IsAlive,
                    RespawnRemainingSeconds = enemy.IsAlive ? 0f : Math.Max(0f, enemy.RespawnAt - now)
                };
            }

            return new GameplaySnapshot
            {
                Revision = _revision,
                LocalPlayerId = localPlayerId,
                Players = players,
                ResourceNodes = nodes,
                Buildings = buildings,
                Enemies = enemies,
                SharedQuest = new GameplaySharedQuestSnapshot
                {
                    QuestId = "camp_coop_01",
                    Dialogue = _questDialogue,
                    Source = _questSource,
                    WoodContributed = _contributedWood,
                    WoodRequired = QuestWoodRequired,
                    StoneContributed = _contributedStone,
                    StoneRequired = QuestStoneRequired,
                    ContributorCount = ContributorCount(),
                    RequiredContributors = RequiredContributors,
                    LocalRewardClaimed = _players.TryGetValue(localPlayerId, out var local) && local.RewardClaimed
                }
            };
        }

        private FrameworkResult BeginRequest(int playerId, string requestId, Vector3 serverPosition, out PlayerState player)
        {
            AddOrUpdatePlayer(playerId, serverPosition);
            player = _players[playerId];
            requestId = (requestId ?? string.Empty).Trim();
            if (requestId.Length == 0 || requestId.Length > 64)
                return Failure(GameplayErrorCodes.InvalidRequest, "请求标识无效。", false).ToUntyped();
            if (!player.Requests.TryAdd(requestId))
                return Failure(GameplayErrorCodes.DuplicateRequest, "重复请求或本局请求数上限已被服务器拒绝。", false).ToUntyped();
            return FrameworkResult.Success();
        }

        private FrameworkResult<GameplaySnapshot> Success(int playerId, float now)
        {
            return FrameworkResult<GameplaySnapshot>.Success(SnapshotFor(playerId, now));
        }

        private static FrameworkResult<GameplaySnapshot> Failure(string code, string message, bool retryable)
        {
            return FrameworkResult<GameplaySnapshot>.Failure(new FrameworkError(code, message, "GameplayServer", retryable));
        }

        private static ResourceNodeState CreateNode(string nodeId, string itemId, Vector3 position, int capacity)
        {
            return new ResourceNodeState
            {
                NodeId = nodeId,
                ItemId = itemId,
                Position = position,
                Remaining = capacity,
                Capacity = capacity
            };
        }

        private PlayerState FindNearestPlayer(Vector3 position)
        {
            PlayerState nearest = null;
            var bestDistance = float.MaxValue;
            foreach (var player in _players.Values)
            {
                var distance = HorizontalDistance(position, player.Position);
                if (distance >= bestDistance || distance > 20f)
                    continue;
                bestDistance = distance;
                nearest = player;
            }
            return nearest;
        }

        private static bool TryGetRecipe(string buildingType, out int woodCost, out int stoneCost)
        {
            buildingType = (buildingType ?? string.Empty).Trim().ToLowerInvariant();
            switch (buildingType)
            {
                case GameplayBuildingTypes.Firepit:
                    woodCost = 2;
                    stoneCost = 1;
                    return true;
                case GameplayBuildingTypes.WallWood:
                    woodCost = 3;
                    stoneCost = 0;
                    return true;
                default:
                    woodCost = 0;
                    stoneCost = 0;
                    return false;
            }
        }

        private bool IsSharedQuestComplete()
        {
            return _contributedWood >= QuestWoodRequired &&
                   _contributedStone >= QuestStoneRequired &&
                   ContributorCount() >= RequiredContributors;
        }

        private int ContributorCount()
        {
            return _contributors.Count;
        }

        private static void AddItem(PlayerState player, string itemId, int delta)
        {
            var quantity = Math.Max(0, GetItem(player, itemId) + delta);
            player.Inventory[itemId] = quantity;
        }

        private static int GetItem(PlayerState player, string itemId)
        {
            return player.Inventory.TryGetValue(itemId, out var quantity) ? quantity : 0;
        }

        private static GameplayInventoryItemSnapshot Item(string itemId, int quantity)
        {
            return new GameplayInventoryItemSnapshot { ItemId = itemId, Quantity = quantity };
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            left.y = 0f;
            right.y = 0f;
            return Vector3.Distance(left, right);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    internal static class GameplayResultExtensions
    {
        public static FrameworkResult ToUntyped(this FrameworkResult<GameplaySnapshot> result)
        {
            return result.Succeeded ? FrameworkResult.Success() : FrameworkResult.Failure(result.Error);
        }
    }
}
