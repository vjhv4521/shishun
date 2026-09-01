using System;
using System.Collections;
using Haven.Framework.HotUpdate;
using Haven.Framework.Resources;
using UnityEngine;

namespace Haven.Framework.Core
{
    public sealed class FrameworkContext
    {
        public FrameworkContext(GameObject root, HotUpdateSettings settings, IServiceRegistry services, IEventBus events)
        {
            Root = root ? root : throw new ArgumentNullException(nameof(root));
            Settings = settings ? settings : throw new ArgumentNullException(nameof(settings));
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Events = events ?? throw new ArgumentNullException(nameof(events));
            CorrelationId = Guid.NewGuid().ToString("N");
        }

        public GameObject Root { get; }
        public HotUpdateSettings Settings { get; }
        public IServiceRegistry Services { get; }
        public IEventBus Events { get; }
        public string CorrelationId { get; }
        public IResourceService Resources { get; internal set; }
        public string ContentVersion { get; internal set; }
    }

    public sealed class HotfixContext
    {
        public HotfixContext(FrameworkContext framework)
        {
            Framework = framework ?? throw new ArgumentNullException(nameof(framework));
        }

        public FrameworkContext Framework { get; }
        public GameObject Root => Framework.Root;
        public IServiceRegistry Services => Framework.Services;
        public IEventBus Events => Framework.Events;
        public IResourceService Resources => Framework.Resources;
        public string ContentVersion => Framework.ContentVersion;
    }

    public interface IHotfixEntry
    {
        IEnumerator Initialize(HotfixContext context);
        void Tick(float deltaTime);
        void FixedTick(float fixedDeltaTime);
        void LateTick(float deltaTime);
        void Shutdown();
    }
}
