using Game.Core;
using Game.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Systems
{
    /// <summary>
    /// Entry point on a persistent object in the Bootstrap scene. Registers all services (facade-first:
    /// dev stubs now, real SDKs at ship time), applies quality settings, builds the economy, loads
    /// the save, grants offline earnings, then loads Main. Drives the GameClock each frame (GDD §14.5).
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float ticksPerSecond = 8f;
        [SerializeField] private EconomyConfig economyConfig;
        [SerializeField] private OfflineConfig offlineConfig;
        [Tooltip("Bakım/yıpranma ayarları. Boş bırakılırsa varsayılan değerlerle ÇALIŞIR " +
                 "(8 saat tolerans, 72 saatte tabana iner) — kapatmak için bir config asset'i " +
                 "bağlayıp Enabled'ı kapat.")]
        [SerializeField] private MaintenanceConfig maintenanceConfig;
        [SerializeField] private ForemanConfig foremanConfig;
        [SerializeField] private VoyageConfig voyageConfig;
        [Tooltip("Bölüm eşikleri ve ödülleri. Boş bırakılırsa varsayılanlarla ÇALIŞIR — " +
                 "asset, sayıları yeniden derlemeden ayarlamak için, özelliği açmak için değil.")]
        [SerializeField] private ChapterConfig chapterConfig;
        [Tooltip("Kaptan kadrosu ve sandık oranları. Boş bırakılırsa varsayılanlarla ÇALIŞIR.")]
        [SerializeField] private CaptainConfig captainConfig;
        [Tooltip("Deniz çatışması ayarları. Boş bırakılırsa varsayılanlarla ÇALIŞIR.")]
        [SerializeField] private SeaCombatConfig seaCombatConfig;
        [Tooltip("Atölye (zanaat) ayarları. Boş bırakılırsa varsayılanlarla ÇALIŞIR.")]
        [SerializeField] private CraftingConfig craftingConfig;
        [SerializeField] private LiveEventConfig liveEventConfig;
        [Tooltip("Döküm Şenliği'nin görev ve sandık tablosu. Boş bırakılırsa varsayılanlarla ÇALIŞIR.")]
        [SerializeField] private FoundryFestivalConfig foundryFestivalConfig;
        [SerializeField] private ContractConfig contractConfig;
        [SerializeField] private QualityConfig qualityConfig;
        [SerializeField] private AudioConfig audioConfig;
        [SerializeField] private JuiceConfig juiceConfig;
        [SerializeField] private AccessibilityConfig accessibilityConfig;
        [Tooltip("Ödüllü reklam kimlikleri. Boş bırakılırsa cihazda reklam hiç açılmaz " +
                 "(editörde zaten anında ödül veren taklit servis çalışır).")]
        [SerializeField] private AdsConfig adsConfig;
        [SerializeField] private string mainSceneName = "Main";
        [SerializeField] private bool loadMainOnStart = true;
        [Tooltip("Boşsa Main tek karede yüklenir ve açılış görseli görünmez.")]
        [SerializeField] private LoadingScreen loadingScreen;

        [Tooltip("SADECE TEST. 0 = kapalı, yani gerçek program (3/6/9/12 saat, gece susar). " +
                 "Sıfırdan büyükse altı bildirimin hepsi bu kadar saniye arayla gider, cihazda " +
                 "birkaç dakikada izlenebilsin diye. YAYINA ÇIKMADAN ÖNCE 0 YAP.")]
        [SerializeField, Min(0)] private int notificationTestSpacingSeconds = 0;

        [Tooltip("SADECE TEST. Açıkken UMP rıza formu AEA bölgesindeymiş gibi zorlanır, böylece " +
                 "GDPR akışı Android'de denenebilir. Cihaz hash'i UMP'nin ilk çalıştırmada logcat'e " +
                 "yazdığı listede olmalı. YAYINA ÇIKMADAN ÖNCE KAPAT.")]
        [SerializeField] private bool forceEeaConsentForTesting;

        public GameClock Clock { get; private set; }
        public SaveService Save { get; private set; }
        public SaveData Data { get; private set; }
        public WalletService Wallet { get; private set; }
        public EconomyService Economy { get; private set; }
        public OfflineReport Offline { get; private set; }
        public MarketService Market { get; private set; }
        public MaintenanceService Maintenance { get; private set; }
        public ForemanService Foremen { get; private set; }
        public GoalService Goals { get; private set; }
        public VoyageService Voyages { get; private set; }
        public ChapterService Chapters { get; private set; }
        public CaptainService Captains { get; private set; }
        public ExpeditionService Expeditions { get; private set; }
        public CraftingService Crafting { get; private set; }
        public LiveEventService LiveEvents { get; private set; }
        public FoundryFestivalService Festival { get; private set; }

        private TimeService _time;
        private NotificationService _notifications;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        private AdMobService _ads;
        private UmpConsentService _consent;
        private MobileIAPService _iap;
#endif

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
#if UNITY_EDITOR
            // editor playtests must keep simulating when the editor loses focus (remote tooling, alt-tab);
            // device builds keep the OS default so the idle/battery rules in GDD §14.5 still apply
            Application.runInBackground = true;
#endif

            // Landscape, both grips, never portrait. Player Settings already say the same thing, but an
            // SDK that merges its own <activity> into the manifest can quietly widen it back; this is the
            // one that holds at runtime. Flags before the mode — the OS refuses to drop the last allowed
            // orientation, so clearing portrait first just re-enables it.
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.AutoRotation;

            // Quality / device tier + frame-rate cap (GDD §14.5)
            ServiceLocator.Register(new QualityService(
                qualityConfig != null ? qualityConfig.TargetFrameRate : 60,
                qualityConfig != null && qualityConfig.VSync));

            // Text first: everything built after this can ask for a translated line while it is building.
            ServiceLocator.Register(new LocalizationService());

            // Presentation. Audio plays for real once the config carries a library.
            ServiceLocator.Register(audioConfig != null
                ? new AudioService(audioConfig.Master, audioConfig.Music, audioConfig.Sfx, audioConfig.Library)
                : new AudioService(1f, 0.6f, 0.8f));
            ServiceLocator.Register(new HapticService(juiceConfig == null || juiceConfig.Haptics));
            CameraShake.Enabled = juiceConfig == null || juiceConfig.ScreenShake;
            if (accessibilityConfig != null) ServiceLocator.Register(accessibilityConfig);

            // Platform facades (dev stubs now, real SDKs need package installs at ship time)
            ServiceLocator.Register<IAnalytics>(new DevAnalyticsService());
            ServiceLocator.Register<IRemoteConfig>(new LocalRemoteConfigService());
            ServiceLocator.Register<ICloudSave>(new LocalCloudSaveStub());
            // Gerçek reklam yalnız cihazda. GMA'nın editör karşılığı yok, o yüzden editörde IAP ile
            // aynı yolu izleyip anında ödül veren taklit servis kalır.
            //
            // Rıza önce: UMP formu ve iOS'ta ATT bitmeden SDK başlatılmaz, bu yüzden MobileAds.Initialize
            // artık AdMobService'in yapıcısında değil, Gather'ın geri çağrısında.
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            _consent = new UmpConsentService(forceEeaConsentForTesting);
            ServiceLocator.Register<IConsent>(_consent);
            _ads = new AdMobService(adsConfig, _consent);
            ServiceLocator.Register<IAdService>(_ads);
            _consent.Gather(_ads.Initialize);
#else
            ServiceLocator.Register<IConsent>(new DevConsentService());
            ServiceLocator.Register<IAdService>(new StubAdService());
#endif
            // Gerçek kasa yalnız Android/iOS cihazda. Editörde Billing/StoreKit yok; oradaki test yolu mağazanın kendi
            // devFreeIAP anahtarı. Kuralı gevşetip editörde de açarsak, UGS bağlantısı olmadığı her
            // oturumda konsol bir başlatma hatası yazar.
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            _iap = new MobileIAPService();
            ServiceLocator.Register<IIAPService>(_iap);
#else
            ServiceLocator.Register<IIAPService>(new StubIAPService());
#endif
            // Local notifications only exist on a device: the editor has no notification manager to
            // register a channel with, so play mode keeps the stub the same way IAP does.
#if UNITY_ANDROID && !UNITY_EDITOR
            ServiceLocator.Register<INotifications>(new AndroidNotifications());
#elif UNITY_IOS && !UNITY_EDITOR
            ServiceLocator.Register<INotifications>(new IOSNotifications());
#else
            ServiceLocator.Register<INotifications>(new StubNotifications());
#endif

            Save = new SaveService();
            ServiceLocator.Register(Save);

            bool hadSave = Save.TryLoad(out SaveData loaded);
            Data = hadSave ? loaded : new SaveData();

            // A save from a build with a different economy is not playable progress — see
            // SaveMigration for why, and for the one constant that arms this.
            if (hadSave && SaveMigration.NeedsReset(Data))
            {
                Debug.LogWarning($"[Save] progress from save version {Data.version} reset for version " +
                                 $"{SaveMigration.CurrentVersion}; purchases kept.");
                Data = SaveMigration.Reset(Data);
                // Stamp the new version on disk immediately. Without this, a player who force-quits
                // before the first autosave would be reset again on the next launch, and again after
                // that — the wipe has to be recorded even if nothing else about the session is.
                Save.Save(Data);
            }

            // Prestige has been retired. Keep an existing player's earned multiplier, but freeze it as
            // a plain permanent bonus and never expose/reset the run again. No save-version bump: this
            // update must preserve progression.
            if (SaveMigration.RetirePrestige(Data, 0.10d)) Save.Save(Data);
            ServiceLocator.Register(Data);

            Clock = new GameClock(ticksPerSecond);
            ServiceLocator.Register(Clock);

            Wallet = new WalletService(Data.wallet);
            ServiceLocator.Register(Wallet);

            Economy = economyConfig != null
                ? new EconomyService(economyConfig.CostGrowth, economyConfig.TierValueMultiplier)
                : new EconomyService(1.09d, 3.2d);
            ServiceLocator.Register(Economy);

            if (economyConfig != null)   // milestone step-multipliers (GDD §5), designer-tunable
            {
                Game.Core.Milestones.Every = economyConfig.MilestoneEvery;
                Game.Core.Milestones.StepMultiplier = economyConfig.MilestoneStepMultiplier;
            }

            _time = new TimeService();
            ServiceLocator.Register(_time);
            ServiceLocator.Register(new RatingPromptService(_time, ServiceLocator.Get<IAnalytics>()));

            var boost = new BoostService(Data, _time);
            ServiceLocator.Register(boost);

            // Wear, and the crews that put it right. Registered before the islands and the yards
            // because both of them read an island's state of repair to know how fast it runs, and
            // evaluated immediately below so the absence that just ended is charged for BEFORE any
            // of that is asked — an island that spent the launch frame at full speed and dropped to
            // 60% a moment later would read as the game breaking rather than as neglect.
            // Game.Core.Maintenance spelled out: the property below is also called Maintenance, and
            // inside this class the name resolves to it rather than to the type.
            Maintenance = new MaintenanceService(Data, _time, Wallet,
                maintenanceConfig != null ? maintenanceConfig.Tuning : Game.Core.Maintenance.Tuning.Default,
                maintenanceConfig == null || maintenanceConfig.Enabled);
            ServiceLocator.Register(Maintenance);
            Maintenance.Evaluate();

            // The yards, and with them the only path cash takes into the wallet. Registered before the
            // offline grant so the pads can be advanced for the absence in the same breath as paying
            // for it, and driven from Update below so it keeps settling across a scene load.
            // The roster, before the yards: MarketService reads its income multiplier every tick, and
            // an island's stations read its speeds every frame. Gems are what buy its chests, which is
            // the first thing in the game they have been able to buy outside the store. The clock is
            // handed over for the free chest's deadline — it is registered well above this.
            Foremen = new ForemanService(Data, Wallet,
                foremanConfig != null ? foremanConfig.ToTuning() : Game.Core.Foremen.Tuning.Default,
                foremanConfig != null ? foremanConfig.ToChestTuning() : Game.Core.MasterChest.Tuning.Default,
                _time,
                foremanConfig != null ? foremanConfig.TierTint : null);
            ServiceLocator.Register(Foremen);

            // The checklist. After the roster because a goal can pay out foreman cards, and before
            // the yards and the contracts because both of them report into it.
            Goals = new GoalService(Data, Wallet, Foremen, _time);
            ServiceLocator.Register(Goals);

            // The spine over the island ladder. After the roster and the wallet because a beat pays
            // gems and foreman cards, and after nothing else: chapters OBSERVE the save rather than
            // being reported into, so no other service has to be built first — or has to know.
            Chapters = new ChapterService(Data, Wallet, Foremen,
                chapterConfig != null ? chapterConfig.ToTuning() : Game.Core.Chapters.Tuning.Default);
            ServiceLocator.Register(Chapters);
            Maintenance.Goals = Goals;   // built before this, and evaluated before this on purpose

            Market = new MarketService(Data, Wallet, boost, Maintenance, Foremen, Goals);
            ServiceLocator.Register(Market);

            // The sea roster, BEFORE the dock: a voyage settles a captain's charts and asks a bosun
            // what the risk is, so the dock is handed one rather than looking for it later. Needs
            // nothing but the save — charts are earned at sea and spent on crates, and neither end of
            // that loop touches the wallet.
            Captains = new CaptainService(Data,
                captainConfig != null ? captainConfig.ToTuning() : Game.Core.Captains.Tuning.Default,
                captainConfig != null ? captainConfig.ToCrateTuning() : Game.Core.CaptainCrate.Tuning.Default);
            ServiceLocator.Register(Captains);

            // The dock. After the yards because it pulls bars off their pads, and after the roster
            // because that is who the cards it brings home belong to. It is the game's second
            // destination for a bar — the first being the counter, which was the only one.
            Voyages = new VoyageService(Data, Market, Foremen, Wallet, _time,
                voyageConfig != null ? voyageConfig.ToTuning() : Game.Core.Voyages.Tuning.Default,
                Captains);
            ServiceLocator.Register(Voyages);

            // The workshop bench. Before the sea service because both ends of its loop attach to
            // one: scraps teach it and wins drop its points. Needs only the save and the clock —
            // points and XP are a third closed loop beside salvage and charts.
            Crafting = new CraftingService(Data, Save, _time,
                craftingConfig != null ? craftingConfig.ToTuning() : Game.Core.Crafting.Tuning.Default,
                seaCombatConfig != null ? seaCombatConfig.ToTuning() : Game.Core.SeaCombat.Tuning.Default);
            ServiceLocator.Register(Crafting);
            Voyages.Crafting = Crafting;   // a claimed voyage pays its flat points

            // Going out WITH a ship rather than watching a bar fill. Holds no save state of its own —
            // standing on a deck is not progress, the voyage is, and the dock already persists all of
            // it on the wall clock. Registered after the dock because it only ever reads from it.
            Expeditions = new ExpeditionService(Voyages, _time, Data, Captains,
                seaCombatConfig != null ? seaCombatConfig.ToTuning()
                                        : Game.Core.SeaCombat.Tuning.Default);
            ServiceLocator.Register(Expeditions);
            Expeditions.Crafting = Crafting;   // scraps teach the bench, wins can drop a point
            Crafting.Expeditions = Expeditions;   // wearing a crafted item goes through the sea's Equip

            // The live-ops wrapper: the schedule only. After the goals because event tasks are read
            // off metrics that already exist, and after nothing else — it owns a window and two
            // arrays and asks no service for anything. With no config wired it holds no events, which
            // is exactly right for a build made before any were authored.
            LiveEvents = new LiveEventService(Data,
                liveEventConfig != null ? liveEventConfig.Definitions() : null,
                _time, ServiceLocator.Get<IAnalytics>());
            ServiceLocator.Register(LiveEvents);

            // The first module on top of it. Last of the lot on purpose: it pays out through the
            // wallet, the roster, the captains and the boost clock, and reads the goal tallies, so
            // every one of them has to exist first. With no festival on the schedule it is inert —
            // Available reads false and nothing above it draws.
            Festival = new FoundryFestivalService(LiveEvents, Goals, Wallet,
                foundryFestivalConfig != null ? foundryFestivalConfig.ToTuning()
                                              : Game.Core.FoundryFestival.Tuning.Default,
                Foremen, Captains, boost, Data, Save, _time);
            ServiceLocator.Register(Festival);

            ServiceLocator.Register(new DailyRewardService(Data, _time));
            ServiceLocator.Register(new FreeRewardService(Data, _time));
            var contract = new ContractService(Wallet, contractConfig, Data, _time, Foremen, Goals);
            ServiceLocator.Register(contract);

            Offline = new OfflineReport();
            ServiceLocator.Register(Offline);
            GrantOffline();

            // Prices the first ship's offers off the rate the last session persisted, so a returning
            // empire is not offered a $500 job while the live income meter is still reading zero.
            contract.Seed(Data.incomeRatePerSec * 60d);

            // Nothing is queued here — a notification only makes sense once the player has left, so the
            // queue is built in OnApplicationPause and torn down again on the way back.
            _notifications = new NotificationService(Data, offlineConfig, _time,
                                                     ServiceLocator.Get<INotifications>(), contract,
                                                     notificationTestSpacingSeconds);
            ServiceLocator.Register(_notifications);
            _notifications.RefreshOpenedTarget();

            ServiceLocator.Get<IAnalytics>()?.Log("session_start");
        }

        private void GrantOffline()
        {
            if (offlineConfig == null || !offlineConfig.Enabled || Data.savedUnixSeconds <= 0L) return;
            long elapsed = _time.ElapsedSince(Data.savedUnixSeconds);

            // The store sells permanent offline upgrades (the "Gece Vardiyasi" offer), so the config is
            // the floor rather than the whole story. Efficiency is clamped: paying past 100% would mean
            // earning more asleep than awake.
            double efficiency = offlineConfig.Efficiency + Data.offlineEfficiencyBonus;
            if (efficiency > 1d) efficiency = 1d;
            long cap = offlineConfig.CapSeconds + Data.offlineCapBonusSeconds;

            // A boost bought with gems is sold in hours, and an idle player spends most of those hours
            // with the app closed, so the part of the credited window it was still running for pays at
            // its multiplier. That overlap lives inside ComputeTotal rather than here because
            // NotificationService has to predict this exact figure hours in advance — see
            // OfflineEarnings for why the two must not be allowed to drift apart.
            BigDouble earned = OfflineEarnings.ComputeTotal(
                new BigDouble(Data.incomeRatePerSec), elapsed, efficiency, cap,
                Data.boostMultiplier, Data.boostEndUnix - Data.savedUnixSeconds);

            if (earned.Mantissa > 0d)
            {
                Wallet.AddCash(earned);
                Offline.Amount = earned;
                Offline.AwaySeconds = elapsed;
                Offline.CreditedSeconds = (cap > 0L && elapsed > cap) ? cap : elapsed;
                Offline.Efficiency = efficiency;
                Offline.Pending = true;
            }

            // The grant above paid for the absence off the rate the yards persisted. This moves the
            // yards themselves forward through the same absence — no cash, just stock — so an
            // unstaffed market is found buried on the next launch and a maxed one is found clear.
            Market?.SettleOffline(elapsed);
        }

        private void Start()
        {
            if (!loadMainOnStart || string.IsNullOrEmpty(mainSceneName)) return;
            // Synchronous LoadScene finishes inside one frame, which is why the splash was never visible.
            // The loading screen owns the async load so it can hold itself up until Main is actually there.
            if (loadingScreen != null) loadingScreen.Begin(mainSceneName, Data);
            else SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Clock?.Advance(dt);
            // Here rather than on any scene object: the yards have to keep settling while the player is
            // on an island, in the market, or watching a loading screen between the two.
            Market?.Tick(dt);
            // Strictly after the yards: a hold pulls off the pads, and the pads have to have taken
            // this tick's deliveries first or the dock is always one frame behind the lorries.
            Voyages?.Tick(dt);
            // Same reason: a repair the player started before sailing away has to keep running while
            // they are somewhere else, and it is the crew's own clock that finishes it.
            Maintenance?.Tick(dt);
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            // The rewarded ad's load backoff, and the timeout on a tap that landed before one was ready.
            _ads?.Tick(dt);
            // ATT izni ilk kareden önce sorulamaz; rıza akışının kalan adımı buradan sürülür.
            _consent?.Tick(dt);
            // Bir dokunuşun mağazada süresiz asılı kalmasına karşı bekçi: cevapsız kalan alım
            // hem onu başlatan düğmeyi hem de sonraki bütün alımları kilitliyordu.
            _iap?.Tick(dt);
#endif
        }

        /// <summary>
        /// Android's reliable "the player is leaving" signal, and where the absence is written down.
        ///
        /// The save has to come FIRST. The notification queue predicts what the welcome-back screen
        /// will pay, and that grant is measured from <c>savedUnixSeconds</c> — so queueing against a
        /// save that has not been stamped yet would quote a figure counted from the previous session.
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                // Before the save, because the festival's counters only move when something reads
                // them: this is the one moment a read cannot be relied on to happen again while the
                // window is still open. A player who finishes a task and immediately leaves on the
                // last evening would otherwise lose it to the closing second.
                Festival?.Sync();
                Save?.Save(Data);
                _notifications?.ScheduleAway();
            }
            else
            {
                _notifications?.Cancel();   // the absence it described is over
                _notifications?.RefreshOpenedTarget();
                ServiceLocator.Get<ContractService>()?.ResumeWallClock();
                // An Android app is backgrounded far more often than it is relaunched, so most
                // absences end HERE rather than in Awake. Without this, a player who left the game
                // in the background over a weekend would come back to a spotless island.
                Maintenance?.Evaluate();
            }
        }

        private void OnApplicationQuit()
        {
            Save?.Save(Data);
            // Android normally pauses before it quits, so this is usually a re-queue of the same plan
            // a second later. ScheduleAway clears the queue before rebuilding it, so that is harmless.
            _notifications?.ScheduleAway();
        }
    }
}
