using System;
using System.Collections;
using Haven.Framework.Core;
using UnityEngine;

namespace Haven.Framework.Resources
{
    public sealed class ResourceLease<TAsset> : IDisposable where TAsset : UnityEngine.Object
    {
        private Action _release;

        public ResourceLease(TAsset asset, Action release)
        {
            Asset = asset ? asset : throw new ArgumentNullException(nameof(asset));
            _release = release;
        }

        public TAsset Asset { get; }
        public bool IsReleased => _release == null;

        public void Dispose()
        {
            var release = _release;
            _release = null;
            release?.Invoke();
        }
    }

    public interface IResourceService
    {
        bool IsReady { get; }
        IEnumerator LoadAsset<TAsset>(string location, Action<FrameworkResult<ResourceLease<TAsset>>> completed)
            where TAsset : UnityEngine.Object;
        IEnumerator LoadRawBytes(string location, Action<FrameworkResult<byte[]>> completed);
        void UnloadUnusedAssets();
    }
}
