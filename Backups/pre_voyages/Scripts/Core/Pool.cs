using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Simple stack-based object pool. Reuses instances instead of allocating, to keep the
    /// hot paths GC-free (GDD §14.5). Provide a factory; optional get/return hooks reset state.
    /// </summary>
    public sealed class Pool<T> where T : class
    {
        private readonly Stack<T> _free = new Stack<T>();
        private readonly Func<T> _factory;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onReturn;

        public Pool(Func<T> factory, Action<T> onGet = null, Action<T> onReturn = null, int prewarm = 0)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _onGet = onGet;
            _onReturn = onReturn;
            for (int i = 0; i < prewarm; i++) _free.Push(_factory());
        }

        public int CountFree => _free.Count;

        public T Get()
        {
            T item = _free.Count > 0 ? _free.Pop() : _factory();
            _onGet?.Invoke(item);
            return item;
        }

        public void Return(T item)
        {
            if (item == null) return;
            _onReturn?.Invoke(item);
            _free.Push(item);
        }
    }
}
