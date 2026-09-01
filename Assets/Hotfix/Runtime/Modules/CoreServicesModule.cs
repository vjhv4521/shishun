using System.Collections;
using Haven.Hotfix.Core;
using Haven.Hotfix.Flow;
using Haven.Hotfix.Services;

namespace Haven.Hotfix.Modules
{
    public sealed class CoreServicesModule : HotfixModuleBase
    {
        public override string Name => "CoreServices";
        public override int Order => -1000;

        protected override IEnumerator OnInitialize()
        {
            var flow = new GameFlowService(Context.Events);
            Context.Services.Register<IGameFlowService>(flow, true);

            if (!Context.Services.TryResolve<INetworkService>(out _))
                Context.Services.Register<INetworkService>(new OfflineNetworkService());
            if (!Context.Services.TryResolve<ILLMService>(out _))
                Context.Services.Register<ILLMService>(new LocalFallbackLlmService());

            flow.TryTransition(GameFlowState.MainMenu);
            yield break;
        }

        protected override void OnShutdown()
        {
            if (Context.Services.TryResolve<INetworkService>(out var network))
                network.Disconnect();
            Context.Services.Remove<ILLMService>();
            Context.Services.Remove<INetworkService>();
            Context.Services.Remove<IGameFlowService>();
        }
    }
}
