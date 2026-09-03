using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The always-on strip under the currency bar: what to do next, and what is in the way.
    ///
    /// WHY IT EXISTS. Everything an idle game asks of a player happens behind a button, and this one
    /// had eleven of them. The island already told you where money could be spent — see
    /// <see cref="UpgradeReadyMarkers"/> — and <see cref="SaleFx"/> already told you when it arrived,
    /// but nothing anywhere named a GOAL. <see cref="Chapters"/> has carried five named beats per
    /// island since the chapter spine landed, with thresholds, notes and progress in eleven
    /// languages, and all of it was reachable only by opening a modal that a player has no reason to
    /// open until they already know what is in it. The banner is that data, on the outside.
    ///
    /// TWO ROWS, ONE QUESTION. The objective card says what to do next; the chip under it says which
    /// stage of the chain is throttling the island. They look like separate features and are not:
    /// both answer "what should I do next?", so they share one strip, one refresh and one timer
    /// rather than becoming two components that each poll the save.
    ///
    /// IT IS A BUTTON, NOT A SIGN. Every state routes to the screen where that work is actually done,
    /// because a banner that names a task and then makes the player hunt for the panel is a worse
    /// version of no banner at all.
    ///
    /// WHY IT IS NOT ON THE ISLAND. The chip cannot be a world badge: two of the five verdicts
    /// <see cref="ProductionBottleneck"/> can return are ORE TRUCKS and CARGO TRUCKS, fleets that own
    /// no building, which is the same reason <see cref="UpgradeReadyMarkers"/> skips them. A badge for
    /// them would hang over open grass. Nor is either row a seventh opener in the HUD's bottom row:
    /// that row re-centres itself on every attach, so a seventh entry starts pushing the ends of it
    /// off a narrow screen.
    ///
    /// Built once, refreshed once a second, and it writes a label only when the text has actually
    /// changed — so a settled banner allocates nothing. It carries no <see cref="LocalizedText"/>,
    /// deliberately: every line here is computed, and a label driven from both would flicker between
    /// them depending on frame order.
    /// </summary>
    public sealed class ObjectiveBannerUI : MonoBehaviour
    {
        [Header("Görseller")]
        [Tooltip("Kartın gövdesi. Boş bırakılırsa UiSkin'in paneli kullanılır.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Darboğaz çipinin hapı. Boş bırakılırsa UiSkin'in hapı kullanılır.")]
        [SerializeField] private Sprite chipPill;
        [Tooltip("Kartın solundaki ikon. Boş bırakılırsa bölüm açıcısının ikonu kullanılır.")]
        [SerializeField] private Sprite icon;

        [Header("Ölçü — referans çözünürlükte piksel")]
        [Tooltip("Şeridin HUD sayfasının genişliğine oranı.")]
        [SerializeField, Range(0.4f, 1f)] private float widthFraction = 0.78f;
        [SerializeField, Min(40f)] private float cardHeight = 138f;
        [SerializeField, Min(24f)] private float chipHeight = 60f;
        [Tooltip("Kartla çipin arası.")]
        [SerializeField, Min(0f)] private float rowGap = 10f;
        [Tooltip("Şeridin üstündeki kurgulanmış HUD parçalarından ne kadar aşağıda duracağı.")]
        [SerializeField, Min(0f)] private float topGap = 14f;
        [Tooltip("Çipin karta göre genişliği.")]
        [SerializeField, Range(0.3f, 1f)] private float chipWidthFraction = 0.66f;

        [Header("Renkler")]
        [SerializeField] private Color cardTint = new Color(0.16f, 0.19f, 0.27f, 0.94f);
        [SerializeField] private Color titleColor = new Color(0.72f, 0.79f, 0.92f, 1f);
        [SerializeField] private Color taskColor = Color.white;
        [SerializeField] private Color barTrack = new Color(0.09f, 0.10f, 0.15f, 1f);
        [SerializeField] private Color barFill = new Color(0.42f, 0.82f, 0.36f, 1f);
        [Tooltip("Ödül bekleyen bir aşamanın çubuğu — yeşil değil, altın.")]
        [SerializeField] private Color barClaim = new Color(0.98f, 0.74f, 0.24f, 1f);
        [SerializeField] private Color chipTint = new Color(0.86f, 0.31f, 0.24f, 1f);

        [Header("Zamanlama")]
        [Tooltip("Aşamalar bildirilmiyor, GÖZLENİYOR — bir seviye alındığında kimse haber vermiyor, " +
                 "o yüzden yoklamak zorunlu. Saniyede bir, HUD'un çeyrek saniyesi değil: her yoklama " +
                 "kaydın seviye listesini bir kez dolaşıyor.")]
        [SerializeField, Min(0.25f)] private float refreshSeconds = 1f;

        // Which of the four things the card is saying. Only the first three draw.
        private const int StateHidden = 0, StateClaim = 1, StateWork = 2, StateDone = 3;

        private const string IconResource = "UI/Buttons/bolum";

        private HudUI _hud;
        private ChapterService _chapters;
        private MarketService _market;
        private LocalizationService _loc;
        private IAnalytics _analytics;
        private CoalOperation _op;

        private StationScreenUI _stations;
        private ChapterUI _chapterScreen;
        private IslandMapUI _map;

        private RectTransform _strip;
        private GameObject _cardRoot, _chipRoot;
        private Text _title, _task, _count, _chipLabel;
        private RectTransform _fill;
        private Image _fillImage;

        // What is on screen right now. The refresh compares against these and touches nothing when
        // they all match, which is what keeps a settled banner from building a string a second.
        private int _state = -1;
        private int _chapter = -1;
        private int _beat = -1;
        private int _have = -1, _need = -1;
        private int _wall = ProductionBottleneck.Unknown;
        private bool _dirty = true;
        private bool _built;

        private float _timer;

        /// <summary>
        /// Handed the HUD that owns the strip. Called by <see cref="HudUI"/> whether it found this
        /// component authored in the scene or made the object itself, so the two paths build the same
        /// screen — the arrangement <see cref="InventoryUI"/> uses for the depo.
        /// </summary>
        public void Adopt(HudUI hud)
        {
            _hud = hud;
            if (!_built) Build();
        }

        private void Awake()
        {
            _chapters = ServiceLocator.Get<ChapterService>();
            _market = ServiceLocator.Get<MarketService>();
            _analytics = ServiceLocator.Get<IAnalytics>();
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            if (_chapters != null) _chapters.Changed += OnChaptersChanged;
        }

        private void OnDestroy()
        {
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
            if (_chapters != null) _chapters.Changed -= OnChaptersChanged;
        }

        private void OnLanguageChanged() => _dirty = true;

        /// <summary>
        /// A claim landed, so the card's state has moved on. Progress itself is never reported —
        /// <see cref="Chapters"/> observes it out of the save — which is why the timer below exists
        /// as well as this.
        /// </summary>
        private void OnChaptersChanged() => _dirty = true;

        // ------------------------------------------------------------------ build
        private void Build()
        {
            if (_hud == null) return;

            float stripHeight = cardHeight + rowGap + chipHeight;
            _strip = _hud.AttachTopStrip("HedefSeridi", widthFraction, stripHeight, topGap);
            if (_strip == null) return;

            Sprite card = cardPanel != null ? cardPanel : UiSkin.Panel;
            Sprite pill = chipPill != null ? chipPill : UiSkin.Pill;
            Sprite art = icon != null ? icon : Resources.Load<Sprite>(IconResource);

            // ---- the objective card: full width, hung from the top of the strip
            var cardGo = new GameObject("Hedef", typeof(RectTransform), typeof(Image), typeof(Button));
            var cardRect = (RectTransform)cardGo.transform;
            cardRect.SetParent(_strip, false);
            cardRect.anchorMin = new Vector2(0f, 1f);
            cardRect.anchorMax = new Vector2(1f, 1f);
            cardRect.pivot = new Vector2(0.5f, 1f);
            cardRect.sizeDelta = new Vector2(0f, cardHeight);
            cardRect.anchoredPosition = Vector2.zero;

            var cardImg = cardGo.GetComponent<Image>();
            cardImg.sprite = card != null ? card : UiSkin.Flat;
            cardImg.type = Image.Type.Sliced;
            cardImg.color = card != null && UiSkin.HasArt ? Color.white : cardTint;
            var cardBtn = cardGo.GetComponent<Button>();
            cardBtn.targetGraphic = cardImg;
            cardBtn.onClick.AddListener(OnObjective);
            _cardRoot = cardGo;

            var iconGo = new GameObject("Ikon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(cardRect, false);
            UiBuild.Anchor((RectTransform)iconGo.transform,
                           new Vector2(0.020f, 0.18f), new Vector2(0.150f, 0.86f));
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite = art;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            iconImg.enabled = art != null;

            _title = Row(cardRect, "Baslik", 30, TextAnchor.LowerLeft, titleColor,
                         new Vector2(0.175f, 0.62f), new Vector2(0.980f, 0.94f));
            _task = Row(cardRect, "Gorev", 40, TextAnchor.UpperLeft, taskColor,
                        new Vector2(0.175f, 0.34f), new Vector2(0.980f, 0.60f));

            RectTransform track = UiBuild.Bar(cardRect, "Yatak", barTrack, barFill,
                                              new Vector2(0.175f, 0.10f), new Vector2(0.980f, 0.30f),
                                              out _fill);
            // The bar sits INSIDE the card's own button, so neither half of it may be raycastable —
            // a tap that landed on the track would be swallowed instead of opening the panel.
            track.GetComponent<Image>().raycastTarget = false;
            _fillImage = _fill.GetComponent<Image>();
            _fillImage.raycastTarget = false;
            _count = Row(track, "Sayi", 26, TextAnchor.MiddleCenter, Color.white,
                         Vector2.zero, Vector2.one);

            // ---- the bottleneck chip: narrower, centred, hung under the card
            //
            // Anchored by FRACTION of the strip rather than given a width in pixels: the strip's own
            // width is solved from the HUD sheet, which may not have been laid out yet when this
            // runs, and a fraction follows it afterwards for nothing.
            var chipGo = new GameObject("Darbogaz", typeof(RectTransform), typeof(Image), typeof(Button));
            var chipRect = (RectTransform)chipGo.transform;
            chipRect.SetParent(_strip, false);
            float half = chipWidthFraction * 0.5f;
            chipRect.anchorMin = new Vector2(0.5f - half, 1f);
            chipRect.anchorMax = new Vector2(0.5f + half, 1f);
            chipRect.pivot = new Vector2(0.5f, 1f);
            chipRect.sizeDelta = new Vector2(0f, chipHeight);
            chipRect.anchoredPosition = new Vector2(0f, -(cardHeight + rowGap));

            var chipImg = chipGo.GetComponent<Image>();
            chipImg.sprite = pill != null ? pill : UiSkin.Flat;
            chipImg.type = Image.Type.Sliced;
            chipImg.color = pill != null && UiSkin.HasArt ? Color.white : chipTint;
            PillFit.Wrap(chipImg);
            var chipBtn = chipGo.GetComponent<Button>();
            chipBtn.targetGraphic = chipImg;
            chipBtn.onClick.AddListener(OnBottleneck);
            _chipRoot = chipGo;

            _chipLabel = Row(chipRect, "Metin", 28, TextAnchor.MiddleCenter, Color.white,
                             new Vector2(0.06f, 0f), new Vector2(0.94f, 1f));

            _cardRoot.SetActive(false);
            _chipRoot.SetActive(false);
            UiPanelSound.AttachButtonsOnly(_strip.gameObject);
            _built = true;
        }

        /// <summary>
        /// One label, anchored to a fraction of its box and shrink-to-fit. Every line here is a
        /// translation of unknown length — German compounds and Russian station names are the ones
        /// that overrun — so none of them is allowed a fixed size.
        /// </summary>
        private static Text Row(Transform parent, string name, int size, TextAnchor anchor,
                                Color colour, Vector2 aMin, Vector2 aMax)
        {
            Text label = UiBuild.Label(parent, name, "", size, anchor);
            UiBuild.Anchor(label.rectTransform, aMin, aMax);
            label.color = colour;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = size;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        // ---------------------------------------------------------------- refresh
        private void Update()
        {
            if (!_built)
            {
                // The HUD adopts this from its own Start, so a frame or two without a strip is
                // normal when the component was authored into the scene instead of made by the HUD.
                if (_hud == null) _hud = FindAnyObjectByType<HudUI>();
                if (_hud == null) return;

                Build();
                if (!_built)
                {
                    // The HUD is not on a RectTransform, so there is no sheet to hang a strip in.
                    // Retrying that every frame forever would hide the misconfiguration instead of
                    // reporting it.
                    Debug.LogWarning("ObjectiveBannerUI: HUD has no sheet to hang the strip in.");
                    enabled = false;
                }
                return;
            }

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshSeconds;
            Refresh();
        }

        private void Refresh()
        {
            if (_chapters == null) _chapters = ServiceLocator.Get<ChapterService>();
            if (_market == null) _market = ServiceLocator.Get<MarketService>();
            if (_op == null || !_op.enabled) BindOp();

            RefreshObjective();
            RefreshBottleneck();
            _dirty = false;
        }

        /// <summary>
        /// Travelling to another island enables a different <see cref="CoalOperation"/> and disables
        /// this one, so the binding is re-checked rather than taken once — the same reason
        /// <see cref="UpgradeReadyMarkers"/> and <see cref="SaleFx"/> do it.
        /// </summary>
        private void BindOp()
        {
            // Active objects only, then filtered on the component's own flag: travelling disables the
            // operation rather than its island, so both halves of the check are needed.
            var all = FindObjectsByType<CoalOperation>();
            for (int i = 0; i < all.Length; i++)
                if (all[i].enabled) { _op = all[i]; return; }
        }

        private void RefreshObjective()
        {
            int chapter = _op != null ? Chapters.Of(_op.IslandKey) : -1;
            if (_chapters == null || chapter < 0) { SetObjective(StateHidden, -1, -1, 0, 0); return; }

            Chapters.Progress p = _chapters.Progress(chapter);
            if (!p.Owned) { SetObjective(StateHidden, chapter, -1, 0, 0); return; }

            Chapters.Tuning t = _chapters.Tuning;

            // A beat that has been earned but not collected outranks the next one to work on: the
            // player is one tap from a reward and should not be sent to look for the next chore.
            int claim = FirstClaimable(chapter, p, t);
            if (claim >= 0)
            {
                SetObjective(StateClaim, chapter, claim, 1, 1);
                return;
            }

            int next = Chapters.NextBeat(p, t);
            if (next < 0)
            {
                SetObjective(StateDone, chapter, -1, 1, 1);
                return;
            }

            int have, need;
            Chapters.BeatCounts(next, p, t, out have, out need);
            SetObjective(StateWork, chapter, next, have, need);
        }

        /// <summary>
        /// The lowest beat that is earned and uncollected, or -1.
        ///
        /// Judged against the <see cref="Chapters.Progress"/> snapshot this refresh already took,
        /// rather than through <c>ChapterService.CanClaim</c>. Two reasons, and the second is the
        /// important one. CanClaim re-derives progress per call, and progress is derived by walking
        /// the save's whole level list — five calls a second from the one screen that is always up.
        /// And a snapshot cannot disagree with itself: asked separately, the claim check and
        /// <see cref="Chapters.NextBeat"/> could be answered from two different reads of the save and
        /// name the same beat as both earned and outstanding.
        /// </summary>
        private int FirstClaimable(int chapter, in Chapters.Progress p, in Chapters.Tuning t)
        {
            for (int b = 0; b < Chapters.BeatCount; b++)
                if (Chapters.Satisfied(b, p, t) && !_chapters.Claimed(chapter, b)) return b;
            return -1;
        }

        private void SetObjective(int state, int chapter, int beat, int have, int need)
        {
            bool same = state == _state && chapter == _chapter && beat == _beat
                     && have == _have && need == _need && !_dirty;
            bool moved = state != _state || chapter != _chapter || beat != _beat;

            _state = state; _chapter = chapter; _beat = beat; _have = have; _need = need;
            if (same) return;

            if (_cardRoot != null) _cardRoot.SetActive(state != StateHidden);
            if (state == StateHidden) return;

            if (moved) _analytics?.Log("objective_changed", "beat", Tag(chapter, state, beat));

            switch (state)
            {
                // Both words are already in the table in eleven languages, and gorev.al is the same
                // CLAIM the goals screen prints on its own claim button — so the two read alike
                // rather than being two translations of one word.
                case StateClaim:
                    _title.text = Loc.T("ortak.hazir");
                    _task.text = Loc.T("bolum.asama." + beat);
                    _count.text = Loc.T("gorev.al");
                    break;
                case StateDone:
                    _title.text = Loc.T("bolum.tamamlandi");
                    _task.text = Loc.T("bolum.sonraki");
                    _count.text = "";
                    break;
                default:
                    _title.text = Loc.T("bolum.bolum") + " " + (chapter + 1) + " · "
                                + Loc.T("bolum.asama." + beat);
                    _task.text = Note(beat);
                    _count.text = have + " / " + need;
                    break;
            }

            float progress = state == StateWork ? Goals.Progress(have, need) : 1f;
            _fill.anchorMax = new Vector2(progress, 1f);
            if (_fillImage != null) _fillImage.color = state == StateClaim ? barClaim : barFill;
        }

        /// <summary>
        /// The beat's own instruction line. <c>bolum.asama.N.not</c> already carries the thresholds as
        /// {0} and {1} in eleven languages, so the numbers come from the tuning rather than from a
        /// second copy of them here.
        /// </summary>
        private string Note(int beat)
        {
            string key = "bolum.asama." + beat + ".not";
            Chapters.Tuning t = _chapters.Tuning;
            switch (beat)
            {
                case Chapters.FirstSmoke: return string.Format(Loc.T(key), t.FirstSmokeLevels);
                case Chapters.TheWorks:   return string.Format(Loc.T(key), t.WorksUnlocks);
                case Chapters.FullSteam:  return string.Format(Loc.T(key), t.FullSteamLevels,
                                                                           t.FullSteamUnlocks);
                default:                  return Loc.T(key);
            }
        }

        private static string Tag(int chapter, int state, int beat)
        {
            string island = Chapters.Island(chapter);
            if (state == StateDone) return island + ".done";
            return island + "." + beat;
        }

        // ------------------------------------------------------------- bottleneck
        private void RefreshBottleneck()
        {
            int wall = ProductionBottleneck.Unknown;
            if (_op != null && _op.FlowReady)
                wall = ProductionBottleneck.Blocked(
                    _op.YardFullSeconds, _op.FurnaceQueueSeconds, _op.BarStoreFullSeconds,
                    _market != null ? _market.OverflowSeconds(_op.IslandKey) : 0d);

            bool same = wall == _wall && !_dirty;
            bool moved = wall != _wall;
            _wall = wall;
            if (same) return;

            int station = ProductionBottleneck.StationOf(wall);
            bool show = wall != ProductionBottleneck.Unknown && station >= 0;
            if (_chipRoot != null) _chipRoot.SetActive(show);
            if (!show) return;

            if (moved) _analytics?.Log("bottleneck_changed", "station", IslandEconomy.Stations[station]);
            _chipLabel.text = Loc.T("rapor.darbogaz") + " · "
                            + Loc.Id("istasyon", IslandEconomy.Stations[station]);
        }

        // ------------------------------------------------------------------ taps
        private void OnObjective()
        {
            _analytics?.Log("objective_tap", "beat", Tag(_chapter, _state, _beat));

            // Levels and buildings are both bought in the station panel, so the two beats that count
            // them go straight there. THE YARD is worked through the door on the island and CLAIM is
            // collected in the log, so both of those open the log — which is also the safe answer for
            // a beat this switch has never heard of.
            if (_state == StateDone) { OpenMap(); return; }
            if (_state == StateWork
                && (_beat == Chapters.FirstSmoke || _beat == Chapters.TheWorks
                    || _beat == Chapters.FullSteam))
            {
                OpenUpgrades();
                return;
            }
            OpenChapters();
        }

        private void OnBottleneck()
        {
            int station = ProductionBottleneck.StationOf(_wall);
            if (station >= 0)
                _analytics?.Log("bottleneck_tap", "station", IslandEconomy.Stations[station]);

            if (_stations == null)
                _stations = FindAnyObjectByType<StationScreenUI>(FindObjectsInactive.Include);
            if (_stations != null) _stations.OpenReport();
        }

        private void OpenUpgrades()
        {
            if (_stations == null)
                _stations = FindAnyObjectByType<StationScreenUI>(FindObjectsInactive.Include);
            if (_stations != null) _stations.Open();
        }

        private void OpenChapters()
        {
            if (_chapterScreen == null)
                _chapterScreen = FindAnyObjectByType<ChapterUI>(FindObjectsInactive.Include);
            if (_chapterScreen != null) _chapterScreen.Show();
        }

        private void OpenMap()
        {
            if (_map == null) _map = FindAnyObjectByType<IslandMapUI>(FindObjectsInactive.Include);
            if (_map != null) _map.ToggleMap();
        }
    }
}
