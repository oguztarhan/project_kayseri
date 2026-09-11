using System.Reflection;
using Game.Core;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>
    /// Phase 5: every spend and every reward the player sees names the currency that moved and the
    /// amount that actually moved. Expected lines are built through <see cref="CurrencyText"/> in the
    /// editor's current language, so the checks hold whichever language the machine runs in; the
    /// first two pin the literal Turkish and English wording.
    /// </summary>
    public sealed class CurrencyFeedbackTests
    {
        private GameObject _host;
        private Texture2D _texture;
        private LocalizationService _loc;
        private string _originalLanguage;

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
            _loc = new LocalizationService();
            _originalLanguage = _loc.Code;
            ServiceLocator.Register(_loc);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            if (_texture != null) Object.DestroyImmediate(_texture);
            _loc.SetLanguage(_originalLanguage);   // SetLanguage writes PlayerPrefs
            ServiceLocator.Clear();
        }

        // ------------------------------------------------------------------ wording

        [Test]
        public void EnglishReceiptsNameTheCurrencyAndTheAmount()
        {
            _loc.SetLanguage("en");
            Assert.That(CurrencyText.Gain(CurrencyId.Salvage, 12L), Is.EqualTo("+12 SALVAGE"));
            Assert.That(CurrencyText.Cost(CurrencyId.MiningScrap, 40L), Is.EqualTo("-40 MINING SCRAP"));
            Assert.That(CurrencyText.Cost(CurrencyId.MiningPoints, 3L), Is.EqualTo("-3 MINING POINTS"));
            Assert.That(CurrencyText.Amount(CurrencyId.Charts, 100L), Is.EqualTo("100 CHARTS"));
            Assert.That(CurrencyText.Amount(CurrencyId.CraftPoints, 3L), Is.EqualTo("3 CRAFT POINTS"));
            Assert.That(CurrencyText.Gain(CurrencyId.Pearls, 2L), Is.EqualTo("+2 PEARLS"));
        }

        [Test]
        public void TurkishReceiptsKeepSalvageAndMiningScrapApart()
        {
            _loc.SetLanguage("tr");
            Assert.That(CurrencyText.Gain(CurrencyId.Salvage, 12L), Is.EqualTo("+12 HURDA"));
            Assert.That(CurrencyText.Cost(CurrencyId.MiningScrap, 40L), Is.EqualTo("-40 MADEN HURDASI"));
            Assert.That(CurrencyText.Amount(CurrencyId.CraftPoints, 3L), Is.EqualTo("3 ZANAAT PUANI"));
            Assert.That(CurrencyText.Amount(CurrencyId.MiningPoints, 3L), Is.EqualTo("3 MADENCİLİK PUANI"));
            Assert.That(string.Format(_loc.Get("wallet.next_regen"), "4:05"), Is.EqualTo("4:05 sonra +1"));
        }

        // ------------------------------------------------------------ mining gear

        [Test]
        public void ANormalCraftThatEquipsNamesThePointsItSpent()
        {
            MiningGearUI ui = Mining(new SaveData { miningPoints = 50L }, out MiningGearService mining);

            Invoke(ui, "OnCraft");

            string line = Result(ui);
            Assert.That(line, Does.Contain(CurrencyText.Cost(CurrencyId.MiningPoints, mining.CraftCost)));
            Assert.That(line, Does.Not.Contain(CurrencyText.Name(CurrencyId.MiningScrap)),
                        "an upgrade into an empty slot moves no scrap and must not claim to");
        }

        [Test]
        public void ANormalCraftThatScrapsNamesThePointsAndTheScrapRefund()
        {
            SaveData data = Maxed(new SaveData { miningPoints = 50L });
            MiningGearUI ui = Mining(data, out MiningGearService mining);
            long scrapBefore = mining.Scrap;

            Invoke(ui, "OnCraft");

            string line = Result(ui);
            Assert.That(line, Does.Contain(CurrencyText.Cost(CurrencyId.MiningPoints, mining.CraftCost)));
            Assert.That(line, Does.Contain(CurrencyText.Gain(CurrencyId.MiningScrap, mining.Scrap - scrapBefore)));
        }

        [Test]
        public void ATargetedCraftThatEquipsNamesBothFees()
        {
            MiningGearUI ui = Mining(new SaveData { miningPoints = 50L, miningScrap = 500L },
                                     out MiningGearService mining);
            long fee = mining.TargetedScrapCost(0);

            Invoke(ui, "OnTargetedCraft");

            string line = Result(ui);
            Assert.That(line, Does.Contain(CurrencyText.Cost(CurrencyId.MiningPoints, mining.CraftCost)));
            Assert.That(line, Does.Contain(CurrencyText.Cost(CurrencyId.MiningScrap, fee)));
        }

        /// <summary>The path that used to throw: its line asked for four arguments and got three.</summary>
        [Test]
        public void ATargetedCraftThatScrapsNamesBothFeesAndTheRefundWithoutThrowing()
        {
            SaveData data = Maxed(new SaveData { miningPoints = 50L, miningScrap = 5000L });
            MiningGearUI ui = Mining(data, out MiningGearService mining);
            long fee = mining.TargetedScrapCost(0);
            long scrapBefore = mining.Scrap;

            Assert.DoesNotThrow(() => Invoke(ui, "OnTargetedCraft"));

            long refund = mining.Scrap - scrapBefore + fee;
            string line = Result(ui);
            Assert.That(line, Does.Contain(CurrencyText.Cost(CurrencyId.MiningPoints, mining.CraftCost)));
            Assert.That(line, Does.Contain(CurrencyText.Cost(CurrencyId.MiningScrap, fee)));
            Assert.That(line, Does.Contain(CurrencyText.Gain(CurrencyId.MiningScrap, refund)));
        }

        [Test]
        public void TheMiningCraftButtonPricesItselfInMiningPoints()
        {
            MiningGearUI ui = Mining(new SaveData { miningPoints = 50L }, out MiningGearService mining);
            Text craft = (Text)Field(ui, "_craftLabel");
            Assert.That(craft.text, Does.Contain(CurrencyText.Amount(CurrencyId.MiningPoints, mining.CraftCost)));
            Assert.That(craft.resizeTextForBestFit, Is.True, "the longer price must shrink, not spill off the pill");
            Assert.That(craft.rectTransform.anchorMin.x, Is.GreaterThan(0f), "the label must sit inside the pill's end caps");
            Assert.That(craft.rectTransform.anchorMax.x, Is.LessThan(1f), "the label must sit inside the pill's end caps");
        }

        // ---------------------------------------------------------------- the sea

        [Test]
        public void AWinsReceiptIsWhatWasBankedAfterTheCollectionsLift()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var captains = new CaptainService(data, Captains.Tuning.Default, CaptainCrate.Tuning.Default);
            Crafting.Tuning bench = Crafting.Tuning.Default;
            bench.PointDropChance = 1d;   // make the point drop certain so its receipt is always exercised
            var sea = new ExpeditionService(new TimeService(), data, captains, SeaCombat.Tuning.Default)
            {
                Cards = MaxedCollection(data, wallet),
                Crafting = new CraftingService(data, null, new TimeService(), bench),
                Pets = new PetService(data, Pets.Tuning.Default, PetChest.Tuning.Default),
            };
            sea.SetSail("coal");

            long charts = captains.Charts, salvage = data.salvage, points = data.craftPoints, pearls = data.pearls;
            Assert.That(sea.RegisterKill(100, 200), Is.True);
            sea.RegisterWin(0, 0);

            Assert.That(sea.LastKillCharts, Is.EqualTo(captains.Charts - charts).And.GreaterThan(100L),
                        "the receipt must carry the collection's lift, not the rolled base");
            Assert.That(sea.LastKillSalvage, Is.EqualTo(data.salvage - salvage).And.GreaterThan(200L));
            Assert.That(sea.LastKillCraftPoints, Is.EqualTo(data.craftPoints - points).And.GreaterThan(0L));
            Assert.That(sea.LastWinPearls, Is.EqualTo(data.pearls - pearls).And.GreaterThan(0L));
        }

        [Test]
        public void TheWinBannerNamesEveryBalanceThatMovedAndNothingThatDidNot()
        {
            MethodInfo receipt = typeof(SeaFightUI).GetMethod("WinReceipt", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(receipt, Is.Not.Null);

            var all = (string)receipt.Invoke(null, new object[] { 3L, 12L, 1L, 2L });
            Assert.That(all, Does.Contain(CurrencyText.Gain(CurrencyId.Charts, 3L)));
            Assert.That(all, Does.Contain(CurrencyText.Gain(CurrencyId.Salvage, 12L)));
            Assert.That(all, Does.Contain(CurrencyText.Gain(CurrencyId.CraftPoints, 1L)));
            Assert.That(all, Does.Contain(CurrencyText.Gain(CurrencyId.Pearls, 2L)));

            var some = (string)receipt.Invoke(null, new object[] { 3L, 12L, 0L, 0L });
            Assert.That(some, Does.Not.Contain(CurrencyText.Name(CurrencyId.CraftPoints)));
            Assert.That(some, Does.Not.Contain(CurrencyText.Name(CurrencyId.Pearls)));
        }

        // ------------------------------------------------------- other spend buttons

        [Test]
        public void CaptainCratePricesNameCharts()
        {
            var captains = new CaptainService(new SaveData(), Captains.Tuning.Default, CaptainCrate.Tuning.Default);
            ServiceLocator.Register(captains);
            _host = new GameObject("CaptainFeedback");
            var ui = _host.AddComponent<CaptainRosterUI>();
            Invoke(ui, "Awake");
            ui.Show();

            int bulk = captains.CrateTuning.BulkCount;
            Assert.That(Label(ui.transform, "AcBir").text,
                        Does.Contain(CurrencyText.Amount(CurrencyId.Charts, captains.CrateCost(1))));
            Assert.That(Label(ui.transform, "AcCok").text,
                        Does.Contain(CurrencyText.Amount(CurrencyId.Charts, captains.CrateCost(bulk))));
        }

        [Test]
        public void ACollectionSetPayingCraftPointsNamesThem()
        {
            MethodInfo text = typeof(CardCollectionUI).GetMethod("RewardText", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(text, Is.Not.Null);
            var line = (string)text.Invoke(null, new object[] { CardCollection.SetRewardKind.CraftPoints, 5L });
            Assert.That(line, Is.EqualTo(CurrencyText.Amount(CurrencyId.CraftPoints, 5L)));
        }

        // ---------------------------------------------------------------- reveals

        [Test]
        public void TheGoalRevealNamesGemsAndWearsTheGemOnlyWhenItPaysThem()
        {
            RewardRevealUI reveal = Reveal(out Image icon);
            Text value = (Text)Field(reveal, "_value");

            reveal.Present(new GoalService.ClaimReceipt(1, 60L, 0, 0));
            Assert.That(value.text, Is.EqualTo(CurrencyText.Gain(CurrencyId.Gems, 60L)));
            Assert.That(icon.enabled, Is.True);

            reveal.Present(new GoalService.ClaimReceipt(1, 0L, 2, 0));
            Assert.That(value.text, Does.Not.Contain(CurrencyText.Name(CurrencyId.Gems)),
                        "a cards-only claim must not announce +0 gems");
            Assert.That(icon.enabled, Is.False);
        }

        [Test]
        public void ARenderedRewardWithoutTheIconsCurrencyHidesTheIcon()
        {
            RewardRevealUI reveal = Reveal(out Image icon);

            reveal.Present("+50 " + CurrencyText.Name(CurrencyId.Charts), false);
            Assert.That(icon.enabled, Is.False, "a charts-only pass tier must not wear the gem");

            reveal.Present("+50 ◆");
            Assert.That(icon.enabled, Is.True);
        }

        // ---------------------------------------------------------------- helpers

        private MiningGearUI Mining(SaveData data, out MiningGearService mining)
        {
            mining = new MiningGearService(data, null, new TimeService());
            ServiceLocator.Register(mining);
            _host = new GameObject("MiningFeedback");
            MiningGearUI ui = _host.AddComponent<MiningGearUI>();
            if (Field(ui, "_resultLabel") == null) Invoke(ui, "Awake");
            ui.Show();
            return ui;
        }

        /// <summary>Every slot already at the top grade, so any craft loses and is scrapped.</summary>
        private static SaveData Maxed(SaveData data)
        {
            data.miningGearGrade = new int[MiningGear.SlotCount];
            for (int i = 0; i < data.miningGearGrade.Length; i++) data.miningGearGrade[i] = Captains.GradeCount;
            return data;
        }

        private static CardCollectionService MaxedCollection(SaveData data, WalletService wallet)
        {
            for (int card = 0; card < CardCollectionCatalogue.Count; card++)
                data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card)).level = CardCollection.MaxLevel;
            return new CardCollectionService(data, null, new TimeService(), wallet);
        }

        private RewardRevealUI Reveal(out Image icon)
        {
            _host = new GameObject("RevealFeedback", typeof(RectTransform));
            _texture = new Texture2D(4, 4);
            Sprite gem = Sprite.Create(_texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            RewardRevealUI reveal = RewardRevealUI.Create((RectTransform)_host.transform, null, gem);
            icon = (Image)Field(reveal, "_icon");
            Assert.That(icon, Is.Not.Null);
            return reveal;
        }

        private static string Result(MiningGearUI ui) => ((Text)Field(ui, "_resultLabel")).text;

        private static Text Label(Transform root, string buttonName)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == buttonName) return all[i].GetComponentInChildren<Text>(true);
            Assert.Fail("Missing UI node: " + buttonName);
            return null;
        }

        private static object Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return field.GetValue(target);
        }

        private static void Invoke(object target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, method);
            info.Invoke(target, null);
        }
    }
}
