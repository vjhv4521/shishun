using System.Collections;
using FishNet.Managing;
using Haven.Framework.Bootstrap;
using Haven.Framework.CampQuests;
using Haven.Framework.Core;
using Haven.Framework.Scenes;
using Haven.Framework.Services;
using Haven.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Haven.Network.PlayModeTests
{
    public sealed class RoomHostPlayModeTests
    {
        [UnityTest]
        [Timeout(30000)]
        public IEnumerator FrameworkDemo_HostAuthenticatesAndStopsCleanly()
        {
            var operation = SceneManager.LoadSceneAsync("FrameworkDemo", LoadSceneMode.Single);
            Assert.IsNotNull(operation);
            while (!operation.isDone)
                yield return null;
            yield return null;

            var bootstrap = GameBootstrap.Instance;
            Assert.IsNotNull(bootstrap);
            bootstrap.RestartForCurrentScene();
            var startupDeadline = Time.realtimeSinceStartup + 15f;
            while (bootstrap.State == BootstrapState.Starting && Time.realtimeSinceStartup < startupDeadline)
                yield return null;
            Assert.AreEqual(BootstrapState.Running, bootstrap.State, bootstrap.LastError?.ToString());
            Assert.IsTrue(bootstrap.Context.Services.TryResolve<INetworkHostService>(out var host));
            Assert.IsTrue(bootstrap.Context.Services.TryResolve<INetworkService>(out var network));

            FrameworkResult result = default;
            var completed = false;
            yield return host.StartHost(new NetworkEndpoint("127.0.0.1", 7770), value =>
            {
                result = value;
                completed = true;
            });

            Assert.IsTrue(completed);
            Assert.IsTrue(result.Succeeded, result.Error?.ToString());
            Assert.IsTrue(host.IsHosting);
            Assert.IsTrue(network.IsConnected);
            Assert.AreEqual(NetworkState.Connected, network.State);
            Assert.GreaterOrEqual(network.ConnectedPeerCount, 1);
            Assert.AreEqual((ushort)7770, host.ShareEndpoint.Port);
            Assert.IsFalse(string.IsNullOrWhiteSpace(host.ShareEndpoint.Host));

            host.StopHost();
            yield return null;
            Assert.AreEqual(NetworkState.Disconnected, network.State);

            bootstrap.PrepareForSceneTransition();
            var manager = Object.FindAnyObjectByType<NetworkManager>();
            if (manager)
                Object.Destroy(manager.gameObject);
            yield return null;
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator MainMenuToWorldGenMap_PreservesCampStewardAndQuestServices()
        {
            yield return LoadScene("MainMenu");

            var bootstrap = GameBootstrap.Instance;
            Assert.IsNotNull(bootstrap, "MainMenu must create the persistent framework bootstrap.");

            var transition = SceneTransitionService.LoadScene("WorldGenMap");
            yield return transition;
            Assert.IsTrue(transition.Succeeded, transition.Error);
            yield return null;

            Assert.IsNotNull(GameObject.Find("[Haven Camp Quests]"),
                "Loading WorldGenMap from MainMenu must not destroy the camp quest root.");
            Assert.IsNotNull(GameObject.Find("CampSteward"),
                "Loading WorldGenMap from MainMenu must preserve the camp steward.");

            bootstrap.RestartForCurrentScene();
            var deadline = Time.realtimeSinceStartup + 20f;
            while (bootstrap.State == BootstrapState.Starting && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.AreEqual(BootstrapState.Running, bootstrap.State, bootstrap.LastError?.ToString());
            Assert.IsTrue(bootstrap.Context.Services.TryResolve<ICampQuestService>(out _),
                "The camp quest service must be installed after entering WorldGenMap from MainMenu.");

            bootstrap.PrepareForSceneTransition();
            yield return LoadScene("MainMenu");
        }

        private static IEnumerator LoadScene(string sceneName)
        {
            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            Assert.IsNotNull(operation, $"Could not load scene: {sceneName}");
            while (!operation.isDone)
                yield return null;
        }
    }
}
