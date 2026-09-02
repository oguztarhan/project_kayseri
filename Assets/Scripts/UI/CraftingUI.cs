using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The workshop screen: the bench on the left, the odds table on the right, and — when a craft
    /// is waiting — the decision card on top of both.
    ///
    /// Built in code for the same reason <see cref="CaptainRosterUI"/> is: the rows come out of
    /// <see cref="Crafting"/>'s own tables, so appending a grade or retouching a bracket should
    /// cost one cell there and nothing here. The opener borrows a real HUD row button, order 4,
    /// after goals, foremen, chapters and captains.
    ///
    /// THE ODDS ARE PRINTED, not implied. Every grade row shows either its share at the CURRENT
    /// level or the level it opens at — the "possibilities per level" the design asks for, and the
    /// disclosure Google Play expects the day points are ever sold.
    ///
    /// Refreshed on open and on <see cref="CraftingService.Changed"/>; the once-a-second Update
    /// only drives the retooling clock, and only while the screen is open.
    /// </summary>
    public sealed class CraftingUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 110;

        [Header("Görseller")]
        [Tooltip("Kart gövdesi — MaviSet/panel_beyaz.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Başlık şeridi — MaviSet/serit_mavi.")]
        [SerializeField] private Sprite ribbon;
        [Tooltip("ÜRET ve karar düğmeleri — MaviSet/btn_hap_kalin.")]
        [SerializeField] private Sprite actionButton;
        [Tooltip("Kapat düğmesi — MaviSet/btn_kapat_yeni.")]
        [SerializeField] private Sprite closeIcon;
        [Tooltip("XP çubuğunun yatağı ve dolgusu — Gostergeler/slider_yatak, bar_dolgu.")]
        [SerializeField] private Sprite barTrack;
        [SerializeField] private Sprite barFill;
        [Tooltip("Puan hapı — MaviSet/gosterge_grafit.")]
        [SerializeField] private Sprite chipPill;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0f, 0f, 0f, 0.62f);
        [SerializeField] private Color backdrop = new Color(0.92f, 0.94f, 0.99f, 0.98f);

        private const string OpenerIconResource = "UI/Buttons/atolye";

        /// <summary>The grade ladder's ink — the same five the captain screen and the sea wear.</summary>
        private static readonly Color[] GradeTint =
        {
            new Color(0.48f, 0.54f, 0.62f, 1f),
            new Color(0.26f, 0.60f, 0.92f, 1f),
            new Color(0.62f, 0.38f, 0.92f, 1f),
            new Color(0.96f, 0.66f, 0.18f, 1f),
            new Color(0.94f, 0.28f, 0.42f, 1f),
        };

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color Good = new Color(0.24f, 0.68f, 0.36f, 1f);
        private static readonly Color Bad = new Color(0.86f, 0.30f, 0.26f, 1f);
        private const float RibbonBand = 0.677f;

        private CraftingService _crafting;
        private ExpeditionService _sea;
        private LocalizationService _loc;
        private RectTransform _root;

        private Text _titleLabel, _pointsLabel, _levelLabel, _tierLabel, _xpLabel, _craftLabel,
                     _gateLabel, _gateClockLabel, _bankLabel, _sourceLabel, _oddsTitleLabel;
        private RectTransform _xpFill;
        private Button _craftBtn;
        private RectTransform _gateCard;

        private readonly Image[] _oddsStripe = new Image[Captains.GradeCount];
        private readonly Text[] _oddsName = new Text[Captains.GradeCount];
        private readonly Text[] _oddsValue = new Text[Captains.GradeCount];

        private RectTransform _decideCard;
        private Text _decideTitle, _decideScore, _decideRows, _decideWorn, _equipLabel, _salvageLabel;

        private GameObject _openerChip;
        private TMP_Text _openerCount;

        private float _pollTimer;
        private string _writtenClock;

        private void Awake()
        {
            _crafting = ServiceLocator.Get<CraftingService>();
            _sea = ServiceLocator.Get<ExpeditionService>();
            Build();
            BuildOpener();
            if (_crafting != null) _crafting.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_crafting != null) _crafting.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnChanged() { if (_root != null && _root.gameObject.activeSelf) Refresh(); RefreshOpener(); }

        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = Loc.T("atolye.baslik");
            if (_oddsTitleLabel != null) _oddsTitleLabel.text = Loc.T("atolye.oranlar");
            if (_gateLabel != null) _gateLabel.text = Loc.T("atolye.yenileniyor");
            if (_bankLabel != null) _bankLabel.text = Loc.T("atolye.birikiyor");
            if (_sourceLabel != null) _sourceLabel.text = Loc.T("atolye.nereden");
            if (_root != null && _root.gameObject.activeSelf) Refresh();
            RefreshOpener();
        }

        public void Show() { if (_root != null) _root.gameObject.SetActive(true); Refresh(); }
        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

        /// <summary>Only the retooling clock needs a pulse, and only while someone is looking.</summary>
        private void Update()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = 1f;
            _crafting?.Poll();   // opens a stop whose deadline has passed; raises Changed if it did
            RefreshGateClock();
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "AtolyeKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();
            BuildHeader();
            BuildBench();
            BuildOdds();
            BuildDecideCard();
        }

        /// <summary>One opaque sheet behind everything — see CaptainRosterUI.BuildBackdrop for why.</summary>
        private void BuildBackdrop()
        {
            RectTransform sheet = Art(_root, "Zemin", cardPanel,
                                      new Vector2(0.020f, 0.020f), new Vector2(0.980f, 0.842f));
            var image = sheet.GetComponent<Image>();
            image.color = backdrop;
            image.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        private void BuildHeader()
        {
            RectTransform band = Art(_root, "Serit", ribbon, new Vector2(0.360f, 0.850f), new Vector2(0.640f, 0.992f));
            _titleLabel = UiBuild.Label(Zone(band, "Yazi", new Vector2(0.13f, RibbonBand - 0.13f),
                                             new Vector2(0.87f, RibbonBand + 0.13f)),
                                        "Text", Loc.T("atolye.baslik"), 38, TextAnchor.MiddleCenter);

            RectTransform chip = Chip(_root, "Puan", new Vector2(0.035f, 0.880f), new Vector2(0.235f, 0.963f));
            _pointsLabel = UiBuild.Label(Zone(chip, "Yazi", new Vector2(0.08f, 0f), new Vector2(0.92f, 1f)),
                                         "Text", string.Empty, 30, TextAnchor.MiddleCenter);
            _pointsLabel.color = Paper;

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                                       closeIcon != null ? closeIcon : UiSkin.ButtonGrey,
                                       new Color(0.30f, 0.34f, 0.42f, 1f), 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.878f, 0.873f), new Vector2(0.938f, 0.970f));
        }

        /// <summary>The bench card: level, XP, the ÜRET button, and the retooling stop when one runs.</summary>
        private void BuildBench()
        {
            RectTransform c = Art(_root, "Tezgah", cardPanel, new Vector2(0.035f, 0.030f), new Vector2(0.475f, 0.815f));

            _levelLabel = UiBuild.Label(Zone(c, "Seviye", new Vector2(0.07f, 0.880f), new Vector2(0.93f, 0.970f)),
                                        "Text", string.Empty, 40, TextAnchor.MiddleCenter);
            _levelLabel.color = Ink;

            _tierLabel = UiBuild.Label(Zone(c, "Kademe", new Vector2(0.07f, 0.820f), new Vector2(0.93f, 0.878f)),
                                       "Text", string.Empty, 24, TextAnchor.MiddleCenter);
            _tierLabel.color = InkSoft;

            RectTransform track = Bar(c, "XpCubuk", new Vector2(0.09f, 0.740f), new Vector2(0.91f, 0.800f), out _xpFill);
            _xpLabel = UiBuild.Label(track, "Yazi", string.Empty, 22, TextAnchor.MiddleCenter);
            _xpLabel.color = Paper;

            _craftBtn = UiBuild.Btn(c, "Uret", string.Empty,
                                    actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                    Good, 30, OnCraft);
            UiBuild.Anchor((RectTransform)_craftBtn.transform, new Vector2(0.10f, 0.560f), new Vector2(0.90f, 0.690f));
            PillFit.Wrap(_craftBtn.GetComponent<Image>());
            _craftLabel = _craftBtn.GetComponentInChildren<Text>();

            // The stop's own strip. It does NOT replace the button — crafting carries on while the
            // bench retools; only the level waits, which is exactly what the strip says.
            _gateCard = Flat(c, "Durak", new Color(0.15f, 0.21f, 0.33f, 0.96f),
                             new Vector2(0.07f, 0.330f), new Vector2(0.93f, 0.530f));
            _gateLabel = UiBuild.Label(Zone(_gateCard, "Baslik", new Vector2(0.05f, 0.58f), new Vector2(0.95f, 0.96f)),
                                       "Text", Loc.T("atolye.yenileniyor"), 26, TextAnchor.MiddleCenter);
            _gateClockLabel = UiBuild.Label(Zone(_gateCard, "Saat", new Vector2(0.05f, 0.24f), new Vector2(0.95f, 0.58f)),
                                            "Text", string.Empty, 34, TextAnchor.MiddleCenter);
            _bankLabel = UiBuild.Label(Zone(_gateCard, "Not", new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.24f)),
                                       "Text", Loc.T("atolye.birikiyor"), 18, TextAnchor.MiddleCenter);
            _bankLabel.color = new Color(0.75f, 0.81f, 0.92f, 1f);

            _sourceLabel = UiBuild.Label(Zone(c, "Nereden", new Vector2(0.07f, 0.040f), new Vector2(0.93f, 0.300f)),
                                         "Text", Loc.T("atolye.nereden"), 20, TextAnchor.LowerCenter);
            _sourceLabel.color = InkFaint;
            Fit(_sourceLabel, 12, 20);
        }

        /// <summary>The odds table: one row per grade, straight off <see cref="Crafting.LevelOdds"/>.</summary>
        private void BuildOdds()
        {
            RectTransform c = Art(_root, "Oranlar", cardPanel, new Vector2(0.505f, 0.030f), new Vector2(0.965f, 0.815f));

            _oddsTitleLabel = UiBuild.Label(Zone(c, "Baslik", new Vector2(0.07f, 0.890f), new Vector2(0.93f, 0.970f)),
                                            "Text", Loc.T("atolye.oranlar"), 30, TextAnchor.MiddleCenter);
            _oddsTitleLabel.color = Ink;

            const float top = 0.860f, bottom = 0.040f;
            float rh = (top - bottom) / Captains.GradeCount;
            for (int g = 0; g < Captains.GradeCount; g++)
            {
                RectTransform row = Zone(c, "Sira" + g, new Vector2(0.06f, top - (g + 1) * rh + 0.012f),
                                                        new Vector2(0.94f, top - g * rh - 0.012f));
                _oddsStripe[g] = Stripe(row, new Vector2(0f, 0.10f), new Vector2(0.035f, 0.90f));
                _oddsStripe[g].color = GradeTint[g];
                _oddsName[g] = UiBuild.Label(Zone(row, "Ad", new Vector2(0.09f, 0f), new Vector2(0.62f, 1f)),
                                             "Text", string.Empty, 26, TextAnchor.MiddleLeft);
                Fit(_oddsName[g], 14, 26);
                _oddsValue[g] = UiBuild.Label(Zone(row, "Deger", new Vector2(0.40f, 0f), new Vector2(1f, 1f)),
                                              "Text", string.Empty, 26, TextAnchor.MiddleRight);
                Fit(_oddsValue[g], 13, 26);
            }
        }

        /// <summary>The decision card: the fresh craft against what the slot wears. Built last so it
        /// draws over both columns; only ever visible while a craft is pending.</summary>
        private void BuildDecideCard()
        {
            _decideCard = Art(_root, "Karar", cardPanel, new Vector2(0.130f, 0.140f), new Vector2(0.870f, 0.790f));
            var image = _decideCard.GetComponent<Image>();
            image.raycastTarget = true;
            var eat = _decideCard.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;

            _decideTitle = UiBuild.Label(Zone(_decideCard, "Baslik", new Vector2(0.06f, 0.880f), new Vector2(0.94f, 0.970f)),
                                         "Text", string.Empty, 34, TextAnchor.MiddleCenter);
            Fit(_decideTitle, 16, 34);

            _decideScore = UiBuild.Label(Zone(_decideCard, "Guc", new Vector2(0.06f, 0.800f), new Vector2(0.94f, 0.875f)),
                                         "Text", string.Empty, 28, TextAnchor.MiddleCenter);

            _decideRows = UiBuild.Label(Zone(_decideCard, "Satirlar", new Vector2(0.10f, 0.360f), new Vector2(0.90f, 0.790f)),
                                        "Text", string.Empty, 24, TextAnchor.UpperLeft);
            _decideRows.color = Ink;

            _decideWorn = UiBuild.Label(Zone(_decideCard, "Mevcut", new Vector2(0.10f, 0.280f), new Vector2(0.90f, 0.355f)),
                                        "Text", string.Empty, 22, TextAnchor.MiddleLeft);
            _decideWorn.color = InkSoft;
            Fit(_decideWorn, 12, 22);

            Button equip = UiBuild.Btn(_decideCard, "Giydir", string.Empty,
                                       actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                       Good, 26, OnEquip);
            UiBuild.Anchor((RectTransform)equip.transform, new Vector2(0.08f, 0.060f), new Vector2(0.48f, 0.200f));
            PillFit.Wrap(equip.GetComponent<Image>());
            _equipLabel = equip.GetComponentInChildren<Text>();

            Button scrap = UiBuild.Btn(_decideCard, "Sok", string.Empty,
                                       actionButton != null ? actionButton : UiSkin.ButtonYellow,
                                       new Color(0.94f, 0.68f, 0.20f, 1f), 26, OnSalvage);
            UiBuild.Anchor((RectTransform)scrap.transform, new Vector2(0.52f, 0.060f), new Vector2(0.92f, 0.200f));
            PillFit.Wrap(scrap.GetComponent<Image>());
            _salvageLabel = scrap.GetComponentInChildren<Text>();
            Fit(_salvageLabel, 12, 26);
        }

        // ---------------------------------------------------------------- opener
        /// <summary>Order 4 in the HUD's bottom row, after the captains. The chip is the point
        /// balance — the thing that makes the button worth pressing.</summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Sprite icon = Resources.Load<Sprite>(OpenerIconResource);
            Button open = hud.AttachBottomButton(4, "BtnAtolye",
                                                 icon != null ? icon : UiSkin.ButtonYellow, Show);
            if (open == null) return;

            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _crafting == null) return;
            long points = _crafting.Points;
            bool show = points > 0L;
            if (_openerChip.activeSelf != show) _openerChip.SetActive(show);
            if (show && _openerCount != null)
            {
                string text = points > 99L ? "99+" : points.ToString();
                if (_openerCount.text != text) _openerCount.text = text;
            }
        }

        // --------------------------------------------------------------- actions
        private void OnCraft()
        {
            if (_crafting == null) return;
            _crafting.TryCraft(out _);   // refresh rides the Changed event; refusal changes nothing
        }

        private void OnEquip()
        {
            if (_crafting == null) return;
            _crafting.EquipPending();
        }

        private void OnSalvage()
        {
            if (_crafting == null) return;
            _crafting.SalvagePending(out _);
        }

        // --------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_crafting == null) return;

            _pointsLabel.text = string.Format(Loc.T("atolye.puan"), _crafting.Points);

            int level = _crafting.Level;
            _levelLabel.text = string.Format(Loc.T("atolye.seviye"), level);
            _tierLabel.text = string.Format(Loc.T("atolye.kademe"), _crafting.CurrentTier + 1);

            RefreshXpBar(level);
            RefreshGate();

            _craftLabel.text = Loc.T("atolye.uret") + "  ·  "
                             + string.Format(Loc.T("atolye.puan"), _crafting.Tuning.CraftCost);
            bool canCraft = !_crafting.HasPending && _crafting.Points >= _crafting.Tuning.CraftCost;
            _craftBtn.interactable = canCraft;

            for (int g = 0; g < Captains.GradeCount; g++)
            {
                _oddsName[g].text = Loc.T("kaptan.derece." + g);
                double odds = _crafting.OddsOf(g);
                if (odds > 0d)
                {
                    _oddsName[g].color = Ink;
                    _oddsStripe[g].color = GradeTint[g];
                    _oddsValue[g].text = PctOdds(odds);
                    _oddsValue[g].color = GradeTint[g];
                }
                else
                {
                    int unlock = Crafting.UnlockLevelOf(g);
                    _oddsName[g].color = InkFaint;
                    _oddsStripe[g].color = new Color(GradeTint[g].r, GradeTint[g].g, GradeTint[g].b, 0.25f);
                    _oddsValue[g].text = unlock > 0 ? string.Format(Loc.T("atolye.acilir"), unlock) : "—";
                    _oddsValue[g].color = InkFaint;
                }
            }

            RefreshDecideCard();
        }

        private void RefreshXpBar(int level)
        {
            long xp = _crafting.Xp;
            long need = Crafting.XpToNext(level);
            long into = Crafting.XpIntoLevel(xp, level);
            if (level >= Crafting.MaxLevel || need <= 0L)
            {
                _xpFill.anchorMax = new Vector2(1f, 1f);
                _xpLabel.text = Loc.T("atolye.maks");
                return;
            }
            long shown = into < need ? into : need;
            _xpFill.anchorMax = new Vector2(need > 0L ? (float)shown / need : 0f, 1f);
            string text = Loc.T("atolye.xp") + "  " + shown + "/" + need;
            if (into > need) text += "  (+" + (into - need) + ")";   // banked behind a stop
            _xpLabel.text = text;
        }

        private void RefreshGate()
        {
            bool gated = _crafting.IsGated;
            if (_gateCard.gameObject.activeSelf != gated) _gateCard.gameObject.SetActive(gated);
            _writtenClock = null;
            if (gated) RefreshGateClock();
        }

        /// <summary>The only per-second write, and only while the strip is up.</summary>
        private void RefreshGateClock()
        {
            if (_gateCard == null || !_gateCard.gameObject.activeSelf || _crafting == null) return;
            double left = _crafting.GateSecondsLeft;
            int hours = (int)(left / 3600d);
            string text = hours > 0
                ? hours + ":" + (((int)left % 3600) / 60).ToString("00") + ":" + ((int)left % 60).ToString("00")
                : UiBuild.Clock((float)left);
            if (text == _writtenClock) return;
            _writtenClock = text;
            _gateClockLabel.text = text;
        }

        private void RefreshDecideCard()
        {
            bool pending = _crafting.HasPending;
            if (_decideCard.gameObject.activeSelf != pending) _decideCard.gameObject.SetActive(pending);
            if (!pending) return;

            SeaCombat.Item item = _crafting.PendingItem();
            SeaCombat.Item cur = _sea != null ? _sea.GearItem(item.Slot) : new SeaCombat.Item { Grade = -1 };
            SeaCombat.Tuning t = _sea != null ? _sea.Combat : SeaCombat.Tuning.Default;
            Color tint = GradeTint[Mathf.Clamp(item.Grade, 0, GradeTint.Length - 1)];

            _decideTitle.text = Loc.T("kaptan.derece." + item.Grade) + "  ·  " + Loc.T("deniz.slot." + item.Slot);
            _decideTitle.color = tint;

            int score = SeaCombat.ItemScore(item, t);
            int delta = score - (_sea != null ? _sea.GearScore(item.Slot) : 0);
            _decideScore.text = Loc.T("deniz.guc") + "  " + score + "   (" + (delta >= 0 ? "+" : "") + delta + ")";
            _decideScore.color = delta >= 0 ? Good : Bad;

            _decideRows.text = ItemRows(item, cur, cur.Grade >= 0);

            _decideWorn.text = cur.Grade < 0
                ? Loc.T("deniz.mevcut") + ":  " + Loc.T("deniz.bos")
                : Loc.T("deniz.mevcut") + ":  " + Loc.T("kaptan.derece." + cur.Grade)
                  + "  ·  " + Loc.T("deniz.guc") + " " + (_sea != null ? _sea.GearScore(item.Slot) : 0);

            _equipLabel.text = Loc.T("deniz.giydir");
            _salvageLabel.text = string.Format(Loc.T("atolye.sok"),
                                               SeaCombat.ScrapFor(item.Grade),
                                               Crafting.SalvageXpFor(item.Grade));
        }

        /// <summary>An item's five rows, tinted by the compare — the sea's loot card, in this ink.</summary>
        private string ItemRows(in SeaCombat.Item item, in SeaCombat.Item against, bool compare)
        {
            string up = "<color=#1E9E4A>", down = "<color=#C43B32>", end = "</color>";
            string hull = Loc.T("deniz.cesaret") + "  +" + N(item.Hull);
            string shot = Loc.T("deniz.slot.0") + "  +" + N(item.Shot);
            string def = Loc.T("deniz.st.savunma") + "  +" + D(item.Def);
            string spd = Loc.T("deniz.st.surat") + "  +" + D(item.Spd);
            string sec = item.Sec == SeaCombat.SecNone
                ? "—" : Loc.T(SecKey(item.Sec)) + "  +" + Pct(item.SecAmt);
            if (compare)
            {
                hull = (item.Hull >= against.Hull ? up : down) + hull + end;
                shot = (item.Shot >= against.Shot ? up : down) + shot + end;
                def = (item.Def >= against.Def ? up : down) + def + end;
                spd = (item.Spd >= against.Spd ? up : down) + spd + end;
                double curSec = against.Grade < 0 ? 0d : against.SecAmt;
                if (item.Sec != SeaCombat.SecNone || curSec > 0d)
                    sec = ((item.Sec == SeaCombat.SecNone ? 0d : item.SecAmt) >= curSec ? up : down) + sec + end;
            }
            return hull + "\n" + shot + "\n" + def + "\n" + spd + "\n" + sec;
        }

        // ---------------------------------------------------------------- pieces
        private static RectTransform Zone(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        private static RectTransform Art(RectTransform parent, string name, Sprite sprite,
                                         Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite != null ? sprite : UiSkin.Panel;
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = img.type == Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = false;
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        private RectTransform Chip(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = chipPill != null ? chipPill : UiSkin.Pill;
            img.type = Image.Type.Sliced;
            img.color = img.sprite != null ? Color.white : new Color(0.16f, 0.20f, 0.28f, 0.95f);
            img.raycastTarget = false;
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        private static RectTransform Flat(RectTransform parent, string name, Color c, Vector2 aMin, Vector2 aMax)
            => UiBuild.Flat(parent, name, c, aMin, aMax);

        private static Image Stripe(RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject("Cizgi", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return img;
        }

        private RectTransform Bar(RectTransform parent, string name, Vector2 aMin, Vector2 aMax,
                                  out RectTransform fill)
        {
            RectTransform track;
            if (barTrack != null)
            {
                track = Art(parent, name, barTrack, aMin, aMax);
                var go = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(track, false);
                var img = go.GetComponent<Image>();
                img.sprite = barFill;
                img.type = Image.Type.Sliced;
                img.raycastTarget = false;
                fill = UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, new Vector2(0f, 1f));
            }
            else
            {
                track = UiBuild.Bar(parent, name, new Color(0.20f, 0.25f, 0.34f, 1f),
                                    new Color(0.30f, 0.72f, 0.40f, 1f), aMin, aMax, out fill);
            }
            return track;
        }

        /// <summary>Shrink-to-fit so a long translation stays on its row.</summary>
        private static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private static string SecKey(int sec)
        {
            switch (sec)
            {
                case SeaCombat.SecCrit:    return "deniz.st.kritik";
                case SeaCombat.SecDodge:   return "deniz.st.manevra";
                case SeaCombat.SecStun:    return "deniz.st.sersem";
                case SeaCombat.SecMend:    return "deniz.st.onarim";
                case SeaCombat.SecBurn:    return "deniz.st.yangin";
                case SeaCombat.SecPlunder: return "deniz.st.yagma";
                case SeaCombat.SecSalvo:   return "deniz.st.salvo";
                case SeaCombat.SecSteal:   return "deniz.st.cancalma";
                case SeaCombat.SecPoison:  return "deniz.st.zehir";
                default:                   return "deniz.bos";
            }
        }

        /// <summary>82% for the big shares, 0.5% never rounded to nothing for the rare ones.</summary>
        private static string PctOdds(double v)
        {
            double pct = v * 100d;
            if (pct >= 10d) return Mathf.RoundToInt((float)pct) + "%";
            double one = System.Math.Round(pct * 10d) / 10d;
            return one.ToString("0.#") + "%";
        }

        private static string Pct(double v) => Mathf.RoundToInt((float)(v * 100d)) + "%";

        private static string N(double v)
        {
            if (v < 999.5d) return Mathf.RoundToInt((float)v).ToString();
            double k = v / 1000d;
            return k < 99.95d ? k.ToString("0.0") + "k" : Mathf.RoundToInt((float)k) + "k";
        }

        private static string D(double v) => (System.Math.Round(v * 10d) / 10d).ToString("0.#");
    }
}
