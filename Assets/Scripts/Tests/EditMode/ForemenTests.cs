using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class ForemenTests
    {
        private static Foremen.Tuning T => Foremen.Tuning.Default;

        private static int[] NoStars() => Foremen.NewStars();

        /// <summary>A roster holding one master at some stars, posted at his own station.</summary>
        private static void One(int master, int stars, out int[] active, out int[] star)
        {
            star = Foremen.NewStars();
            star[master] = stars;
            active = Foremen.NewActive();
            active[Foremen.StationOf(master)] = master;
        }

        private static int Legendary(int station) => Foremen.IndexOf(station, Foremen.Rarity.Legendary);

        // ---- an empty roster must change nothing anywhere ----------------------------------------

        [Test]
        public void EmptyRoster_PaysNothing()
        {
            int[] none = Foremen.NewActive();
            Assert.That(Foremen.IncomeMultiplier(none, NoStars(), T), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Foremen.OfflineBonus(none, NoStars(), T), Is.EqualTo(0d).Within(1e-9));
            for (int s = 0; s < Foremen.StationCount; s++)
                Assert.That(Foremen.StationMultiplier(none, NoStars(), s, T), Is.EqualTo(1d).Within(1e-9),
                            "station " + s);
        }

        [Test]
        public void NullRoster_IsTreatedAsEmpty()
        {
            Assert.That(Foremen.IncomeMultiplier(null, null, T), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Foremen.StationMultiplier(null, null, Foremen.Mine, T), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Foremen.OfflineBonus(null, null, T), Is.EqualTo(0d).Within(1e-9));
            Assert.That(Foremen.HiredCount(null), Is.Zero);
            Assert.That(Foremen.ActiveAt(null, null, Foremen.Mine), Is.EqualTo(-1));
        }

        // ---- the roster is five stations of three ------------------------------------------------

        [Test]
        public void TheRoster_IsThreeMastersAtEachOfFiveStations()
        {
            Assert.That(Foremen.Roster.Length, Is.EqualTo(Foremen.Count));
            Assert.That(Foremen.Count, Is.EqualTo(Foremen.StationCount * Foremen.PerStation));

            for (int s = 0; s < Foremen.StationCount; s++)
                for (int r = 0; r < Foremen.RarityCount; r++)
                {
                    int master = Foremen.IndexOf(s, (Foremen.Rarity)r);
                    Assert.That(Foremen.StationOf(master), Is.EqualTo(s), "station of " + master);
                    Assert.That((int)Foremen.RankOf(master), Is.EqualTo(r), "rarity of " + master);
                }
        }

        [Test]
        public void EveryMaster_HasHisOwnLocId()
        {
            // The loc table keys off these and the roster screen prints them. Two masters sharing an
            // id would silently draw the same name on two cards.
            for (int a = 0; a < Foremen.Count; a++)
            {
                Assert.That(Foremen.IdOf(a), Is.Not.Empty, "master " + a);
                for (int b = a + 1; b < Foremen.Count; b++)
                    Assert.That(Foremen.IdOf(a), Is.Not.EqualTo(Foremen.IdOf(b)), "duplicate id at " + b);
            }
        }

        [Test]
        public void IndexOf_RejectsAStationOffTheRoster()
        {
            Assert.That(Foremen.IndexOf(-1, Foremen.Rarity.Common), Is.EqualTo(-1));
            Assert.That(Foremen.IndexOf(Foremen.StationCount, Foremen.Rarity.Common), Is.EqualTo(-1));
        }

        [Test]
        public void EveryRosterStation_MapsToOneEconomySlotAndBack()
        {
            // The island walks the eight legacy economy slots; this mapping is the only place the two
            // numbering schemes meet, so a drift here posts the mine master at the smelter.
            Assert.That(Foremen.EconomyStation.Length, Is.EqualTo(Foremen.StationCount));
            for (int s = 0; s < Foremen.StationCount; s++)
                Assert.That(Foremen.RosterStationOf(Foremen.EconomyStation[s]), Is.EqualTo(s));

            Assert.That(Foremen.RosterStationOf(IslandEconomy.Train), Is.EqualTo(-1),
                        "the train has no master");
            Assert.That(Foremen.RosterStationOf(-1), Is.EqualTo(-1));
        }

        // ---- rarity is drawn, not earned ---------------------------------------------------------

        [Test]
        public void RarityIsFixed_AndSurvivesEveryStar()
        {
            int master = Legendary(Foremen.Mine);
            for (int stars = 0; stars <= Foremen.MaxStars; stars++)
                Assert.That(Foremen.RankOf(master), Is.EqualTo(Foremen.Rarity.Legendary), "at " + stars);
        }

        [Test]
        public void ALegendaryMaster_IsDrawnAsLegendaryOnTheSharedCard()
        {
            // RosterCardState.Rarity runs to five because the captains do. A master's Legendary must
            // land on that enum's Legendary and not on its Epic, or the frame colour and the word
            // printed on the card disagree.
            Assert.That(Foremen.CardRarity(Foremen.Rarity.Common), Is.EqualTo(RosterCardState.Rarity.Common));
            Assert.That(Foremen.CardRarity(Foremen.Rarity.Rare), Is.EqualTo(RosterCardState.Rarity.Rare));
            Assert.That(Foremen.CardRarity(Foremen.Rarity.Legendary),
                        Is.EqualTo(RosterCardState.Rarity.Legendary));
        }

        // ---- what a star is worth ----------------------------------------------------------------

        [Test]
        public void LegendaryTopsOutAtFiveTimesOutput()
        {
            // +400% at the last star is the number the card advertises. A tuning change that moves it
            // is changing the promise.
            int master = Legendary(Foremen.Mine);
            Assert.That(Foremen.SkillValue(master, Foremen.MaxStars, Foremen.Skill.Throughput, T),
                        Is.EqualTo(4.00d).Within(1e-9));

            One(master, Foremen.MaxStars, out int[] active, out int[] star);
            Assert.That(Foremen.StationMultiplier(active, star, Foremen.Mine, T),
                        Is.EqualTo(5.00d).Within(1e-9));
        }

        [Test]
        public void EveryStar_IsWorthExactlyAFifthOfTheMaster()
        {
            for (int master = 0; master < Foremen.Count; master++)
            {
                double top = Foremen.SkillAtMax(Foremen.RankOf(master), Foremen.Skill.Throughput, T);
                for (int stars = 1; stars <= Foremen.MaxStars; stars++)
                    Assert.That(Foremen.SkillValue(master, stars, Foremen.Skill.Throughput, T),
                                Is.EqualTo(top * stars / Foremen.MaxStars).Within(1e-9),
                                "master " + master + " at " + stars);
            }
        }

        [Test]
        public void ARarerMaster_IsWorthMoreAtEveryStar()
        {
            for (int stars = 1; stars <= Foremen.MaxStars; stars++)
            {
                double common = Foremen.SkillValue(Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common),
                                                   stars, Foremen.Skill.Throughput, T);
                double rare = Foremen.SkillValue(Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Rare),
                                                 stars, Foremen.Skill.Throughput, T);
                double legendary = Foremen.SkillValue(Legendary(Foremen.Mine),
                                                      stars, Foremen.Skill.Throughput, T);
                Assert.That(rare, Is.GreaterThan(common), "at " + stars);
                Assert.That(legendary, Is.GreaterThan(rare), "at " + stars);
            }
        }

        [Test]
        public void AFreshLegendary_BeatsAMaxedCommon()
        {
            // This is the whole point of a drawn rarity: the card you find is an upgrade over the card
            // you ground. If it ever stops being true, the Legendary is a cosmetic.
            double fresh = Foremen.SkillValue(Legendary(Foremen.Mine), 1, Foremen.Skill.Throughput, T);
            double ground = Foremen.SkillValue(Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common),
                                               Foremen.MaxStars, Foremen.Skill.Throughput, T);
            Assert.That(fresh, Is.GreaterThan(ground));
        }

        [Test]
        public void IncomeIsAShareOfThroughput_NotASecondTable()
        {
            int master = Legendary(Foremen.Refinery);
            Assert.That(Foremen.SkillValue(master, Foremen.MaxStars, Foremen.Skill.Income, T),
                        Is.EqualTo(Foremen.SkillValue(master, Foremen.MaxStars, Foremen.Skill.Throughput, T)
                                   * T.IncomeShare).Within(1e-9));
        }

        [Test]
        public void AMaster_OnlySpeedsTheirOwnStation()
        {
            One(Legendary(Foremen.Refinery), Foremen.MaxStars, out int[] active, out int[] star);
            Assert.That(Foremen.StationMultiplier(active, star, Foremen.Refinery, T), Is.GreaterThan(1d));
            Assert.That(Foremen.StationMultiplier(active, star, Foremen.Mine, T),
                        Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void AllLegendary_LandsOnTheIntendedSecondGear()
        {
            // The roster replaced a retired prestige that handed out 70x at coal, which the economy
            // pass measured as the thing breaking the ladder. Five posted Legendaries must land where
            // the eight-master roster did — 3.0x — because that is where the ladder was solved.
            var star = Foremen.NewStars();
            var active = Foremen.NewActive();
            for (int s = 0; s < Foremen.StationCount; s++)
            {
                int master = Legendary(s);
                star[master] = Foremen.MaxStars;
                active[s] = master;
            }
            Assert.That(Foremen.IncomeMultiplier(active, star, T), Is.EqualTo(3.0d).Within(0.05d));
            Assert.That(Foremen.OfflineBonus(active, star, T), Is.EqualTo(1.0d).Within(1e-9));
        }

        [Test]
        public void ABenchedMaster_PaysNothing()
        {
            // You own as many as the chests give you and put ONE of the three to work. A benched card
            // that still paid would triple every station bonus and make posting meaningless.
            var star = Foremen.NewStars();
            for (int m = 0; m < Foremen.Count; m++) star[m] = Foremen.MaxStars;

            var active = Foremen.NewActive();
            active[Foremen.Mine] = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common);
            Assert.That(Foremen.StationMultiplier(active, star, Foremen.Mine, T),
                        Is.EqualTo(1d + T.ThroughputCommon).Within(1e-9));
        }

        // ---- posting is validated, not trusted ---------------------------------------------------

        [Test]
        public void APostingIsRefused_ForAMasterOfAnotherStation()
        {
            var star = Foremen.NewStars();
            for (int m = 0; m < Foremen.Count; m++) star[m] = Foremen.MaxStars;

            var active = Foremen.NewActive();
            active[Foremen.Mine] = Legendary(Foremen.Market);      // hand-edited save
            Assert.That(Foremen.ActiveAt(active, star, Foremen.Mine), Is.EqualTo(-1));
            Assert.That(Foremen.StationMultiplier(active, star, Foremen.Mine, T),
                        Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void APostingIsRefused_ForAMasterNobodyOwns()
        {
            var active = Foremen.NewActive();
            active[Foremen.Mine] = Legendary(Foremen.Mine);
            Assert.That(Foremen.ActiveAt(active, NoStars(), Foremen.Mine), Is.EqualTo(-1));
        }

        [Test]
        public void APostingIsRefused_ForAnIndexOffTheRoster()
        {
            var star = Foremen.NewStars();
            star[0] = 1;
            var active = Foremen.NewActive();
            foreach (int bad in new[] { Foremen.Count, Foremen.Count + 50, -7 })
            {
                active[Foremen.Mine] = bad;
                Assert.That(Foremen.ActiveAt(active, star, Foremen.Mine), Is.EqualTo(-1), "index " + bad);
            }
        }

        [Test]
        public void ANewPostingBoard_HasNobodyAnywhere()
        {
            // Slot 0 is a real master, so a zeroed array would post the Common mine master at every
            // station the moment a save arrived without this field.
            int[] active = Foremen.NewActive();
            Assert.That(active.Length, Is.EqualTo(Foremen.StationCount));
            for (int s = 0; s < Foremen.StationCount; s++) Assert.That(active[s], Is.EqualTo(-1));
        }

        [Test]
        public void BestOwned_PicksTheRarestCardYouHaveThere()
        {
            var star = Foremen.NewStars();
            Assert.That(Foremen.BestOwnedAt(star, Foremen.Port), Is.EqualTo(-1), "nothing owned");

            star[Foremen.IndexOf(Foremen.Port, Foremen.Rarity.Common)] = Foremen.MaxStars;
            Assert.That(Foremen.BestOwnedAt(star, Foremen.Port),
                        Is.EqualTo(Foremen.IndexOf(Foremen.Port, Foremen.Rarity.Common)));

            star[Legendary(Foremen.Port)] = 1;
            Assert.That(Foremen.BestOwnedAt(star, Foremen.Port), Is.EqualTo(Legendary(Foremen.Port)),
                        "a one-star Legendary still beats a maxed Common");
        }

        // ---- stars are clamped, not trusted ------------------------------------------------------

        [Test]
        public void StarsAboveMax_AreClamped()
        {
            int master = Legendary(Foremen.Mine);
            One(master, Foremen.MaxStars, out int[] honestActive, out int[] honest);
            One(master, Foremen.MaxStars * 100, out int[] _, out int[] tampered);

            Assert.That(Foremen.IncomeMultiplier(honestActive, tampered, T),
                        Is.EqualTo(Foremen.IncomeMultiplier(honestActive, honest, T)).Within(1e-9));
            Assert.That(Foremen.StarsOf(tampered, master), Is.EqualTo(Foremen.MaxStars));
        }

        [Test]
        public void NegativeStars_ReadAsUnhired()
        {
            One(Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common), -4,
                out int[] active, out int[] star);
            Assert.That(Foremen.StarsOf(star, 0), Is.EqualTo(Foremen.NotHired));
            Assert.That(Foremen.IsHired(star, 0), Is.False);
            Assert.That(Foremen.IncomeMultiplier(active, star, T), Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void ShortRoster_DoesNotThrow()
        {
            // A save written before the roster existed arrives short; the service pads it, but the
            // maths must survive being handed one anyway.
            var stunted = new int[2];
            var active = Foremen.NewActive();
            Assert.That(Foremen.IncomeMultiplier(active, stunted, T), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Foremen.StationMultiplier(active, stunted, Foremen.Market, T),
                        Is.EqualTo(1d).Within(1e-9));
            Assert.That(Foremen.ActiveAt(new int[1], stunted, Foremen.Market), Is.EqualTo(-1));
        }

        // ---- the cost of the road ----------------------------------------------------------------

        [Test]
        public void StarringUp_GetsDearerEveryStar()
        {
            for (int master = 0; master < Foremen.Count; master++)
            {
                int prev = 0;
                for (int stars = 1; stars < Foremen.MaxStars; stars++)
                {
                    int cards = Foremen.CardsToStar(master, stars, T);
                    Assert.That(cards, Is.GreaterThan(prev), "master " + master + " star " + stars);
                    prev = cards;
                }
            }
        }

        [Test]
        public void AMaxedMaster_CostsNothingFurther()
        {
            for (int master = 0; master < Foremen.Count; master++)
                Assert.That(Foremen.CardsToStar(master, Foremen.MaxStars, T), Is.Zero, "master " + master);
        }

        [Test]
        public void ARarerMaster_CostsMoreToFinish()
        {
            // The opposite of what Captains does, and right for the opposite reason — see the tuning's
            // own note. If this ever inverts, a Legendary becomes a coupon rather than a road.
            int common = Foremen.CardsToMax(Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common), T);
            int rare = Foremen.CardsToMax(Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Rare), T);
            int legendary = Foremen.CardsToMax(Legendary(Foremen.Mine), T);

            Assert.That(common, Is.EqualTo(50));
            Assert.That(rare, Is.GreaterThan(common));
            Assert.That(legendary, Is.GreaterThan(rare));
            Assert.That(legendary, Is.EqualTo(100));
        }

        [Test]
        public void CardsToMax_IsTheSumOfEveryStep()
        {
            for (int master = 0; master < Foremen.Count; master++)
            {
                int sum = 0;
                for (int stars = 1; stars < Foremen.MaxStars; stars++)
                    sum += Foremen.CardsToStar(master, stars, T);
                Assert.That(Foremen.CardsToMax(master, T), Is.EqualTo(sum), "master " + master);
            }
        }

        // ---- completion --------------------------------------------------------------------------

        [Test]
        public void RosterComplete_OnlyWhenEveryCardIsMaxed()
        {
            var star = Foremen.NewStars();
            for (int m = 0; m < Foremen.Count; m++) star[m] = Foremen.MaxStars;
            Assert.That(Foremen.RosterComplete(star), Is.True);
            Assert.That(Foremen.HiredCount(star), Is.EqualTo(Foremen.Count));

            star[Legendary(Foremen.Market)] = Foremen.MaxStars - 1;
            Assert.That(Foremen.RosterComplete(star), Is.False);
        }
    }
}
