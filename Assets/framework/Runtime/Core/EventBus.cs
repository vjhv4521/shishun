using System;
using System.Collections.Generic;

namespace Haven.Framework.Core
{
    public interface IEventBus
    {
        IDisposable Subscribe<TEvent>(Action<TEvent> listener);
        void Publish<TEvent>(TEvent eventData);
        void Clear();
    }

    public sealed class EventBus : IEventBus
    {
        private readonly object _gate = new object();
        private readonly Dictionary<Type, List<Delegate>> _listeners = new Dictionary<Type, List<Delegate>>();

        public IDisposable Subscribe<TEvent>(Action<TEvent> listener)
        {
            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            lock (_gate)
            {
                var type = typeof(TEvent);
                if (!_listeners.TryGetValue(type, out var list))
                {
                    list = new List<Delegate>();
                    _listeners.Add(type, list);
                }
                if (!list.Contains(listener))
                    list.Add(listener);
            }

            return new Subscription<TEvent>(this, listener);
        }

        public void Publish<TEvent>(TEvent eventData)
        {
            Delegate[] snapshot;
            lock (_gate)
            {
                if (!_listeners.TryGetValue(typeof(TEvent), out var list) || list.Count == 0)
                    return;
                snapshot = list.ToArray();
            }

            foreach (var callback in snapshot)
            {
                try
                {
                    ((Action<TEvent>)callback).Invoke(eventData);
                }
                catch (Exception exception)
                {
                    GameLog.Error("EventBus", $"Listener failed for event {typeof(TEvent).FullName}.", "EVENT_LISTENER_FAILED", exception);
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
                _listeners.Clear();
        }

        private void Unsubscribe<TEvent>(Action<TEvent> listener)
        {
            lock (_gate)
            {
                if (!_listeners.TryGetValue(typeof(TEvent), out var list))
                    return;
                list.Remove(listener);
                if (list.Count == 0)
                    _listeners.Remove(typeof(TEvent));
            }
        }

        private sealed class Subscription<TEvent> : IDisposable
        {
            private EventBus _owner;
            private Action<TEvent> _listener;

            public Subscription(EventBus owner, Action<TEvent> listener)
            {
                _owner = owner;
                _listener = listener;
            }

            public void Dispose()
            {
                if (_owner == null)
                    return;
                _owner.Unsubscribe(_listener);
                _owner = null;
                _listener = null;
            }
        }
    }
}
