using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Who can be collected: twenty-four cards across three sets, and what each set is worth
    /// finishing. The catalogue's counterpart in the other two rosters is <see cref="Foremen.Roster"/>
    /// and <see cref="Captains.Roster"/>, and it lives in code for the same two reasons they do.
    ///
    /// WHY THE CATALOGUE IS CODE AND NOT AN ASSET. First, these strings are SAVE KEYS — a card is
    /// addressed by <see cref="Card.Id"/> in every save file on every device, so an Inspector field
    /// nobody meant to touch is an orphaned collection. Second, every other roster in this project
    /// works with no asset present at all, and a feature that cannot be tested until somebody
    /// authors a ScriptableObject is a feature whose tests describe a hypothetical. What genuinely
    /// belongs in <c>Data/CardCollectionConfig</c> is the ART — a sprite cannot be a literal — and
    /// the tuning scalars, and that is exactly what is there.
    ///
    /// ONE SOURCE OF MEMBERSHIP. A card knows its set; a set does not keep a list of its cards. The
    /// draft plan had both, which is two things that can disagree and one of them silently wrong.
    /// A set's contents are every card naming it, in authored order, and <see cref="Card.Id"/> is
    /// required to begin with its own set id — so identity and membership cannot drift apart either.
    /// <c>CardCollectionCatalogueTests</c> is what holds that promise.
    ///
    /// NO PER-CARD BALANCE NUMBERS. A card declares what KIND of thing it does and how rare it is,
    /// and <see cref="CardCollection.PerLevel"/> turns that pair into a value. Twenty-four
    /// hand-tuned constants would be twenty-four chances to mistype one, and the value was never
    /// per-card in the first place — it was always a function of rarity, which is what makes a
    /// Legendary worth finding.
    ///
    /// RARITY MIX IS UNIFORM. Every set is 3 Common, 2 Rare, 2 Epic and 1 Legendary, so which set a
    /// player completes first is decided by luck rather than by which tab they happened to open. No
    /// card carries Mythic at launch; the rung exists, and <see cref="CardCollectionPack.WeightOf"/>
    /// keeps it unreachable until one does.
    /// </summary>
    public static class CardCollectionCatalogue
    {
        /// <summary>The separator between a card's set and its own name. Part of the save key, so it
        /// is as fixed as the ids either side of it.</summary>
        public const char IdSeparator = '/';

        /// <summary>One card: what it is, where it belongs, and what kind of thing it does.</summary>
        public struct Card
        {
            /// <summary>Save key. Lower-case ASCII, <c>set_id/card_name</c>, never renamed after
            /// release. Display name, description, art and every balance value may change freely.</summary>
            public string Id;

            /// <summary>The set this card completes. Always the part of <see cref="Id"/> before the
            /// separator.</summary>
            public string SetId;

            public RosterCardState.Rarity Rarity;

            /// <summary>The one thing this card does. Its size comes from
            /// <see cref="CardCollection.PerLevel"/> against the rarity above.</summary>
            public CardCollection.EffectKind Effect;
        }

        /// <summary>One set: what finishing it turns on forever, and what it pays once.</summary>
        public struct Set
        {
            /// <summary>Save key for the one-time reward claim. Never renamed after release.</summary>
            public string Id;

            /// <summary>The permanent bonus, live from the moment the last card is owned and derived
            /// from completion for ever after — never from whether the reward below was claimed. A
            /// player who forgets to tap Claim must not lose it.</summary>
            public CardCollection.EffectKind Bonus;
            public double BonusValue;

            /// <summary>The one-time reward, claimable exactly once.</summary>
            public CardCollection.SetRewardKind RewardKind;
            public long RewardAmount;
        }

        // ------------------------------------------------------------------- sets
        /// <summary>
        /// Three sets, one gameplay loop each, and three bonuses that touch three different numbers.
        /// None of them moves anything the captain or foreman rosters already move.
        ///
        /// Rewards are sized off anchors that already exist: 400 gems is about 2.7 full weekly
        /// milestone tracks, 40 craft points about three heavy sea days at the workshop's 20% drop
        /// chance, and 250 charts exactly two and a half captain crates.
        /// </summary>
        public static readonly Set[] Sets =
        {
            new Set
            {
                Id = "coal_industry",
                Bonus = CardCollection.EffectKind.IncomeMultiplier,
                BonusValue = 0.08d,
                RewardKind = CardCollection.SetRewardKind.Gems,
                RewardAmount = 400L,
            },
            new Set
            {
                Id = "workshop_guild",
                Bonus = CardCollection.EffectKind.CraftPointDropChance,
                BonusValue = 0.06d,
                RewardKind = CardCollection.SetRewardKind.CraftPoints,
                RewardAmount = 40L,
            },
            new Set
            {
                Id = "deep_waters",
                Bonus = CardCollection.EffectKind.SeaSalvageMultiplier,
                BonusValue = 0.15d,
                RewardKind = CardCollection.SetRewardKind.Charts,
                RewardAmount = 250L,
            },
        };

        // ------------------------------------------------------------------ cards
        /// <summary>
        /// Everyone who can be found, set-major and in the order the collection screen shows them.
        /// Authored order is display order; nothing addresses a card by position, so this may be
        /// reordered — but no id may ever be changed, because saves are full of them.
        /// </summary>
        public static readonly Card[] Cards =
        {
            // ---- coal_industry — the island loop. Six income cards and two that teach the bench,
            //      because a coal yard's ledger is the same skill as a foundry's.
            Card_("coal_industry/foreman_ledger", RosterCardState.Rarity.Common,    CardCollection.EffectKind.IncomeMultiplier),
            Card_("coal_industry/ore_scale",      RosterCardState.Rarity.Common,    CardCollection.EffectKind.IncomeMultiplier),
            Card_("coal_industry/lamp_oil",       RosterCardState.Rarity.Common,    CardCollection.EffectKind.IncomeMultiplier),
            Card_("coal_industry/shift_whistle",  RosterCardState.Rarity.Rare,      CardCollection.EffectKind.IncomeMultiplier),
            Card_("coal_industry/assay_kit",      RosterCardState.Rarity.Rare,      CardCollection.EffectKind.CraftXpMultiplier),
            Card_("coal_industry/cable_drum",     RosterCardState.Rarity.Epic,      CardCollection.EffectKind.IncomeMultiplier),
            Card_("coal_industry/bond_seal",      RosterCardState.Rarity.Epic,      CardCollection.EffectKind.CraftXpMultiplier),
            Card_("coal_industry/pit_charter",    RosterCardState.Rarity.Legendary, CardCollection.EffectKind.IncomeMultiplier),

            // ---- workshop_guild — the bench. Points are its bottleneck and XP is its ladder, so it
            //      carries both, plus one income card so no set is a single note.
            Card_("workshop_guild/whetstone",     RosterCardState.Rarity.Common,    CardCollection.EffectKind.CraftXpMultiplier),
            Card_("workshop_guild/bellows",       RosterCardState.Rarity.Common,    CardCollection.EffectKind.CraftXpMultiplier),
            Card_("workshop_guild/tally_stick",   RosterCardState.Rarity.Common,    CardCollection.EffectKind.CraftXpMultiplier),
            Card_("workshop_guild/gauge_block",   RosterCardState.Rarity.Rare,      CardCollection.EffectKind.CraftPointDropChance),
            Card_("workshop_guild/master_key",    RosterCardState.Rarity.Rare,      CardCollection.EffectKind.CraftXpMultiplier),
            Card_("workshop_guild/forge_permit",  RosterCardState.Rarity.Epic,      CardCollection.EffectKind.CraftPointDropChance),
            Card_("workshop_guild/pattern_book",  RosterCardState.Rarity.Epic,      CardCollection.EffectKind.IncomeMultiplier),
            Card_("workshop_guild/guild_charter", RosterCardState.Rarity.Legendary, CardCollection.EffectKind.CraftPointDropChance),

            // ---- deep_waters — the sea. Salvage and charts split four and four: both are closed
            //      loops that never touch the main economy, which is why they carry the big numbers.
            Card_("deep_waters/tide_table",       RosterCardState.Rarity.Common,    CardCollection.EffectKind.SeaSalvageMultiplier),
            Card_("deep_waters/rope_coil",        RosterCardState.Rarity.Common,    CardCollection.EffectKind.SeaSalvageMultiplier),
            Card_("deep_waters/star_glass",       RosterCardState.Rarity.Common,    CardCollection.EffectKind.SeaChartMultiplier),
            Card_("deep_waters/pilot_log",        RosterCardState.Rarity.Rare,      CardCollection.EffectKind.SeaChartMultiplier),
            Card_("deep_waters/grapnel",          RosterCardState.Rarity.Rare,      CardCollection.EffectKind.SeaSalvageMultiplier),
            Card_("deep_waters/deep_chart",       RosterCardState.Rarity.Epic,      CardCollection.EffectKind.SeaChartMultiplier),
            Card_("deep_waters/wreck_map",        RosterCardState.Rarity.Epic,      CardCollection.EffectKind.SeaSalvageMultiplier),
            Card_("deep_waters/admiralty_seal",   RosterCardState.Rarity.Legendary, CardCollection.EffectKind.SeaChartMultiplier),
        };

        /// <summary>Builds a card, taking its set from its own id so the two can never disagree.</summary>
        private static Card Card_(string id, RosterCardState.Rarity rarity, CardCollection.EffectKind effect)
            => new Card { Id = id, SetId = SetIdIn(id), Rarity = rarity, Effect = effect };

        /// <summary>The set part of a card id, or "" for an id with no separator in it.</summary>
        public static string SetIdIn(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return string.Empty;
            int cut = cardId.IndexOf(IdSeparator);
            return cut <= 0 ? string.Empty : cardId.Substring(0, cut);
        }

        public static int Count => Cards.Length;
        public static int SetCount => Sets.Length;

        // ---------------------------------------------------------------- indices
        // Built once, at type initialisation. Every lookup below is a dictionary hit or an array
        // read — nothing here walks the catalogue at runtime, because the collection screen asks
        // these questions once per card per refresh and a pack asks them on every open.
        private static readonly Dictionary<string, int> CardIndex;
        private static readonly Dictionary<string, int> SetIndex;
        private static readonly int[] Census;              // cards per rarity
        private static readonly int[][] ByRarity;          // card indices, rarity-major
        private static readonly int[][] BySet;             // card indices, set-major, authored order

        static CardCollectionCatalogue()
        {
            CardIndex = new Dictionary<string, int>(Cards.Length, StringComparer.Ordinal);
            for (int i = 0; i < Cards.Length; i++) CardIndex[Cards[i].Id] = i;

            SetIndex = new Dictionary<string, int>(Sets.Length, StringComparer.Ordinal);
            for (int s = 0; s < Sets.Length; s++) SetIndex[Sets[s].Id] = s;

            Census = new int[CardCollection.RarityCount];
            for (int i = 0; i < Cards.Length; i++)
            {
                int r = (int)Cards[i].Rarity;
                if (r >= 0 && r < Census.Length) Census[r]++;
            }

            ByRarity = new int[CardCollection.RarityCount][];
            for (int r = 0; r < ByRarity.Length; r++)
            {
                ByRarity[r] = new int[Census[r]];
                int n = 0;
                for (int i = 0; i < Cards.Length; i++)
                    if ((int)Cards[i].Rarity == r) ByRarity[r][n++] = i;
            }

            BySet = new int[Sets.Length][];
            for (int s = 0; s < Sets.Length; s++)
            {
                int n = 0;
                for (int i = 0; i < Cards.Length; i++)
                    if (string.Equals(Cards[i].SetId, Sets[s].Id, StringComparison.Ordinal)) n++;

                BySet[s] = new int[n];
                n = 0;
                for (int i = 0; i < Cards.Length; i++)
                    if (string.Equals(Cards[i].SetId, Sets[s].Id, StringComparison.Ordinal))
                        BySet[s][n++] = i;
            }
        }

        // ------------------------------------------------------------------- read
        public static bool Exists(int card) => card >= 0 && card < Cards.Length;
        public static bool SetExists(int set) => set >= 0 && set < Sets.Length;

        /// <summary>Where a card id sits in the catalogue, or -1 for one this build does not carry.
        /// An id a save holds and the catalogue does not is not an error — it is a card from a build
        /// the player has since updated past, and it must be kept rather than dropped.</summary>
        public static int IndexOf(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return -1;
            return CardIndex.TryGetValue(cardId, out int i) ? i : -1;
        }

        public static int SetIndexOf(string setId)
        {
            if (string.IsNullOrEmpty(setId)) return -1;
            return SetIndex.TryGetValue(setId, out int s) ? s : -1;
        }

        public static string IdOf(int card) => Exists(card) ? Cards[card].Id : string.Empty;

        public static RosterCardState.Rarity RarityOf(int card)
            => Exists(card) ? Cards[card].Rarity : RosterCardState.Rarity.Common;

        public static CardCollection.EffectKind EffectOf(int card)
            => Exists(card) ? Cards[card].Effect : CardCollection.EffectKind.IncomeMultiplier;

        public static string SetOf(int card) => Exists(card) ? Cards[card].SetId : string.Empty;

        /// <summary>Which set a card belongs to, by index, or -1 for a card whose set is missing.</summary>
        public static int SetIndexOfCard(int card)
            => Exists(card) ? SetIndexOf(Cards[card].SetId) : -1;

        /// <summary>
        /// How many cards carry each rarity — what <see cref="CardCollectionPack"/> divides its
        /// weight among, and what keeps it from rolling a rarity nobody carries. A fresh copy each
        /// call, because the pack takes it as an argument and a shared array would let a caller
        /// rewrite the catalogue's census by accident.
        /// </summary>
        public static int[] RarityCensus() => (int[])Census.Clone();

        public static int CountOfRarity(RosterCardState.Rarity rarity)
        {
            int r = (int)rarity;
            return r >= 0 && r < Census.Length ? Census[r] : 0;
        }

        /// <summary>The nth card carrying a rarity, or -1 when nobody does. The other half of the
        /// pack's two rolls: rarity first, then flat among everyone at it.</summary>
        public static int OfRarity(RosterCardState.Rarity rarity, int nth)
        {
            int r = (int)rarity;
            if (r < 0 || r >= ByRarity.Length) return -1;
            int[] pool = ByRarity[r];
            return nth < 0 || nth >= pool.Length ? -1 : pool[nth];
        }

        /// <summary>Every card in a set, in authored order. The array is the catalogue's own and must
        /// not be written to — it is handed out rather than copied because the collection screen asks
        /// for it on every refresh.</summary>
        public static int[] CardsInSet(int set)
            => SetExists(set) ? BySet[set] : Array.Empty<int>();

        public static int CardsInSetCount(int set) => SetExists(set) ? BySet[set].Length : 0;

        // --------------------------------------------------------------- loc keys
        // Derived rather than authored. A second string per card is a second thing to get wrong, and
        // these are the keys the eleven-language table is filled against.
        public static string NameKey(int card) => Exists(card) ? "koleksiyon.kart." + Cards[card].Id + ".ad" : string.Empty;
        public static string DescriptionKey(int card) => Exists(card) ? "koleksiyon.kart." + Cards[card].Id + ".aciklama" : string.Empty;
        public static string SetNameKey(int set) => SetExists(set) ? "koleksiyon.set." + Sets[set].Id + ".ad" : string.Empty;
        public static string SetDescriptionKey(int set) => SetExists(set) ? "koleksiyon.set." + Sets[set].Id + ".aciklama" : string.Empty;

        // ----------------------------------------------------------------- worth
        /// <summary>What this card adds at <paramref name="level"/>, everything applied. Zero for a
        /// card nobody owns.</summary>
        public static double EffectValue(int card, int level, in CardCollection.Tuning t)
        {
            if (!Exists(card)) return 0d;
            return CardCollection.CardEffect(
                CardCollection.PerLevel(Cards[card].Effect, Cards[card].Rarity, t), level);
        }

        /// <summary>Copies this card needs for its next level. 0 at the ceiling and 0 unowned.</summary>
        public static int DuplicatesToLevel(int card, int level, in CardCollection.Tuning t)
            => Exists(card) ? CardCollection.DuplicatesToLevel(Cards[card].Rarity, level, t) : 0;

        /// <summary>Gems a copy of this card is worth once it is maxed.</summary>
        public static long OverflowGems(int card, in CardCollection.Tuning t)
            => Exists(card) ? CardCollection.OverflowGems(Cards[card].Rarity, t) : 0L;
    }
}
