using System.Reflection;
using NUnit.Framework;
using Game.Core;
using Game.Systems;
using Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Tests
{
    public class WalletUiSmokeTests
    {
        private CurrencyRegistry _registry;
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_registry != null)
            {
                _registry.Dispose();
                _registry = null;
            }
            if (_host != null) Object.DestroyImmediate(_host);
            ServiceLocator.Clear();
        }

        [Test]
        public void WalletBuildsFiveGroupsAndAllTenRowsAndCanClose()
        {
            Register(new SaveData());
            _host = new GameObject("WalletSmoke");
            WalletUI ui = _host.AddComponent<WalletUI>();
            ui.Initialize(null);

            ui.Show();

            Assert.That(ui.IsOpen, Is.True);
            Assert.That(ui.BuiltRowCount, Is.EqualTo(10));
            Assert.That(ui.BuiltGroupCount, Is.EqualTo(5));
            ui.Hide();
            Assert.That(ui.IsOpen, Is.False);
        }

        [Test]
        public void WalletRowsRefreshWhenAnExistingSourceChanges()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            ServiceLocator.Register(wallet);
            _registry = new CurrencyRegistry(null, wallet, null, null, null, null, null);
            ServiceLocator.Register(_registry);
            ServiceLocator.Register(new LocalizationService());

            _host = new GameObject("WalletLiveSmoke");
            WalletUI ui = _host.AddComponent<WalletUI>();
            ui.Initialize(null);
            ui.Show();
            Assert.That(ui.DisplayedValue(CurrencyId.Cash), Is.EqualTo("0"));

            wallet.AddCash(new BigDouble(42d));

            Assert.That(ui.DisplayedValue(CurrencyId.Cash), Is.EqualTo("42"));
        }

        [Test]
        public void WalletLabelsHaveTurkishAndEnglishCoverage()
        {
            var localization = new LocalizationService();
            string original = localization.Code;
            try
            {
                localization.SetLanguage("tr");
                Assert.That(localization.Get("wallet.title"), Is.EqualTo("CÜZDAN"));
                Assert.That(localization.Get("currency.cash"), Is.EqualTo("NAKİT"));
                Assert.That(localization.Get("currency.pet_essence"), Is.EqualTo("PET ÖZÜ"));

                localization.SetLanguage("en");
                Assert.That(localization.Get("wallet.title"), Is.EqualTo("WALLET"));
                Assert.That(localization.Get("currency.cash"), Is.EqualTo("CASH"));
                Assert.That(localization.Get("currency.pet_essence"), Is.EqualTo("PET ESSENCE"));
            }
            finally
            {
                localization.SetLanguage(original);   // SetLanguage writes PlayerPrefs
            }
        }

        [Test]
        public void EveryRowShowsItsDescriptionAndOnlyRegeneratingRowsCountDown()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var time = new TimeService();
            var mining = new MiningGearService(data, null, time);
            ServiceLocator.Register(wallet);
            ServiceLocator.Register(new LocalizationService());
            _registry = new CurrencyRegistry(time, wallet, null, null, mining, null, null);
            ServiceLocator.Register(_registry);

            WalletUI ui = Open();

            for (int i = 0; i < _registry.Definitions.Count; i++)
            {
                CurrencyDefinition d = _registry.Definitions[i];
                Assert.That(ui.DisplayedDescription(d.Id), Is.EqualTo(Loc.T(d.LocalizedDescriptionKey)), d.Id.ToString());
                Assert.That(ui.DisplayedDescription(d.Id), Is.Not.Empty.And.Not.EqualTo(d.LocalizedDescriptionKey));
                if (!d.Regenerates)
                    Assert.That(ui.DisplayedTimer(d.Id), Is.Empty, d.Id + " does not regenerate");
            }
            Assert.That(ui.DisplayedValue(CurrencyId.MiningPoints), Is.EqualTo("0/" + mining.PointCap));
            // A second may tick between the refresh and this read; either side of it is correct.
            float left = (float)mining.SecondsToNextPoint;
            Assert.That(ui.DisplayedTimer(CurrencyId.MiningPoints),
                        Is.EqualTo(string.Format(Loc.T("wallet.next_regen"), UiBuild.Clock(left)))
                          .Or.EqualTo(string.Format(Loc.T("wallet.next_regen"), UiBuild.Clock(left + 1f))));
        }

        /// <summary>
        /// Mining Points are banked only when the pool is polled. Found in the Phase 7 live run: with
        /// the wallet open and the mining screen shut, the row's countdown reached zero and started
        /// over while the balance never moved.
        /// </summary>
        [Test]
        public void TheWalletBanksMiningPointsOnOpenAndAsItsCountdownRunsOut()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var time = new TimeService();
            var mining = new MiningGearService(data, null, time);
            ServiceLocator.Register(wallet);
            ServiceLocator.Register(mining);
            ServiceLocator.Register(new LocalizationService());
            _registry = new CurrencyRegistry(time, wallet, null, null, mining, null, null);
            ServiceLocator.Register(_registry);
            long perTick = mining.Tuning.PointsPerTick;
            long tick = (long)mining.Tuning.TickSeconds;

            data.miningPointsStampUnix -= tick;   // a point earned while the wallet was shut
            WalletUI ui = Open();
            Assert.That(ui.DisplayedValue(CurrencyId.MiningPoints), Is.EqualTo(perTick + "/" + mining.PointCap),
                        "opening the wallet banks what the clock already earned");

            data.miningPointsStampUnix -= tick * 2L;   // two more go by with it open
            typeof(WalletUI).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
            Assert.That(ui.DisplayedValue(CurrencyId.MiningPoints), Is.EqualTo(perTick * 3L + "/" + mining.PointCap),
                        "the once-a-second pulse banks them too");
        }

        [Test]
        public void RowsReuseTheGamesArtAndEveryBadgeLeftIsDistinct()
        {
            Register(new SaveData());
            WalletUI ui = Open();

            // Loadable in any scene: the sea kit's salvage and chart, the workshop's opener art.
            Assert.That(ui.HasArtIcon(CurrencyId.Salvage), Is.True);
            Assert.That(ui.HasArtIcon(CurrencyId.Charts), Is.True);
            Assert.That(ui.HasArtIcon(CurrencyId.CraftPoints), Is.True);

            var bare = new System.Collections.Generic.List<CurrencyId>();
            foreach (CurrencyId id in System.Enum.GetValues(typeof(CurrencyId)))
            {
                if (ui.HasArtIcon(id)) continue;
                Text mark = Find(_host.transform, "Para_" + id).Find("Simge/Harf").GetComponent<Text>();
                Assert.That(mark.enabled, Is.True, id + " has neither art nor a badge");
                Assert.That(mark.text, Is.Not.Empty, id.ToString());
                bare.Add(id);
            }

            // The marks for badge-only rows come from localized initials, so check every language.
            System.Reflection.MethodInfo badgeMark = typeof(WalletUI).GetMethod(
                "BadgeMark", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(badgeMark, Is.Not.Null);
            var localization = ServiceLocator.Get<LocalizationService>();
            string original = localization.Code;
            try
            {
                for (int l = 0; l < localization.Languages.Count; l++)
                {
                    string code = localization.Languages[l].Code;
                    localization.SetLanguage(code);
                    var marks = new System.Collections.Generic.HashSet<string>();
                    for (int i = 0; i < bare.Count; i++)
                    {
                        CurrencyRegistry.TryDescribe(bare[i], out CurrencyDefinition d);
                        var mark = (string)badgeMark.Invoke(null, new object[] { d });
                        Assert.That(mark, Is.Not.Empty, code + " " + bare[i]);
                        Assert.That(marks.Add(mark), Is.True, code + ": " + bare[i] + " shares its badge mark '" + mark + "'");
                    }
                }
            }
            finally
            {
                localization.SetLanguage(original);
            }
        }

        /// <summary>
        /// The list clips with RectMask2D. It used a stencil Mask over a 0.001-alpha image, which the
        /// UI shader alpha-clips before it can write stencil, so in the running game every row was
        /// masked away and the wallet opened empty. An EditMode test never renders, so this pins the
        /// component itself.
        /// </summary>
        [Test]
        public void TheListClipsByRectSoItsRowsAreDrawn()
        {
            Register(new SaveData());
            Open();

            Transform viewport = Find(_host.transform, "Gorunum");
            Assert.That(viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(viewport.GetComponent<Mask>(), Is.Null, "a stencil Mask over a clear image draws nothing");
        }

        /// <summary>
        /// The portrait Shipyard scene, where a fresh save boots, has no HUD rail to hang the opener on.
        /// Found in the Phase 7 live run: a brand-new player had no way into the wallet. Without a HUD
        /// the wallet brings its own button, and a tap on it opens the wallet.
        /// </summary>
        [Test]
        public void WithoutAHudTheWalletBringsItsOwnOpenerAndItOpensTheWallet()
        {
            Register(new SaveData());
            _host = new GameObject("WalletShipyardSmoke");
            WalletUI ui = _host.AddComponent<WalletUI>();
            ui.Initialize(null);
            Assert.That(ui.IsOpen, Is.False, "the wallet starts closed");

            Transform opener = Find(_host.transform, WalletUI.OpenerButtonName);
            Assert.That(opener.GetComponentInParent<Canvas>().name, Is.EqualTo("CuzdanAciciKanvas"));
            Assert.That(opener.GetComponent<Button>().interactable, Is.True);
            Assert.That(opener.GetComponentInChildren<Text>().text, Is.EqualTo(Loc.T("wallet.title")));

            opener.GetComponent<Button>().onClick.Invoke();
            Assert.That(ui.IsOpen, Is.True, "the tap opened the wallet");
            Assert.That(ui.BuiltRowCount, Is.EqualTo(10));
            Assert.That(opener.GetComponentInParent<Canvas>().sortingOrder,
                        Is.LessThan(Find(_host.transform, "CuzdanKanvas").GetComponent<Canvas>().sortingOrder),
                        "the open wallet covers its own button");
        }

        /// <summary>A tap on a row used to walk up to the scrim's dismiss button and close the wallet.</summary>
        [Test]
        public void TappingARowDoesNotCloseTheWallet()
        {
            Register(new SaveData());
            WalletUI ui = Open();

            GameObject row = Find(_host.transform, "Para_" + CurrencyId.Gems).gameObject;
            GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(row);
            Assert.That(handler, Is.Not.Null);
            Assert.That(handler.name, Is.EqualTo("Zemin"), "the sheet must eat the tap before the scrim does");
        }

        /// <summary>The game is portrait-only; these are the shapes it ships to, from a 16:9 phone to a
        /// 21:9 one and the two tablet shapes. Only the ratio matters to a 1080×1920 scaler matched at
        /// 0.5, so the canvas is sized as that scaler would size it and every label is measured at the
        /// smallest size its best-fit may shrink to, in the two languages the phase covers.</summary>
        [Test]
        public void EveryRowFitsAtEverySupportedPortraitAspectInTurkishAndEnglish()
        {
            Register(new SaveData());
            var localization = ServiceLocator.Get<LocalizationService>();
            string original = localization.Code;
            WalletUI ui = Open();

            Canvas canvas = Find(_host.transform, "CuzdanKanvas").GetComponent<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = (RectTransform)canvas.transform;
            RectTransform safe = (RectTransform)Find(_host.transform, "Guvenli");
            RectTransform content = (RectTransform)Find(_host.transform, "Icerik");

            Vector2[] aspects =
            {
                new Vector2(9f, 16f), new Vector2(9f, 19.5f), new Vector2(9f, 20f), new Vector2(9f, 21f),
                new Vector2(10f, 16f), new Vector2(3f, 4f),
            };
            string[] languages = { "tr", "en" };
            try
            {
                for (int l = 0; l < languages.Length; l++)
                {
                    localization.SetLanguage(languages[l]);
                    ui.Show();   // re-reads every label in the new language
                    for (int a = 0; a < aspects.Length; a++)
                    {
                        float width = Mathf.Sqrt(aspects[a].x / aspects[a].y * 1080f * 1920f);
                        canvasRect.sizeDelta = new Vector2(width, width * aspects[a].y / aspects[a].x);
                        bool tall = aspects[a].y / aspects[a].x > 2f;   // a notch up top, a gesture bar below
                        safe.anchorMin = new Vector2(0f, tall ? 0.02f : 0f);
                        safe.anchorMax = new Vector2(1f, tall ? 0.965f : 1f);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

                        string where = languages[l] + " " + aspects[a].x + ":" + aspects[a].y;
                        foreach (CurrencyId id in System.Enum.GetValues(typeof(CurrencyId)))
                            AssertRowFits(Find(_host.transform, "Para_" + id), where + " " + id);
                    }
                }
            }
            finally
            {
                localization.SetLanguage(original);
            }
        }

        private static void AssertRowFits(Transform row, string where)
        {
            RectTransform name = (RectTransform)row.Find("Ad");
            RectTransform value = (RectTransform)row.Find("Deger");
            RectTransform description = (RectTransform)row.Find("Aciklama");
            RectTransform timer = (RectTransform)row.Find("Sure");
            Assert.That(Overlaps(name, value), Is.False, where + ": name runs into the value");
            Assert.That(Overlaps(name, description), Is.False, where + ": name runs into the description");
            Assert.That(Overlaps(description, timer) && timer.GetComponent<Text>().text.Length > 0, Is.False,
                        where + ": description runs into the countdown");

            Text[] labels = row.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < labels.Length; i++)
                if (labels[i].enabled && labels[i].text.Length > 0) AssertFits(labels[i], where);
        }

        /// <summary>Fits at the smallest size best-fit may choose: every word on one line, and the
        /// wrapped block no taller than its rect. Anything past that is truncated on a device.</summary>
        private static void AssertFits(Text label, string where)
        {
            Vector2 size = label.rectTransform.rect.size;
            Assert.That(size.x, Is.GreaterThan(0f), where + " " + label.name + " has no width");
            TextGenerationSettings settings = label.GetGenerationSettings(size);
            settings.resizeTextForBestFit = false;
            settings.fontSize = label.resizeTextForBestFit ? label.resizeTextMinSize : label.fontSize;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            var generator = new TextGenerator();
            float unit = label.pixelsPerUnit;

            float height = generator.GetPreferredHeight(label.text, settings) / unit;
            Assert.That(height, Is.LessThanOrEqualTo(size.y + 0.5f),
                        where + " " + label.name + " '" + label.text + "' needs " + height + " of " + size.y);

            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            string[] words = label.text.Split(' ', '\n');
            for (int w = 0; w < words.Length; w++)
            {
                float wide = generator.GetPreferredWidth(words[w], settings) / unit;
                Assert.That(wide, Is.LessThanOrEqualTo(size.x + 0.5f),
                            where + " " + label.name + " word '" + words[w] + "' is " + wide + " of " + size.x);
            }
        }

        private static bool Overlaps(RectTransform a, RectTransform b)
        {
            var ca = new Vector3[4];
            var cb = new Vector3[4];
            a.GetWorldCorners(ca);
            b.GetWorldCorners(cb);
            const float slack = 0.5f;
            return ca[0].x < cb[2].x - slack && cb[0].x < ca[2].x - slack
                && ca[0].y < cb[2].y - slack && cb[0].y < ca[2].y - slack;
        }

        private WalletUI Open()
        {
            _host = new GameObject("WalletPhase5Smoke");
            WalletUI ui = _host.AddComponent<WalletUI>();
            ui.Initialize(null);
            ui.Show();
            return ui;
        }

        private static Transform Find(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            Assert.Fail("Missing UI node: " + name);
            return null;
        }

        private void Register(SaveData data)
        {
            var wallet = new WalletService(data.wallet);
            ServiceLocator.Register(wallet);
            _registry = new CurrencyRegistry(null, wallet, null, null, null, null, null);
            ServiceLocator.Register(_registry);
            ServiceLocator.Register(new LocalizationService());
        }
    }
}
