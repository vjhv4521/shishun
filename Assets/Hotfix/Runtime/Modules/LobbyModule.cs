using System.Collections;
using Haven.Framework.Demo;
using Haven.Framework.Services;
using Haven.Hotfix.Core;
using Haven.Hotfix.Flow;
using Haven.Hotfix.Lobby;
using UnityEngine;

namespace Haven.Hotfix.Modules
{
    public sealed class LobbyModule : HotfixModuleBase
    {
        private LobbyPresenter _presenter;

        public override string Name => "Lobby";
        public override int Order => -500;

        protected override IEnumerator OnInitialize()
        {
#if (UNITY_SERVER || HAVEN_SERVER_BUILD) && !UNITY_EDITOR
            yield break;
#else
            if (TryBindPresenter(out var view))
                yield return _presenter.InitializeHotUpdateDemo();
#endif
        }

        public override void Tick(float deltaTime)
        {
#if !(UNITY_SERVER || HAVEN_SERVER_BUILD) || UNITY_EDITOR
            // During MainMenu -> FrameworkDemo scene transitions the hotfix entry can
            // initialize before the HUD becomes discoverable. Keep retrying until the
            // scene view and its AOT services are ready instead of leaving a dead UI.
            if (_presenter == null && TryBindPresenter(out var view))
                view.Run(_presenter.InitializeHotUpdateDemo());
#endif
        }

        private bool TryBindPresenter(out HavenDemoHud view)
        {
            view = Object.FindAnyObjectByType<HavenDemoHud>();
            if (!view ||
                !Context.Services.TryResolve<INetworkService>(out var network) ||
                !Context.Services.TryResolve<IRoomService>(out var rooms) ||
                !Context.Services.TryResolve<IGameFlowService>(out var flow))
            {
                return false;
            }

            _presenter = new LobbyPresenter(
                view,
                network,
                rooms,
                flow,
                Context.Events,
                Context.Resources,
                Context.ContentVersion);
            return true;
        }

        protected override void OnShutdown()
        {
            _presenter?.Dispose();
            _presenter = null;
        }
    }
}
