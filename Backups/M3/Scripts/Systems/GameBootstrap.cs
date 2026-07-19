using Game.Core;
using Game.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Systems
{
    /// <summary>
    /// Entry point on a persistent object in the Bootstrap scene. Registers services (facade-first),
    /// builds the economy, loads the save, grants offline earnings (GDD §7), then loads the Main scene.
    /// Drives the GameClock each frame — the sim is decoupled from render (GDD §14.5).
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float ticksPerSecond = 8f;
        [SerializeField] private EconomyConfig economyConfig;
        [SerializeField] private OfflineConfig offlineConfig;
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

            ServiceLocator.Register<IAnalytics>(new DevAnalyticsService());
            ServiceLocator.Register<IConsent>(new DevConsentService());
            ServiceLocator.Register<IRemoteConfig>(new LocalRemoteConfigService());

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

            _time = new TimeService();
            ServiceLocator.Register(_time);

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
