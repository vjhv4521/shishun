using Haven.Framework.Services;
using Haven.Networking;
using NUnit.Framework;

namespace Haven.Framework.Tests
{
    public sealed class RoomSessionTests
    {
        [Test]
        public void Create_MakesCreatorReadyHost()
        {
            var room = CreateRoom();
            var member = room.SnapshotFor(10).Members[0];

            Assert.AreEqual(RoomPhase.Lobby, room.Phase);
            Assert.AreEqual(10, member.MemberId);
            Assert.IsTrue(member.IsHost);
            Assert.IsTrue(member.IsReady);
        }

        [Test]
        public void Join_RejectsDuplicateNameAndFifthMember()
        {
            var room = CreateRoom(4);
            Assert.IsTrue(room.Join(20, "Guest").Succeeded);
            Assert.IsTrue(room.Join(30, "Third").Succeeded);
            Assert.IsTrue(room.Join(40, "Fourth").Succeeded);

            var duplicateRoom = CreateRoom();
            Assert.IsTrue(duplicateRoom.Join(20, "Guest").Succeeded);
            Assert.AreEqual(RoomErrorCodes.DuplicateName, duplicateRoom.Join(30, "guest").Error.Code);
            Assert.AreEqual(RoomErrorCodes.Full, room.Join(50, "Fifth").Error.Code);
        }

        [Test]
        public void Join_RejectsInvalidName()
        {
            var room = CreateRoom();
            var result = room.Join(20, "\u0001\u0002");

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(RoomErrorCodes.InvalidName, result.Error.Code);
        }

        [Test]
        public void Guest_CanReadyAndCancelReady()
        {
            var room = CreateRoom();
            room.Join(20, "Guest");

            Assert.IsTrue(room.SetReady(20, true).Value.Members[1].IsReady);
            Assert.IsFalse(room.SetReady(20, false).Value.Members[1].IsReady);
        }

        [Test]
        public void Start_RejectsNonHostMinimumPlayersAndUnreadyGuest()
        {
            var room = CreateRoom();
            Assert.AreEqual(RoomErrorCodes.MinimumPlayers, room.BeginLoading(10).Error.Code);

            room.Join(20, "Guest");
            Assert.AreEqual(RoomErrorCodes.NotHost, room.BeginLoading(20).Error.Code);
            Assert.AreEqual(RoomErrorCodes.NotReady, room.BeginLoading(10).Error.Code);
        }

        [Test]
        public void Start_SucceedsWhenEveryGuestIsReady()
        {
            var room = CreateRoom();
            room.Join(20, "Guest");
            room.SetReady(20, true);

            var result = room.BeginLoading(10);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(RoomPhase.Loading, room.Phase);
            room.CompleteLoading();
            Assert.AreEqual(RoomPhase.InGame, room.Phase);
        }

        [Test]
        public void HostLeave_TransfersHostByJoinOrder()
        {
            var room = CreateRoom();
            room.Join(20, "First");
            room.Join(30, "Second");

            room.Remove(10);
            var snapshot = room.SnapshotFor(20);

            Assert.IsTrue(snapshot.Members[0].IsHost);
            Assert.AreEqual(20, snapshot.Members[0].MemberId);
            Assert.IsTrue(snapshot.Members[0].IsReady);
        }

        [Test]
        public void LoadingDisconnectRollback_ReturnsLobbyAndClearsGuestReady()
        {
            var room = CreateRoom();
            room.Join(20, "Guest");
            room.Join(30, "Disconnecting");
            room.SetReady(20, true);
            room.SetReady(30, true);
            room.BeginLoading(10);

            room.Remove(30);
            room.RollbackLoading();
            var snapshot = room.SnapshotFor(10);

            Assert.AreEqual(RoomPhase.Lobby, snapshot.Phase);
            Assert.IsFalse(snapshot.Members[1].IsReady);
        }

        [Test]
        public void InGameDisconnect_CanLeaveRoomEmpty()
        {
            var room = CreateRoom();
            room.Join(20, "Guest");
            room.SetReady(20, true);
            room.BeginLoading(10);
            room.CompleteLoading();

            room.Remove(20);
            Assert.AreEqual(RoomPhase.InGame, room.Phase);
            room.Remove(10);
            Assert.IsTrue(room.IsEmpty);
        }

        private static RoomSession CreateRoom(int maximumPlayers = 4)
        {
            var result = RoomSession.Create("123456", maximumPlayers, 2, 10, "Host");
            Assert.IsTrue(result.Succeeded, result.Error?.ToString());
            return result.Value;
        }
    }
}
