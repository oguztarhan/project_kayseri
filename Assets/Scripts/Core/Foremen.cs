using System;

namespace Game.Core
{
    /// <summary>
    /// The master roster as pure maths: who can be found, what he is worth at his station, and what a
    /// star costs. The market's counterpart is <see cref="MarketFlow"/> and the island's is
    /// <see cref="IslandEconomy"/>; this is the third, and it exists for the same reason as those two —
    /// a number the player is asked to invest in should be readable in one file rather than discovered
    /// by playing.
    ///
    /// FIFTEEN MASTERS, THREE PER STATION. The five stations are the five the player actually upgrades
    /// (<see cref="IslandEconomy.MajorStationIds"/>) and each carries a Common, a Rare and a Legendary.
    /// RARITY IS A FACT ABOUT THE MASTER, not about how far you have taken him — the roster used to
    /// derive five tiers from a star count, which made "Legendary" a synonym for "levelled" and left
    /// nothing to actually find. A Legendary mine master is now somebody a chest handed you.
    ///
    /// ONE ACTIVE PER STATION. You own as many as the chests give you and put ONE of the three to work.
    /// Stacking all three would triple every station bonus and turn the C/R/L ladder into an additive
    /// pile where the Common you replaced still mattered; picking one makes the Legendary an upgrade
    /// you feel the moment it lands, and leaves the other two as the collection they were drawn for.
    ///
    /// ACCOUNT-WIDE, NOT PER-ISLAND. A master works on every island at once. Per-island would go stale
    /// the moment you sailed; account-wide means the mine master you starred up on coal is still the
    /// mine master on diamond, which is what makes the roster worth keeping rather than rebuying.
    ///
    /// THREE SKILLS, ONE TABLE. Every master has the same three — throughput, income ceiling and
    /// offline earnings — and rarity and stars scale them. Bespoke skills per station would read
    /// better and would be fifteen separate balance problems in eleven languages; one table is a thing
    /// that can be tuned in an afternoon and cannot quietly disagree with itself.
    ///
    /// WHY THROUGHPUT ALONE WOULD NOT DO. Every island's income is capped in MarketService, so a player
    /// near their cap — exactly the player who has been playing long enough to own masters — would see
    /// a throughput bonus do nothing at all. <see cref="Skill.Income"/> is the half that pays;
    /// throughput is the half you can watch.
    /// </summary>
    public static class Foremen
    {
        /// <summary>The five stations a master can run, in roster order. Saves address masters by an
        /// index derived from this, so it must never be reordered.</summary>
        public const int StationCount = 5;

        /// <summary>One Common, one Rare, one Legendary at every station.</summary>
        public const int PerStation = 3;

        /// <summary>How many masters exist. Station-major: see <see cref="IndexOf"/>.</summary>
        public const int Count = StationCount * PerStation;

        /// <summary>Nobody there. The first card for an empty slot puts a master at one star; there is
        /// no zero-star master. Kept as a name rather than a literal because saves are full of it.</summary>
        public const int NotHired = 0;

        /// <summary>How far a master can be taken. Five stars, drawn as five pips on his card.</summary>
        public const int MaxStars = 5;

        /// <summary>What a master IS, fixed when he is drawn. Three rather than five: the roster is
        /// five stations wide already, and a fifteen-card collection that spans five grades has grades
        /// nobody ever completes.</summary>
        public enum Rarity { Common = 0, Rare = 1, Legendary = 2 }

        public const int RarityCount = 3;

        /// <summary>
        /// The three things every master does. Shared rather than bespoke — see the class summary.
        ///   - <see cref="Throughput"/> speeds his own station's chain on every island.
        ///   - <see cref="Income"/> lifts the empire income ceiling, which is the half that survives
        ///     the per-island cap.
        ///   - <see cref="Offline"/> adds to how much of your rate keeps earning while the app is shut.
        /// </summary>
        public enum Skill { Throughput = 0, Income = 1, Offline = 2 }

        public const int SkillCount = 3;

        // Roster station indices. These address THIS roster, not the saved upgrade matrix.
        public const int Mine = 0, Deposit = 1, Refinery = 2, Port = 3, Market = 4;

        /// <summary>
        /// Which saved economy slot each roster station speeds. The upgrade matrix still carries all
        /// eight legacy indices and the port's globals still live in the old power slot, so this is the
        /// one place the two numbering schemes are allowed to meet — see
        /// <see cref="IslandEconomy.PlayerStations"/>.
        /// </summary>
        public static readonly int[] EconomyStation =
        {
            IslandEconomy.Mine,      // maden
            IslandEconomy.Storage,   // depo
            IslandEconomy.Smelter,   // rafineri
            IslandEconomy.Power,     // liman — the port's globals sit in the legacy power slot
            IslandEconomy.Market,    // pazar
        };

        /// <summary>
        /// Which roster station a saved economy slot belongs to, or -1 for one no master runs. The
        /// island walks all eight legacy slots when it posts bodies, and three of them — the train and
        /// the two truck fleets — have no master, so this is what tells it to leave that post empty
        /// rather than to guess. The inverse of <see cref="EconomyStation"/>.
        /// </summary>
        public static int RosterStationOf(int economySlot)
        {
            for (int s = 0; s < StationCount; s++)
                if (EconomyStation[s] == economySlot) return s;
            return -1;
        }

        /// <summary>Loc suffixes for the five stations, used by <c>usta.istasyon.*</c>.</summary>
        public static readonly string[] StationIds = { "mine", "deposit", "refinery", "port", "market" };

        /// <summary>One master: who he is, where he works, and what he is worth being.</summary>
        public struct Card
        {
            /// <summary>Loc id. Stays lower-case ASCII because it is also the key into the table.</summary>
            public string Id;
            public int Station;
            public Rarity Rank;
        }

        /// <summary>
        /// Everyone who can be found, station-major and rarity-ascending inside each station, so
        /// <see cref="IndexOf"/> is arithmetic rather than a search. Saves address these positions.
        /// </summary>
        public static readonly Card[] Roster =
        {
            new Card { Id = "hasan",  Station = Mine,     Rank = Rarity.Common    },
            new Card { Id = "sukru",  Station = Mine,     Rank = Rarity.Rare      },
            new Card { Id = "nazmi",  Station = Mine,     Rank = Rarity.Legendary },

            new Card { Id = "bekir",  Station = Deposit,  Rank = Rarity.Common    },
            new Card { Id = "necla",  Station = Deposit,  Rank = Rarity.Rare      },
            new Card { Id = "rasim",  Station = Deposit,  Rank = Rarity.Legendary },

            new Card { Id = "fikri",  Station = Refinery, Rank = Rarity.Common    },
            new Card { Id = "sema",   Station = Refinery, Rank = Rarity.Rare      },
            new Card { Id = "zeki",   Station = Refinery, Rank = Rarity.Legendary },

            new Card { Id = "cemil",  Station = Port,     Rank = Rarity.Common    },
            new Card { Id = "sedef",  Station = Port,     Rank = Rarity.Rare      },
            new Card { Id = "mahmut", Station = Port,     Rank = Rarity.Legendary },

            new Card { Id = "riza",   Station = Market,   Rank = Rarity.Common    },
            new Card { Id = "leyla",  Station = Market,   Rank = Rarity.Rare      },
            new Card { Id = "hikmet", Station = Market,   Rank = Rarity.Legendary },
        };

        // ------------------------------------------------------------------ tuning
        /// <summary>Everything a designer can move, so the numbers below are defaults rather than
        /// decisions. Mirrors the shape of <see cref="IslandEconomy.Tuning"/>.</summary>
        public struct Tuning
        {
            /// <summary>What a MAXED master of each rarity adds to his station's throughput, as a
            /// fraction on top of 1.0. Stars pay a fifth of this each — linear on purpose, so "three
            /// of five stars" reads as three fifths of the card and not as a curve to look up.</summary>
            public double ThroughputCommon, ThroughputRare, ThroughputLegendary;

            /// <summary>Percentage points a maxed master of each rarity adds to offline efficiency.
            /// On its OWN scale rather than derived from the throughput above, because efficiency is
            /// a fraction of one and throughput is an unbounded multiplier — deriving one from the
            /// other put five Legendaries past 100% efficiency, which pays more for being away.</summary>
            public double OfflineCommon, OfflineRare, OfflineLegendary;

            /// <summary>How much of a master's throughput also lifts the empire income ceiling. The
            /// whole boost would be enormous across five stations; a tenth of it is the half that
            /// pays.</summary>
            public double IncomeShare;

            /// <summary>Cards to go from star S to S+1: Base + Step * (S - 1), before the rarity
            /// scale below.</summary>
            public int CardBase, CardStep;

            /// <summary>
            /// The curve above, scaled per rarity. A RARER MASTER COSTS MORE, which is the opposite of
            /// what <see cref="Captains"/> does and is right for the opposite reason: a captain's grade
            /// is how hard he is to FIND, so the rare one has to be cheap to raise or he is never
            /// raised at all. A master's rarity is how good he is, and every one of the fifteen drops
            /// often enough to be finishable — so the Legendary paying double is the only thing making
            /// his tier a road rather than a coupon.
            /// </summary>
            public double CardScaleCommon, CardScaleRare, CardScaleLegendary;

            public static Tuning Default => new Tuning
            {
                // A maxed Legendary is +400% at his station and the Common he replaced was +50%. The
                // spread is what makes a Legendary worth chasing; the floor is what keeps a Common
                // worth starring up while you wait for one.
                ThroughputCommon    = 0.50d,
                ThroughputRare      = 1.50d,
                ThroughputLegendary = 4.00d,

                // Five maxed Legendaries land the empire at 1 + 5(4.0 x 0.10) = 3.0x, which is where
                // the eight-master roster it replaced was solved. Prestige used to hand out 70x at
                // coal and the economy pass measured that as the thing breaking the ladder; this stays
                // an order of magnitude below it and is earned over months.
                IncomeShare = 0.10d,

                // Five maxed Legendaries add 100 points to offline efficiency, which the config caps
                // rather than this file: a bonus is allowed to be big, it is not allowed to make being
                // away better than playing. See OfflineConfig.
                OfflineCommon    = 0.05d,
                OfflineRare      = 0.10d,
                OfflineLegendary = 0.20d,

                // 5,10,15,20 = 50 cards to max a Common, four star-ups rather than nine. Rare is 70
                // and Legendary 100 after the scales below.
                CardBase = 5, CardStep = 5,

                CardScaleCommon    = 1.00d,
                CardScaleRare      = 1.40d,
                CardScaleLegendary = 2.00d,
            };
        }

        // -------------------------------------------------------------------- read
        public static bool Exists(int master) => master >= 0 && master < Roster.Length;

        /// <summary>Where in the roster the station's card of this rarity sits. Arithmetic, because
        /// <see cref="Roster"/> is station-major and rarity-ascending.</summary>
        public static int IndexOf(int station, Rarity rank)
        {
            if (station < 0 || station >= StationCount) return -1;
            return station * PerStation + (int)rank;
        }

        public static int StationOf(int master) => Exists(master) ? Roster[master].Station : Mine;

        public static Rarity RankOf(int master) => Exists(master) ? Roster[master].Rank : Rarity.Common;

        /// <summary>Loc id, or "" for an index off the roster.</summary>
        public static string IdOf(int master) => Exists(master) ? Roster[master].Id : string.Empty;

        /// <summary>
        /// The rarity as the shared card grammar spells it. <see cref="RosterCardState.Rarity"/> runs
        /// to five because the captains do; a master's Legendary is that enum's Legendary and not its
        /// Epic, so the colour a card is drawn in matches the word printed on it.
        /// </summary>
        public static RosterCardState.Rarity CardRarity(Rarity rank)
            => rank == Rarity.Legendary ? RosterCardState.Rarity.Legendary
             : rank == Rarity.Rare      ? RosterCardState.Rarity.Rare
             :                            RosterCardState.Rarity.Common;

        // ------------------------------------------------------------------ worth
        /// <summary>What a MAXED master of this rarity is worth at the named skill.</summary>
        public static double SkillAtMax(Rarity rank, Skill skill, in Tuning t)
        {
            switch (skill)
            {
                case Skill.Throughput:
                    return rank == Rarity.Legendary ? t.ThroughputLegendary
                         : rank == Rarity.Rare      ? t.ThroughputRare
                         :                            t.ThroughputCommon;
                case Skill.Offline:
                    return rank == Rarity.Legendary ? t.OfflineLegendary
                         : rank == Rarity.Rare      ? t.OfflineRare
                         :                            t.OfflineCommon;
                default:
                    // Income is a share of throughput rather than a table of its own, so a designer
                    // moving a station's worth cannot forget to move what it pays the empire.
                    return SkillAtMax(rank, Skill.Throughput, t) * Math.Max(0d, t.IncomeShare);
            }
        }

        /// <summary>
        /// What this master at <paramref name="stars"/> is worth at the named skill. Zero for an empty
        /// slot, so an empty roster changes nothing anywhere and the whole feature can be switched off
        /// by leaving it empty. Clamped at both ends — a hand-edited save must not be able to buy
        /// itself a bigger multiplier than a maxed Legendary.
        /// </summary>
        public static double SkillValue(int master, int stars, Skill skill, in Tuning t)
        {
            if (!Exists(master) || stars <= NotHired) return 0d;
            if (stars > MaxStars) stars = MaxStars;
            return SkillAtMax(RankOf(master), skill, t) * stars / MaxStars;
        }

        // ------------------------------------------------------------------ active
        /// <summary>
        /// Which master is working at <paramref name="station"/>, or -1 for nobody. Validates rather
        /// than trusts: an index off the roster, one belonging to another station, or one nobody owns
        /// all read as an empty post, so a hand-edited save cannot put a Legendary to work at a
        /// station he was never drawn for.
        /// </summary>
        public static int ActiveAt(int[] active, int[] stars, int station)
        {
            if (active == null || station < 0 || station >= StationCount) return -1;
            if (station >= active.Length) return -1;
            int master = active[station];
            if (!Exists(master) || StationOf(master) != station) return -1;
            return StarsOf(stars, master) > NotHired ? master : -1;
        }

        /// <summary>
        /// Whoever should be working at this station when nobody has been picked: the best owned card
        /// there, rarest first and then most stars. A roster that starts every station empty until the
        /// player opens a screen would hide the whole feature behind a menu.
        /// </summary>
        public static int BestOwnedAt(int[] stars, int station)
        {
            int pick = -1;
            for (int r = PerStation - 1; r >= 0; r--)
            {
                int master = IndexOf(station, (Rarity)r);
                if (master < 0 || StarsOf(stars, master) <= NotHired) continue;
                pick = master;
                break;
            }
            return pick;
        }

        /// <summary>One station's throughput multiplier. 1.0 when nobody is working there.</summary>
        public static double StationMultiplier(int[] active, int[] stars, int station, in Tuning t)
        {
            int master = ActiveAt(active, stars, station);
            return master < 0 ? 1d : 1d + SkillValue(master, StarsOf(stars, master), Skill.Throughput, t);
        }

        /// <summary>
        /// What the whole roster is worth to income, as one multiplier on the empire. This is the half
        /// that survives the per-island income cap — see the class summary. Only the five active
        /// masters pay it; a card sitting in the collection is worth nothing until you post him.
        /// </summary>
        public static double IncomeMultiplier(int[] active, int[] stars, in Tuning t)
        {
            double sum = 0d;
            for (int s = 0; s < StationCount; s++)
            {
                int master = ActiveAt(active, stars, s);
                if (master >= 0) sum += SkillValue(master, StarsOf(stars, master), Skill.Income, t);
            }
            return 1d + sum;
        }

        /// <summary>Percentage points the five active masters add to offline efficiency.</summary>
        public static double OfflineBonus(int[] active, int[] stars, in Tuning t)
        {
            double sum = 0d;
            for (int s = 0; s < StationCount; s++)
            {
                int master = ActiveAt(active, stars, s);
                if (master >= 0) sum += SkillValue(master, StarsOf(stars, master), Skill.Offline, t);
            }
            return sum;
        }

        // ------------------------------------------------------------------ price
        /// <summary>
        /// Cards to take <paramref name="master"/> from <paramref name="stars"/> to the next star.
        /// 0 at the ceiling, and never less than one — a star that costs nothing is not a star.
        /// </summary>
        public static int CardsToStar(int master, int stars, in Tuning t)
        {
            if (!Exists(master) || stars < 1 || stars >= MaxStars) return 0;
            double curve = Math.Max(1, t.CardBase) + Math.Max(0, t.CardStep) * (stars - 1);
            double scale = CardScale(RankOf(master), t);
            int n = (int)Math.Round(curve * scale, MidpointRounding.AwayFromZero);
            return n < 1 ? 1 : n;
        }

        private static double CardScale(Rarity rank, in Tuning t)
        {
            double scale = rank == Rarity.Legendary ? t.CardScaleLegendary
                         : rank == Rarity.Rare      ? t.CardScaleRare
                         :                            t.CardScaleCommon;
            return scale <= 0d ? 1d : scale;
        }

        /// <summary>Every card a master will ever need, for a progress readout that does not lie about
        /// how long the road is.</summary>
        public static int CardsToMax(int master, in Tuning t)
        {
            int total = 0;
            for (int s = 1; s < MaxStars; s++) total += CardsToStar(master, s, t);
            return total;
        }

        // ------------------------------------------------------------------ state
        public static int StarsOf(int[] stars, int master)
        {
            if (stars == null || master < 0 || master >= stars.Length) return NotHired;
            int n = stars[master];
            if (n < NotHired) return NotHired;
            return n > MaxStars ? MaxStars : n;
        }

        public static bool IsHired(int[] stars, int master) => StarsOf(stars, master) > NotHired;

        public static bool IsMaxed(int[] stars, int master) => StarsOf(stars, master) >= MaxStars;

        /// <summary>A fresh, empty roster.</summary>
        public static int[] NewStars() => new int[Count];

        /// <summary>A fresh posting board: nobody at any station.</summary>
        public static int[] NewActive()
        {
            var active = new int[StationCount];
            for (int s = 0; s < StationCount; s++) active[s] = -1;
            return active;
        }

        /// <summary>True once every card is found and starred out — the end of the collection.</summary>
        public static bool RosterComplete(int[] stars)
        {
            if (stars == null || stars.Length < Count) return false;
            for (int m = 0; m < Count; m++)
                if (stars[m] < MaxStars) return false;
            return true;
        }

        /// <summary>How many masters you have at all. The number worth putting on a collection screen.</summary>
        public static int HiredCount(int[] stars)
        {
            if (stars == null) return 0;
            int n = 0, len = stars.Length < Count ? stars.Length : Count;
            for (int m = 0; m < len; m++) if (stars[m] > NotHired) n++;
            return n;
        }
    }
}
