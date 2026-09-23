using System;
using System.Collections;
using Haven.Framework.Core;
using UnityEngine;

namespace Haven.Framework.Services
{
    public enum SurvivalCommandType : byte
    {
        None = 0,
        MoveTo = 1,
        Interact = 2,
        Attack = 3,
        InventoryMove = 4,
        InventoryAction = 5,
        Craft = 6,
        Build = 7,
        QuestAction = 8
    }

    public enum SurvivalInventoryKind : byte
    {
        None = 0,
        Inventory = 1,
        Equipment = 2,
        Bag = 3
    }

    [Serializable]
    public struct SurvivalCommand
    {
        public string RequestId;
        public SurvivalCommandType Type;
        public string TargetUid;
        public string DataId;
        public string ActionId;
        public SurvivalInventoryKind SourceInventory;
        public SurvivalInventoryKind TargetInventory;
        public int SourceSlot;
        public int TargetSlot;
        public int Quantity;
        public Vector3 Position;
        public Quaternion Rotation;

        public static SurvivalCommand Create(SurvivalCommandType type)
        {
            return new SurvivalCommand
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Type = type,
                SourceSlot = -1,
                TargetSlot = -1,
                Rotation = Quaternion.identity
            };
        }
    }

    [Serializable]
    public struct SurvivalInventorySlotSnapshot
    {
        public SurvivalInventoryKind Inventory;
        public int Slot;
        public string ItemId;
        public int Quantity;
        public float Durability;
        public string ItemUid;
    }

    [Serializable]
    public struct SurvivalAttributeSnapshot
    {
        public int Type;
        public float Value;
    }

    [Serializable]
    public struct SurvivalPublicPlayerSnapshot
    {
        public int PlayerId;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool IsMoving;
        public bool IsBusy;
        public bool IsDead;
        public string EquippedItemId;
    }

    [Serializable]
    public struct SurvivalStringIntSnapshot
    {
        public string Key;
        public int Value;
    }

    [Serializable]
    public struct SurvivalStringFloatSnapshot
    {
        public string Key;
        public float Value;
    }

    [Serializable]
    public struct SurvivalStringSnapshot
    {
        public string Key;
        public string Value;
    }

    [Serializable]
    public struct SurvivalDroppedItemSnapshot
    {
        public string Uid;
        public string ItemId;
        public string Scene;
        public Vector3 Position;
        public int Quantity;
        public float Durability;
    }

    [Serializable]
    public struct SurvivalConstructionSnapshot
    {
        public string Uid;
        public string DataId;
        public string Scene;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Durability;
    }

    [Serializable]
    public struct SurvivalSceneObjectSnapshot
    {
        public string Uid;
        public string Scene;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    [Serializable]
    public struct SurvivalSnapshot
    {
        public int SchemaVersion;
        public long Revision;
        public int LocalPlayerId;
        public int Day;
        public float DayTime;
        public int Gold;
        public SurvivalInventorySlotSnapshot[] PrivateInventory;
        public SurvivalAttributeSnapshot[] PrivateAttributes;
        public SurvivalPublicPlayerSnapshot[] Players;
        public string[] RemovedObjectUids;
        public string[] HiddenObjectUids;
        public SurvivalStringIntSnapshot[] WorldInts;
        public SurvivalStringFloatSnapshot[] WorldFloats;
        public SurvivalStringSnapshot[] WorldStrings;
        public SurvivalDroppedItemSnapshot[] DroppedItems;
        public SurvivalConstructionSnapshot[] Constructions;
        public SurvivalSceneObjectSnapshot[] SceneObjects;

        public bool HasState => SchemaVersion > 0 && Revision > 0;

        public static SurvivalSnapshot Empty => new SurvivalSnapshot
        {
            SchemaVersion = 1,
            LocalPlayerId = -1,
            PrivateInventory = Array.Empty<SurvivalInventorySlotSnapshot>(),
            PrivateAttributes = Array.Empty<SurvivalAttributeSnapshot>(),
            Players = Array.Empty<SurvivalPublicPlayerSnapshot>(),
            RemovedObjectUids = Array.Empty<string>(),
            HiddenObjectUids = Array.Empty<string>(),
            WorldInts = Array.Empty<SurvivalStringIntSnapshot>(),
            WorldFloats = Array.Empty<SurvivalStringFloatSnapshot>(),
            WorldStrings = Array.Empty<SurvivalStringSnapshot>(),
            DroppedItems = Array.Empty<SurvivalDroppedItemSnapshot>(),
            Constructions = Array.Empty<SurvivalConstructionSnapshot>(),
            SceneObjects = Array.Empty<SurvivalSceneObjectSnapshot>()
        };
    }

    public readonly struct SurvivalSnapshotChanged
    {
        public SurvivalSnapshotChanged(SurvivalSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public SurvivalSnapshot Snapshot { get; }
    }

    public readonly struct SurvivalCommandCompleted
    {
        public SurvivalCommandCompleted(string requestId, bool succeeded, string errorCode, string message)
        {
            RequestId = requestId ?? string.Empty;
            Succeeded = succeeded;
            ErrorCode = errorCode ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string RequestId { get; }
        public bool Succeeded { get; }
        public string ErrorCode { get; }
        public string Message { get; }
    }

    public interface ISurvivalCommandRouter
    {
        bool ShouldRouteCommands { get; }
        bool Submit(SurvivalCommand command);
    }

    public interface INetworkPlayerSimulation
    {
        bool IsReady { get; }
        bool IsLocalOwner { get; }
        int PlayerId { get; }
        void InitializeNetworkRole(bool isServer, bool isOwner, int playerId, ISurvivalCommandRouter router);
        Vector3 CaptureWorldMovement();
        void ApplyServerMovement(Vector3 movement);
        SurvivalPublicPlayerSnapshot CapturePublicState();
        void ApplyPublicState(SurvivalPublicPlayerSnapshot state);
        bool ExecuteServerCommand(SurvivalCommand command, out string errorCode, out string message);
        SurvivalSnapshot CaptureSnapshot(long revision);
        void ApplySnapshot(SurvivalSnapshot snapshot);
        void ShutdownNetworkRole();
    }

    public interface ISurvivalSessionService
    {
        SurvivalSnapshot Current { get; }
        bool Submit(SurvivalCommand command);
        IEnumerator Refresh(Action<FrameworkResult<SurvivalSnapshot>> completed);
        void ClearSession();
    }
}
