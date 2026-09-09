using System;
using Game.Core;
using Game.Data;

namespace Game.Systems
{
    /// <summary>
    /// The card collection: who owns what, what a pack hands over, and what the whole thing is
    /// currently worth. The third roster service beside <see cref="ForemanService"/> and
    /// <see cref="CaptainService"/>, and deliberately not an extension of either — see Docs/PLAN_14
    /// for why Plan #05's rule against a third one was reversed.
    ///
    /// THE SERVICE OWNS EVERY DECISION. The UI never picks a card, never touches a duplicate count
    /// and never works out whether a guarantee is due. It asks for a pack to be opened and is handed
    /// a receipt describing what happened. That is not ceremony: a screen that could choose a card is
    /// a screen that can be made to choose a better one.
    ///
    /// THE DICE LIVE HERE AND NOWHERE ELSE. <see cref="CardCollectionPack"/> takes its roll as an
    /// argument and touches no random number generator, which is what lets the tests assert its whole
    /// distribution exactly. This class supplies the randomness, and takes it injected so the tests
    /// can supply their own — the same split <see cref="ExpeditionService"/> and
    /// <see cref="ForemanService"/> keep.
    ///
    /// EVERY MUTATION SAVES. A pack claimed, a pack opened, a card levelled, a reward taken — all of
    /// them reach the disk before the method returns. The alternative is a player who opens their
    /// Legendary, gets a phone call, and comes back to a save that never heard about it.
    ///
    /// THE EFFECT SNAPSHOT IS RECOMPUTED ON CHANGE, NEVER PER FRAME. See
    /// <see cref="CardCollectionEffects"/>.
    /// </summary>
    public sealed class CardCollectionService
    {
        private readonly SaveData _data;
        private readonly SaveService _save;
        private readonly TimeService _time;
        private readonly CardCollection.Tuning _tuning;
        private readonly CardCollectionPack.Tuning _pack;
        private readonly Random _random;

        /// <summary>The catalogue's rarity census, taken once — it cannot change at runtime, and the
        /// pack asks for it on every open.</summary>
        private readonly int[] _census;

        /// <summary>Reused by <see cref="Recompute"/> so opening a pack allocates nothing.</summary>
        private readonly double[] _totals = new double[CardCollection.EffectKindCount];

        private CardCollectionEffects _effects;

        /// <summary>
        /// Raised after any change has been computed AND saved, never before — a screen that animated
        /// a reveal off an event fired mid-mutation would be showing a state the disk disagrees with.
        /// </summary>
        public event Action Changed;

        /// <summary>
        /// The services a completed set may have to pay through. Late-bound rather than constructor
        /// arguments because a set reward is claimed perhaps three times in a player's life, while
        /// this class is built before most of them exist — the same pattern
        /// <c>GameBootstrap</c> already uses for <c>Expeditions.Crafting</c> and
        /// <c>Maintenance.Goals</c>. All optional: a reward with nobody to pay it is reported unpaid
        /// rather than silently marked claimed.
        /// </summary>
        public CaptainService Captains { get; set; }
        public CraftingService Crafting { get; set; }
        public ForemanService Foremen { get; set; }

        private readonly WalletService _wallet;
        private readonly int _dailyPacks;

        public CardCollectionService(SaveData data, SaveService save, TimeService time,
                                     WalletService wallet, CardCollectionConfig config = null,
                                     Random random = null)
        {
            _data = data;
            _save = save;
            _time = time;
            _wallet = wallet;
            _random = random ?? new Random();

            _tuning = config != null ? config.ToTuning() : CardCollection.Tuning.Default;
            _pack = config != null ? config.ToPackTuning() : CardCollectionPack.Tuning.Default;
            _dailyPacks = config != null ? config.DailyPacks : 1;

            _census = CardCollectionCatalogue.RarityCensus();

            Normalise();
            Recompute();
        }

        private CardCollectionSaveData Block => _data?.cardCollection;

        /// <summary>Repairs whatever the disk handed over, and writes the repair back if there was
        /// one — otherwise the same fixing happens on every launch for ever.</summary>
        private void Normalise()
        {
            if (_data == null) return;
            if (_data.cardCollection == null) _data.cardCollection = new CardCollectionSaveData();
            if (_data.cardCollection.Normalise()) _save?.Save(_data);
        }

        private long NowUnix() => _time != null ? _time.NowUnix() : 0L;

        // ------------------------------------------------------------------- read
        public CardCollection.Tuning Tuning => _tuning;
        public CardCollectionPack.Tuning PackTuning => _pack;

        /// <summary>What the collection is worth right now. Already capped; read it, do not re-cap
        /// it.</summary>
        public CardCollectionEffects Effects => _effects;

        /// <summary>The census the odds sheet needs, so it can omit a rarity nobody carries rather
        /// than printing it at 0%.</summary>
        public int[] RarityCensus() => (int[])_census.Clone();

        public int UnopenedPackCount => Block != null ? Block.unopenedPacks : 0;
        public int PacksOpened => Block != null ? Block.packsOpened : 0;
        public int PullsSinceEpic => Block != null ? Block.pullsSinceEpic : 0;
        public int PullsSinceLegendary => Block != null ? Block.pullsSinceLegendary : 0;

        public int CardCount => CardCollectionCatalogue.Count;
        public int SetCount => CardCollectionCatalogue.SetCount;

        public int LevelOf(int card)
        {
            if (Block == null || !CardCollectionCatalogue.Exists(card)) return CardCollection.NotOwned;
            CardCollectionProgress row = Block.Find(CardCollectionCatalogue.IdOf(card));
            return row != null ? CardCollection.ClampLevel(row.level) : CardCollection.NotOwned;
        }

        public int DuplicatesOf(int card)
        {
            if (Block == null || !CardCollectionCatalogue.Exists(card)) return 0;
            CardCollectionProgress row = Block.Find(CardCollectionCatalogue.IdOf(card));
            return row != null && row.duplicates > 0 ? row.duplicates : 0;
        }

        public bool IsOwned(int card) => CardCollection.IsOwned(LevelOf(card));
        public bool IsMaxed(int card) => CardCollection.IsMaxed(LevelOf(card));

        /// <summary>True for a card drawn but not yet looked at — the "NEW" badge.</summary>
        public bool IsNew(int card)
        {
            if (Block == null || !CardCollectionCatalogue.Exists(card)) return false;
            CardCollectionProgress row = Block.Find(CardCollectionCatalogue.IdOf(card));
            return row != null && row.level > CardCollection.NotOwned && !row.seen;
        }

        public int OwnedCardCount
        {
            get
            {
                int n = 0;
                for (int card = 0; card < CardCollectionCatalogue.Count; card++)
                    if (IsOwned(card)) n++;
                return n;
            }
        }

        public int CompletedSetCount
        {
            get
            {
                int n = 0;
                for (int s = 0; s < CardCollectionCatalogue.SetCount; s++)
                    if (IsSetComplete(s)) n++;
                return n;
            }
        }

        /// <summary>
        /// One card in the grammar every roster screen in this project already speaks, so
        /// <see cref="RosterCardQuery"/>'s allocation-free filter and sort work on the collection
        /// grid without a line of new code.
        ///
        /// <c>Role</c> carries the effect kind. That field exists so a roster can print what a card
        /// DOES, which is exactly what it is being used for; there is no captain's role here to
        /// collide with it. <c>Busy</c> is always false — no card is ever away at sea.
        /// </summary>
        public RosterCardState CardState(int card)
        {
            if (!CardCollectionCatalogue.Exists(card))
                return new RosterCardState(card, RosterCardState.Rarity.Common, 0, 0,
                                           CardCollection.MaxLevel, 0, 0, 0d, false);

            int level = LevelOf(card);
            return new RosterCardState(
                card,
                CardCollectionCatalogue.RarityOf(card),
                (int)CardCollectionCatalogue.EffectOf(card),
                level,
                CardCollection.MaxLevel,
                DuplicatesOf(card),
                CardCollectionCatalogue.DuplicatesToLevel(card, level, _tuning),
                CardCollectionCatalogue.EffectValue(card, level, _tuning),
                false);
        }

        public RosterCardState CardState(string cardId) => CardState(CardCollectionCatalogue.IndexOf(cardId));

        /// <summary>True when every card in the set is owned at level 1 or better.</summary>
        public bool IsSetComplete(int set)
        {
            int[] cards = CardCollectionCatalogue.CardsInSet(set);
            if (cards.Length == 0) return false;
            for (int i = 0; i < cards.Length; i++)
                if (!IsOwned(cards[i])) return false;
            return true;
        }

        public CollectionSetState SetState(int set)
        {
            if (!CardCollectionCatalogue.SetExists(set)) return CollectionSetState.None;

            CardCollectionCatalogue.Set definition = CardCollectionCatalogue.Sets[set];
            int[] cards = CardCollectionCatalogue.CardsInSet(set);

            int owned = 0;
            for (int i = 0; i < cards.Length; i++) if (IsOwned(cards[i])) owned++;

            return new CollectionSetState(
                set, definition.Id, owned, cards.Length,
                definition.Bonus, definition.BonusValue,
                definition.RewardKind, definition.RewardAmount,
                Block != null && Block.HasClaimedSetReward(definition.Id));
        }

        public CollectionSetState SetState(string setId)
            => SetState(CardCollectionCatalogue.SetIndexOf(setId));

        // ------------------------------------------------------------- daily pack
        /// <summary>
        /// The UTC day the free pack is counted against — the same calendar Plan #07 put the goals
        /// on, so a player's day rolls over in one place rather than two.
        /// </summary>
        public int Today => Goals.DayNumber(NowUnix());

        /// <summary>
        /// True when today's free pack has not been taken. Strictly greater rather than not-equal, so
        /// a clock rolled backwards only ever DELAYS the next pack — it can never pay a second one.
        /// </summary>
        public bool DailyPackReady => Block != null && _dailyPacks > 0 && Today > Block.dailyPackDay;

        /// <summary>Seconds to the next UTC midnight, for a countdown. Zero once it is ready.</summary>
        public long DailyPackSecondsLeft
        {
            get
            {
                if (DailyPackReady) return 0L;
                long now = NowUnix();
                if (now <= 0L) return 0L;
                long into = now % 86400L;
                return 86400L - into;
            }
        }

        /// <summary>
        /// Takes today's free pack. It BANKS an unopened pack rather than opening one — the reveal is
        /// the player's moment, and a pack that opened itself while they were looking at something
        /// else has been spent on nothing.
        /// </summary>
        public bool TryClaimDailyPack()
        {
            if (!DailyPackReady) return false;

            Block.dailyPackDay = Today;
            Block.unopenedPacks += _dailyPacks;
            Commit();
            return true;
        }

        /// <summary>
        /// Packs from anywhere else — a goal milestone, an achievement tier, a completed set. The
        /// source is taken for the analytics call and to make the caller say where it came from.
        /// </summary>
        public void GrantPacks(int count, CardCollection.PackSource source)
        {
            if (Block == null || count <= 0) return;
            Block.unopenedPacks += count;
            Commit();
        }

        // ------------------------------------------------------------ pack opening
        /// <summary>
        /// What one pack turned out to be. Everything a reveal needs, and everything an analytics
        /// call needs, decided before the screen is told anything.
        /// </summary>
        public readonly struct PackOpenReceipt
        {
            public readonly int Card;
            public readonly string CardId;
            public readonly RosterCardState.Rarity Rarity;

            /// <summary>The first copy — this card was locked and is now level 1.</summary>
            public readonly bool WasNew;

            /// <summary>The card was already at level 5, so the copy paid gems instead.</summary>
            public readonly bool WasOverflow;
            public readonly long GemsPaid;

            public readonly int Level;
            public readonly int Duplicates;

            /// <summary>This copy can be spent on a level right now.</summary>
            public readonly bool UpgradeReady;

            /// <summary>This card was the last one its set needed. The set's permanent bonus is
            /// already live by the time this receipt is handed over.</summary>
            public readonly bool CompletedSet;
            public readonly int Set;

            public PackOpenReceipt(int card, string cardId, RosterCardState.Rarity rarity,
                                   bool wasNew, bool wasOverflow, long gemsPaid,
                                   int level, int duplicates, bool upgradeReady,
                                   bool completedSet, int set)
            {
                Card = card;
                CardId = cardId ?? string.Empty;
                Rarity = rarity;
                WasNew = wasNew;
                WasOverflow = wasOverflow;
                GemsPaid = gemsPaid;
                Level = level;
                Duplicates = duplicates;
                UpgradeReady = upgradeReady;
                CompletedSet = completedSet;
                Set = set;
            }

            /// <summary>A duplicate banked toward a level — the outcome that is neither a new card
            /// nor a wasted one.</summary>
            public bool WasDuplicate => !WasNew && !WasOverflow;
        }

        public bool CanOpenPack => Block != null && Block.unopenedPacks > 0
                                   && CardCollectionCatalogue.Count > 0;

        /// <summary>
        /// Opens one pack, atomically: a card is chosen, the counters move, the effects are rebuilt
        /// and the save is written before anything is returned. Either all of that happened or none
        /// of it did.
        ///
        /// Refuses rather than throwing when there is no pack — a button held down is the common
        /// case, not an error.
        /// </summary>
        public bool TryOpenPack(out PackOpenReceipt receipt)
        {
            receipt = default;
            if (!CanOpenPack) return false;

            int sinceEpic = Block.pullsSinceEpic;
            int sinceLegendary = Block.pullsSinceLegendary;

            RosterCardState.Rarity rarity = CardCollectionPack.RollRarity(
                _random.NextDouble(), _census, sinceEpic, sinceLegendary, _pack);

            int nth = CardCollectionPack.RollIndexInRarity(
                _random.NextDouble(), CardCollectionCatalogue.CountOfRarity(rarity));
            int card = CardCollectionCatalogue.OfRarity(rarity, nth);

            // The catalogue and the census cannot disagree — the census is built from the catalogue —
            // so this is unreachable. It is here because "unreachable" and "cannot happen" are
            // different claims, and spending a pack on nothing is the worse failure.
            if (!CardCollectionCatalogue.Exists(card)) return false;

            int set = CardCollectionCatalogue.SetIndexOfCard(card);
            bool setWasComplete = set >= 0 && IsSetComplete(set);

            CardCollectionProgress row = Block.FindOrAdd(CardCollectionCatalogue.IdOf(card));

            bool wasNew = false, wasOverflow = false;
            long gems = 0L;

            if (row.level <= CardCollection.NotOwned)
            {
                // Only a pack draw can create level 1 — never a pile of duplicates.
                row.level = 1;
                row.seen = false;
                wasNew = true;
            }
            else if (CardCollection.IsMaxed(row.level))
            {
                // Nothing left to spend a copy on, so it pays instead. Leaving it visible and inert
                // is the one option that silently discards value; taking maxed cards out of the pool
                // would make the published odds untrue.
                wasOverflow = true;
                gems = CardCollectionCatalogue.OverflowGems(card, _tuning);
                if (gems > 0L) _wallet?.AddGems(gems);
            }
            else
            {
                row.duplicates++;
            }

            CardCollectionPack.Advance(rarity, ref sinceEpic, ref sinceLegendary);
            Block.pullsSinceEpic = sinceEpic;
            Block.pullsSinceLegendary = sinceLegendary;

            Block.unopenedPacks--;
            Block.packsOpened++;

            bool completedSet = set >= 0 && !setWasComplete && IsSetComplete(set);

            // Before the receipt is built, so a reveal that reads Effects sees the set bonus the
            // card it is about to show turned on.
            Recompute();
            Commit();

            receipt = new PackOpenReceipt(
                card, CardCollectionCatalogue.IdOf(card), rarity,
                wasNew, wasOverflow, gems,
                row.level, row.duplicates,
                CardCollection.CanLevel(rarity, row.level, row.duplicates, _tuning),
                completedSet, set);
            return true;
        }

        // ---------------------------------------------------------------- levelling
        public bool CanUpgrade(int card)
        {
            if (!CardCollectionCatalogue.Exists(card)) return false;
            return CardCollection.CanLevel(CardCollectionCatalogue.RarityOf(card),
                                           LevelOf(card), DuplicatesOf(card), _tuning);
        }

        public bool CanUpgrade(string cardId) => CanUpgrade(CardCollectionCatalogue.IndexOf(cardId));

        /// <summary>
        /// Spends exactly the configured copies and adds one level. Refuses a card that is unowned,
        /// maxed, or short — the UI is not allowed to work any of those out for itself.
        /// </summary>
        public bool TryUpgrade(int card)
        {
            if (Block == null || !CanUpgrade(card)) return false;

            CardCollectionProgress row = Block.Find(CardCollectionCatalogue.IdOf(card));
            if (row == null) return false;

            int cost = CardCollectionCatalogue.DuplicatesToLevel(card, row.level, _tuning);
            if (cost <= 0 || row.duplicates < cost) return false;

            row.duplicates -= cost;
            row.level = CardCollection.ClampLevel(row.level + 1);

            Recompute();
            Commit();
            return true;
        }

        public bool TryUpgrade(string cardId) => TryUpgrade(CardCollectionCatalogue.IndexOf(cardId));

        /// <summary>How many cards could be levelled right now — the collection opener's badge.</summary>
        public int UpgradeReadyCount()
        {
            int n = 0;
            for (int card = 0; card < CardCollectionCatalogue.Count; card++) if (CanUpgrade(card)) n++;
            return n;
        }

        /// <summary>Clears a card's "NEW" badge. Presentation only, but it is saved — a badge that
        /// came back after an app restart would read as a bug.</summary>
        public bool MarkSeen(int card)
        {
            if (Block == null || !CardCollectionCatalogue.Exists(card)) return false;
            CardCollectionProgress row = Block.Find(CardCollectionCatalogue.IdOf(card));
            if (row == null || row.seen || row.level <= CardCollection.NotOwned) return false;

            row.seen = true;
            Commit();
            return true;
        }

        // ------------------------------------------------------------- set rewards
        /// <summary>What a claimed set paid.</summary>
        public readonly struct SetRewardReceipt
        {
            public readonly int Set;
            public readonly string SetId;
            public readonly CardCollection.SetRewardKind Kind;
            public readonly long Amount;

            /// <summary>False when nothing was available to pay through — the claim is still
            /// recorded, but the screen must not celebrate a payment that did not land.</summary>
            public readonly bool Paid;

            public SetRewardReceipt(int set, string setId, CardCollection.SetRewardKind kind,
                                    long amount, bool paid)
            {
                Set = set;
                SetId = setId ?? string.Empty;
                Kind = kind;
                Amount = amount;
                Paid = paid;
            }
        }

        public bool CanClaimSetReward(int set) => SetState(set).RewardClaimable;

        public bool CanClaimSetReward(string setId)
            => CanClaimSetReward(CardCollectionCatalogue.SetIndexOf(setId));

        /// <summary>
        /// Takes a completed set's one-time reward. Validates completion and refuses a second claim
        /// through <see cref="CardCollectionSaveData.claimedSetRewardIds"/> — the same idempotency
        /// key list the league and the IAP receipts keep.
        ///
        /// This has NO bearing on the permanent set bonus, which is derived from the cards owned and
        /// is already live. A player who never presses this keeps everything they earned.
        /// </summary>
        public bool TryClaimSetReward(int set, out SetRewardReceipt receipt)
        {
            receipt = default;
            if (Block == null || !CanClaimSetReward(set)) return false;

            CardCollectionCatalogue.Set definition = CardCollectionCatalogue.Sets[set];

            // Recorded BEFORE paying. A payer that throws must not leave a reward that can be taken
            // again — the same order every idempotent claim in this project uses.
            Block.claimedSetRewardIds.Add(definition.Id);
            bool paid = Pay(definition.RewardKind, definition.RewardAmount);

            Commit();
            receipt = new SetRewardReceipt(set, definition.Id, definition.RewardKind,
                                           definition.RewardAmount, paid);
            return true;
        }

        public bool TryClaimSetReward(string setId, out SetRewardReceipt receipt)
            => TryClaimSetReward(CardCollectionCatalogue.SetIndexOf(setId), out receipt);

        /// <summary>How many sets have a reward waiting — the other half of the opener's badge.</summary>
        public int ClaimableSetRewardCount()
        {
            int n = 0;
            for (int s = 0; s < CardCollectionCatalogue.SetCount; s++) if (CanClaimSetReward(s)) n++;
            return n;
        }

        /// <summary>
        /// Hands a reward to whichever system owns that currency. Returns false when there is nobody
        /// to hand it to, which happens in tests and in a bootstrap that has not wired the optional
        /// services — reported rather than swallowed, so a screen cannot celebrate nothing.
        /// </summary>
        private bool Pay(CardCollection.SetRewardKind kind, long amount)
        {
            if (amount <= 0L) return false;

            switch (kind)
            {
                case CardCollection.SetRewardKind.Gems:
                    if (_wallet == null) return false;
                    _wallet.AddGems(amount);
                    return true;

                case CardCollection.SetRewardKind.CraftPoints:
                    if (Crafting == null) return false;
                    Crafting.AddPoints(amount);
                    return true;

                case CardCollection.SetRewardKind.Charts:
                    if (Captains == null) return false;
                    Captains.AddCharts(amount);
                    return true;

                case CardCollection.SetRewardKind.ForemanCards:
                    if (Foremen == null) return false;
                    Foremen.GrantRandomDuplicates((int)amount);
                    return true;

                case CardCollection.SetRewardKind.Salvage:
                    // Salvage is a plain field with no service in front of it — the sea reads it
                    // straight off the save.
                    if (_data == null) return false;
                    _data.salvage += amount;
                    return true;

                case CardCollection.SetRewardKind.Pack:
                    GrantPacks((int)amount, CardCollection.PackSource.EventReserved);
                    return true;

                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ upkeep
        /// <summary>
        /// Rebuilds the effect snapshot. Walks twenty-four cards and three sets, which is why it runs
        /// on change and not on a tick — and why <see cref="_totals"/> is a field rather than a local,
        /// so opening a pack allocates nothing.
        /// </summary>
        private void Recompute()
        {
            for (int k = 0; k < _totals.Length; k++) _totals[k] = 0d;

            for (int card = 0; card < CardCollectionCatalogue.Count; card++)
            {
                int level = LevelOf(card);
                if (level <= CardCollection.NotOwned) continue;

                int kind = (int)CardCollectionCatalogue.EffectOf(card);
                if (kind >= 0 && kind < _totals.Length)
                    _totals[kind] += CardCollectionCatalogue.EffectValue(card, level, _tuning);
            }

            for (int s = 0; s < CardCollectionCatalogue.SetCount; s++)
            {
                if (!IsSetComplete(s)) continue;
                int kind = (int)CardCollectionCatalogue.Sets[s].Bonus;
                if (kind >= 0 && kind < _totals.Length)
                    _totals[kind] += CardCollectionCatalogue.Sets[s].BonusValue;
            }

            // Capped here, once, so no consumer downstream can read past a cap by forgetting it.
            _effects = new CardCollectionEffects(
                CardCollection.Capped(CardCollection.EffectKind.IncomeMultiplier, _totals[0], _tuning),
                CardCollection.Capped(CardCollection.EffectKind.CraftXpMultiplier, _totals[1], _tuning),
                CardCollection.Capped(CardCollection.EffectKind.CraftPointDropChance, _totals[2], _tuning),
                CardCollection.Capped(CardCollection.EffectKind.SeaSalvageMultiplier, _totals[3], _tuning),
                CardCollection.Capped(CardCollection.EffectKind.SeaChartMultiplier, _totals[4], _tuning));
        }

        /// <summary>Writes, then tells. Never the other way round.</summary>
        private void Commit()
        {
            _save?.Save(_data);
            Changed?.Invoke();
        }
    }
}
