using System;
using System.Collections;
using Haven.Framework.Core;
using UnityEngine;

namespace Haven.Framework.Services
{
    [Serializable]
    public struct GameplayInventoryItemSnapshot
    {
        public string ItemId;
        public int Quantity;
    }

    [Serializable]
    public struct GameplayQuestSnapshot
    {
        public string QuestId;
        public int CollectedWood;
        public int RequiredWood;
        public int BuiltCampfires;
        public int RequiredCampfires;
        public int DefeatedEnemies;
        public int RequiredEnemies;
        public bool RewardClaimed;

        public bool IsComplete =>
            CollectedWood >= RequiredWood &&
            BuiltCampfires >= RequiredCampfires &&
            DefeatedEnemies >= RequiredEnemies;
    }

    [Serializable]
    public struct GameplayPlayerSnapshot
    {
        public int PlayerId;
        public Vector3 Position;
        public GameplayInventoryItemSnapshot[] Inventory;
        public GameplayQuestSnapshot Quest;
    }

    [Serializable]
    public struct GameplaySharedQuestSnapshot
    {
        public string QuestId;
        public string Dialogue;
        public string Source;
        public int WoodContributed;
        public int WoodRequired;
        public int StoneContributed;
        public int StoneRequired;
        public int ContributorCount;
        public int RequiredContributors;
        public bool LocalRewardClaimed;

        public bool IsComplete => WoodContributed >= WoodRequired &&
                                  StoneContributed >= StoneRequired &&
                                  ContributorCount >= RequiredContributors;
    }

    [Serializable]
    public struct GameplayCommandResult
    {
        public string RequestId;
        public string Command;
        public bool Succeeded;
        public string ErrorCode;
        public string Message;
    }

    [Serializable]
    public struct GameplayResourceNodeSnapshot
    {
        public string NodeId;
        public string ItemId;
        public Vector3 Position;
        public int Remaining;
        public int Capacity;
        public float RespawnRemainingSeconds;
    }

    [Serializable]
    public struct GameplayBuildingSnapshot
    {
        public string BuildingId;
        public string BuildingType;
        public int OwnerPlayerId;
        public Vector3 Position;
        public float Yaw;
    }

    [Serializable]
    public struct GameplayEnemySnapshot
    {
        public string EnemyId;
        public Vector3 Position;
        public int Health;
        public int MaximumHealth;
        public int TargetPlayerId;
        public bool IsAlive;
        public float RespawnRemainingSeconds;
    }

    [Serializable]
    public struct GameplaySnapshot
    {
        public long Revision;
        public int LocalPlayerId;
        public GameplayPlayerSnapshot[] Players;
        public GameplayResourceNodeSnapshot[] ResourceNodes;
        public GameplayBuildingSnapshot[] Buildings;
        public GameplayEnemySnapshot[] Enemies;
        public GameplaySharedQuestSnapshot SharedQuest;

        public bool HasState => Revision > 0;

        public static GameplaySnapshot Empty => new GameplaySnapshot
        {
            Revision = 0,
            LocalPlayerId = -1,
            Players = Array.Empty<GameplayPlayerSnapshot>(),
            ResourceNodes = Array.Empty<GameplayResourceNodeSnapshot>(),
            Buildings = Array.Empty<GameplayBuildingSnapshot>(),
            Enemies = Array.Empty<GameplayEnemySnapshot>()
        };

        public bool TryGetLocalPlayer(out GameplayPlayerSnapshot player)
        {
            foreach (var candidate in Players ?? Array.Empty<GameplayPlayerSnapshot>())
            {
                if (candidate.PlayerId != LocalPlayerId)
                    continue;
                player = candidate;
                return true;
            }
            player = default;
            return false;
        }
    }

    public readonly struct GameplaySnapshotChanged
    {
        public GameplaySnapshotChanged(GameplaySnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public GameplaySnapshot Snapshot { get; }
    }

    public readonly struct GameplayInventoryChanged
    {
        public GameplayInventoryChanged(GameplayInventoryItemSnapshot[] items) { Items = items; }
        public GameplayInventoryItemSnapshot[] Items { get; }
    }

    public readonly struct GameplaySharedQuestChanged
    {
        public GameplaySharedQuestChanged(GameplaySharedQuestSnapshot quest) { Quest = quest; }
        public GameplaySharedQuestSnapshot Quest { get; }
    }

    public readonly struct GameplayCommandCompleted
    {
        public GameplayCommandCompleted(GameplayCommandResult result) { Result = result; }
        public GameplayCommandResult Result { get; }
    }

    public static class GameplayItemIds
    {
        public const string Wood = "wood";
        public const string Stone = "stone";
        public const string Coin = "coin";
        public const string Axe = "axe";
    }

    public static class GameplayBuildingTypes
    {
        public const string Firepit = "firepit";
        public const string WallWood = "wall_wood";
    }

    public static class GameplayErrorCodes
    {
        public const string NotConnected = "GAMEPLAY_NOT_CONNECTED";
        public const string NotInGame = "GAMEPLAY_NOT_IN_GAME";
        public const string RequestTimeout = "GAMEPLAY_REQUEST_TIMEOUT";
        public const string InvalidRequest = "GAMEPLAY_INVALID_REQUEST";
        public const string DuplicateRequest = "GAMEPLAY_DUPLICATE_REQUEST";
        public const string RateLimited = "GAMEPLAY_RATE_LIMITED";
        public const string NotFound = "GAMEPLAY_TARGET_NOT_FOUND";
        public const string OutOfRange = "GAMEPLAY_OUT_OF_RANGE";
        public const string Depleted = "GAMEPLAY_RESOURCE_DEPLETED";
        public const string InsufficientItems = "GAMEPLAY_INSUFFICIENT_ITEMS";
        public const string InvalidQuantity = "GAMEPLAY_INVALID_QUANTITY";
        public const string InvalidPlacement = "GAMEPLAY_INVALID_PLACEMENT";
        public const string EnemyDefeated = "GAMEPLAY_ENEMY_DEFEATED";
        public const string QuestIncomplete = "GAMEPLAY_QUEST_INCOMPLETE";
        public const string RewardClaimed = "GAMEPLAY_REWARD_CLAIMED";
        public const string ProtocolMismatch = "GAMEPLAY_PROTOCOL_MISMATCH";
    }

    public interface ICoopGameplayService
    {
        GameplaySnapshot Current { get; }
        IEnumerator Refresh(Action<FrameworkResult<GameplaySnapshot>> completed);
        IEnumerator Collect(string resourceNodeId, Action<FrameworkResult<GameplaySnapshot>> completed);
        IEnumerator CraftAxe(Action<FrameworkResult<GameplaySnapshot>> completed);
        IEnumerator Contribute(string itemId, int quantity, Action<FrameworkResult<GameplaySnapshot>> completed);
        IEnumerator Build(string buildingType, Vector3 requestedPosition, float yaw,
            Action<FrameworkResult<GameplaySnapshot>> completed);
        IEnumerator Attack(string enemyId, Action<FrameworkResult<GameplaySnapshot>> completed);
        IEnumerator ClaimQuestReward(Action<FrameworkResult<GameplaySnapshot>> completed);
    }
}
