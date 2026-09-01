using System;
using System.Collections;
using System.Collections.Generic;
using Haven.Framework.Core;

namespace Haven.Hotfix.Core
{
    public sealed class HotfixModuleHost
    {
        private const string LogModule = "HotfixModules";
        private readonly List<IHotfixModule> _modules = new List<IHotfixModule>();
        private readonly List<IHotfixModule> _initialized = new List<IHotfixModule>();

        public IReadOnlyList<IHotfixModule> Modules => _modules;

        public void Add(IHotfixModule module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));
            if (_modules.Exists(item => item.GetType() == module.GetType()))
                throw new InvalidOperationException($"Hotfix module already exists: {module.GetType().FullName}");
            _modules.Add(module);
        }

        public IEnumerator Initialize(HotfixContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            _modules.Sort((left, right) => left.Order.CompareTo(right.Order));
            foreach (var module in _modules)
            {
                GameLog.Info(LogModule, $"Initializing module '{module.Name}'.", "MODULE_INIT", context.Framework.CorrelationId);
                var routine = module.Initialize(context);
                if (routine != null)
                    yield return routine;
                _initialized.Add(module);
            }
        }

        public void Tick(float deltaTime)
        {
            foreach (var module in _initialized)
                InvokeSafely(module, item => item.Tick(deltaTime), "MODULE_TICK_FAILED");
        }

        public void FixedTick(float fixedDeltaTime)
        {
            foreach (var module in _initialized)
                InvokeSafely(module, item => item.FixedTick(fixedDeltaTime), "MODULE_FIXED_TICK_FAILED");
        }

        public void LateTick(float deltaTime)
        {
            foreach (var module in _initialized)
                InvokeSafely(module, item => item.LateTick(deltaTime), "MODULE_LATE_TICK_FAILED");
        }

        public void Shutdown()
        {
            for (var index = _initialized.Count - 1; index >= 0; index--)
                InvokeSafely(_initialized[index], item => item.Shutdown(), "MODULE_SHUTDOWN_FAILED");
            _initialized.Clear();
            _modules.Clear();
        }

        private static void InvokeSafely(IHotfixModule module, Action<IHotfixModule> action, string errorCode)
        {
            try
            {
                action(module);
            }
            catch (Exception exception)
            {
                GameLog.Error(LogModule, $"Module '{module.Name}' failed.", errorCode, exception);
            }
        }
    }
}
