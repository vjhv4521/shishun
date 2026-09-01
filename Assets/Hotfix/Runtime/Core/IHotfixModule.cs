using System.Collections;
using Haven.Framework.Core;

namespace Haven.Hotfix.Core
{
    public interface IHotfixModule
    {
        string Name { get; }
        int Order { get; }
        IEnumerator Initialize(HotfixContext context);
        void Tick(float deltaTime);
        void FixedTick(float fixedDeltaTime);
        void LateTick(float deltaTime);
        void Shutdown();
    }

    public abstract class HotfixModuleBase : IHotfixModule
    {
        protected HotfixContext Context { get; private set; }

        public abstract string Name { get; }
        public virtual int Order => 0;

        public IEnumerator Initialize(HotfixContext context)
        {
            Context = context;
            return OnInitialize();
        }

        public virtual void Tick(float deltaTime)
        {
        }

        public virtual void FixedTick(float fixedDeltaTime)
        {
        }

        public virtual void LateTick(float deltaTime)
        {
        }

        public void Shutdown()
        {
            try
            {
                OnShutdown();
            }
            finally
            {
                Context = null;
            }
        }

        protected virtual IEnumerator OnInitialize()
        {
            yield break;
        }

        protected virtual void OnShutdown()
        {
        }
    }
}
