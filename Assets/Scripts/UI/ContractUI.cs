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
                 "başlığıyla okunur — üç kart da aynı lacivert kart sanatını kullanıyor, o yüzden bu " +
                 "renkler lacivert üstünde okunacak kadar açık.")]
        [SerializeField] private Color easyTint = new Color(0.42f, 0.90f, 0.52f);
        [SerializeField] private Color normalTint = new Color(1.00f, 0.79f, 0.27f);
        [SerializeField] private Color hardTint = new Color(1.00f, 0.47f, 0.42f);

        [Tooltip("Sayaç akarken ekranın yenilenme aralığı (saniye).")]
        [SerializeField] private float refreshInterval = 0.1f;

        [Header("Reklamla iki katı")]
        [Tooltip("Kontrat ödülünü ikiye katlamak için günde kaç kez reklam izlenebilir. Mağazadan " +
                 "alınan ek hak bu yuvaya işlemez — bkz. AdSlotId.")]
        [SerializeField] private int adChargesPerDay = 2;

        /// <summary>
        /// This screen's own rewarded-ad slot. Separate from every other slot on purpose: the daily
        /// allowance here is the contract loop's, and sharing an id with the offline bonus or the free
        /// cash button would let one screen eat the other's charges.
        ///
        /// The store's freeRewardBonusCharges perk is deliberately NOT added to this slot's allowance.
        /// Contract cash scales with the empire rather than being a flat handout, so a bought charge
        /// here would be worth more the further in the player is — an unbounded purchase, which is not
        /// what that perk was priced as. AdRewardUI is the only place it applies.
        /// </summary>
        private const string AdSlotId = "kontrat";

        private ContractService _contract;
        private CoalOperation _op;
        private IAdService _ad;
        private FreeRewardService _free;
        private BoostService _boost;
        private AdRewardUI _adScreen;
        private GameObject _pacePrompt;
        private GameObject _adPill;
        private TMP_Text _adPillLabel;
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
        private readonly TMP_Text[] _offerCards = new TMP_Text[ContractService.TierCount];
        private readonly GameObject[] _offerSwap = new GameObject[ContractService.TierCount];

        // What the three cards are currently showing. Refresh runs ten times a second for the clock on
        // the RUNNING job, but an offer does not change while it sits on the table — rebuilding its four
        // strings anyway allocated a dozen strings per tick, all of them identical to the last dozen,
        // for as long as the player left the screen open. The ids say when there is really new text.
        private readonly int[] _shownOfferId = new int[ContractService.TierCount];
        private string _shownUnit;

        private void Awake()
        {
            ApplyLandscapeLayout();
        }

        private void Start()
        {
            _contract = ServiceLocator.Get<ContractService>();
            _ad = ServiceLocator.Get<IAdService>();
            _free = ServiceLocator.Get<FreeRewardService>();
            _boost = ServiceLocator.Get<BoostService>();
            // The ad screen is a scene object rather than a service, so it is found once here and kept.
            // HudUI has it as an Inspector field; this screen does not need one wired to ask it for the
            // same boost the HUD's own button asks for.
            _adScreen = FindAnyObjectByType<AdRewardUI>(FindObjectsInactive.Include);
            if (barFillArea != null) _barFullWidth = barFillArea.rect.width;
            if (cardRoot == null && cardImage != null) cardRoot = cardImage.gameObject;
            _slotRoot = SiblingOfCard(streakText);

            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (claimButton != null) claimButton.onClick.AddListener(OnClaim);

            BuildOffers();
            BuildStatus();
            BuildAdPill();
            BuildPacePrompt();

            if (panelRoot != null) panelRoot.SetActive(false);
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        private void ApplyLandscapeLayout()
        {
            if (Screen.width <= Screen.height || layoutPanel == null) return;

            SetRect(layoutPanel, Vector2.zero, new Vector2(1900f, 900f));
            SetRect(pageBackground, Vector2.zero, new Vector2(1900f, 900f));
            // Panelin kendi mavi başlık şeridi üstteki 100 birimi kaplıyor; başlık ve kapat düğmesi
            // onun dikey ortasına (y = 450 - 50) oturuyor.
            SetRect(titleRibbon, new Vector2(0f, 400f), new Vector2(900f, 210f));
            SetRect(closeButton != null ? closeButton.transform as RectTransform : null,
                    new Vector2(858f, 400f), new Vector2(84f, 84f));
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
            if (_contract == null) _contract = ServiceLocator.Get<ContractService>();
            if (_contract == null || panelRoot == null) return;
            // Nothing here watches for a language change, so the cached offer text is thrown away every
            // time the screen opens rather than being trusted across a trip to the settings menu.
            _shownUnit = null;
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

            RefreshPacePrompt(state);

            if (card && streakText != null)
                streakText.text = _contract.Streak > 0
                    ? string.Format(Loc.T("kontrat.seri"), _contract.Streak)
                    : Loc.T("kontrat.ilk");
        }

        // ---------------- the running job ----------------

        private void RefreshCard(bool done)
        {
            if (doneBadge != null) doneBadge.SetActive(done);
            if (timerChip != null) timerChip.SetActive(!done);
            if (rewardRow != null) rewardRow.SetActive(!done);
            if (claimButton != null) claimButton.gameObject.SetActive(done);
            // Out of charges hides the pill rather than dimming it, the way the offline bonus does: a
            // dead control on a reward screen reads as the game being broken, not as a limit.
            if (_adPill != null)
            {
                bool offer = done && AdReady && CanDouble;
                if (_adPill.activeSelf != offer) _adPill.SetActive(offer);
            }

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

        /// <summary>
        /// The "you are going to miss this" strip, shown under the running card only while the job is
        /// actually heading for a miss. It offers the one thing that changes the outcome: a boost speeds
        /// the furnace up rather than only the money, so the ×2 really does close the gap — see
        /// <see cref="ContractService.Pace"/>.
        ///
        /// Under the card rather than on it. The card is full — badge, target, clock, bar, reward row —
        /// and every attempt to fit another control onto it lands on top of something. The panel below it
        /// is empty, the strip only exists while it is needed, and it is anchored off the card's own rect
        /// so the two stay together in whichever layout is running.
        /// </summary>
        private void BuildPacePrompt()
        {
            if (runningCard == null || runningCard.parent == null) return;

            // The orange action capsule, capped near its own proportion — the strip used to be 8:1, which
            // the caps survive but the cream inlay does not: stretched that far it reads as a text field.
            // 96 tall is the gap the prefab leaves between the running card and the next slot, less a
            // 22-unit clearance either side.
            const float height = 96f;
            Button btn = EkranKit.Capsule(runningCard.parent, "HizUyarisi", EkranKit.Get("btn_turuncu"),
                                          Vector2.zero, Vector2.zero, OnPacePrompt);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = runningCard.anchorMin;
            rt.anchorMax = runningCard.anchorMax;
            rt.pivot = runningCard.pivot;
            rt.sizeDelta = new Vector2(Mathf.Min(runningCard.sizeDelta.x * 0.86f, height * 5.2f), height);
            rt.anchoredPosition = runningCard.anchoredPosition
                                - new Vector2(0f, runningCard.rect.height * 0.5f + height * 0.5f + 22f);

            TMP_Text label = Text(rt, "Yazi", 24, TextAlignmentOptions.Center, EkranKit.InlayMin, EkranKit.InlayMax);
            label.text = Loc.T("kontrat.geride");

            _pacePrompt = btn.gameObject;
            _pacePrompt.SetActive(false);
        }

        /// <summary>
        /// Hidden unless it can do something. Not behind, no ad to show, or a boost already running —
        /// in all three the strip would be either a lie or a control that refuses, and the running card
        /// is the last place in the game to put one of those.
        /// </summary>
        private void RefreshPacePrompt(ContractService.PortState state)
        {
            if (_pacePrompt == null) return;
            bool warn = state == ContractService.PortState.Active
                     && _contract.BehindPace
                     && AdReady
                     && _adScreen != null
                     && (_boost == null || !_boost.IsActive);
            if (_pacePrompt.activeSelf != warn) _pacePrompt.SetActive(warn);
        }

        private void OnPacePrompt()
        {
            if (_adScreen == null) return;
            _adScreen.WatchBoost();
        }

        /// <summary>Whether an ad can be shown at all — owning remove-ads counts as ready.</summary>
        private bool AdReady => (_free != null && _free.AdsRemoved) || (_ad != null && _ad.Available);

        /// <summary>
        /// No cooldown is passed. A contract is its own spacing: the ship has to sail, come back and be
        /// finished again before this button exists a second time, which is minutes at the very best.
        /// The daily charge count is what caps it.
        /// </summary>
        private bool CanDouble => _free == null || _free.CanWatch(AdSlotId, adChargesPerDay, 0f);

        private void OnDoubleClaim()
        {
            if (_contract == null || !_contract.Claimable || !AdReady || !CanDouble) return;
            if (_free != null && _free.AdsRemoved) { PayDoubled(); return; }
            _ad.ShowRewarded(PayDoubled);
        }

        /// <summary>
        /// Runs when the ad has actually paid out. The charge is spent BEFORE the claim so that the one
        /// save the claim writes carries both — a charge that reached the wallet but not the disk is a
        /// free second ad on the next launch. Re-checking Claimable first is what makes that safe: the
        /// state is Reward, so the claim below cannot fail and cannot leave the charge spent for nothing.
        /// </summary>
        private void PayDoubled()
        {
            if (_contract == null || !_contract.Claimable) return;
            if (_free != null) _free.Consume(AdSlotId);
            if (!_contract.Claim(2d)) return;
            var audio = ServiceLocator.Get<AudioService>();
            if (audio != null) audio.Play(SoundId.Coin);
            ServiceLocator.Get<RatingPromptService>()?.RecordContractSuccess();
            Refresh();
        }

        /// <summary>
        /// The double-it pill, built into the slot the countdown chip occupies while the job is running.
        /// That slot is the one piece of the card that is guaranteed free at exactly the moment this
        /// button exists: <see cref="RefreshCard"/> hides the chip the instant the target is met, and the
        /// rest of the card — the ore badge, the TARGET MET pill, the full bar, the claim button — is
        /// occupied in this state. Borrowing the chip's rect rather than authoring numbers means the two
        /// cannot drift apart if the card is ever relaid out.
        ///
        /// Built in code rather than wired in the Inspector because the authored card has no slot for it,
        /// and the three offer cards already set the precedent for this screen drawing its own controls.
        /// </summary>
        private void BuildAdPill()
        {
            var chipRt = timerChip != null ? timerChip.transform as RectTransform : null;
            if (chipRt == null || chipRt.parent == null) return;

            // The orange action capsule, sized off the chip's WIDTH at the art's own 2.77:1 so the cream
            // inlay the label sits on keeps its shape. Off the height it came out 332 wide in a 280-wide
            // slot and ran over the card's right rim.
            Button btn = EkranKit.Capsule(chipRt.parent, "ReklamIkiKat", EkranKit.Get("btn_turuncu"),
                                          Vector2.zero, Vector2.zero, OnDoubleClaim);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = chipRt.anchorMin;
            rt.anchorMax = chipRt.anchorMax;
            rt.pivot = chipRt.pivot;
            float width = chipRt.sizeDelta.x;
            rt.sizeDelta = new Vector2(width, width / 2.77f);
            rt.anchoredPosition = chipRt.anchoredPosition;

            _adPillLabel = Text(rt, "Yazi", 26, TextAlignmentOptions.Center, EkranKit.InlayMin, EkranKit.InlayMax);
            _adPillLabel.text = Loc.T("kontrat.odul_iki_kat");

            _adPill = btn.gameObject;
            _adPill.SetActive(false);
        }

        private void OnClaim()
        {
            if (_contract == null || !_contract.Claimable) return;
            if (!_contract.Claim()) return;
            var audio = ServiceLocator.Get<AudioService>();
            if (audio != null) audio.Play(SoundId.Coin);
            ServiceLocator.Get<RatingPromptService>()?.RecordContractSuccess();
            Refresh();
        }

        // ---------------- the three offers ----------------

        private void RefreshOffers()
        {
            string unit = OreWord();
            // Consumed first and unconditionally: a board that re-cut itself has to reach the screen
            // even in the vanishingly unlikely case that it came back with the same ids.
            bool changed = _contract.ConsumeBoardRefreshed();
            changed |= !string.Equals(unit, _shownUnit);
            for (int i = 0; i < ContractService.TierCount && !changed; i++)
                changed = _shownOfferId[i] != _contract.GetOffer(i).Id;
            if (!changed) return;

            _shownUnit = unit;
            for (int i = 0; i < ContractService.TierCount; i++)
            {
                ContractService.Offer o = _contract.GetOffer(i);
                _shownOfferId[i] = o.Id;
                if (_offerTask[i] != null)
                    _offerTask[i].text = string.Format(Loc.T("kontrat.isle"), Units(o.Units), unit);
                if (_offerTime[i] != null) _offerTime[i].text = ClockText(o.Seconds);
                if (_offerPay[i] != null) _offerPay[i].text = "$" + NumberFormatter.Format(new BigDouble(o.Cash));
                if (_offerGems[i] != null) _offerGems[i].text = "+" + o.Gems;
                // The easy job pays one, and "+1 cards" is not something a shipped game says. Only
                // the singular needs its own word: the plural row already covers every count above one,
                // and the languages that inflect further than that do not agree on where.
                if (_offerCards[i] != null)
                    _offerCards[i].text = o.Cards > 0
                        ? "+" + o.Cards + " " + Loc.T(o.Cards == 1 ? "ustabasi.kart_tekil"
                                                                  : "ustabasi.kart")
                        : string.Empty;
                // The budget is per visit, not per card: once it is spent every swap goes, so the
                // screen does not present a control that would only ever refuse.
                if (_offerSwap[i] != null && _offerSwap[i].activeSelf != _contract.CanSwap)
                    _offerSwap[i].SetActive(_contract.CanSwap);
            }
        }

        private void OnSwap(int tier)
        {
            if (_contract == null || !_contract.HasOffers) return;
            if (!_contract.Swap(tier, _shownOfferId[tier]))
            {
                var denied = ServiceLocator.Get<AudioService>();
                if (denied != null) denied.Play(SoundId.Denied);
                return;
            }
            var audio = ServiceLocator.Get<AudioService>();
            if (audio != null) audio.Play(SoundId.Tap);
            Refresh();
        }

        private void OnAccept(int tier)
        {
            if (_contract == null || !_contract.HasOffers) return;
            // The id the card was drawn with, so a board that changed under the finger refuses the tap
            // instead of signing whatever moved into the slot. The next refresh draws the new one.
            if (!_contract.Accept(tier, _shownOfferId[tier], OreWord())) return;
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
            // Loc, not OreName: OreName is the island key upper-cased, so a Turkish player was reading
            // "240 COAL İŞLE" on the only line of this card that names what the job is.
            return _op != null ? Loc.Id("cevher", _op.IslandKey) : _contract.UnitWord;
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

            // Fractions of the 976 x 1575 sheet in UI_Kontrat. Its top 287 units are the crest and the
            // league ribbon laid across it (centred 242 down, as LadderUI lays the same pair), so the
            // subtitle starts 350 down and the rows below it; the bottom 103 are the frame.
            TMP_Text title = Text(root, "Baslik", 40, TextAlignmentOptions.Center,
                                  new Vector2(0.08f, 0.733f), new Vector2(0.92f, 0.778f));
            title.text = Loc.T("kontrat.teklifler");

            // Three rows of the navy card, top down — in landscape too. Each row is 868 x 300 units,
            // the card's own 2.9:1, with a 24-unit gap; the three portrait columns landscape used to get
            // would have stood the art on end. What landscape changes is how far in the rows start.
            bool landscape = Screen.width > Screen.height;
            float left = landscape ? 0.20f : 0.055f;
            Color[] tints = { easyTint, normalTint, hardTint };
            string[] keys = { "kontrat.kolay", "kontrat.normal", "kontrat.zor" };
            for (int i = 0; i < ContractService.TierCount; i++)
            {
                float top = 0.7206f - i * 0.2057f;
                BuildOfferCard(root, i, keys[i], tints[i], left, 1f - left, top - 0.1905f, top);
            }

            _offersRoot.SetActive(false);
        }

        /// <summary>
        /// One offer: the navy card, a column of what the job is and pays on the left, and its two
        /// controls stacked on the right — the pale swap capsule over the orange accept capsule.
        ///
        /// EVERY BAND IS TALLER THAN ITS TYPE. The white card this replaced gave the difficulty and the
        /// pay bands about 24 units each for 30- and 40-point type; auto-size cannot go below its
        /// floor, and a line that still does not fit under Ellipsis is dropped whole — which is why
        /// EASY / NORMAL / HARD and the cash never showed. Here each band clears a line of its type.
        ///
        /// EVERYTHING STAYS IN THE NAVY WELL, which is 0.19–0.81 of the card's height; outside it is the
        /// bright rim, where the first build put the difficulty and the meta row.
        ///
        /// The capsules are placed at their art's own proportion (btn_bos 4.4:1, btn_turuncu 2.77:1) on
        /// a card 868 by 300 units; both come out 0.335 of the card wide, so they share a column.
        /// </summary>
        private void BuildOfferCard(RectTransform parent, int tier, string tierKey, Color tint,
                                    float xMin, float xMax, float yMin, float yMax)
        {
            Image img = EkranKit.Sliced(parent, "Teklif" + tier, EkranKit.Get("kart_lacivert"),
                                        new Vector2(xMin, yMin), new Vector2(xMax, yMax), false);
            img.raycastTarget = true;
            var rt = img.rectTransform;

            // The whole card still signs, as before; the accept capsule says where to press.
            int captured = tier;
            var btn = img.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnAccept(captured));

            _offerTier[tier] = Text(rt, "Zorluk", 28, TextAlignmentOptions.MidlineLeft,
                                    new Vector2(0.085f, 0.655f), new Vector2(0.58f, 0.80f));
            _offerTier[tier].text = Loc.T(tierKey);
            _offerTier[tier].color = tint;

            _offerPay[tier] = Text(rt, "Odul", 36, TextAlignmentOptions.MidlineLeft,
                                   new Vector2(0.085f, 0.47f), new Vector2(0.58f, 0.655f));
            _offerPay[tier].color = EkranKit.Paper;

            _offerTask[tier] = Text(rt, "Is", 24, TextAlignmentOptions.MidlineLeft,
                                    new Vector2(0.085f, 0.335f), new Vector2(0.58f, 0.47f));
            _offerTask[tier].color = EkranKit.Paper;

            // Clock, gems and foreman cards share the bottom band. The cards were the whole reason a
            // contract is worth running and the card never said so — a player comparing three jobs
            // could only see the cash.
            _offerTime[tier] = Text(rt, "Sure", 22, TextAlignmentOptions.MidlineLeft,
                                    new Vector2(0.085f, 0.195f), new Vector2(0.24f, 0.335f));
            _offerTime[tier].color = EkranKit.PaperSoft;

            _offerGems[tier] = Text(rt, "Elmas", 22, TextAlignmentOptions.MidlineLeft,
                                    new Vector2(0.24f, 0.195f), new Vector2(0.36f, 0.335f));
            _offerGems[tier].color = new Color(0.45f, 0.82f, 1f);

            _offerCards[tier] = Text(rt, "Kart", 22, TextAlignmentOptions.MidlineLeft,
                                     new Vector2(0.36f, 0.195f), new Vector2(0.58f, 0.335f));
            _offerCards[tier].color = new Color(0.80f, 0.66f, 1f);

            // The swap is a Button of its own on top of the card's Button: the raycast goes to the
            // topmost graphic, so pressing it never signs.
            Button swap = EkranKit.Capsule(rt, "Degistir", EkranKit.Get("btn_bos"),
                                           new Vector2(0.60f, 0.57f), new Vector2(0.934f, 0.79f),
                                           () => OnSwap(captured));
            TMP_Text swapLabel = Text((RectTransform)swap.transform, "Yazi", 24, TextAlignmentOptions.Center,
                                      EkranKit.CapsMin, EkranKit.CapsMax);
            swapLabel.text = Loc.T("kontrat.degistir");
            _offerSwap[tier] = swap.gameObject;

            // Not a Button: a second press target on the card would only duplicate the card's own.
            Image accept = EkranKit.Sliced(rt, "Kabul", EkranKit.Get("btn_turuncu"),
                                           new Vector2(0.60f, 0.20f), new Vector2(0.935f, 0.55f), true);
            TMP_Text take = Text(accept.rectTransform, "Yazi", 26, TextAlignmentOptions.Center,
                                 EkranKit.InlayMin, EkranKit.InlayMax);
            take.text = Loc.T("kontrat.kabul");
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

            // On the navy card, at the offer rows' own 868 x 300 and centred in the space under the
            // ribbon, so the empty pier reads as a state of the same screen rather than one line of type
            // floating on a bare sheet.
            Image card = EkranKit.Sliced(root, "Kart", EkranKit.Get("kart_lacivert"),
                                         new Vector2(0.055f, 0.330f), new Vector2(0.945f, 0.5205f), false);
            _statusText = Text(card.rectTransform, "Yazi", 46, TextAlignmentOptions.Center,
                               new Vector2(0.08f, 0.21f), new Vector2(0.92f, 0.79f));
            _statusText.color = EkranKit.Paper;
            _statusText.textWrappingMode = TextWrappingModes.Normal;   // two lines: the word, then the clock
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
            // Navy by default: the sheet, the cream inlay and the pale capsule are all near-white. Labels
            // on the navy card set their own paper ink.
            t.color = EkranKit.Ink;
            t.raycastTarget = false;      // the card under it is the tap target
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

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
