using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Minimal service registry. Concrete services are registered at bootstrap and resolved
    /// by type/interface elsewhere. Main-thread only (not thread-safe) — that's all a game needs.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();

        public static void Register<T>(T service) where T : class => Services[typeof(T)] = service;

        public static T Get<T>() where T : class
            => Services.TryGetValue(typeof(T), out var s) ? (T)s : null;

        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out var s))
            {
                service = (T)s;
                return true;
            }
            service = null;
            return false;
        }

        public static void Clear() => Services.Clear();
    }
}
