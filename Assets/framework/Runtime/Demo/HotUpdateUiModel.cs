using Haven.Framework.Bootstrap;
using Haven.Framework.HotUpdate;

namespace Haven.Framework.Demo
{
    internal sealed class HotUpdateUiModel
    {
        private const float CompletionHoldSeconds = 1.5f;

        public HotUpdateProgress Progress { get; private set; }
        public bool DownloadObserved { get; private set; }
        public int DownloadedFiles { get; private set; }
        public int TotalFiles { get; private set; }
        public long DownloadedBytes { get; private set; }
        public long TotalBytes { get; private set; }
        public float CompletionHoldUntil { get; private set; }

        public void Observe(HotUpdateProgress progress, float realtime)
        {
            Progress = progress;
            if ((progress.Stage == HotUpdateStage.CreateDownloader || progress.Stage == HotUpdateStage.DownloadFiles) &&
                (progress.TotalFiles > 0 || progress.TotalBytes > 0))
            {
                DownloadObserved = true;
                DownloadedFiles = progress.DownloadedFiles;
                TotalFiles = progress.TotalFiles;
                DownloadedBytes = progress.DownloadedBytes;
                TotalBytes = progress.TotalBytes;
            }

            if (progress.Stage == HotUpdateStage.Completed && DownloadObserved)
                CompletionHoldUntil = realtime + CompletionHoldSeconds;
        }

        public bool ShouldDraw(BootstrapState state, float realtime)
        {
            return state != BootstrapState.Running || realtime < CompletionHoldUntil;
        }

        public void ResetForRetry()
        {
            Progress = default;
            DownloadObserved = false;
            DownloadedFiles = 0;
            TotalFiles = 0;
            DownloadedBytes = 0;
            TotalBytes = 0;
            CompletionHoldUntil = 0f;
        }
    }
}
