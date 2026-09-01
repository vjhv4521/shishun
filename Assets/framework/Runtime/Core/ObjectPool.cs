using System;
using System.Collections.Generic;

namespace Haven.Framework.Core
{
    public interface IObjectPool<T> where T : class
    {
        int InactiveCount { get; }
        T Rent();
        void Return(T item);
        void Clear();
    }

    public sealed class ObjectPool<T> : IObjectPool<T> where T : class
    {
        private readonly Stack<T> _items;
        private readonly Func<T> _factory;
        private readonly Action<T> _onRent;
        private readonly Action<T> _onReturn;
        private readonly Action<T> _onDestroy;
        private readonly int _maxSize;

        public ObjectPool(Func<T> factory, Action<T> onRent = null, Action<T> onReturn = null, Action<T> onDestroy = null, int initialCapacity = 0, int maxSize = 128)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (initialCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            if (maxSize <= 0 || initialCapacity > maxSize)
                throw new ArgumentOutOfRangeException(nameof(maxSize));

            _onRent = onRent;
            _onReturn = onReturn;
            _onDestroy = onDestroy;
            _maxSize = maxSize;
            _items = new Stack<T>(initialCapacity);
            for (var i = 0; i < initialCapacity; i++)
                _items.Push(_factory());
        }

        public int InactiveCount => _items.Count;

        public T Rent()
        {
            var item = _items.Count > 0 ? _items.Pop() : _factory();
            _onRent?.Invoke(item);
            return item;
        }

        public void Return(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            _onReturn?.Invoke(item);
            if (_items.Count < _maxSize)
                _items.Push(item);
            else
                _onDestroy?.Invoke(item);
        }

        public void Clear()
        {
            if (_onDestroy != null)
            {
                foreach (var item in _items)
                    _onDestroy(item);
            }
            _items.Clear();
        }
    }
}
