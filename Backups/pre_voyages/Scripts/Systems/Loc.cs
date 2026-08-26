using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Shorthand for <see cref="LocalizationService"/>, because looking a line up is something seventy
    /// call sites do and none of them should need a cached field and a null check to do it.
    ///
    /// Deliberately does not cache the service: <see cref="ServiceLocator"/> resolves through a dictionary,
    /// which is cheaper than the risk of a stale static surviving a domain reload with the old table in it.
    /// Never used per-frame — screens localise on enable and on language change, not in Update.
    /// </summary>
    public static class Loc
    {
        /// <summary>The line for <paramref name="key"/>, or the key itself when there is no table.</summary>
        public static string T(string key)
        {
            var s = ServiceLocator.Get<LocalizationService>();
            return s != null ? s.Get(key) : key;
        }

        /// <summary>
        /// The line for an id the gameplay layer owns — a station, an axis, an unlock, an island.
        /// Those stay English in the simulation because they are also lookup keys (see
        /// <c>IslandPhaseController</c>'s driver table), so translating them at the source would break the
        /// island's art. Prefix and id are joined and lower-cased: <c>("istasyon", "ORE TRUCKS")</c> asks
        /// for <c>istasyon.ore_trucks</c>. An id with no row shows the raw id rather than nothing.
        /// </summary>
        public static string Id(string prefix, string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            var s = ServiceLocator.Get<LocalizationService>();
            if (s == null) return id;

            string key = prefix + "." + id.ToLowerInvariant().Replace(' ', '_').Replace('+', '_');
            return s.Has(key) ? s.Get(key) : id;
        }
    }
}
