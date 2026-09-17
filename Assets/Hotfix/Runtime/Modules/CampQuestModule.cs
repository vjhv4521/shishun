using System.Collections;
using Haven.Framework.CampQuests;
using Haven.Hotfix.CampQuests;
using Haven.Hotfix.Core;
using UnityEngine;

namespace Haven.Hotfix.Modules
{
    public sealed class CampQuestModule : HotfixModuleBase
    {
        private bool _installed;
        public override string Name => "CampQuests";

        protected override IEnumerator OnInitialize()
        {
            if (!Context.Services.TryResolve<ICampQuestWorldBridge>(out var world)) yield break;
            var config = Context.Services.Resolve<CampQuestConfiguration>();
            var generator = Context.Services.Resolve<ICampQuestGenerator>();
            Context.Services.Register<ICampQuestService>(new CampQuestService(
                JsonUtility.FromJson<CampQuestCatalog>(config.CatalogJson), world, generator, Context.Events));
            if (Context.Services.TryResolve<ICampNpcChatGenerator>(out var chatGenerator))
                Context.Services.Register<ICampNpcChatService>(new CampNpcChatService(
                    Context.Services.Resolve<ICampQuestService>(), chatGenerator));
            _installed = true;
        }

        protected override void OnShutdown()
        {
            if (_installed)
            {
                Context.Services.Remove<ICampNpcChatService>();
                Context.Services.Remove<ICampQuestService>();
            }
            _installed = false;
        }
    }
}
