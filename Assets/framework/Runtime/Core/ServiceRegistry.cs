using System;
using System.Collections.Generic;

namespace Haven.Framework.Core
{
    public interface IServiceRegistry
    {
        void Register<TService>(TService instance, bool replace = false) where TService : class;
        bool TryResolve<TService>(out TService service) where TService : class;
        TService Resolve<TService>() where TService : class;
        bool Remove<TService>(bool dispose = true) where TService : class;
        void Clear(bool dispose = true);
    }

    public sealed class ServiceRegistry : IServiceRegistry
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        public void Register<TService>(TService instance, bool replace = false) where TService : class
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            var contract = typeof(TService);
            if (_services.TryGetValue(contract, out var previous))
            {
                if (!replace)
                    throw new InvalidOperationException($"Service is already registered: {contract.FullName}");
                if (!ReferenceEquals(previous, instance) && previous is IDisposable disposable)
                    disposable.Dispose();
            }
            _services[contract] = instance;
        }

        public bool TryResolve<TService>(out TService service) where TService : class
        {
            if (_services.TryGetValue(typeof(TService), out var value))
            {
                service = value as TService;
                return service != null;
            }
            service = null;
            return false;
        }

        public TService Resolve<TService>() where TService : class
        {
            if (TryResolve<TService>(out var service))
                return service;
            throw new KeyNotFoundException($"Service is not registered: {typeof(TService).FullName}");
        }

        public bool Remove<TService>(bool dispose = true) where TService : class
        {
            var contract = typeof(TService);
            if (!_services.TryGetValue(contract, out var value))
                return false;
            _services.Remove(contract);
            if (dispose && value is IDisposable disposable)
                disposable.Dispose();
            return true;
        }

        public void Clear(bool dispose = true)
        {
            if (dispose)
            {
                foreach (var service in _services.Values)
                {
                    if (service is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (Exception exception)
                        {
                            GameLog.Error("ServiceRegistry", $"Failed to dispose {service.GetType().FullName}.", "SERVICE_DISPOSE_FAILED", exception);
                        }
                    }
                }
            }
            _services.Clear();
        }
    }
}
