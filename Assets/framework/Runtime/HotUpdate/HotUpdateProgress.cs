using Haven.Framework.Core;

namespace Haven.Framework.HotUpdate
{
    public enum HotUpdateStage
    {
        None,
        InitializeFramework,
        InitializePackage,
        RequestVersion,
        LoadManifest,
        CreateDownloader,
        DownloadFiles,
        LoadAotMetadata,
        LoadHotfixAssemblies,
        StartHotfix,
        Completed,
        Failed
    }

    public readonly struct HotUpdateProgress
    {
        public HotUpdateProgress(HotUpdateStage stage, float normalizedProgress, string message, long downloadedBytes = 0, long totalBytes = 0, int downloadedFiles = 0, int totalFiles = 0)
        {
            Stage = stage;
            NormalizedProgress = normalizedProgress < 0f ? 0f : normalizedProgress > 1f ? 1f : normalizedProgress;
            Message = message ?? string.Empty;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            DownloadedFiles = downloadedFiles;
            TotalFiles = totalFiles;
        }

        public HotUpdateStage Stage { get; }
        public float NormalizedProgress { get; }
        public string Message { get; }
        public long DownloadedBytes { get; }
        public long TotalBytes { get; }
        public int DownloadedFiles { get; }
        public int TotalFiles { get; }
    }

    public readonly struct HotUpdateFailed
    {
        public HotUpdateFailed(FrameworkError error)
        {
            Error = error;
        }

        public FrameworkError Error { get; }
    }

    public readonly struct HotUpdateCompleted
    {
        public HotUpdateCompleted(string contentVersion)
        {
            ContentVersion = contentVersion ?? string.Empty;
        }

        public string ContentVersion { get; }
    }
}
