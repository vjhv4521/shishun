using UnityEngine;

namespace Haven.Framework.Scenes
{
    public sealed class SceneTransitionHandle : CustomYieldInstruction
    {
        public bool IsDone { get; private set; }
        public bool Succeeded => IsDone && string.IsNullOrEmpty(Error);
        public string Error { get; private set; }
        public override bool keepWaiting => !IsDone;

        internal void Complete(string error = null)
        {
            Error = error;
            IsDone = true;
        }
    }

    public static class SceneTransitionService
    {
        public const string LoadingSceneName = "Loading";
        private const float MinimumLoadingScreenSeconds = 0.45f;

        private static SceneTransitionHandle _activeHandle;

        public static bool IsLoading => _activeHandle != null && !_activeHandle.IsDone;
        public static float Progress { get; private set; }
        public static string Status { get; private set; } = "正在准备…";
        public static string TargetScene { get; private set; } = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _activeHandle = null;
            Progress = 0f;
            Status = "正在准备…";
            TargetScene = string.Empty;
        }

        public static SceneTransitionHandle LoadScene(string targetScene)
        {
            var handle = new SceneTransitionHandle();
            var normalizedTarget = targetScene?.Trim();
            if (string.IsNullOrEmpty(normalizedTarget))
                return Fail(handle, "目标场景名称不能为空。");
            if (normalizedTarget == LoadingSceneName)
                return Fail(handle, "Loading 场景不能作为自身的目标场景。");
            if (IsLoading)
                return Fail(handle, $"正在加载 {TargetScene}，请勿重复切换场景。");
            if (!Application.CanStreamedLevelBeLoaded(LoadingSceneName))
                return Fail(handle, $"Build Settings 中缺少加载场景：{LoadingSceneName}");
            if (!Application.CanStreamedLevelBeLoaded(normalizedTarget))
                return Fail(handle, $"Build Settings 中缺少目标场景：{normalizedTarget}");

            _activeHandle = handle;
            TargetScene = normalizedTarget;
            Progress = 0f;
            Status = "正在打开加载界面…";

            var runnerObject = new GameObject("[Haven Scene Transition]");
            UnityEngine.Object.DontDestroyOnLoad(runnerObject);
            runnerObject.AddComponent<SceneTransitionRunner>().Begin(handle, normalizedTarget, MinimumLoadingScreenSeconds);
            return handle;
        }

        internal static void Report(float progress, string status)
        {
            Progress = Mathf.Clamp01(progress);
            if (!string.IsNullOrWhiteSpace(status))
                Status = status;
        }

        internal static void Finish(SceneTransitionHandle handle, string error = null)
        {
            handle.Complete(error);
            if (!object.ReferenceEquals(_activeHandle, handle))
                return;

            if (string.IsNullOrEmpty(error))
            {
                Progress = 1f;
                Status = "加载完成";
            }
            else
            {
                Status = error;
            }
            _activeHandle = null;
        }

        private static SceneTransitionHandle Fail(SceneTransitionHandle handle, string error)
        {
            handle.Complete(error);
            Debug.LogError($"[Haven Scene Transition] {error}");
            return handle;
        }
    }

}
