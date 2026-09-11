using System.Globalization;
using Game.Systems;

namespace Game.UI
{
    /// <summary>
    /// The one wording for "how much of which currency" on a receipt, a reward line or a price. Every
    /// screen used to spell its own ("PTS", "hurda", a bare number), which left Craft Points and
    /// Mining Points both reading "PTS" and sea Salvage and Mining Scrap both reading "hurda". Names
    /// come from the <see cref="CurrencyRegistry"/> definitions so the wallet and every receipt agree.
    /// Called on taps and refreshes, never per frame.
    /// </summary>
    public static class CurrencyText
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static string Name(CurrencyId id)
            => CurrencyRegistry.TryDescribe(id, out CurrencyDefinition d) ? Loc.T(d.LocalizedNameKey) : id.ToString();

        public static string Description(CurrencyId id)
            => CurrencyRegistry.TryDescribe(id, out CurrencyDefinition d) ? Loc.T(d.LocalizedDescriptionKey) : string.Empty;

        /// <summary>"+12 SALVAGE" — something that landed.</summary>
        public static string Gain(CurrencyId id, long amount)
            => string.Format(Loc.T("currency.gain"), amount.ToString(Culture), Name(id));

        /// <summary>"-3 MINING POINTS" — something that was paid.</summary>
        public static string Cost(CurrencyId id, long amount)
            => string.Format(Loc.T("currency.cost"), amount.ToString(Culture), Name(id));

        /// <summary>"100 CHARTS" — a price on a button, before anything is paid.</summary>
        public static string Amount(CurrencyId id, long amount)
            => string.Format(Loc.T("currency.amount"), amount.ToString(Culture), Name(id));
    }
}
