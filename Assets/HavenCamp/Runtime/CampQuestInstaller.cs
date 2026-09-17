using System;
using System.Collections;
using Haven.Framework.CampQuests;
using Haven.Framework.Composition;
using Haven.Framework.Core;
using UnityEngine;

namespace Haven.Camp
{
    public sealed class CampQuestInstaller : MonoBehaviour, IFrameworkServiceInstaller
    {
        [SerializeField] private Transform steward;
        [SerializeField] private TextAsset catalog;
        private FrameworkContext _context;
        public int Order => 50;

        public IEnumerator Install(FrameworkContext context)
        {
            if (!steward || !catalog) throw new InvalidOperationException("Camp steward or catalog is missing.");
            _context = context;
            context.Services.Register(new CampQuestConfiguration { CatalogJson = catalog.text });
            context.Services.Register<ICampQuestWorldBridge>(new CampQuestWorldBridge(steward));
            context.Services.Register<ICampQuestGenerator>(new CampQuestGatewayClient());
            context.Services.Register<ICampNpcChatGenerator>(new CampNpcChatGatewayClient());
            yield break;
        }

        public void Uninstall()
        {
            if (_context == null) return;
            _context.Services.Remove<ICampNpcChatService>();
            _context.Services.Remove<ICampQuestService>();
            _context.Services.Remove<ICampNpcChatGenerator>();
            _context.Services.Remove<ICampQuestGenerator>();
            _context.Services.Remove<ICampQuestWorldBridge>();
            _context.Services.Remove<CampQuestConfiguration>();
            _context = null;
        }

        private void OnDestroy() { Uninstall(); }
        public void Configure(Transform npc, TextAsset config) { steward = npc; catalog = config; }
    }
}
