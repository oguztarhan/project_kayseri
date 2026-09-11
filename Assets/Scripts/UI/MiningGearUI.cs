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
    /// Refreshed on open and on <see cref="MiningGearService.Changed"/>; the once-a-second Update
    /// only drives the next-point countdown, and only while the screen is open.
    /// </summary>
    public sealed class MiningGearUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 111;

        [Header("Görseller")]
        [Tooltip("Kart gövdesi — MaviSet/panel_beyaz.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("ÜRET düğmesi — MaviSet/btn_hap_kalin.")]
        [SerializeField] private Sprite actionButton;
        [Tooltip("Kapat düğmesi — MaviSet/btn_kapat_yeni.")]
        [SerializeField] private Sprite closeIcon;
        [Tooltip("Puan hapı — MaviSet/gosterge_grafit.")]
        [SerializeField] private Sprite chipPill;
        [Tooltip("Yuva simgeleri: kazma, miğfer, çanta, fener — bu sırayla (Game.Core.MiningGear " +
                 "slot sabitleri). Boş bırakılan bir yuva yalnızca adını gösterir.")]
        [SerializeField] private Sprite[] slotIcons = new Sprite[MiningGear.SlotCount];

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0f, 0f, 0f, 0.62f);
        [SerializeField] private Color backdrop = new Color(0.92f, 0.94f, 0.99f, 0.98f);

        private const string OpenerIconResource = "UI/Buttons/maden";
        private const string OpenerButtonName = "BtnMaden";

        /// <summary>The grade ladder's ink — the same five the workshop and the sea wear.</summary>
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

        private MiningGearService _mining;
        private LocalizationService _loc;
        private RectTransform _root;

        private Text _titleLabel, _pointsLabel, _bonusLabel, _craftLabel, _nextPointLabel, _resultLabel;
        private Text _targetLabel, _targetCostLabel;
        private Button _craftBtn, _targetBtn;
        private int _selectedSlot;

        private readonly RectTransform[] _slotCard = new RectTransform[MiningGear.SlotCount];
        private readonly Image[] _slotStripe = new Image[MiningGear.SlotCount];
        private readonly Image[] _slotIcon = new Image[MiningGear.SlotCount];
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
            if (_titleLabel != null) _titleLabel.text = Loc.T("madenci.baslik");
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

        /// <summary>One opaque sheet behind everything, eating taps so they never reach the dismiss
        /// scrim under it — see CraftingUI.BuildBackdrop for why.</summary>
        private void BuildBackdrop()
        {
            RectTransform sheet = Art(_root, "Zemin", cardPanel, new Vector2(0.06f, 0.140f), new Vector2(0.94f, 0.820f));
            var image = sheet.GetComponent<Image>();
            image.color = backdrop;
            image.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        private void BuildHeader()
        {
            _titleLabel = UiBuild.Label(Zone(_root, "Baslik", new Vector2(0.10f, 0.752f), new Vector2(0.82f, 0.808f)),
                                        "Text", Loc.T("madenci.baslik"), 36, TextAnchor.MiddleLeft);
            _titleLabel.color = Ink;

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                                       closeIcon != null ? closeIcon : UiSkin.ButtonGrey,
                                       new Color(0.30f, 0.34f, 0.42f, 1f), 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.858f, 0.755f), new Vector2(0.920f, 0.808f));

            RectTransform chip = Chip(_root, "Puan", new Vector2(0.10f, 0.690f), new Vector2(0.42f, 0.745f));
            _pointsLabel = UiBuild.Label(Zone(chip, "Yazi", new Vector2(0.08f, 0f), new Vector2(0.92f, 1f)),
                                         "Text", string.Empty, 26, TextAnchor.MiddleCenter);
            _pointsLabel.color = Paper;

            _bonusLabel = UiBuild.Label(Zone(_root, "Bonus", new Vector2(0.45f, 0.690f), new Vector2(0.90f, 0.745f)),
                                        "Text", string.Empty, 26, TextAnchor.MiddleRight);
            _bonusLabel.color = Good;
        }

        /// <summary>The four worn slots, laid out as a 2x2 grid: pickaxe, helmet, bag, lantern, in
        /// <see cref="MiningGear"/>'s own slot order.</summary>
        private void BuildSlots()
        {
            const float gridTop = 0.660f, gridBottom = 0.385f, gridLeft = 0.10f, gridRight = 0.90f;
            const float pad = 0.015f;
            float colW = (gridRight - gridLeft) / 2f;
            float rowH = (gridTop - gridBottom) / 2f;

            for (int i = 0; i < MiningGear.SlotCount; i++)
            {
                int col = i % 2, row = i / 2;
                Vector2 aMin = new Vector2(gridLeft + col * colW + pad, gridTop - (row + 1) * rowH + pad);
                Vector2 aMax = new Vector2(gridLeft + (col + 1) * colW - pad, gridTop - row * rowH - pad);

                RectTransform card = Art(_root, "Yuva" + i, cardPanel, aMin, aMax);
                _slotCard[i] = card;

                var select = card.gameObject.AddComponent<Button>();
                select.transition = Selectable.Transition.None;
                select.targetGraphic = card.GetComponent<Image>();
                if (select.targetGraphic != null) select.targetGraphic.raycastTarget = true;
                int picked = i;
                select.onClick.AddListener(() => SelectSlot(picked));

                _slotStripe[i] = Stripe(card, new Vector2(0f, 0f), new Vector2(0.05f, 1f));
                _slotStripe[i].color = GradeTint[0];

                RectTransform iconZone = Zone(card, "Simge", new Vector2(0.10f, 0.32f), new Vector2(0.38f, 0.90f));
                var iconGo = new GameObject("Img", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(iconZone, false);
                _slotIcon[i] = iconGo.GetComponent<Image>();
                _slotIcon[i].preserveAspect = true;
                _slotIcon[i].raycastTarget = false;
                _slotIcon[i].enabled = false;
                if (slotIcons != null && i < slotIcons.Length && slotIcons[i] != null)
                {
                    _slotIcon[i].sprite = slotIcons[i];
                    _slotIcon[i].enabled = true;
                }
                UiBuild.Anchor((RectTransform)iconGo.transform, Vector2.zero, Vector2.one);

                _slotName[i] = UiBuild.Label(Zone(card, "Ad", new Vector2(0.42f, 0.52f), new Vector2(0.95f, 0.90f)),
                                             "Text", Loc.T("madenci.yuva." + i), 22, TextAnchor.MiddleLeft);
                _slotName[i].color = Ink;
                Fit(_slotName[i], 12, 22);

                _slotGrade[i] = UiBuild.Label(Zone(card, "Derece", new Vector2(0.10f, 0.08f), new Vector2(0.95f, 0.38f)),
                                              "Text", string.Empty, 22, TextAnchor.MiddleLeft);
                Fit(_slotGrade[i], 12, 22);
            }
        }

        private void BuildFooter()
        {
            _craftBtn = UiBuild.Btn(_root, "Uret", string.Empty,
                                    actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                    Good, 30, OnCraft);
            UiBuild.Anchor((RectTransform)_craftBtn.transform, new Vector2(0.10f, 0.215f), new Vector2(0.47f, 0.300f));
            PillFit.Wrap(_craftBtn.GetComponent<Image>());
            _craftLabel = _craftBtn.GetComponentInChildren<Text>();

            _targetBtn = UiBuild.Btn(_root, "HedefliUret", string.Empty,
                                     actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                     new Color(0.20f, 0.50f, 0.82f, 1f), 24, OnTargetedCraft);
            UiBuild.Anchor((RectTransform)_targetBtn.transform, new Vector2(0.53f, 0.215f), new Vector2(0.90f, 0.300f));
            PillFit.Wrap(_targetBtn.GetComponent<Image>());
            _targetLabel = _targetBtn.GetComponentInChildren<Text>();

            _targetCostLabel = UiBuild.Label(Zone(_root, "HedefMaliyet", new Vector2(0.10f, 0.165f), new Vector2(0.90f, 0.210f)),
                                             "Text", string.Empty, 18, TextAnchor.MiddleCenter);
            _targetCostLabel.color = InkSoft;
            Fit(_targetCostLabel, 11, 18);

            _nextPointLabel = UiBuild.Label(Zone(_root, "SonrakiPuan", new Vector2(0.10f, 0.120f), new Vector2(0.90f, 0.158f)),
                                            "Text", string.Empty, 20, TextAnchor.MiddleCenter);
            _nextPointLabel.color = InkFaint;
            Fit(_nextPointLabel, 12, 20);

            _resultLabel = UiBuild.Label(Zone(_root, "Sonuc", new Vector2(0.10f, 0.045f), new Vector2(0.90f, 0.112f)),
                                         "Text", string.Empty, 22, TextAnchor.UpperCenter);
            _resultLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            Fit(_resultLabel, 12, 22);
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

            string slotName = Loc.T("madenci.yuva." + result.Slot);
            string gradeName = Loc.T("kaptan.derece." + result.Grade);
            if (result.Equipped)
            {
                _resultLabel.text = string.Format(Loc.T("madenci.kusanildi"), gradeName, slotName);
                _resultLabel.color = GradeTint[Mathf.Clamp(result.Grade, 0, GradeTint.Length - 1)];
            }
            else
            {
                _resultLabel.text = string.Format(Loc.T("madenci.hurda"), result.ScrapEarned);
                _resultLabel.color = InkSoft;
            }
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

            string slotName = Loc.T("madenci.yuva." + result.Slot);
            string gradeName = Loc.T("kaptan.derece." + result.Grade);
            _resultLabel.text = result.Equipped
                ? string.Format(Loc.T("madenci.hedef.kusanildi"), gradeName, slotName, result.ScrapSpent)
                : string.Format(Loc.T("madenci.hedef.hurda"), gradeName, result.ScrapSpent, result.ScrapEarned);
            _resultLabel.color = result.Equipped
                ? GradeTint[Mathf.Clamp(result.Grade, 0, GradeTint.Length - 1)]
                : InkSoft;
        }

        // --------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_mining == null) return;

            _pointsLabel.text = string.Format(Loc.T("madenci.puan"), _mining.Points);
            _bonusLabel.text = string.Format(Loc.T("madenci.bonus"), Pct(_mining.IncomeMultiplier - 1d));

            for (int i = 0; i < MiningGear.SlotCount; i++)
            {
                int grade = _mining.WornGrade(i);
                Image cardImage = _slotCard[i].GetComponent<Image>();
                if (cardImage != null) cardImage.color = i == _selectedSlot
                    ? new Color(0.84f, 0.92f, 1f, 1f)
                    : Color.white;
                if (grade >= 0)
                {
                    Color tint = GradeTint[Mathf.Clamp(grade, 0, GradeTint.Length - 1)];
                    _slotStripe[i].color = tint;
                    _slotGrade[i].text = Loc.T("kaptan.derece." + grade);
                    _slotGrade[i].color = tint;
                    _slotIcon[i].color = Color.white;
                }
                else
                {
                    _slotStripe[i].color = new Color(GradeTint[0].r, GradeTint[0].g, GradeTint[0].b, 0.35f);
                    _slotGrade[i].text = Loc.T("deniz.bos");
                    _slotGrade[i].color = InkFaint;
                    _slotIcon[i].color = new Color(1f, 1f, 1f, 0.45f);
                }
            }

            _craftLabel.text = Loc.T("madenci.uret") + "  ·  " + string.Format(Loc.T("madenci.puan"), _mining.CraftCost);
            _craftBtn.interactable = _mining.CanCraft;

            long scrapCost = _mining.TargetedScrapCost(_selectedSlot);
            _targetLabel.text = string.Format(Loc.T("madenci.hedef.uret"), Loc.T("madenci.yuva." + _selectedSlot));
            _targetCostLabel.text = string.Format(Loc.T("madenci.hedef.maliyet"),
                                                   _mining.CraftCost, scrapCost, _mining.Points, _mining.Scrap);
            _targetBtn.interactable = _mining.CanTargetedCraft(_selectedSlot);

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
