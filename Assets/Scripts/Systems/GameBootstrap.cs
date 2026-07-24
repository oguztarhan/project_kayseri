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
        [SerializeField] private DailyRewardConfig dailyConfig;
        [SerializeField] private QualityConfig qualityConfig;
        [SerializeField] private AudioConfig audioConfig;
        [SerializeField] private JuiceConfig juiceConfig;
        [SerializeField] private AccessibilityConfig accessibilityConfig;
        [SerializeField] private string mainSceneName = "Main";
        [SerializeField] private bool loadMainOnStart = true;

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

            ServiceLocator.Register(new BoostService());
            ServiceLocator.Register(new DailyRewardService(Data, _time, dailyConfig != null ? dailyConfig.RewardGems : 5L));

            Offline = new OfflineReport();
            ServiceLocator.Register(Offline);
            GrantOffline();

            ServiceLocator.Get<IAnalytics>()?.Log("session_start");
        }

        private void GrantOffline()
        {
            if (offlineConfig == null || !offlineConfig.Enabled || Data.savedUnixSeconds <= 0L) return;
            long elapsed = _time.ElapsedSince(Data.savedUnixSeconds);
            BigDouble earned = OfflineEarnings.Compute(new BigDouble(Data.incomeRatePerSec), elapsed, offlineConfig.Efficiency, offlineConfig.CapSeconds);
            if (earned.Mantissa > 0d)
            {
                Wallet.AddCash(earned);
                Offline.Amount = earned;
                Offline.Pending = true;
            }
        }

        private void Start()
        {
            if (loadMainOnStart && !string.IsNullOrEmpty(mainSceneName))
                SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
        }

        private void Update() => Clock?.Advance(Time.deltaTime);

        private void OnApplicationPause(bool paused) { if (paused) Save?.Save(Data); }
        private void OnApplicationQuit() => Save?.Save(Data);
    }
}
