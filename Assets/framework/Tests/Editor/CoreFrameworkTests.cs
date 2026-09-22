using System;
using System.Collections;
using Haven.Framework.Bootstrap;
using Haven.Framework.Core;
using Haven.Framework.Demo;
using Haven.Framework.HotUpdate;
using Haven.Hotfix.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Haven.Framework.Tests
{
    public sealed class CoreFrameworkTests
    {
        [Test]
        public void EventBus_SubscriptionCanBeDisposed()
        {
            var bus = new EventBus();
            var received = 0;
            var subscription = bus.Subscribe<int>(value => received += value);

            bus.Publish(2);
            subscription.Dispose();
            bus.Publish(3);

            Assert.AreEqual(2, received);
        }

        [Test]
        public void ServiceRegistry_ResolvesAndDisposesServices()
        {
            var registry = new ServiceRegistry();
            var service = new DisposableService();
            registry.Register<IDisposable>(service);

            Assert.AreSame(service, registry.Resolve<IDisposable>());
            registry.Clear();
            Assert.IsTrue(service.Disposed);
        }

        [Test]
        public void ObjectPool_ReusesReturnedInstance()
        {
            var created = 0;
            var pool = new ObjectPool<object>(() =>
            {
                created++;
                return new object();
            });

            var first = pool.Rent();
            pool.Return(first);
            var second = pool.Rent();

            Assert.AreSame(first, second);
            Assert.AreEqual(1, created);
        }

        [Test]
        public void GameFlow_RejectsInvalidAndPublishesValidTransition()
        {
            var bus = new EventBus();
            GameFlowStateChanged last = default;
            bus.Subscribe<GameFlowStateChanged>(value => last = value);
            var flow = new GameFlowService(bus);

            Assert.IsFalse(flow.TryTransition(GameFlowState.InGame));
            Assert.IsTrue(flow.TryTransition(GameFlowState.MainMenu));
            Assert.AreEqual(GameFlowState.Boot, last.Previous);
            Assert.AreEqual(GameFlowState.MainMenu, last.Current);
        }

        [Test]
        public void GameFlow_SupportsRoomLoadingGameAndLobbyReturn()
        {
            var flow = new GameFlowService(new EventBus());

            Assert.IsTrue(flow.TryTransition(GameFlowState.MainMenu));
            Assert.IsTrue(flow.TryTransition(GameFlowState.Lobby));
            Assert.IsTrue(flow.TryTransition(GameFlowState.Room));
            Assert.IsTrue(flow.TryTransition(GameFlowState.LoadingGame));
            Assert.IsTrue(flow.TryTransition(GameFlowState.InGame));
            Assert.IsTrue(flow.TryTransition(GameFlowState.ReturningToLobby));
            Assert.IsTrue(flow.TryTransition(GameFlowState.Lobby));
        }

        [Test]
        public void HotUpdateUiModel_TracksDownloadAndHoldsCompletion()
        {
            var model = new HotUpdateUiModel();
            model.Observe(new HotUpdateProgress(HotUpdateStage.CreateDownloader, 1f, "found", 0, 2048, 0, 2), 10f);
            model.Observe(new HotUpdateProgress(HotUpdateStage.DownloadFiles, 0.5f, "download", 1024, 2048, 1, 2), 10.5f);
            model.Observe(new HotUpdateProgress(HotUpdateStage.Completed, 1f, "done"), 11f);

            Assert.IsTrue(model.DownloadObserved);
            Assert.AreEqual(1, model.DownloadedFiles);
            Assert.AreEqual(2, model.TotalFiles);
            Assert.AreEqual(1024, model.DownloadedBytes);
            Assert.IsTrue(model.ShouldDraw(BootstrapState.Running, 12f));
            Assert.IsFalse(model.ShouldDraw(BootstrapState.Running, 12.6f));

            model.ResetForRetry();
            Assert.IsFalse(model.DownloadObserved);
            Assert.IsTrue(model.ShouldDraw(BootstrapState.Starting, 100f));
        }

        [Test]
        public void HotUpdateUi_MapsRetryableDownloadFailureToChinese()
        {
            var error = new FrameworkError("HU_DOWNLOAD_FAILED", "download failed", "HotUpdate", true);

            Assert.That(HavenDemoHud.ToChineseHotUpdateError(error), Does.Contain("重试"));
            Assert.AreEqual("2.0 KiB", HavenDemoHud.FormatBytes(2048));
        }

        [UnityTest]
        public IEnumerator Bootstrap_EditorDirect_StartsHotfixRuntime()
        {
            yield return new EnterPlayMode();

            var timeout = Time.realtimeSinceStartup + 10f;
            while ((GameBootstrap.Instance == null || GameBootstrap.Instance.State == BootstrapState.Starting) &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            var bootstrap = GameBootstrap.Instance;
            Assert.IsNotNull(bootstrap, "GameBootstrap was not created before the scene loaded.");
            Assert.AreEqual(BootstrapState.Running, bootstrap.State, bootstrap.LastError?.ToString());
            Assert.IsTrue(bootstrap.Context.Services.TryResolve<IGameFlowService>(out var flow));
            Assert.AreEqual(GameFlowState.MainMenu, flow.Current);

            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SafeCoroutine_CapturesNestedExceptions()
        {
            Exception captured = null;

            yield return SafeCoroutine.Run(OuterRoutine(), exception => captured = exception);

            Assert.IsInstanceOf<InvalidOperationException>(captured);
            Assert.AreEqual("nested failure", captured.Message);
        }

        private static IEnumerator OuterRoutine()
        {
            yield return ThrowingRoutine();
        }

        private static IEnumerator ThrowingRoutine()
        {
            yield return null;
            throw new InvalidOperationException("nested failure");
        }

        private sealed class DisposableService : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
