using System;
using System.Collections.Generic;
using Haven.Framework.Core;
using Haven.Framework.Services;

namespace Haven.Networking
{
    internal sealed class RoomSession
    {
        private sealed class Member
        {
            public int Id;
            public string Name;
            public bool IsHost;
            public bool IsReady;
            public long JoinOrder;
        }

        private readonly List<Member> _members = new List<Member>();
        private readonly int _minimumPlayers;
        private long _nextJoinOrder;

        private RoomSession(string roomCode, int maximumPlayers, int minimumPlayers)
        {
            RoomCode = roomCode;
            MaximumPlayers = maximumPlayers;
            _minimumPlayers = minimumPlayers;
            Phase = RoomPhase.Lobby;
        }

        public string RoomCode { get; }
        public int MaximumPlayers { get; }
        public RoomPhase Phase { get; private set; }
        public int Count => _members.Count;
        public bool IsEmpty => _members.Count == 0;

        public static FrameworkResult<RoomSession> Create(string roomCode, int maximumPlayers, int minimumPlayers, int creatorId, string displayName)
        {
            if (!TryNormalizeName(displayName, out var normalized))
                return Failure<RoomSession>(RoomErrorCodes.InvalidName, "昵称必须包含 1–16 个非控制字符。");

            var room = new RoomSession(roomCode, Math.Max(2, maximumPlayers), Math.Max(2, minimumPlayers));
            room._members.Add(new Member
            {
                Id = creatorId,
                Name = normalized,
                IsHost = true,
                IsReady = true,
                JoinOrder = room._nextJoinOrder++
            });
            return FrameworkResult<RoomSession>.Success(room);
        }

        public FrameworkResult<RoomSnapshot> Join(int memberId, string displayName)
        {
            if (Contains(memberId))
                return Failure<RoomSnapshot>(RoomErrorCodes.AlreadyJoined, "该连接已经在房间中。");
            if (Phase == RoomPhase.Loading)
                return Failure<RoomSnapshot>(RoomErrorCodes.InProgress, "房间正在加载场景，请稍后加入。");
            if (_members.Count >= MaximumPlayers)
                return Failure<RoomSnapshot>(RoomErrorCodes.Full, "房间已满。");
            if (!TryNormalizeName(displayName, out var normalized))
                return Failure<RoomSnapshot>(RoomErrorCodes.InvalidName, "昵称必须包含 1–16 个非控制字符。");
            if (_members.Exists(member => string.Equals(member.Name, normalized, StringComparison.OrdinalIgnoreCase)))
                return Failure<RoomSnapshot>(RoomErrorCodes.DuplicateName, "房间内已有相同昵称。");

            _members.Add(new Member
            {
                Id = memberId,
                Name = normalized,
                IsHost = false,
                IsReady = Phase == RoomPhase.InGame,
                JoinOrder = _nextJoinOrder++
            });
            return FrameworkResult<RoomSnapshot>.Success(SnapshotFor(memberId));
        }

        public FrameworkResult<RoomSnapshot> SetReady(int memberId, bool ready)
        {
            var member = Find(memberId);
            if (member == null)
                return Failure<RoomSnapshot>(RoomErrorCodes.NotFound, "你不在当前房间中。");
            if (Phase != RoomPhase.Lobby)
                return Failure<RoomSnapshot>(RoomErrorCodes.InProgress, "游戏加载或进行中，不能修改准备状态。");

            member.IsReady = member.IsHost || ready;
            return FrameworkResult<RoomSnapshot>.Success(SnapshotFor(memberId));
        }

        public FrameworkResult<RoomSnapshot> BeginLoading(int memberId)
        {
            var member = Find(memberId);
            if (member == null)
                return Failure<RoomSnapshot>(RoomErrorCodes.NotFound, "你不在当前房间中。");
            if (!member.IsHost)
                return Failure<RoomSnapshot>(RoomErrorCodes.NotHost, "只有房主可以开始游戏。");
            if (Phase != RoomPhase.Lobby)
                return Failure<RoomSnapshot>(RoomErrorCodes.InProgress, "房间已经开始加载或游戏。");
            if (_members.Count < _minimumPlayers)
                return Failure<RoomSnapshot>(RoomErrorCodes.MinimumPlayers, $"至少需要 {_minimumPlayers} 名玩家才能开始。");
            if (_members.Exists(value => !value.IsHost && !value.IsReady))
                return Failure<RoomSnapshot>(RoomErrorCodes.NotReady, "仍有玩家尚未准备。");

            Phase = RoomPhase.Loading;
            return FrameworkResult<RoomSnapshot>.Success(SnapshotFor(memberId));
        }

        public void CompleteLoading()
        {
            if (Phase == RoomPhase.Loading)
                Phase = RoomPhase.InGame;
        }

        public void RollbackLoading()
        {
            if (Phase != RoomPhase.Loading)
                return;
            Phase = RoomPhase.Lobby;
            foreach (var member in _members)
                member.IsReady = member.IsHost;
        }

        public bool Remove(int memberId)
        {
            var member = Find(memberId);
            if (member == null)
                return false;

            var wasHost = member.IsHost;
            _members.Remove(member);
            if (wasHost && _members.Count > 0)
            {
                var nextHost = _members[0];
                foreach (var candidate in _members)
                {
                    if (candidate.JoinOrder < nextHost.JoinOrder)
                        nextHost = candidate;
                }
                nextHost.IsHost = true;
                nextHost.IsReady = true;
            }
            return true;
        }

        public bool Contains(int memberId)
        {
            return Find(memberId) != null;
        }

        public int[] MemberIds()
        {
            var result = new int[_members.Count];
            for (var index = 0; index < _members.Count; index++)
                result[index] = _members[index].Id;
            return result;
        }

        public RoomSnapshot SnapshotFor(int localMemberId)
        {
            var members = new RoomMemberSnapshot[_members.Count];
            for (var index = 0; index < _members.Count; index++)
            {
                var member = _members[index];
                members[index] = new RoomMemberSnapshot
                {
                    MemberId = member.Id,
                    DisplayName = member.Name,
                    IsHost = member.IsHost,
                    IsReady = member.IsReady
                };
            }

            return new RoomSnapshot
            {
                RoomCode = RoomCode,
                Phase = Phase,
                MaximumPlayers = MaximumPlayers,
                LocalMemberId = localMemberId,
                Members = members
            };
        }

        private Member Find(int memberId)
        {
            return _members.Find(member => member.Id == memberId);
        }

        private static bool TryNormalizeName(string value, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var buffer = new char[Math.Min(16, value.Length)];
            var length = 0;
            foreach (var character in value.Trim())
            {
                if (char.IsControl(character))
                    continue;
                if (length == buffer.Length)
                    break;
                buffer[length++] = character;
            }
            normalized = new string(buffer, 0, length).Trim();
            return normalized.Length > 0;
        }

        private static FrameworkResult<T> Failure<T>(string code, string message)
        {
            return FrameworkResult<T>.Failure(new FrameworkError(code, message, "Room", false));
        }
    }
}
