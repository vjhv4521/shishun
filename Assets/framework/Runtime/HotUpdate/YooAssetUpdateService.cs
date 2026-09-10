using System;
using System.Collections;
using System.Collections.Generic;
using Haven.Framework.Core;
using Haven.Framework.Resources;
using UnityEngine;
using YooAsset;

namespace Haven.Framework.HotUpdate
{
    public sealed class YooAssetUpdateService
    {
        private const string Module = "HotUpdate";
        private readonly FrameworkContext _context;
        private readonly HotUpdateSettings _settings;
        private readonly YooAssetResourceService _resources;
        private readonly Action<HotUpdateProgress> _report;

        public YooAssetUpdateService(FrameworkContext context, YooAssetResourceService resources, Action<HotUpdateProgress> report)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _settings = context.Settings;
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _report = report;
            Result = FrameworkResult.Failure(new FrameworkError("UPDATE_NOT_STARTED", "Update workflow has not started.", Module));
        }

        public FrameworkResult Result { get; private set; }
        public ResourcePackage Package { get; private set; }

        public IEnumerator Run()
        {
            if (_settings.PlayMode == HotUpdatePlayMode.EditorDirect)
            {
#if !UNITY_EDITOR
                Result = Fail("HU_EDITOR_DIRECT_IN_PLAYER", "EditorDirect mode cannot run in a player build. Use Offline or Host mode.", false);
                yield break;
#else
                _context.ContentVersion = "editor-direct";
                Result = FrameworkResult.Success();
                Report(HotUpdateStage.InitializePackage, 1f, "Editor direct mode: content update skipped.");
                yield break;
#endif
            }

            if (!YooAssets.IsInitialized)
                YooAssets.Initialize();

            if (!YooAssets.TryGetPackage(_settings.PackageName, out var package))
                package = YooAssets.CreatePackage(_settings.PackageName);
            Package = package;

            yield return InitializePackage();
            if (!Result.Succeeded)
                yield break;

            _resources.BindPackage(Package);
            _context.Resources = _resources;

            yield return RequestVersion();
            if (!Result.Succeeded)
                yield break;

            yield return LoadManifest();
            if (!Result.Succeeded)
                yield break;

            yield return DownloadFiles();
            if (!Result.Succeeded)
                yield break;

            Result = FrameworkResult.Success();
        }

        private IEnumerator InitializePackage()
        {
            Report(HotUpdateStage.InitializePackage, 0f, $"Initializing YooAsset package '{_settings.PackageName}'.");
            if (Package.InitializeStatus == EOperationStatus.Succeeded)
            {
                Result = FrameworkResult.Success();
                Report(HotUpdateStage.InitializePackage, 1f, "Reusing the initialized YooAsset package.");
                yield break;
            }

            InitializePackageOperation operation = null;

            try
            {
                switch (_settings.PlayMode)
                {
                    case HotUpdatePlayMode.EditorSimulate:
#if UNITY_EDITOR
                        var buildResult = EditorSimulateBuildInvoker.Build(_settings.PackageName, (int)EBundleType.VirtualAssetBundle);
                        var editorOptions = new EditorSimulateModeOptions
                        {
                            EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory)
                        };
                        operation = Package.InitializePackageAsync(editorOptions);
#else
                        Result = Fail("HU_EDITOR_MODE_IN_PLAYER", "EditorSimulate mode cannot run in a player build.", false);
                        yield break;
#endif
                        break;

                    case HotUpdatePlayMode.Offline:
                        var offlineOptions = new OfflinePlayModeOptions
                        {
                            BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
                        };
                        operation = Package.InitializePackageAsync(offlineOptions);
                        break;

                    case HotUpdatePlayMode.Host:
                        var remoteService = new HavenRemoteService(BuildRemoteRoot(_settings.PrimaryHost), BuildRemoteRoot(_settings.FallbackHost));
                        var builtin = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
                        builtin.AddParameter(EFileSystemParameter.CopyBuiltinPackageManifest, true);
                        var cache = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
                        cache.AddParameter(EFileSystemParameter.DownloadMaxConcurrency, _settings.DownloadMaxConcurrency);
                        cache.AddParameter(EFileSystemParameter.DownloadWatchdogTimeout, _settings.RequestTimeoutSeconds);
                        var hostOptions = new HostPlayModeOptions
                        {
                            BuiltinFileSystemParameters = builtin,
                            CacheFileSystemParameters = cache
                        };
                        operation = Package.InitializePackageAsync(hostOptions);
                        break;

                    default:
                        Result = Fail("HU_MODE_UNSUPPORTED", $"Unsupported play mode: {_settings.PlayMode}.", false);
                        yield break;
                }
            }
            catch (Exception exception)
            {
                Result = FrameworkResult.Failure(new FrameworkError("HU_INIT_START_FAILED", "Could not start YooAsset initialization.", Module, true, exception));
                yield break;
            }

            yield return operation;
            if (operation.Status != EOperationStatus.Succeeded)
            {
                Result = Fail("HU_INIT_FAILED", operation.Error, true);
                yield break;
            }

            Result = FrameworkResult.Success();
            Report(HotUpdateStage.InitializePackage, 1f, "YooAsset package initialized.");
        }

        private IEnumerator RequestVersion()
        {
            Report(HotUpdateStage.RequestVersion, 0f, "Requesting package version.");
            for (var attempt = 0; attempt <= _settings.WorkflowRetryCount; attempt++)
            {
                var options = new RequestPackageVersionOptions(true, _settings.RequestTimeoutSeconds);
                var operation = Package.RequestPackageVersionAsync(options);
                yield return operation;
                if (operation.Status == EOperationStatus.Succeeded)
                {
                    _context.ContentVersion = operation.PackageVersion;
                    Result = FrameworkResult.Success();
                    Report(HotUpdateStage.RequestVersion, 1f, $"Package version: {operation.PackageVersion}.");
                    yield break;
                }

                GameLog.Warning(Module, $"Package version request attempt {attempt + 1} failed: {operation.Error}", "HU_VERSION_RETRY", _context.CorrelationId);
                if (attempt < _settings.WorkflowRetryCount && _settings.RetryDelaySeconds > 0f)
                    yield return new WaitForSecondsRealtime(_settings.RetryDelaySeconds);
            }
            Result = Fail("HU_VERSION_FAILED", "Could not request a package version after all retries.", true);
        }

        private IEnumerator LoadManifest()
        {
            Report(HotUpdateStage.LoadManifest, 0f, "Loading package manifest.");
            for (var attempt = 0; attempt <= _settings.WorkflowRetryCount; attempt++)
            {
                var options = new LoadPackageManifestOptions(_context.ContentVersion, _settings.RequestTimeoutSeconds);
                var operation = Package.LoadPackageManifestAsync(options);
                yield return operation;
                if (operation.Status == EOperationStatus.Succeeded)
                {
                    Result = FrameworkResult.Success();
                    Report(HotUpdateStage.LoadManifest, 1f, "Package manifest loaded.");
                    yield break;
                }

                GameLog.Warning(Module, $"Manifest attempt {attempt + 1} failed: {operation.Error}", "HU_MANIFEST_RETRY", _context.CorrelationId);
                if (attempt < _settings.WorkflowRetryCount && _settings.RetryDelaySeconds > 0f)
                    yield return new WaitForSecondsRealtime(_settings.RetryDelaySeconds);
            }
            Result = Fail("HU_MANIFEST_FAILED", "Could not load the package manifest after all retries.", true);
        }

        private IEnumerator DownloadFiles()
        {
            Report(HotUpdateStage.CreateDownloader, 0f, "Calculating content download.");
            var options = new ResourceDownloaderOptions(_settings.DownloadMaxConcurrency, _settings.DownloadFileRetryCount);
            var downloader = Package.CreateResourceDownloader(options);
            if (downloader.TotalDownloadCount == 0)
            {
                Result = FrameworkResult.Success();
                Report(HotUpdateStage.DownloadFiles, 1f, "Content is up to date.");
                yield break;
            }

            Report(HotUpdateStage.CreateDownloader, 1f, $"Downloading {downloader.TotalDownloadCount} files.", 0, downloader.TotalDownloadBytes, 0, downloader.TotalDownloadCount);
            downloader.DownloadProgressChanged += OnDownloadProgress;
            downloader.DownloadError += OnDownloadError;
            downloader.StartDownload();
            yield return downloader;
            downloader.DownloadProgressChanged -= OnDownloadProgress;
            downloader.DownloadError -= OnDownloadError;

            if (downloader.Status != EOperationStatus.Succeeded)
            {
                Result = Fail("HU_DOWNLOAD_FAILED", downloader.Error, true);
                yield break;
            }

            Result = FrameworkResult.Success();
            Report(HotUpdateStage.DownloadFiles, 1f, "Content download completed.", downloader.TotalDownloadBytes, downloader.TotalDownloadBytes, downloader.TotalDownloadCount, downloader.TotalDownloadCount);
        }

        private void OnDownloadProgress(DownloadProgressChangedEventArgs args)
        {
            Report(HotUpdateStage.DownloadFiles, args.Progress, "Downloading content.", args.CurrentDownloadBytes, args.TotalDownloadBytes, args.CurrentDownloadCount, args.TotalDownloadCount);
        }

        private void OnDownloadError(DownloadErrorEventArgs args)
        {
            GameLog.Warning(Module, $"Download failed for '{args.FileName}': {args.ErrorInfo}", "HU_FILE_DOWNLOAD_FAILED", _context.CorrelationId);
        }

        private FrameworkResult Fail(string code, string message, bool retryable)
        {
            return FrameworkResult.Failure(new FrameworkError(code, message, Module, retryable));
        }

        private void Report(HotUpdateStage stage, float progress, string message, long downloadedBytes = 0, long totalBytes = 0, int downloadedFiles = 0, int totalFiles = 0)
        {
            _report?.Invoke(new HotUpdateProgress(stage, progress, message, downloadedBytes, totalBytes, downloadedFiles, totalFiles));
        }

        private string BuildRemoteRoot(string host)
        {
            var root = (host ?? string.Empty).Trim().TrimEnd('/');
            if (!_settings.AppendPlatformAndVersion)
                return root;
            return $"{root}/{GetPlatformFolder()}/{_settings.AppVersion}";
        }

        private static string GetPlatformFolder()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android:
                    return "Android";
                case RuntimePlatform.IPhonePlayer:
                    return "IPhone";
                case RuntimePlatform.WebGLPlayer:
                    return "WebGL";
                case RuntimePlatform.OSXPlayer:
                    return "MacOS";
                case RuntimePlatform.LinuxPlayer:
                    return "Linux";
                default:
                    return "PC";
            }
        }

        private sealed class HavenRemoteService : IRemoteService
        {
            private readonly string _primary;
            private readonly string _fallback;

            public HavenRemoteService(string primary, string fallback)
            {
                _primary = primary;
                _fallback = string.IsNullOrWhiteSpace(fallback) ? primary : fallback;
            }

            public IReadOnlyList<string> GetRemoteUrls(string fileName)
            {
                if (string.Equals(_primary, _fallback, StringComparison.OrdinalIgnoreCase))
                    return new[] { $"{_primary}/{fileName}" };
                return new[]
                {
                    $"{_primary}/{fileName}",
                    $"{_fallback}/{fileName}"
                };
            }
        }
    }
}
