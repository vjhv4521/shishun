using System;
using System.Collections;
using Haven.Framework.Core;
using UnityEngine;
using YooAsset;

namespace Haven.Framework.Resources
{
    public sealed class YooAssetResourceService : IResourceService
    {
        private const string Module = "Resources";
        private ResourcePackage _package;

        public bool IsReady => _package != null && _package.InitializeStatus == EOperationStatus.Succeeded;

        internal void BindPackage(ResourcePackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public IEnumerator LoadAsset<TAsset>(string location, Action<FrameworkResult<ResourceLease<TAsset>>> completed)
            where TAsset : UnityEngine.Object
        {
            if (completed == null)
                throw new ArgumentNullException(nameof(completed));
            if (string.IsNullOrWhiteSpace(location))
            {
                completed(FrameworkResult<ResourceLease<TAsset>>.Failure(
                    new FrameworkError("RES_INVALID_LOCATION", "Resource location is empty.", Module)));
                yield break;
            }

            if (_package == null)
            {
                yield return LoadFromUnityResources(location, completed);
                yield break;
            }

            AssetHandle handle = null;
            try
            {
                handle = _package.LoadAssetAsync<TAsset>(location);
            }
            catch (Exception exception)
            {
                completed(FrameworkResult<ResourceLease<TAsset>>.Failure(
                    new FrameworkError("RES_LOAD_START_FAILED", $"Could not start loading '{location}'.", Module, false, exception)));
                yield break;
            }

            // HandleBase is IEnumerator AND IDisposable. Flattening it through SafeCoroutine
            // would release it before the caller can inspect the result or transfer the lease.
            while (!handle.IsDone) yield return null;
            if (handle.Status != EOperationStatus.Succeeded)
            {
                var error = handle.Error;
                handle.Release();
                completed(FrameworkResult<ResourceLease<TAsset>>.Failure(
                    new FrameworkError("RES_LOAD_FAILED", $"Failed to load '{location}': {error}", Module, true)));
                yield break;
            }

            var asset = handle.GetAssetObject<TAsset>();
            if (!asset)
            {
                handle.Release();
                completed(FrameworkResult<ResourceLease<TAsset>>.Failure(
                    new FrameworkError("RES_TYPE_MISMATCH", $"Resource '{location}' is not {typeof(TAsset).FullName}.", Module)));
                yield break;
            }

            completed(FrameworkResult<ResourceLease<TAsset>>.Success(
                new ResourceLease<TAsset>(asset, handle.Release)));
        }

        public IEnumerator LoadRawBytes(string location, Action<FrameworkResult<byte[]>> completed)
        {
            if (completed == null)
                throw new ArgumentNullException(nameof(completed));
            if (string.IsNullOrWhiteSpace(location))
            {
                completed(FrameworkResult<byte[]>.Failure(
                    new FrameworkError("RAW_INVALID_LOCATION", "Raw file location is empty.", Module)));
                yield break;
            }

            if (_package == null)
            {
                ResourceRequest request = UnityEngine.Resources.LoadAsync<TextAsset>(location);
                yield return request;
                var textAsset = request.asset as TextAsset;
                if (!textAsset)
                {
                    completed(FrameworkResult<byte[]>.Failure(
                        new FrameworkError("RAW_EDITOR_NOT_FOUND", $"Resources TextAsset not found: '{location}'.", Module)));
                    yield break;
                }
                var copy = (byte[])textAsset.bytes.Clone();
                completed(FrameworkResult<byte[]>.Success(copy));
                yield break;
            }

            AssetHandle handle = null;
            try
            {
                // LegacyBuildPipeline stores .bytes as TextAsset inside an AssetBundle,
                // even when PackRawFile gives the bundle a .rawfile extension. Raw pipelines
                // instead return RawFileObject. Support both representations without guessing from names.
                handle = _package.LoadAssetAsync<UnityEngine.Object>(location);
            }
            catch (Exception exception)
            {
                completed(FrameworkResult<byte[]>.Failure(
                    new FrameworkError("RAW_LOAD_START_FAILED", $"Could not start loading '{location}'.", Module, false, exception)));
                yield break;
            }

            while (!handle.IsDone) yield return null;
            if (handle.Status != EOperationStatus.Succeeded)
            {
                var error = handle.Error;
                handle.Release();
                completed(FrameworkResult<byte[]>.Failure(
                    new FrameworkError("RAW_LOAD_FAILED", $"Failed to load '{location}': {error}", Module, true)));
                yield break;
            }

            var rawAsset = handle.GetAssetObject<UnityEngine.Object>();
            var bytes = rawAsset is RawFileObject rawFile ? rawFile.GetBytes() : (rawAsset as TextAsset)?.bytes;
            if (bytes == null)
            {
                handle.Release();
                completed(FrameworkResult<byte[]>.Failure(
                    new FrameworkError("RAW_TYPE_MISMATCH", $"Resource '{location}' is neither TextAsset nor RawFileObject.", Module)));
                yield break;
            }

            var result = bytes == null ? null : (byte[])bytes.Clone();
            handle.Release();
            if (result == null || result.Length == 0)
            {
                completed(FrameworkResult<byte[]>.Failure(
                    new FrameworkError("RAW_EMPTY", $"Raw resource '{location}' is empty.", Module)));
                yield break;
            }
            completed(FrameworkResult<byte[]>.Success(result));
        }

        public void UnloadUnusedAssets()
        {
            _package?.UnloadUnusedAssetsAsync();
        }

        private static IEnumerator LoadFromUnityResources<TAsset>(string location, Action<FrameworkResult<ResourceLease<TAsset>>> completed)
            where TAsset : UnityEngine.Object
        {
            var request = UnityEngine.Resources.LoadAsync<TAsset>(location);
            yield return request;
            var asset = request.asset as TAsset;
            if (!asset)
            {
                completed(FrameworkResult<ResourceLease<TAsset>>.Failure(
                    new FrameworkError("RES_EDITOR_NOT_FOUND", $"Resources asset not found: '{location}'.", Module)));
                yield break;
            }
            completed(FrameworkResult<ResourceLease<TAsset>>.Success(new ResourceLease<TAsset>(asset, null)));
        }
    }
}
