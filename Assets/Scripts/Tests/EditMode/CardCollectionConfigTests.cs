using NUnit.Framework;
using UnityEngine;
using Game.Core;
using Game.Data;

namespace Game.Tests
{
    /// <summary>
    /// The config asset. Its serialised fields are a SECOND COPY of the defaults in
    /// <see cref="CardCollection.Tuning"/> and <see cref="CardCollectionPack.Tuning"/> — that is
    /// unavoidable, because a ScriptableObject's Inspector values have to live in its own fields —
    /// and two copies of a balance table drift. These tests are what stops them.
    ///
    /// A fresh instance stands in for a freshly created asset: <c>CreateInstance</c> gives every
    /// field its declared initialiser, which is exactly what Unity writes into a new .asset.
    /// </summary>
    public class CardCollectionConfigTests
    {
        private CardCollectionConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<CardCollectionConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void AFreshAssetCarriesExactlyTheShippedPackOdds()
        {
            CardCollectionPack.Tuning fromAsset = _config.ToPackTuning();
            CardCollectionPack.Tuning shipped = CardCollectionPack.Tuning.Default;

            Assert.That(fromAsset.CommonWeight, Is.EqualTo(shipped.CommonWeight).Within(1e-12));
            Assert.That(fromAsset.RareWeight, Is.EqualTo(shipped.RareWeight).Within(1e-12));
            Assert.That(fromAsset.EpicWeight, Is.EqualTo(shipped.EpicWeight).Within(1e-12));
            Assert.That(fromAsset.LegendaryWeight, Is.EqualTo(shipped.LegendaryWeight).Within(1e-12));
            Assert.That(fromAsset.MythicWeight, Is.EqualTo(shipped.MythicWeight).Within(1e-12));

            Assert.That(fromAsset.EpicPity, Is.EqualTo(shipped.EpicPity));
            Assert.That(fromAsset.LegendaryPity, Is.EqualTo(shipped.LegendaryPity));
            Assert.That(fromAsset.SoftPityStart, Is.EqualTo(shipped.SoftPityStart));
            Assert.That(fromAsset.SoftPityStep, Is.EqualTo(shipped.SoftPityStep).Within(1e-12));
        }

        [Test]
        public void AFreshAssetCarriesExactlyTheShippedCollectionTuning()
        {
            CardCollection.Tuning fromAsset = _config.ToTuning();
            CardCollection.Tuning shipped = CardCollection.Tuning.Default;

            Assert.That(fromAsset.DuplicateCurve, Is.EqualTo(shipped.DuplicateCurve));
            Assert.That(fromAsset.EffectPerLevel, Is.EqualTo(shipped.EffectPerLevel));
            Assert.That(fromAsset.OverflowGems, Is.EqualTo(shipped.OverflowGems));
            Assert.That(fromAsset.EffectCaps, Is.EqualTo(shipped.EffectCaps));
            Assert.That(fromAsset.CraftPointChanceCeiling,
                        Is.EqualTo(shipped.CraftPointChanceCeiling).Within(1e-12));
        }

        [Test]
        public void AFreshAssetProducesTheSameBalanceTableTheCatalogueWasApprovedAgainst()
        {
            // The end-to-end version of the two tests above: whatever the asset says, the completed
            // collection must still be worth what Docs/PLAN_14 published.
            CardCollection.Tuning t = _config.ToTuning();

            double income = 0d;
            for (int card = 0; card < CardCollectionCatalogue.Count; card++)
                if (CardCollectionCatalogue.EffectOf(card) == CardCollection.EffectKind.IncomeMultiplier)
                    income += CardCollectionCatalogue.EffectValue(card, CardCollection.MaxLevel, t);

            Assert.That(income, Is.EqualTo(0.160d).Within(1e-9));
            Assert.That(CardCollection.DuplicatesToMax(RosterCardState.Rarity.Common, t), Is.EqualTo(28));
            Assert.That(CardCollection.OverflowGems(RosterCardState.Rarity.Legendary, t), Is.EqualTo(40L));
        }

        [Test]
        public void TheArrayFieldsAreTheRightShape()
        {
            CardCollection.Tuning t = _config.ToTuning();

            Assert.That(t.DuplicateCurve.Length,
                        Is.EqualTo(CardCollection.RarityCount * CardCollection.LevelSteps),
                        "five rarities of four rungs");
            Assert.That(t.EffectPerLevel.Length,
                        Is.EqualTo(CardCollection.EffectKindCount * CardCollection.RarityCount),
                        "five effects of five rarities");
            Assert.That(t.EffectCaps.Length, Is.EqualTo(CardCollection.EffectKindCount));
            Assert.That(t.OverflowGems.Length, Is.EqualTo(CardCollection.RarityCount));
            Assert.That(_config.RarityTint.Length, Is.EqualTo(CardCollection.RarityCount),
                        "one tint per rung, and the word beside it comes from kaptan.derece.*");
        }

        [Test]
        public void TheDailyPackIsOneAndOnlyOne()
        {
            Assert.That(_config.DailyPacks, Is.EqualTo(1));
        }

        // ---- art ----------------------------------------------------------------------------------

        [Test]
        public void AnUnwiredAssetHasNoArtAndDoesNotThrowLookingForIt()
        {
            // The whole feature has to run before a single sprite is assigned — cards simply have
            // no faces. A null here is a missing picture, never a missing card.
            for (int card = 0; card < CardCollectionCatalogue.Count; card++)
                Assert.That(_config.FaceOf(CardCollectionCatalogue.IdOf(card)), Is.Null);

            for (int s = 0; s < CardCollectionCatalogue.SetCount; s++)
                Assert.That(_config.BannerOf(CardCollectionCatalogue.Sets[s].Id), Is.Null);

            Assert.That(_config.PackIcon, Is.Null);
            Assert.That(_config.CollectionIcon, Is.Null);
        }

        [Test]
        public void ArtLookupsRefuseNonsenseRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => _config.FaceOf(null));
            Assert.DoesNotThrow(() => _config.FaceOf(""));
            Assert.DoesNotThrow(() => _config.BannerOf(null));
            Assert.That(_config.FaceOf("no_such_set/no_such_card"), Is.Null);
        }
    }
}
