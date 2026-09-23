using System.Collections;
using System.Collections.Generic;
using Haven.Framework.Bootstrap;
using Haven.Framework.Services;
using Haven.Framework.Scenes;
using Haven.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace SurvivalEngine
{
    public class PausePanel : UISlotPanel
    {
        [Header("Pause Panel")]
        public Image speaker_btn;
        public Sprite speaker_on;
        public Sprite speaker_off;

        private static PausePanel _instance;

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

            if(speaker_btn != null)
                speaker_btn.sprite = PlayerData.Get().master_volume > 0.1f ? speaker_on : speaker_off;

        }

        public override void Hide(bool instant = false)
        {
            base.Hide(instant);

        }

        public void OnClickSave()
        {
            if (PlayerData.IsTransientSession())
                return;
            TheGame.Get().Save();
        }

        public void OnClickLoad()
        {
            if (PlayerData.IsTransientSession())
                return;
            if (PlayerData.HasLastSave())
                StartCoroutine(LoadRoutine());
            else
                StartCoroutine(NewRoutine());
        }

        public void OnClickNew()
        {
            if (PlayerData.IsTransientSession())
                return;
            StartCoroutine(NewRoutine());
        }

        public void OnClickQuit()
        {
            StartCoroutine(QuitRoutine());
        }

        private IEnumerator QuitRoutine()
        {
            var settings = Resources.Load<HavenNetworkSettings>(HavenNetworkSettings.DefaultResourceName);
            var menuScene = settings != null ? settings.MenuSceneName : "MainMenu";
            if (!Application.CanStreamedLevelBeLoaded(menuScene))
            {
                Debug.LogError($"[Haven] Main menu scene is not available in Build Settings: {menuScene}");
                yield break;
            }

            var game = TheGame.Get();
            if (game != null)
                game.Unpause();

            var context = GameBootstrap.Instance?.Context;
            if (context != null && context.Services.TryResolve<ISurvivalSessionService>(out var survival))
                survival.ClearSession();
            if (context != null && context.Services.TryResolve<IRoomService>(out var room) && room.Current.HasRoom)
            {
                bool completed = false;
                yield return room.LeaveRoom(_ => completed = true);
                if (!completed)
                    Debug.LogWarning("[Haven] Leave-room request ended without a response; disconnecting anyway.");
            }
            if (context != null && context.Services.TryResolve<INetworkHostService>(out var host) && host.IsHosting)
                host.StopHost();
            else if (context != null && context.Services.TryResolve<INetworkService>(out var network))
                network.Disconnect();

            PlayerData.EndTransientSession();
            GameBootstrap.Instance?.PrepareForSceneTransition();
            var transition = SceneTransitionService.LoadScene(menuScene);
            if (!transition.Succeeded && transition.IsDone)
                Debug.LogError($"[Haven] Could not return to the main menu: {transition.Error}");
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

        public void OnClickMusicToggle()
        {
            PlayerData.Get().master_volume = PlayerData.Get().master_volume > 0.1f ? 0f : 1f;
            TheAudio.Get().RefreshVolume();
        }

        public static PausePanel Get()
        {
            return _instance;
        }
    }

}
