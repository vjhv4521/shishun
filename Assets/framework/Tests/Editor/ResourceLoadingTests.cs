using System;
using System.Collections;
using Haven.Framework.Core;
using Haven.Framework.HotUpdate;
using Haven.Framework.Resources;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Haven.Framework.Tests
{
    public sealed class ResourceLoadingTests
    {
        [UnitySetUp]
        public IEnumerator EnterRuntime() { yield return new EnterPlayMode(); }

        [UnityTearDown]
        public IEnumerator LeaveRuntime() { yield return new ExitPlayMode(); }

        [UnityTest]
        public IEnumerator SimulatedBundleBytesSurviveSafeCoroutineUntilCopiedAndLeaseDisposed()
        {
            var settings = HotUpdateSettings.CreateRuntimeDefault();
            Assert.IsNotNull(settings);
            var serialized = new SerializedObject(settings);
            var playMode = serialized.FindProperty("playMode");
            Assert.IsNotNull(playMode, "Runtime settings must expose the serialized play mode.");
            playMode.enumValueIndex = (int)HotUpdatePlayMode.EditorSimulate;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var owner = new GameObject("Resource loading regression check");
            var context = new FrameworkContext(owner, settings, new ServiceRegistry(), new EventBus());
            var resources = new YooAssetResourceService();
            var update = new YooAssetUpdateService(context, resources, _ => { });
            Exception caught = null;
            yield return SafeCoroutine.Run(update.Run(), exception => caught = exception);
            Assert.IsNull(caught);
            Assert.IsTrue(update.Result.Succeeded, update.Result.Error?.ToString());
            FrameworkResult<byte[]> bytes = default;
            yield return SafeCoroutine.Run(resources.LoadRawBytes("mscorlib.dll", result => bytes = result), exception => caught = exception);
            Assert.IsNull(caught);
            Assert.IsTrue(bytes.Succeeded, bytes.Error?.ToString());
            Assert.AreEqual((byte)'M', bytes.Value[0]);
            Assert.AreEqual((byte)'Z', bytes.Value[1]);
            FrameworkResult<ResourceLease<TextAsset>> asset = default;
            yield return SafeCoroutine.Run(resources.LoadAsset<TextAsset>("CampQuestCatalog", result => asset = result), exception => caught = exception);
            Assert.IsNull(caught);
            Assert.IsTrue(asset.Succeeded, asset.Error?.ToString());
            Assert.That(asset.Value.Asset.text, Does.Contain("fire_preparation_small"));
            asset.Value.Dispose();

            FrameworkResult<ResourceLease<Sprite>> badge = default;
            yield return SafeCoroutine.Run(resources.LoadAsset<Sprite>("HotUpdateDemoBadge", result => badge = result), exception => caught = exception);
            Assert.IsNull(caught);
            Assert.IsTrue(badge.Succeeded, badge.Error?.ToString());
            Assert.Greater(badge.Value.Asset.rect.width, 0f);
            badge.Value.Dispose();
            UnityEngine.Object.Destroy(owner);
            UnityEngine.Object.Destroy(settings);
        }
    }
}
