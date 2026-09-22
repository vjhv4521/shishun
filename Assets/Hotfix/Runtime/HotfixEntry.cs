using System.Collections;
using Haven.Framework.Core;
using Haven.Hotfix.Core;
using Haven.Hotfix.Modules;

namespace Haven.Hotfix
{
    public readonly struct HotfixRuntimeReady
    {
        public HotfixRuntimeReady(string contentVersion)
        {
            ContentVersion = contentVersion ?? string.Empty;
        }

        public string ContentVersion { get; }
    }

    public sealed class HotfixEntry : IHotfixEntry
    {
        private HotfixContext _context;
        private HotfixModuleHost _modules;

        public IEnumerator Initialize(HotfixContext context)
        {
            _context = context;
            _modules = new HotfixModuleHost();

            // Gameplay, Network, AIGC and UI modules are added here as the project grows.
            // Keep this entry and all business modules in Haven.Hotfix so they remain replaceable.
            _modules.Add(new CoreServicesModule());
            _modules.Add(new LobbyModule());
            _modules.Add(new CampQuestModule());

            yield return _modules.Initialize(context);
            context.Events.Publish(new HotfixRuntimeReady(context.ContentVersion));
            GameLog.Info("Hotfix", $"Hotfix runtime ready. contentVersion={context.ContentVersion}", "HOTFIX_READY", context.Framework.CorrelationId);
        }

        public void Tick(float deltaTime)
        {
            _modules?.Tick(deltaTime);
        }

        public void FixedTick(float fixedDeltaTime)
        {
            _modules?.FixedTick(fixedDeltaTime);
        }

        public void LateTick(float deltaTime)
        {
            _modules?.LateTick(deltaTime);
        }

        public void Shutdown()
        {
            _modules?.Shutdown();
            _modules = null;
            _context = null;
        }
    }
}
