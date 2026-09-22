using System;
using System.Collections;
using Haven.Framework.Core;

namespace Haven.Framework.Services
{
    public enum RoomPhase
    {
        None,
        Lobby,
        Loading,
        InGame
    }

    [Serializable]
    public struct RoomMemberSnapshot
    {
        public int MemberId;
        public string DisplayName;
        public bool IsHost;
        public bool IsReady;
    }

    [Serializable]
    public struct RoomSnapshot
    {
        public string RoomCode;
        public RoomPhase Phase;
        public int MaximumPlayers;
        public int LocalMemberId;
        public RoomMemberSnapshot[] Members;

        public bool HasRoom => Phase != RoomPhase.None && !string.IsNullOrEmpty(RoomCode);
        public int MemberCount => Members?.Length ?? 0;

        public static RoomSnapshot Empty => new RoomSnapshot
        {
            RoomCode = string.Empty,
            Phase = RoomPhase.None,
            MaximumPlayers = 0,
            LocalMemberId = -1,
            Members = Array.Empty<RoomMemberSnapshot>()
        };
    }

    public readonly struct RoomSnapshotChanged
    {
        public RoomSnapshotChanged(RoomSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public RoomSnapshot Snapshot { get; }
    }

    public readonly struct RoomGameLoadProgressChanged
    {
        public RoomGameLoadProgressChanged(float progress, int loadedMembers, int totalMembers, string message = null)
        {
            Progress = Math.Max(0f, Math.Min(1f, progress));
            LoadedMembers = Math.Max(0, loadedMembers);
            TotalMembers = Math.Max(0, totalMembers);
            Message = message ?? string.Empty;
        }

        public float Progress { get; }
        public int LoadedMembers { get; }
        public int TotalMembers { get; }
        public string Message { get; }
    }

    public static class RoomErrorCodes
    {
        public const string NotFound = "ROOM_NOT_FOUND";
        public const string Full = "ROOM_FULL";
        public const string InProgress = "ROOM_IN_PROGRESS";
        public const string NotHost = "ROOM_NOT_HOST";
        public const string MinimumPlayers = "ROOM_MIN_PLAYERS";
        public const string NotReady = "ROOM_NOT_READY";
        public const string InvalidName = "ROOM_INVALID_NAME";
        public const string RequestTimeout = "ROOM_REQUEST_TIMEOUT";
        public const string NotConnected = "ROOM_NOT_CONNECTED";
        public const string AlreadyJoined = "ROOM_ALREADY_JOINED";
        public const string DuplicateName = "ROOM_DUPLICATE_NAME";
        public const string ProtocolMismatch = "ROOM_PROTOCOL_MISMATCH";
        public const string LoadFailed = "ROOM_LOAD_FAILED";
    }

    public interface IRoomService
    {
        RoomSnapshot Current { get; }
        IEnumerator CreateRoom(string displayName, Action<FrameworkResult<RoomSnapshot>> completed);
        IEnumerator JoinRoom(string roomCode, string displayName, Action<FrameworkResult<RoomSnapshot>> completed);
        IEnumerator SetReady(bool ready, Action<FrameworkResult<RoomSnapshot>> completed);
        IEnumerator StartGame(Action<FrameworkResult<RoomSnapshot>> completed);
        IEnumerator LeaveRoom(Action<FrameworkResult<RoomSnapshot>> completed);
    }
}
