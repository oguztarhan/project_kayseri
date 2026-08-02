using Game.Core;
using Game.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Systems
{
    /// <summary>
    /// Entry point on a persistent object in the Bootstrap scene. Registers all services (facade-first:
    /// dev stubs now, real SDKs at ship time), applies quality settings, builds economy + prestige, loads
    /// the save, grants offline earnings, then loads Main. Drives the GameClock each frame (GDD §14.5).
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float ticksPerSecond = 8f;
        [SerializeField] private EconomyConfig economyConfig;
        [SerializeField] private OfflineConfig offlineConfig;
        [SerializeField] private PrestigeConfig prestigeConfig;
        [SerializeField] private ContractConfig contractConfig;
        [SerializeField] private QualityConfig qualityConfig;
        [SerializeField] private AudioConfig audioConfig;
        [SerializeField] private JuiceConfig juiceConfig;
        [SerializeField] private AccessibilityConfig accessibilityConfig;
        [SerializeField] private string mainSceneName = "Main";
        [SerializeField] private bool loadMainOnStart = true;
        [Tooltip("Boşsa Main tek karede yüklenir ve açılış görseli görünmez.")]
        [SerializeField] private LoadingScreen loadingScreen;

        public GameClock Clock { get; private set; }
        public SaveService Save { get; private set; }
        public SaveData Data { get; private set; }
        public WalletService Wallet { get; private set; }
        public EconomyService Economy { get; private set; }
        public OfflineReport Offline { get; private set; }

        private TimeService _time;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
#if UNITY_EDITOR
            // editor playtests must keep simulating when the editor loses focus (remote tooling, alt-tab);
            // device builds keep the OS default so the idle/battery rules in GDD §14.5 still apply
            Application.runInBackground = true;
#endif

            // Quality / device tier + frame-rate cap (GDD §14.5)
            ServiceLocator.Register(new QualityService(
                qualityConfig != null ? qualityConfig.TargetFrameRate : 60,
                qualityConfig != null && qualityConfig.VSync));

            // Presentation facades (silent/no-op until content is supplied)
            ServiceLocator.Register(audioConfig != null
                ? new AudioService(audioConfig.Master, audioConfig.Music, audioConfig.Sfx)
                : new AudioService(1f, 0.6f, 0.8f));
            ServiceLocator.Register(new VFXService());
            ServiceLocator.Register(new HapticService(juiceConfig == null || juiceConfig.Haptics));
            if (accessibilityConfig != null) ServiceLocator.Register(accessibilityConfig);

            // Platform facades (dev stubs now, real SDKs need package installs at ship time)
            ServiceLocator.Register<IAnalytics>(new DevAnalyticsService());
            ServiceLocator.Register<IConsent>(new DevConsentService());
            ServiceLocator.Register<IRemoteConfig>(new LocalRemoteConfigService());
            ServiceLocator.Register<ICloudSave>(new LocalCloudSaveStub());
            ServiceLocator.Register<IAdService>(new StubAdService());
            ServiceLocator.Register<IIAPService>(new StubIAPService());
            ServiceLocator.Register<INotifications>(new StubNotifications());

            Save = new SaveService();
            ServiceLocator.Register(Save);

            Data = Save.TryLoad(out SaveData loaded) ? loaded : new SaveData();
            ServiceLocator.Register(Data);

            Clock = new GameClock(ticksPerSecond);
            ServiceLocator.Register(Clock);

            Wallet = new WalletService(Data.wallet);
            ServiceLocator.Register(Wallet);

            Economy = economyConfig != null
                ? new EconomyService(economyConfig.CostGrowth, economyConfig.TierValueMultiplier, economyConfig.ManagerBonus, economyConfig.ManagerCostBase)
                : new EconomyService(1.09d, 3.2d);
            ServiceLocator.Register(Economy);

            if (economyConfig != null)   // milestone step-multipliers (GDD §5), designer-tunable
            {
                Game.Core.Milestones.Every = economyConfig.MilestoneEvery;
                Game.Core.Milestones.StepMultiplier = economyConfig.MilestoneStepMultiplier;
            }

            var prestige = prestigeConfig != null
                ? new PrestigeService(Data, prestigeConfig.InvestorK, prestigeConfig.BonusPerInvestor, prestigeConfig.Threshold)
                : new PrestigeService(Data, 1d, 0.02d, 1000d);
            ServiceLocator.Register(prestige);

            _time = new TimeService();
            ServiceLocator.Register(_time);

            ServiceLocator.Register(new BoostService(Data, _time));
            ServiceLocator.Register(new DailyRewardService(Data, _time));
            ServiceLocator.Register(new FreeRewardService(Data, _time));
            var contract = contractConfig != null
                ? new ContractService(Wallet, contractConfig.TargetUnits, contractConfig.TimeLimitSeconds,
                                      contractConfig.RewardCash, contractConfig.RewardGems)
                : new ContractService(Wallet, 100d, 60f, 500d, 2L);
            ServiceLocator.Register(contract);

            Offline = new OfflineReport();
            ServiceLocator.Register(Offline);
            GrantOffline();

            // After the offline grant, so the money earned while away is not also counted as progress
            // toward the opening contract.
            contract.Seed(Data.incomeRatePerSec * 60d);

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

            BigDouble earned = OfflineEarnings.Compute(new BigDouble(Data.incomeRatePerSec), elapsed, efficiency, cap);

            // A boost bought with gems is sold in hours, and an idle player spends most of those hours
            // with the app closed — so the part of the credited window the boost was still running for
            // pays at its multiplier. Only the EXTRA is added here; the line above already paid the
            // whole window at ×1. The credited window starts when the player left, so the overlap is
            // measured from savedUnixSeconds forward, not backward from now.
            long credited = (cap > 0L && elapsed > cap) ? cap : elapsed;
            long boosted = Data.boostEndUnix - Data.savedUnixSeconds;
            if (boosted > credited) boosted = credited;
            if (boosted > 0L && Data.boostMultiplier > 1d)
                earned += OfflineEarnings.Compute(new BigDouble(Data.incomeRatePerSec), boosted,
                                                  efficiency * (Data.boostMultiplier - 1d), 0L);

            if (earned.Mantissa > 0d)
            {
                Wallet.AddCash(earned);
                Offline.Amount = earned;
                Offline.AwaySeconds = elapsed;
                Offline.CreditedSeconds = (cap > 0L && elapsed > cap) ? cap : elapsed;
                Offline.Efficiency = efficiency;
                Offline.Pending = true;
            }
        }

        private void Start()
        {
            if (!loadMainOnStart || string.IsNullOrEmpty(mainSceneName)) return;
            // Synchronous LoadScene finishes inside one frame, which is why the splash was never visible.
            // The loading screen owns the async load so it can hold itself up until Main is actually there.
            if (loadingScreen != null) loadingScreen.Begin(mainSceneName, Data);
            else SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
        }

        private void Update() => Clock?.Advance(Time.deltaTime);

        private void OnApplicationPause(bool paused) { if (paused) Save?.Save(Data); }
        private void OnApplicationQuit() => Save?.Save(Data);
    }
}
