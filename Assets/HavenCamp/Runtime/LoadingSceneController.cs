using System.Collections;
using Haven.Framework.Scenes;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Haven.Camp.UI
{
    [DisallowMultipleComponent]
    public sealed class LoadingSceneController : MonoBehaviour
    {
        [SerializeField] private Image progressFill;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private TMP_Text percentLabel;

        private void Start()
        {
            if (!SceneTransitionService.IsLoading)
                StartCoroutine(ReturnToMenuWhenOpenedDirectly());
        }

        private void Update()
        {
            var progress = SceneTransitionService.Progress;
            if (progressFill)
                progressFill.fillAmount = progress;
            if (statusLabel)
                statusLabel.text = SceneTransitionService.IsLoading ? SceneTransitionService.Status : "正在返回主菜单…";
            if (percentLabel)
                percentLabel.text = $"{Mathf.RoundToInt(progress * 100f)}%";
        }

        private IEnumerator ReturnToMenuWhenOpenedDirectly()
        {
            yield return new WaitForSecondsRealtime(0.35f);
            if (Application.CanStreamedLevelBeLoaded("MainMenu"))
                SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
        }

        public void Configure(Image fill, TMP_Text status, TMP_Text percent)
        {
            progressFill = fill;
            statusLabel = status;
            percentLabel = percent;
        }
    }
}
