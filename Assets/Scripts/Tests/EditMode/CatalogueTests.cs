using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The production catalogue. Almost all of it is a transcription of the ten recipe assets and
    /// is asserted here so a retouched recipe cannot drift silently away from the screen that
    /// prints it — but the rule worth the file is the LOCK, which is transitive: a ruby ring is a
    /// gold bar and a cut ruby, so standing on Ruby Island with no gold is not enough, and the row
    /// has to name gold rather than ruby.
    /// </summary>
    public class CatalogueTests
    {
        private const int Coal = 0, Copper = 1, Iron = 2, Silver = 3,
                          Gold = 4, Ruby = 5, Emerald = 6, Diamond = 7;

        private static int Product(string key)
        {
            for (int e = Catalogue.OreCount; e < Catalogue.EntryCount; e++)
                if (Catalogue.KeyOf(e) == key) return e;
            Assert.Fail("no catalogue entry for " + key);
            return -1;
        }

        /// <summary>Owning exactly these rungs and nothing else.</summary>
        private static bool[] Owns(params int[] rungs)
        {
            var owned = new bool[Catalogue.OreCount];
            for (int i = 0; i < rungs.Length; i++) owned[rungs[i]] = true;
            return owned;
        }

        private static bool[] OwnsAll()
        {
            var owned = new bool[Catalogue.OreCount];
            for (int i = 0; i < owned.Length; i++) owned[i] = true;
            return owned;
        }

        // ---- the table ---------------------------------------------------------------------------

        [Test]
        public void TheLadderIsTheEightIslandKeys()
        {
            Assert.That(Catalogue.OreCount, Is.EqualTo(8));
            Assert.That(Catalogue.OreKeys,
                        Is.EqualTo(new[] { "coal", "copper", "iron", "silver",
                                           "gold", "ruby", "emerald", "diamond" }));
            Assert.That(Catalogue.EntryCount, Is.EqualTo(Catalogue.OreCount + Catalogue.ProductCount));
        }

        [Test]
        public void EveryEntryIsExactlyOneOfOreOrProduct()
        {
            for (int e = 0; e < Catalogue.EntryCount; e++)
                Assert.That(Catalogue.IsOre(e) != Catalogue.IsProduct(e), Is.True, "entry " + e);

            foreach (int off in new[] { -1, Catalogue.EntryCount, 999 })
            {
                Assert.That(Catalogue.IsOre(off), Is.False);
                Assert.That(Catalogue.IsProduct(off), Is.False);
                Assert.That(Catalogue.KeyOf(off), Is.Empty);
                Assert.That(Catalogue.IslandOf(off), Is.EqualTo(-1));
                Assert.That(Catalogue.InputCount(off), Is.Zero);
                Assert.That(Catalogue.InputAt(off, 0), Is.EqualTo(-1));
            }
        }

        [Test]
        public void EveryInputSitsAtALowerEntryIndexThanItsProduct()
        {
            // This is what makes IsDiscovered's recursion terminate — see the Catalogue header.
            // Authored data, so it is pinned rather than trusted.
            for (int e = 0; e < Catalogue.EntryCount; e++)
            {
                int n = Catalogue.InputCount(e);
                for (int i = 0; i < n; i++)
                {
                    int input = Catalogue.InputAt(e, i);
                    Assert.That(input, Is.InRange(0, Catalogue.EntryCount - 1));
                    Assert.That(input, Is.LessThan(e), Catalogue.KeyOf(e) + " input " + i);
                }
            }
        }

        [Test]
        public void OreIsDugRatherThanMade()
        {
            for (int e = 0; e < Catalogue.OreCount; e++)
            {
                Assert.That(Catalogue.InputCount(e), Is.Zero);
                Assert.That(Catalogue.SecondsOf(e), Is.Zero);
                Assert.That(Catalogue.IslandOf(e), Is.EqualTo(e), "an ore's island is its own rung");
                Assert.That(Catalogue.KeyOf(e), Is.EqualTo(Catalogue.OreKeys[e]));
            }
        }

        [Test]
        public void TheRecipesAreTheOnesTheAssetsAuthor()
        {
            int coke = Product("coke");
            Assert.That(Catalogue.InputCount(coke), Is.EqualTo(1));
            Assert.That(Catalogue.InputAt(coke, 0), Is.EqualTo(Coal));
            Assert.That(Catalogue.SecondsOf(coke), Is.EqualTo(1d));

            int steel = Product("steel_beam");
            Assert.That(Catalogue.InputCount(steel), Is.EqualTo(2), "steel is a combine recipe");
            Assert.That(Catalogue.InputAt(steel, 0), Is.EqualTo(Iron));
            Assert.That(Catalogue.InputAt(steel, 1), Is.EqualTo(Coal));
            Assert.That(Catalogue.SecondsOf(steel), Is.EqualTo(1.5d));

            int ring = Product("ruby_ring");
            Assert.That(Catalogue.InputAt(ring, 0), Is.EqualTo(Product("gold_bar")));
            Assert.That(Catalogue.InputAt(ring, 1), Is.EqualTo(Product("cut_ruby")));
            Assert.That(Catalogue.IslandOf(ring), Is.EqualTo(Ruby), "the ring is made on ruby");

            int crown = Product("diamond_crown");
            Assert.That(Catalogue.InputAt(crown, 0), Is.EqualTo(Product("gold_bar")));
            Assert.That(Catalogue.InputAt(crown, 1), Is.EqualTo(Product("polished_diamond")));
            Assert.That(Catalogue.SecondsOf(crown), Is.EqualTo(2.5d));
        }

        // ---- locked recipes ----------------------------------------------------------------------

        [Test]
        public void AnOreIsDiscoveredWithItsIslandAndNotBefore()
        {
            bool[] coalOnly = Owns(Coal);
            Assert.That(Catalogue.IsDiscovered(Coal, coalOnly), Is.True);
            for (int e = 1; e < Catalogue.OreCount; e++)
                Assert.That(Catalogue.IsDiscovered(e, coalOnly), Is.False, Catalogue.OreKeys[e]);
        }

        [Test]
        public void ASingleInputProductNeedsOnlyItsOwnIsland()
        {
            int coke = Product("coke");
            Assert.That(Catalogue.IsDiscovered(coke, Owns(Coal)), Is.True);
            Assert.That(Catalogue.IsDiscovered(coke, Owns(Copper)), Is.False);
            Assert.That(Catalogue.MissingIsland(coke, Owns(Copper)), Is.EqualTo(Coal));
        }

        [Test]
        public void ACombineRecipeNeedsEveryIngredientsIsland()
        {
            int steel = Product("steel_beam");
            Assert.That(Catalogue.IsDiscovered(steel, Owns(Iron)), Is.False, "steel still wants coal");
            Assert.That(Catalogue.MissingIsland(steel, Owns(Iron)), Is.EqualTo(Coal));
            Assert.That(Catalogue.IsDiscovered(steel, Owns(Iron, Coal)), Is.True);
        }

        [Test]
        public void ARubyRingWantsGoldEvenStandingOnRuby()
        {
            // The rule the whole file exists for: the lock resolves THROUGH the inputs, so a ring
            // is locked on Ruby Island alone, and the row names gold rather than ruby.
            int ring = Product("ruby_ring");
            bool[] rubyOnly = Owns(Ruby);
            Assert.That(Catalogue.IsDiscovered(ring, rubyOnly), Is.False);
            Assert.That(Catalogue.MissingIsland(ring, rubyOnly), Is.EqualTo(Gold));
            Assert.That(Catalogue.IsDiscovered(ring, Owns(Ruby, Gold)), Is.True);
        }

        [Test]
        public void TheMissingIslandIsTheLowestRungAndNotTheLastStep()
        {
            // A crown wants gold and diamond. Telling a player on silver to buy Diamond Island is
            // telling them the last step; gold is the next one, and it is what the row prints.
            int crown = Product("diamond_crown");
            Assert.That(Catalogue.MissingIsland(crown, Owns(Coal)), Is.EqualTo(Gold));
            Assert.That(Catalogue.MissingIsland(crown, Owns(Coal, Gold)), Is.EqualTo(Diamond));
            Assert.That(Catalogue.IsDiscovered(crown, Owns(Gold, Diamond)), Is.True);
        }

        [Test]
        public void MissingIslandIsMinusOneOnceAnEntryIsOpen()
        {
            bool[] all = OwnsAll();
            for (int e = 0; e < Catalogue.EntryCount; e++)
            {
                Assert.That(Catalogue.IsDiscovered(e, all), Is.True, Catalogue.KeyOf(e));
                Assert.That(Catalogue.MissingIsland(e, all), Is.EqualTo(-1), Catalogue.KeyOf(e));
            }
        }

        [Test]
        public void EveryEntryIsLockedAndNamesARungWithNothingOwned()
        {
            var none = new bool[Catalogue.OreCount];
            for (int e = 0; e < Catalogue.EntryCount; e++)
            {
                Assert.That(Catalogue.IsDiscovered(e, none), Is.False, Catalogue.KeyOf(e));
                Assert.That(Catalogue.MissingIsland(e, none),
                            Is.InRange(0, Catalogue.OreCount - 1), Catalogue.KeyOf(e));
            }
        }

        [Test]
        public void ANullOrShortOwnershipArrayReadsAsLockedRatherThanThrowing()
        {
            // The catalogue is a screen; one that asks before the world has loaded must draw locks.
            Assert.DoesNotThrow(() => Catalogue.IsDiscovered(Diamond, null));
            Assert.That(Catalogue.IsDiscovered(Coal, null), Is.False);
            Assert.That(Catalogue.IsDiscovered(Diamond, new[] { true, true }), Is.False);
            Assert.That(Catalogue.MissingIsland(Diamond, new[] { true, true }), Is.EqualTo(Diamond));
        }
    }
}
