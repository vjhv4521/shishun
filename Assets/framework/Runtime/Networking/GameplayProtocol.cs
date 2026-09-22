using FishNet.Broadcast;
using Haven.Framework.Services;
using UnityEngine;

namespace Haven.Networking
{
    internal enum GameplayCommand : byte
    {
        Refresh,
        Collect,
        Build,
        Attack,
        ClaimQuestReward,
        CraftAxe,
        Contribute
    }

    internal struct GameplayCommandBroadcast : IBroadcast
    {
        public string RequestId;
        public int ProtocolVersion;
        public GameplayCommand Command;
        public string TargetId;
        public Vector3 RequestedPosition;
        public float Yaw;
        public int Quantity;
    }

    internal struct GameplayInventoryItemWire
    {
        public string ItemId;
        public int Quantity;
    }

    internal struct GameplayQuestWire
    {
        public string QuestId;
        public int CollectedWood;
        public int RequiredWood;
        public int BuiltCampfires;
        public int RequiredCampfires;
        public int DefeatedEnemies;
        public int RequiredEnemies;
        public bool RewardClaimed;
    }

    internal struct GameplayPlayerWire
    {
        public int PlayerId;
        public Vector3 Position;
        public GameplayInventoryItemWire[] Inventory;
        public GameplayQuestWire Quest;
    }

    internal struct GameplayResourceNodeWire
    {
        public string NodeId;
        public string ItemId;
        public Vector3 Position;
        public int Remaining;
        public int Capacity;
        public float RespawnRemainingSeconds;
    }

    internal struct GameplayBuildingWire
    {
        public string BuildingId;
        public string BuildingType;
        public int OwnerPlayerId;
        public Vector3 Position;
        public float Yaw;
    }

    internal struct GameplayEnemyWire
    {
        public string EnemyId;
        public Vector3 Position;
        public int Health;
        public int MaximumHealth;
        public int TargetPlayerId;
        public bool IsAlive;
        public float RespawnRemainingSeconds;
    }

    internal struct GameplaySnapshotWire
    {
        public long Revision;
        public int LocalPlayerId;
        public GameplayPlayerWire[] Players;
        public GameplayResourceNodeWire[] ResourceNodes;
        public GameplayBuildingWire[] Buildings;
        public GameplayEnemyWire[] Enemies;
        public GameplaySharedQuestWire SharedQuest;
    }

    internal struct GameplaySharedQuestWire
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
    }

    internal struct GameplayResponseBroadcast : IBroadcast
    {
        public string RequestId;
        public bool Succeeded;
        public string ErrorCode;
        public string ErrorMessage;
        public bool Retryable;
        public GameplaySnapshotWire Snapshot;
    }

    internal struct GameplaySnapshotBroadcast : IBroadcast
    {
        public GameplaySnapshotWire Snapshot;
    }
}
