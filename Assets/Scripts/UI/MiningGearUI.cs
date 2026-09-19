using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The captain's mining loadout screen: the four worn slots, the income bonus they add, and the
    /// craft button that fills them.
    ///
    /// Built in code for the same reason <see cref="CraftingUI"/> is: the slots and grades come out
    /// of <see cref="MiningGear"/>'s own tables. Unlike the workshop this screen has no decision
    /// card — a craft here always resolves to exactly one outcome (equipped or scrapped, see
    /// <see cref="MiningGearService.TryCraft"/>) — so there is nothing left waiting between visits
    /// and nothing here needs to be more than a readout plus a button.
    ///
    /// DRAWN FROM <see cref="EkranKit"/> ONLY: the league sheet, ribbon and green capsule, the
    /// mining-point pill, the bolted equipment slot, the orange action capsule. No design set has a
    /// pickaxe, helmet, bag or lantern icon, so a slot says its name and grade inside its own well.
    ///
    /// Refreshed on open and on <see cref="MiningGearService.Changed"/>; the once-a-second Update
    /// only drives the next-point countdown, and only while the screen is open.
    /// </summary>
    public sealed class MiningGearUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 111;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0f, 0f, 0f, 0.62f);

        private const string OpenerIconResource = "UI/Buttons/maden";
        private const string OpenerButtonName = "BtnMaden";

        /// <summary>
        /// The grade ladder's ink, lifted for the slot's navy well — the workshop's five at the same
        /// hues, but the common grey and the rare blue at their old values all but vanish on navy.
        /// </summary>
        private static readonly Color[] GradeTint =
        {
            new Color(0.80f, 0.85f, 0.92f, 1f),
            new Color(0.45f, 0.76f, 1f, 1f),
            new Color(0.78f, 0.60f, 1f, 1f),
            new Color(1f, 0.76f, 0.30f, 1f),
            new Color(1f, 0.48f, 0.60f, 1f),
        };

        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.50f, 0.56f, 0.65f, 1f);
        private static readonly Color Good = new Color(0.20f, 0.60f, 0.30f, 1f);

        /// <summary>
        /// A slot that is not the targeted one sits a shade back. The art is pre-coloured, so this is
        /// a gentle multiply, not a recolour — enough to point at the selected frame, not so much it
        /// reads as locked.
        /// </summary>
        private static readonly Color SlotResting = new Color(0.78f, 0.82f, 0.90f, 1f);
        private static readonly Vector3 SlotPicked = new Vector3(1.05f, 1.05f, 1f);

        /// <summary>The equipment slot art's own aspect (300×286) and its dark well, as fractions.</summary>
        private const float SlotAspect = 300f / 286f;
        private static readonly Vector2 WellMin = new Vector2(0.164f, 0.172f);
        private static readonly Vector2 WellMax = new Vector2(0.836f, 0.853f);

        /// <summary>
        /// The pale field of the mining-point pill. The left cap is the pickaxe badge and
        /// <see cref="PillFit"/> scales it with the box's height, so the field starts where that cap
        /// ends — at the heights used here a little over a third of the way across.
        /// </summary>
        private static readonly Vector2 PillFieldMin = new Vector2(0.40f, 0.26f);
        private static readonly Vector2 PillFieldMax = new Vector2(0.90f, 0.74f);

        private MiningGearService _mining;
        private LocalizationService _loc;
        private RectTransform _root;

        private Text _titleLabel, _pointsLabel, _bonusLabel, _craftLabel, _nextPointLabel, _resultLabel;
        private Text _targetLabel, _targetCostLabel;
        private Button _craftBtn, _targetBtn;
        private Sprite _craftFace, _targetFace, _deadFace;
        private int _selectedSlot;

        private readonly RectTransform[] _slotCard = new RectTransform[MiningGear.SlotCount];
        private readonly Text[] _slotName = new Text[MiningGear.SlotCount];
        private readonly Text[] _slotGrade = new Text[MiningGear.SlotCount];

        private GameObject _openerChip;
        private TMP_Text _openerCount;

        private float _pollTimer;

        private void Awake()
        {
            _mining = ServiceLocator.Get<MiningGearService>();
            Build();
            BuildOpener();
            if (_mining != null) _mining.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_mining != null) _mining.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnChanged() { if (_root != null && _root.gameObject.activeSelf) Refresh(); RefreshOpener(); }

        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = EtkinlikKit.OneLine(Loc.T("madenci.baslik"));
            for (int i = 0; i < MiningGear.SlotCount; i++)
                if (_slotName[i] != null) _slotName[i].text = Loc.T("madenci.yuva." + i);
            if (_root != null && _root.gameObject.activeSelf) Refresh();
            RefreshOpener();
        }

        public void Show()
        {
            if (_root != null) _root.gameObject.SetActive(true);
            if (_resultLabel != null) _resultLabel.text = string.Empty;
            Refresh();
        }

        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

        /// <summary>Only the next-point countdown needs a pulse, and only while someone is looking.</summary>
        private void Update()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = 1f;
            _mining?.Poll();   // banks any whole ticks elapsed; raises Changed if the pool moved
            RefreshNextPoint();
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            _craftFace = EkranKit.Get("btn_turuncu");
            _deadFace = EkranKit.Get("btn_bos");
            _targetFace = LigKit.Get("al_butonu");

            RectTransform canvas = UiBuild.Canvas(transform, "MadenKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();
            BuildHeader();
            BuildSlots();
            BuildFooter();
            UiBuild.InsetContent(_root);
        }

        /// <summary>
        /// The league sheet, eating taps so they never reach the dismiss scrim under it. Across
        /// 0.03–0.97 for the reason <see cref="LadderUI"/> records: that width is what keeps the
        /// star crest in its top-centre slice unstretched.
        /// </summary>
        private void BuildBackdrop()
        {
            Image sheet = EkranKit.Sliced(_root, "Zemin", LigKit.Board,
                                          new Vector2(0.030f, 0.050f), new Vector2(0.970f, 0.870f), false);
            sheet.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        /// <summary>
        /// The ribbon across the rail under the crest and the close disc on the sheet's corner, both
        /// at the league board's offsets from its top edge, then the points pill and the bonus.
        /// </summary>
        private void BuildHeader()
        {
            _titleLabel = EtkinlikKit.Header(_root, EtkinlikKit.OneLine(Loc.T("madenci.baslik")), Hide);

            Image pill = EkranKit.Sliced(_root, "Puan", EkranKit.Get("hap_puan"),
                                         new Vector2(0.110f, 0.636f), new Vector2(0.500f, 0.688f), true);
            _pointsLabel = UiBuild.Label(Zone(pill.rectTransform, "Yazi", PillFieldMin, PillFieldMax),
                                         "Text", string.Empty, 28, TextAnchor.MiddleCenter);
            _pointsLabel.color = EkranKit.Ink;
            Fit(_pointsLabel, 14, 28);

            _bonusLabel = UiBuild.Label(Zone(_root, "Bonus", new Vector2(0.52f, 0.636f), new Vector2(0.888f, 0.688f)),
                                        "Text", string.Empty, 28, TextAnchor.MiddleRight);
            _bonusLabel.color = Good;
            Fit(_bonusLabel, 14, 28);
        }

        /// <summary>
        /// The four worn slots as a 2x2 grid — pickaxe, helmet, bag, lantern, in
        /// <see cref="MiningGear"/>'s own slot order. Each cell holds a frame locked to the art's
        /// aspect: the bolted corners would smear under a nine-slice, and a stretched frame would
        /// throw the well off the fractions its labels are seated on.
        /// </summary>
        private void BuildSlots()
        {
            Sprite frame = EkranKit.Get("yuva");
            // Narrower than the content edges: a frame is only as wide as its row is tall allows, so
            // full-width cells left a gutter between the columns wider than the frames' own bolts.
            const float gridTop = 0.625f, gridBottom = 0.345f, gridLeft = 0.160f, gridRight = 0.840f;
            const float gapX = 0.010f, gapY = 0.005f;
            float colW = (gridRight - gridLeft) / 2f;
            float rowH = (gridTop - gridBottom) / 2f;

            for (int i = 0; i < MiningGear.SlotCount; i++)
            {
                int col = i % 2, row = i / 2;
                Vector2 aMin = new Vector2(gridLeft + col * colW + gapX, gridTop - (row + 1) * rowH + gapY);
                Vector2 aMax = new Vector2(gridLeft + (col + 1) * colW - gapX, gridTop - row * rowH - gapY);
                RectTransform cell = Zone(_root, "Hucre" + i, aMin, aMax);

                Image card = EkranKit.Sliced(cell, "Yuva" + i, frame, Vector2.zero, Vector2.one, false);
                card.type = Image.Type.Simple;
                card.raycastTarget = true;
                var fitter = card.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = SlotAspect;
                _slotCard[i] = card.rectTransform;

                var select = card.gameObject.AddComponent<Button>();
                select.transition = Selectable.Transition.None;
                select.targetGraphic = card;
                int picked = i;
                select.onClick.AddListener(() => SelectSlot(picked));

                RectTransform well = Zone(card.rectTransform, "Kuyu", WellMin, WellMax);
                _slotName[i] = UiBuild.Label(Zone(well, "Ad", new Vector2(0.08f, 0.50f), new Vector2(0.92f, 0.86f)),
                                             "Text", Loc.T("madenci.yuva." + i), 32, TextAnchor.MiddleCenter);
                _slotName[i].color = EkranKit.Paper;
                Fit(_slotName[i], 16, 32);

                _slotGrade[i] = UiBuild.Label(Zone(well, "Derece", new Vector2(0.08f, 0.16f), new Vector2(0.92f, 0.46f)),
                                              "Text", string.Empty, 26, TextAnchor.MiddleCenter);
                Fit(_slotGrade[i], 14, 26);
            }
        }

        private void BuildFooter()
        {
            // One line on the cream inlay — "CRAFT · 3 MINING POINTS" — shrinking for a long language;
            // the inlay is too short a band for the price to take a second line.
            _craftBtn = EkranKit.Capsule(_root, "Uret", _craftFace,
                                         new Vector2(0.170f, 0.263f), new Vector2(0.830f, 0.330f), OnCraft);
            _craftLabel = UiBuild.Label(Zone(_craftBtn.transform as RectTransform, "Yazi", EkranKit.InlayMin, EkranKit.InlayMax),
                                        "Text", string.Empty, 30, TextAnchor.MiddleCenter);
            _craftLabel.color = EkranKit.Ink;
            Fit(_craftLabel, 14, 30);
            _craftLabel.verticalOverflow = VerticalWrapMode.Truncate;
            UiBuild.Anchor(_craftLabel.rectTransform, new Vector2(0.02f, 0f), new Vector2(0.98f, 1f));

            _targetBtn = EkranKit.Capsule(_root, "HedefliUret", _targetFace,
                                          new Vector2(0.240f, 0.200f), new Vector2(0.760f, 0.252f), OnTargetedCraft);
            _targetLabel = UiBuild.Label(Zone(_targetBtn.transform as RectTransform, "Yazi", EkranKit.CapsMin, EkranKit.CapsMax),
                                         "Text", string.Empty, 28, TextAnchor.MiddleCenter);
            Fit(_targetLabel, 14, 28);

            _targetCostLabel = UiBuild.Label(Zone(_root, "HedefMaliyet", new Vector2(0.110f, 0.168f), new Vector2(0.888f, 0.196f)),
                                             "Text", string.Empty, 22, TextAnchor.MiddleCenter);
            _targetCostLabel.color = InkSoft;
            Fit(_targetCostLabel, 12, 22);

            _nextPointLabel = UiBuild.Label(Zone(_root, "SonrakiPuan", new Vector2(0.110f, 0.142f), new Vector2(0.888f, 0.168f)),
                                            "Text", string.Empty, 22, TextAnchor.MiddleCenter);
            _nextPointLabel.color = InkFaint;
            Fit(_nextPointLabel, 12, 22);

            _resultLabel = UiBuild.Label(Zone(_root, "Sonuc", new Vector2(0.110f, 0.100f), new Vector2(0.888f, 0.142f)),
                                         "Text", string.Empty, 24, TextAnchor.MiddleCenter);
            Fit(_resultLabel, 12, 24);
        }

        // ---------------------------------------------------------------- opener
        /// <summary>Order 7 in the HUD's bottom row, between the league and the More button — lands
        /// in the More sheet alongside the other secondary screens, the same way the workshop's
        /// opener does. The chip is the point balance.</summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Sprite icon = Resources.Load<Sprite>(OpenerIconResource);
            Button open = hud.AttachBottomButton(7, OpenerButtonName,
                                                 icon != null ? icon : UiSkin.ButtonYellow, Show);
            if (open == null) return;

            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _mining == null) return;
            long points = _mining.Points;
            bool show = points > 0L;
            if (_openerChip.activeSelf != show) _openerChip.SetActive(show);
            if (show && _openerCount != null)
            {
                string text = points > 99L ? "99+" : points.ToString();
                if (_openerCount.text != text) _openerCount.text = text;
            }
        }

        // --------------------------------------------------------------- actions
        private void SelectSlot(int slot)
        {
            if (slot < 0 || slot >= MiningGear.SlotCount) return;
            _selectedSlot = slot;
            Refresh();
        }

        private void OnCraft()
        {
            if (_mining == null) return;
            MiningGearService.CraftResult result = _mining.TryCraft();
            if (!result.Crafted) return;   // refused for lack of points; refresh rides Changed either way
            ShowResult(result);
        }

        private void OnTargetedCraft()
        {
            if (_mining == null) return;
            MiningGearService.CraftResult result = _mining.TryTargetedCraft(_selectedSlot);
            if (!result.Crafted)
            {
                Refresh();
                _resultLabel.text = Loc.T("madenci.hedef.yetersiz");
                _resultLabel.color = InkSoft;
                return;
            }
            ShowResult(result);
        }

        /// <summary>
        /// The sheet is near-white, so a result takes the grade's hue only as far as it still reads
        /// there: the lifted navy-well tints are too pale for it, and an equip is written in green.
        /// </summary>
        private void ShowResult(in MiningGearService.CraftResult result)
        {
            _resultLabel.text = ResultText(result);
            _resultLabel.color = result.Equipped ? Good : InkSoft;
        }

        /// <summary>What was made, then a receipt of every balance the craft moved, each by name:
        /// the points always, the targeting fee when there was one, and the scrap refund — from the
        /// loser, or from the old piece an upgrade pushed out of its slot.</summary>
        private static string ResultText(in MiningGearService.CraftResult r)
        {
            string outcome = string.Format(Loc.T(r.Equipped ? "madenci.kusanildi" : "madenci.hurdaya_dondu"),
                                           Loc.T("kaptan.derece." + r.Grade), Loc.T("madenci.yuva." + r.Slot));
            string receipt = string.Empty;
            if (r.PointsSpent > 0L) receipt = CurrencyText.Cost(CurrencyId.MiningPoints, r.PointsSpent);
            if (r.ScrapSpent > 0L) receipt = Joined(receipt, CurrencyText.Cost(CurrencyId.MiningScrap, r.ScrapSpent));
            if (r.ScrapEarned > 0L) receipt = Joined(receipt, CurrencyText.Gain(CurrencyId.MiningScrap, r.ScrapEarned));
            return receipt.Length > 0 ? outcome + "\n" + receipt : outcome;
        }

        private static string Joined(string head, string tail) => head.Length > 0 ? head + "  ·  " + tail : tail;

        // --------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_mining == null) return;

            _pointsLabel.text = string.Format(Loc.T("madenci.puan"), _mining.Points);
            _bonusLabel.text = string.Format(Loc.T("madenci.bonus"), Pct(_mining.IncomeMultiplier - 1d));

            for (int i = 0; i < MiningGear.SlotCount; i++)
            {
                bool picked = i == _selectedSlot;
                Image cardImage = _slotCard[i].GetComponent<Image>();
                if (cardImage != null) cardImage.color = picked ? Color.white : SlotResting;
                _slotCard[i].localScale = picked ? SlotPicked : Vector3.one;

                int grade = _mining.WornGrade(i);
                if (grade >= 0)
                {
                    _slotGrade[i].text = Loc.T("kaptan.derece." + grade);
                    _slotGrade[i].color = GradeTint[Mathf.Clamp(grade, 0, GradeTint.Length - 1)];
                }
                else
                {
                    _slotGrade[i].text = Loc.T("deniz.bos");
                    _slotGrade[i].color = new Color(EkranKit.PaperSoft.r, EkranKit.PaperSoft.g, EkranKit.PaperSoft.b, 0.6f);
                }
            }

            bool canCraft = _mining.CanCraft;
            _craftLabel.text = Loc.T("madenci.uret") + "  ·  " + CurrencyText.Amount(CurrencyId.MiningPoints, _mining.CraftCost);
            EkranKit.SetFace(_craftBtn, _craftFace, _deadFace, canCraft);

            long scrapCost = _mining.TargetedScrapCost(_selectedSlot);
            bool canTarget = _mining.CanTargetedCraft(_selectedSlot);
            _targetLabel.text = string.Format(Loc.T("madenci.hedef.uret"), Loc.T("madenci.yuva." + _selectedSlot));
            _targetLabel.color = canTarget ? EkranKit.Paper : EkranKit.Ink;
            _targetCostLabel.text = string.Format(Loc.T("madenci.hedef.maliyet"),
                                                   _mining.CraftCost, scrapCost, _mining.Points, _mining.Scrap);
            EkranKit.SetFace(_targetBtn, _targetFace, _deadFace, canTarget);

            RefreshNextPoint();
        }

        private void RefreshNextPoint()
        {
            if (_nextPointLabel == null || _mining == null) return;
            long cap = _mining.PointCap;
            if (cap > 0L && _mining.Points >= cap)
            {
                _nextPointLabel.text = string.Empty;
                return;
            }
            _nextPointLabel.text = string.Format(Loc.T("madenci.sonraki"), UiBuild.Clock((float)_mining.SecondsToNextPoint));
        }

        // ---------------------------------------------------------------- pieces
        private static RectTransform Zone(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        /// <summary>Shrink-to-fit so a long translation stays on its row.</summary>
        private static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        /// <summary>Magnitude only — the "+" already lives in the "madenci.bonus" line itself.</summary>
        private static string Pct(double v) => Mathf.RoundToInt((float)(v * 100d)) + "%";
    }
}
