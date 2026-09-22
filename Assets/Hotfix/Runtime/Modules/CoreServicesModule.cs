using System.Collections;
using Haven.Hotfix.Core;
using Haven.Hotfix.Flow;
using Haven.Hotfix.Services;
using Haven.Framework.Services;

namespace Haven.Hotfix.Modules
{
    public sealed class CoreServicesModule : HotfixModuleBase
    {
        private bool _ownsNetworkService;
        private bool _ownsLlmService;
        private bool _ownsRoomService;

        public override string Name => "CoreServices";
        public override int Order => -1000;

        protected override IEnumerator OnInitialize()
        {
            var flow = new GameFlowService(Context.Events);
            Context.Services.Register<IGameFlowService>(flow, true);

            if (!Context.Services.TryResolve<INetworkService>(out _))
            {
                Context.Services.Register<INetworkService>(new OfflineNetworkService());
                _ownsNetworkService = true;
            }
            if (!Context.Services.TryResolve<ILLMService>(out _))
            {
                Context.Services.Register<ILLMService>(new LocalFallbackLlmService());
                _ownsLlmService = true;
            }
            if (!Context.Services.TryResolve<IRoomService>(out _))
            {
                Context.Services.Register<IRoomService>(new OfflineRoomService());
                _ownsRoomService = true;
            }

            flow.TryTransition(GameFlowState.MainMenu);
            yield break;
        }

        protected override void OnShutdown()
        {
            if (_ownsNetworkService && Context.Services.TryResolve<INetworkService>(out var network))
                network.Disconnect();
            if (_ownsLlmService)
                Context.Services.Remove<ILLMService>();
            if (_ownsRoomService)
                Context.Services.Remove<IRoomService>();
            if (_ownsNetworkService)
                Context.Services.Remove<INetworkService>();
            _ownsLlmService = false;
            _ownsRoomService = false;
            _ownsNetworkService = false;
            Context.Services.Remove<IGameFlowService>();
        }
    }
}
