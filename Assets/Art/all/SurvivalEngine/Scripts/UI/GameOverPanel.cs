using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SurvivalEngine
{
    public class GameOverPanel : UISlotPanel
    {
        private static GameOverPanel _instance;

        protected override void Awake()
        {
            base.Awake();
            _instance = this;
        }

        protected override void Start()
        {
            base.Start();

        }

        protected override void Update()
        {
            base.Update();

        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            if (!PlayerData.IsTransientSession())
                return;

            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                var click = button.onClick;
                for (var index = 0; index < click.GetPersistentEventCount(); index++)
                {
                    if (click.GetPersistentTarget(index) != this)
                        continue;
                    var method = click.GetPersistentMethodName(index);
                    if (method == nameof(OnClickLoad))
                    {
                        var tmpLabel = button.GetComponentInChildren<TMP_Text>(true);
                        if (tmpLabel)
                            tmpLabel.text = "返回主菜单";
                        var uguiLabel = button.GetComponentInChildren<Text>(true);
                        if (uguiLabel)
                            uguiLabel.text = "返回主菜单";
                    }
                    else if (method == nameof(OnClickNew))
                        button.interactable = false;
                }
            }
        }

        public void OnClickLoad()
        {
            if (PlayerData.IsTransientSession())
            {
                PausePanel.Get()?.OnClickQuit();
                return;
            }
            if (PlayerData.HasLastSave())
                StartCoroutine(LoadRoutine());
            else
                StartCoroutine(NewRoutine());
        }

        public void OnClickNew()
        {
            if (PlayerData.IsTransientSession())
            {
                PausePanel.Get()?.OnClickQuit();
                return;
            }
            StartCoroutine(NewRoutine());
        }

        private IEnumerator LoadRoutine()
        {
            BlackPanel.Get().Show();

            yield return new WaitForSeconds(1f);

            TheGame.Load();
        }

        private IEnumerator NewRoutine()
        {
            BlackPanel.Get().Show();

            yield return new WaitForSeconds(1f);

            TheGame.NewGame();
        }

        public static GameOverPanel Get()
        {
            return _instance;
        }
    }

}
