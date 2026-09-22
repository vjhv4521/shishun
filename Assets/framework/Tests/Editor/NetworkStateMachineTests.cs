using Haven.Framework.Services;
using Haven.Networking;
using NUnit.Framework;

namespace Haven.Framework.Tests
{
    public sealed class NetworkStateMachineTests
    {
        [Test]
        public void TryTransition_ConnectionLifecycle_ReachesDisconnected()
        {
            var machine = new NetworkStateMachine();

            Assert.AreEqual(NetworkState.Idle, machine.State);
            Assert.IsTrue(machine.TryTransition(NetworkState.Starting));
            Assert.IsTrue(machine.TryTransition(NetworkState.Connecting));
            Assert.IsTrue(machine.TryTransition(NetworkState.Connected));
            Assert.IsTrue(machine.TryTransition(NetworkState.Disconnected));
            Assert.AreEqual(NetworkState.Disconnected, machine.State);
        }

        [Test]
        public void TryTransition_InvalidJump_IsRejected()
        {
            var machine = new NetworkStateMachine();

            Assert.IsFalse(machine.TryTransition(NetworkState.Connected));
            Assert.AreEqual(NetworkState.Idle, machine.State);
        }

        [Test]
        public void TryTransition_Failure_AllowsRetry()
        {
            var machine = new NetworkStateMachine();

            Assert.IsTrue(machine.TryTransition(NetworkState.Starting));
            Assert.IsTrue(machine.TryTransition(NetworkState.Failed));
            Assert.IsTrue(machine.TryTransition(NetworkState.Starting));
        }

        [TestCase(10, 0, 0, 1, true)]
        [TestCase(172, 16, 0, 1, true)]
        [TestCase(172, 31, 255, 254, true)]
        [TestCase(192, 168, 1, 1, true)]
        [TestCase(172, 32, 0, 1, false)]
        [TestCase(8, 8, 8, 8, false)]
        public void IsPrivateIPv4_RecognizesLanRanges(int a, int b, int c, int d, bool expected)
        {
            Assert.AreEqual(expected, LanAddressResolver.IsPrivateIPv4(new[] { (byte)a, (byte)b, (byte)c, (byte)d }));
        }

        [Test]
        public void RoomAdmissionRules_ProtocolMismatch_IsRejected()
        {
            var decision = RoomAdmissionRules.Evaluate(2, 1, 1, 4);

            Assert.IsFalse(decision.Accepted);
            Assert.AreEqual("NETWORK_PROTOCOL_MISMATCH", decision.ErrorCode);
        }

        [Test]
        public void RoomAdmissionRules_FullRoom_IsRejected()
        {
            var decision = RoomAdmissionRules.Evaluate(1, 1, 5, 4);

            Assert.IsFalse(decision.Accepted);
            Assert.AreEqual("NETWORK_SERVER_FULL", decision.ErrorCode);
        }

        [Test]
        public void RoomAdmissionRules_CompatibleRoom_IsAccepted()
        {
            var decision = RoomAdmissionRules.Evaluate(1, 1, 4, 4);

            Assert.IsTrue(decision.Accepted);
            Assert.IsEmpty(decision.ErrorCode);
        }
    }
}
