using FishNet.Broadcast;
using Haven.Framework.Services;

namespace Haven.Networking
{
    internal enum RoomCommand : byte
    {
        Create,
        Join,
        SetReady,
        StartGame,
        Leave
    }

    internal struct RoomCommandBroadcast : IBroadcast
    {
        public string RequestId;
        public int ProtocolVersion;
        public RoomCommand Command;
        public string DisplayName;
        public string RoomCode;
        public bool Ready;
    }

    internal struct RoomMemberWire
    {
        public int MemberId;
        public string DisplayName;
        public bool IsHost;
        public bool IsReady;
    }

    internal struct RoomSnapshotWire
    {
        public string RoomCode;
        public RoomPhase Phase;
        public int MaximumPlayers;
        public int LocalMemberId;
        public RoomMemberWire[] Members;
    }

    internal struct RoomResponseBroadcast : IBroadcast
    {
        public string RequestId;
        public bool Succeeded;
        public string ErrorCode;
        public string ErrorMessage;
        public bool Retryable;
        public RoomSnapshotWire Snapshot;
    }

    internal struct RoomSnapshotBroadcast : IBroadcast
    {
        public RoomSnapshotWire Snapshot;
    }

    internal struct RoomLoadProgressBroadcast : IBroadcast
    {
        public float Progress;
        public int LoadedMembers;
        public int TotalMembers;
        public string Message;
    }
}
