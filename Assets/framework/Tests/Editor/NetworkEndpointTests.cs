using Haven.Framework.Services;
using NUnit.Framework;

namespace Haven.Framework.Tests
{
    public sealed class NetworkEndpointTests
    {
        [TestCase("127.0.0.1", "127.0.0.1", 7770)]
        [TestCase("192.168.1.20:8888", "192.168.1.20", 8888)]
        [TestCase("localhost:7000", "localhost", 7000)]
        public void TryParse_ValidAddress_ReturnsEndpoint(string value, string expectedHost, int expectedPort)
        {
            Assert.IsTrue(NetworkEndpoint.TryParse(value, 7770, out var endpoint));
            Assert.AreEqual(expectedHost, endpoint.Host);
            Assert.AreEqual((ushort)expectedPort, endpoint.Port);
        }

        [TestCase("")]
        [TestCase("http://127.0.0.1:7770")]
        [TestCase("127.0.0.1:0")]
        [TestCase("127.0.0.1:not-a-port")]
        [TestCase("127.0.0.1/path")]
        public void TryParse_InvalidAddress_ReturnsFalse(string value)
        {
            Assert.IsFalse(NetworkEndpoint.TryParse(value, 7770, out _));
        }
    }
}
