using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Haven.Framework.Scenes
{
    [AddComponentMenu("")]
    public sealed class SceneTransitionRunner : MonoBehaviour
    {
        private SceneTransitionHandle _handle;
        private bool _finished;

        public void Begin(SceneTransitionHandle handle, string targetScene, float minimumDisplaySeconds)
        {
            _handle = handle;
            StartCoroutine(Run(targetScene, minimumDisplaySeconds));
        }

        private IEnumerator Run(string targetScene, float minimumDisplaySeconds)
        {
            Time.timeScale = 1f;
            var loadingOperation = SceneManager.LoadSceneAsync(SceneTransitionService.LoadingSceneName, LoadSceneMode.Single);
            if (loadingOperation == null)
            {
                Finish("无法打开加载场景。");
                yield break;
            }

            while (!loadingOperation.isDone)
            {
                SceneTransitionService.Report(Mathf.Clamp01(loadingOperation.progress / 0.9f) * 0.08f, "正在打开加载界面…");
                yield return null;
            }

            yield return null;
            var visibleSince = Time.realtimeSinceStartup;
            SceneTransitionService.Report(0.1f, $"正在加载 {targetScene}…");

            AsyncOperation targetOperation;
            try
            {
                targetOperation = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
            }
            catch (Exception exception)
            {
                Finish($"加载场景 {targetScene} 时发生异常：{exception.Message}");
                yield break;
            }

            if (targetOperation == null)
            {
                Finish($"无法加载目标场景：{targetScene}");
                yield break;
            }

            targetOperation.allowSceneActivation = false;
            while (targetOperation.progress < 0.9f)
            {
                var normalized = Mathf.Clamp01(targetOperation.progress / 0.9f);
                SceneTransitionService.Report(0.1f + normalized * 0.86f, $"正在加载 {targetScene}…");
                yield return null;
            }

            SceneTransitionService.Report(0.98f, "正在初始化场景…");
            while (Time.realtimeSinceStartup - visibleSince < minimumDisplaySeconds)
                yield return null;

            targetOperation.allowSceneActivation = true;
            while (!targetOperation.isDone)
                yield return null;

            SceneTransitionService.Report(1f, "加载完成");
            yield return null;
            Finish(null);
        }

        private void Finish(string error)
        {
            if (_finished)
                return;
            _finished = true;
            SceneTransitionService.Finish(_handle, error);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (!_finished && _handle != null)
                SceneTransitionService.Finish(_handle, "场景加载流程被意外中止。");
        }
    }
}
