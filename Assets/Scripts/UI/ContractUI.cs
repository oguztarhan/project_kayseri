using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The port contract screen (GDD §9, Figma "ekran_kontrat"). It shows whatever the ship at the pier
    /// is doing: a countdown while it is still over the horizon, three jobs to choose from once it moors,
    /// the running job's clock and bar after one is taken, and the claim once the target is met.
    ///
    /// The authored card in UI_Kontrat is the RUNNING job — target line, clock, bar, reward row, claim
    /// button, all wired in the Inspector as before. The three offer cards and the horizon countdown are
    /// built in code inside the same panel: three cards cannot be authored as one prefab slot without
    /// wiring fifteen more references, and <see cref="LanguageMenuUI"/> already sets the precedent for a
    /// screen that draws its own list.
    ///
    /// Nothing on this screen expires. The ship waits on the offers and waits on an unclaimed reward, so
    /// there is no way to lose anything by not looking at it — see <see cref="ContractService"/>.
    /// </summary>
    [DefaultExecutionOrder(-110)]
    public sealed class ContractUI : MonoBehaviour
    {
        [Header("Panel (UI_Kontrat prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;

        [Header("Yatay yerleşim")]
        [SerializeField] private RectTransform layoutPanel;
        [SerializeField] private RectTransform titleRibbon;
        [SerializeField] private RectTransform pageBackground;
        [SerializeField] private RectTransform runningCard;
        [SerializeField] private RectTransform nextSlot;

        [Header("Kontrat kartı (sürmekte olan iş)")]
        [Tooltip("Kartın kökü. Boş bırakılırsa kart görselinin kendi nesnesi kullanılır. Teklifler " +
                 "gösterilirken ve gemi yokken bu kapanır.")]
        [SerializeField] private GameObject cardRoot;
        [SerializeField] private Image cardImage;
        [Tooltip("Kontrat sürerken kullanılan kart görseli.")]
        [SerializeField] private Sprite cardRunning;
        [Tooltip("Hedef tutunca kullanılan yeşil kart görseli.")]
        [SerializeField] private Sprite cardDone;
        [SerializeField] private GameObject doneBadge;
        [Tooltip("İşlenen / hedef.")]
        [SerializeField] private TMP_Text targetText;
        [SerializeField] private GameObject timerChip;
        [SerializeField] private TMP_Text timerText;

        [Header("İlerleme çubuğu")]
        [Tooltip("Dolgu alanı — genişliği ilerlemeye göre değişir.")]
        [SerializeField] private RectTransform barFillArea;

        [Header("Ödül satırı (kontrat sürerken)")]
        [SerializeField] private GameObject rewardRow;
        [SerializeField] private TMP_Text rewardCashText;
        [SerializeField] private TMP_Text rewardGemsText;

        [Header("Topla (kontrat bitince)")]
        [SerializeField] private Button claimButton;
        [SerializeField] private TMP_Text claimLabel;

        [Header("Sıradaki yuva")]
        [SerializeField] private TMP_Text streakText;

        [Header("Teklif kartları (kod ile kurulur)")]
        [Tooltip("KOLAY / NORMAL / ZOR başlıklarının rengi. Zorluk kartın kendi rengiyle değil " +
                 "başlığıyla okunur — üç kart da aynı beyaz kart sanatını kullanıyor, o yüzden bu " +
                 "renkler beyaz üstünde okunacak kadar koyu.")]
        [SerializeField] private Color easyTint = new Color(0.13f, 0.60f, 0.29f);
        [SerializeField] private Color normalTint = new Color(0.83f, 0.52f, 0.05f);
        [SerializeField] private Color hardTint = new Color(0.78f, 0.20f, 0.16f);

        [Tooltip("Sayaç akarken ekranın yenilenme aralığı (saniye).")]
        [SerializeField] private float refreshInterval = 0.1f;

        private ContractService _contract;
        private CoalOperation _op;
        private GameObject _slotRoot;   // the authored "SIRADAKİ KONTRAT" slot — part of the running view
        private float _barFullWidth;
        private float _timer;

        // Code-built pieces: the three offer cards, and the line that shows while there is no ship.
        private GameObject _offersRoot;
        private GameObject _statusRoot;
        private TMP_Text _statusText;
        private readonly TMP_Text[] _offerTier = new TMP_Text[ContractService.TierCount];
        private readonly TMP_Text[] _offerTask = new TMP_Text[ContractService.TierCount];
        private readonly TMP_Text[] _offerTime = new TMP_Text[ContractService.TierCount];
        private readonly TMP_Text[] _offerPay = new TMP_Text[ContractService.TierCount];
        private readonly TMP_Text[] _offerGems = new TMP_Text[ContractService.TierCount];

        private void Awake()
        {
            ApplyLandscapeLayout();
        }

        private void Start()
        {
            _contract = ServiceLocator.Get<ContractService>();
            if (barFillArea != null) _barFullWidth = barFillArea.rect.width;
            if (cardRoot == null && cardImage != null) cardRoot = cardImage.gameObject;
            _slotRoot = SiblingOfCard(streakText);

            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (claimButton != null) claimButton.onClick.AddListener(OnClaim);

            BuildOffers();
            BuildStatus();

            if (panelRoot != null) panelRoot.SetActive(false);
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        private void ApplyLandscapeLayout()
        {
            if (Screen.width <= Screen.height || layoutPanel == null) return;

            SetRect(layoutPanel, Vector2.zero, new Vector2(1900f, 900f));
            SetRect(pageBackground, Vector2.zero, new Vector2(1900f, 900f));
            SetRect(titleRibbon, new Vector2(0f, 350f), new Vector2(900f, 210f));
            SetRect(closeButton != null ? closeButton.transform as RectTransform : null,
                    new Vector2(900f, 350f), new Vector2(120f, 120f));
            SetRect(runningCard, new Vector2(-465f, -35f), new Vector2(900f, 468f));
            SetRect(nextSlot, new Vector2(465f, -35f), new Vector2(908f, 484f));
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void Update()
        {
            if (panelRoot == null || !panelRoot.activeSelf) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        /// <summary>Whether the screen is up — the port badge stands down while it is.</summary>
        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        public void Toggle()
        {
            if (panelRoot == null) return;
            if (panelRoot.activeSelf) { Hide(); return; }
            Open();
        }

        public void Open()
        {
            if (_contract == null || panelRoot == null) return;
            Refresh();
            panelRoot.SetActive(true);
        }

        public void Hide()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Refresh()
        {
            if (_contract == null) return;

            ContractService.PortState state = _contract.State;
            bool offering = state == ContractService.PortState.Offering;
            bool reward = state == ContractService.PortState.Reward;
            bool card = reward || state == ContractService.PortState.Active;

            if (cardRoot != null && cardRoot.activeSelf != card) cardRoot.SetActive(card);
            // The slot's own header reads "NEXT CONTRACT", which is the running view's language — over
            // three offers it would be describing something else entirely.
            if (_slotRoot != null && _slotRoot.activeSelf != card) _slotRoot.SetActive(card);
            if (_offersRoot != null && _offersRoot.activeSelf != offering) _offersRoot.SetActive(offering);
            bool idle = !card && !offering;
            if (_statusRoot != null && _statusRoot.activeSelf != idle) _statusRoot.SetActive(idle);

            if (offering) RefreshOffers();
            else if (card) RefreshCard(reward);
            else RefreshStatus(state);

            if (card && streakText != null)
                streakText.text = _contract.Streak > 0
                    ? string.Format(Loc.T("kontrat.seri"), _contract.Streak)
                    : Loc.T("kontrat.ilk");
        }

        // ---------------- the running job ----------------

        private void RefreshCard(bool done)
        {
            if (cardImage != null) cardImage.sprite = done ? cardDone : cardRunning;
            if (doneBadge != null) doneBadge.SetActive(done);
            if (timerChip != null) timerChip.SetActive(!done);
            if (rewardRow != null) rewardRow.SetActive(!done);
            if (claimButton != null) claimButton.gameObject.SetActive(done);

            if (targetText != null)
                targetText.text = done
                    ? Loc.T("kontrat.hedef_tuttu")
                    : Units(_contract.DoneUnits) + " / " + Units(_contract.TargetUnits) + " " + _contract.UnitWord;
            if (timerText != null) timerText.text = ClockText(_contract.SecondsLeft);

            if (barFillArea != null)
                barFillArea.sizeDelta = new Vector2(_barFullWidth * (float)_contract.Progress01, barFillArea.sizeDelta.y);

            if (rewardCashText != null) rewardCashText.text = "$" + NumberFormatter.Format(_contract.Reward);
            if (rewardGemsText != null) rewardGemsText.text = "+" + _contract.RewardGems;
            if (claimLabel != null) claimLabel.text = Loc.T("ortak.odulu_al");
        }

        private void OnClaim()
        {
            if (_contract == null || !_contract.Claimable) return;
            _contract.Claim();
            var audio = ServiceLocator.Get<AudioService>();
            if (audio != null) audio.Play(SoundId.Coin);
            Refresh();
        }

        // ---------------- the three offers ----------------

        private void RefreshOffers()
        {
            string unit = OreWord();
            for (int i = 0; i < ContractService.TierCount; i++)
            {
                ContractService.Offer o = _contract.GetOffer(i);
                if (_offerTask[i] != null)
                    _offerTask[i].text = string.Format(Loc.T("kontrat.isle"), Units(o.Units), unit);
                if (_offerTime[i] != null) _offerTime[i].text = ClockText(o.Seconds);
                if (_offerPay[i] != null) _offerPay[i].text = "$" + NumberFormatter.Format(new BigDouble(o.Cash));
                if (_offerGems[i] != null) _offerGems[i].text = "+" + o.Gems;
            }
        }

        private void OnAccept(int tier)
        {
            if (_contract == null || !_contract.HasOffers) return;
            if (!_contract.Accept(tier, OreWord())) return;
            var audio = ServiceLocator.Get<AudioService>();
            if (audio != null) audio.Play(SoundId.Upgrade);
            Refresh();
        }

        /// <summary>
        /// What the island under the player calls its ore. Re-resolved on use rather than cached: only
        /// one <see cref="CoalOperation"/> is ever enabled and travelling swaps which one it is.
        /// </summary>
        private string OreWord()
        {
            if (_op == null || !_op.enabled)
            {
                _op = null;
                var all = FindObjectsByType<CoalOperation>(FindObjectsInactive.Exclude);
                for (int i = 0; i < all.Length; i++)
                    if (all[i].enabled) { _op = all[i]; break; }
            }
            return _op != null ? _op.OreName : _contract.UnitWord;
        }

        // ---------------- no ship at the pier ----------------

        private void RefreshStatus(ContractService.PortState state)
        {
            if (_statusText == null) return;
            switch (state)
            {
                case ContractService.PortState.Arriving:
                    _statusText.text = Loc.T("kontrat.gemi_geliyor");
                    break;
                case ContractService.PortState.Departing:
                    _statusText.text = _contract.LastResult == ContractService.Result.Failed
                        ? Loc.T("kontrat.kacirildi")
                        : Loc.T("kontrat.teslim_edildi");
                    break;
                default:
                    _statusText.text = Loc.T("kontrat.gemi_yolda") + "\n" + ClockText(_contract.SecondsToShip);
                    break;
            }
        }

        // ---------------- code-built layout ----------------

        private void BuildOffers()
        {
            RectTransform body = Body();
            if (body == null) return;

            _offersRoot = new GameObject("Teklifler", typeof(RectTransform));
            var root = (RectTransform)_offersRoot.transform;
            root.SetParent(body, false);
            Stretch(root, Vector2.zero, Vector2.one);
            root.SetAsLastSibling();

            bool landscape = Screen.width > Screen.height;
            TMP_Text title = Text(root, "Baslik", 38, TextAlignmentOptions.Center,
                                  landscape ? new Vector2(0.30f, 0.79f) : new Vector2(0.06f, 0.82f),
                                  landscape ? new Vector2(0.70f, 0.88f) : new Vector2(0.94f, 0.89f));
            title.text = Loc.T("kontrat.teklifler");

            Color[] tints = { easyTint, normalTint, hardTint };
            string[] keys = { "kontrat.kolay", "kontrat.normal", "kontrat.zor" };
            for (int i = 0; i < ContractService.TierCount; i++)
            {
                if (landscape)
                {
                    float left = 0.035f + i * 0.3225f;
                    BuildOfferCard(root, i, keys[i], tints[i], left, left + 0.2875f, 0.16f, 0.73f);
                }
                else
                {
                    float top = 0.78f - i * 0.235f;
                    BuildOfferCard(root, i, keys[i], tints[i], top - 0.205f, top);
                }
            }

            _offersRoot.SetActive(false);
        }

        private void BuildOfferCard(RectTransform parent, int tier, string tierKey, Color tint,
                                    float yMin, float yMax)
            => BuildOfferCard(parent, tier, tierKey, tint, 0.06f, 0.94f, yMin, yMax);

        private void BuildOfferCard(RectTransform parent, int tier, string tierKey, Color tint,
                                    float xMin, float xMax, float yMin, float yMax)
        {
            var go = new GameObject("Teklif" + tier, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt, new Vector2(xMin, yMin), new Vector2(xMax, yMax));

            var img = go.GetComponent<Image>();
            img.sprite = cardRunning != null ? cardRunning : UiSkin.Panel;
            img.type = Image.Type.Sliced;
            img.color = cardRunning != null ? Color.white : new Color(0.15f, 0.19f, 0.27f, 0.95f);

            int captured = tier;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnAccept(captured));

            // A compact card: header/pay, job, meta row, then one clear action strip. Keeping each item
            // in its own band prevents the sparse corner layout and long localisation collisions.
            Image header = Plate(rt, "BaslikSeridi", new Vector2(0.035f, 0.75f),
                                 new Vector2(0.965f, 0.955f), new Color(tint.r, tint.g, tint.b, 0.10f));
            header.raycastTarget = false;

            _offerTier[tier] = Text(rt, "Zorluk", 31, TextAlignmentOptions.MidlineLeft,
                                    new Vector2(0.07f, 0.77f), new Vector2(0.53f, 0.94f));
            _offerTier[tier].text = Loc.T(tierKey);
            _offerTier[tier].color = tint;

            _offerPay[tier] = Text(rt, "Odul", 33, TextAlignmentOptions.MidlineRight,
                                   new Vector2(0.50f, 0.77f), new Vector2(0.93f, 0.94f));

            _offerTask[tier] = Text(rt, "Is", 35, TextAlignmentOptions.MidlineLeft,
                                    new Vector2(0.07f, 0.47f), new Vector2(0.93f, 0.72f));

            _offerTime[tier] = Text(rt, "Sure", 28, TextAlignmentOptions.MidlineLeft,
                                    new Vector2(0.07f, 0.28f), new Vector2(0.46f, 0.45f));
            _offerTime[tier].color = Dim(_offerTime[tier].color, 0.6f);

            _offerGems[tier] = Text(rt, "Elmas", 28, TextAlignmentOptions.MidlineRight,
                                    new Vector2(0.52f, 0.28f), new Vector2(0.93f, 0.45f));
            _offerGems[tier].color = new Color(0.16f, 0.45f, 0.78f);

            Image action = Plate(rt, "KabulSeridi", new Vector2(0.07f, 0.065f),
                                 new Vector2(0.93f, 0.235f), new Color(tint.r, tint.g, tint.b, 0.15f));
            action.raycastTarget = false;
            TMP_Text take = Text(rt, "Kabul", 27, TextAlignmentOptions.Center,
                                 new Vector2(0.10f, 0.075f), new Vector2(0.90f, 0.225f));
            take.text = Loc.T("kontrat.kabul");
            take.color = tint;
        }

        private static Image Plate(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt, aMin, aMax);
            var image = go.GetComponent<Image>();
            image.sprite = UiSkin.Flat;
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        private void BuildStatus()
        {
            RectTransform body = Body();
            if (body == null) return;

            _statusRoot = new GameObject("GemiDurumu", typeof(RectTransform));
            var root = (RectTransform)_statusRoot.transform;
            root.SetParent(body, false);
            Stretch(root, Vector2.zero, Vector2.one);
            root.SetAsLastSibling();

            _statusText = Text(root, "Yazi", 46, TextAlignmentOptions.Center,
                               new Vector2(0.08f, 0.40f), new Vector2(0.92f, 0.62f));
            _statusRoot.SetActive(false);
        }

        /// <summary>
        /// The authored window the card sits in, which is where everything built here belongs too —
        /// parented to the panel root instead, the offers laid themselves out against the raw screen and
        /// escaped the letterbox that holds the rest of the screen at the size it was drawn.
        /// </summary>
        private RectTransform Body()
        {
            if (cardRoot != null && cardRoot.transform.parent is RectTransform card) return card;
            return panelRoot != null ? panelRoot.transform as RectTransform : null;
        }

        /// <summary>
        /// Walks up from an authored element to the child of the card's own parent that contains it —
        /// how the "next contract" slot is found without asking for a fifteenth Inspector reference.
        /// </summary>
        private GameObject SiblingOfCard(Component child)
        {
            if (child == null || cardRoot == null) return null;
            Transform stop = cardRoot.transform.parent;
            Transform t = child.transform;
            while (t != null && t.parent != stop) t = t.parent;
            return t != null ? t.gameObject : null;
        }

        /// <summary>
        /// A label matching the authored card's font, so the code-built half of this screen does not read
        /// as a different game from the half that came out of Figma.
        /// </summary>
        private TMP_Text Text(RectTransform parent, string name, float size, TextAlignmentOptions align,
                              Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt, aMin, aMax);

            var t = go.AddComponent<TextMeshProUGUI>();
            if (targetText != null && targetText.font != null) t.font = targetText.font;
            t.fontSize = size;
            t.enableAutoSizing = true;
            t.fontSizeMin = Mathf.Max(18f, size * 0.68f);
            t.fontSizeMax = size;
            t.alignment = align;
            // The card art is near-white, so the ink is the authored card's own dark navy rather than the
            // white every other floating label in this game uses. Taken off the card instead of hardcoded,
            // so re-skinning the screen does not leave this half of it behind.
            t.color = targetText != null ? targetText.color : new Color32(30, 43, 71, 255);
            t.raycastTarget = false;      // the card under it is the tap target
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private static Color Dim(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

        private static void Stretch(RectTransform rt, Vector2 aMin, Vector2 aMax)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static string Units(double v) => NumberFormatter.Format(new BigDouble(v), 1);

        /// <summary>"1:05" / "0:47" — seconds always two digits so the clock does not jitter in width.</summary>
        public static string ClockText(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.CeilToInt(seconds);
            int m = total / 60;
            int s = total - m * 60;
            return m + ":" + (s < 10 ? "0" + s : s.ToString());
        }
    }
}
