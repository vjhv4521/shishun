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
#if UNITY_SERVER && !UNITY_EDITOR
            yield break;
#else
            var view = Object.FindAnyObjectByType<HavenDemoHud>();
            if (view &&
                Context.Services.TryResolve<INetworkService>(out var network) &&
                Context.Services.TryResolve<IRoomService>(out var rooms) &&
                Context.Services.TryResolve<IGameFlowService>(out var flow))
            {
                _presenter = new LobbyPresenter(view, network, rooms, flow, Context.Events, Context.Resources, Context.ContentVersion);
                yield return _presenter.InitializeHotUpdateDemo();
            }
#endif
        }

        protected override void OnShutdown()
        {
            _presenter?.Dispose();
            _presenter = null;
        }
    }
}
