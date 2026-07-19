using Game.Core;
using Game.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Systems
{
    /// <summary>
    /// Entry point on a persistent object in the Bootstrap scene. Registers services (facade-first:
    /// dev stubs now, real SDKs at M5), builds the economy from <see cref="EconomyConfig"/>, loads
    /// the save, then loads the Main gameplay scene. Drives the GameClock each frame — the sim is
    /// decoupled from render (GDD §14.5).
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float ticksPerSecond = 8f;
        [SerializeField] private EconomyConfig economyConfig;
        [SerializeField] private string mainSceneName = "Main";
        [SerializeField] private bool loadMainOnStart = true;

        public GameClock Clock { get; private set; }
        public SaveService Save { get; private set; }
        public SaveData Data { get; private set; }
        public WalletService Wallet { get; private set; }
        public EconomyService Economy { get; private set; }

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
                ? new EconomyService(economyConfig.CostGrowth, economyConfig.TierValueMultiplier)
                : new EconomyService(1.09d, 3.2d);
            ServiceLocator.Register(Economy);

            ServiceLocator.Get<IAnalytics>()?.Log("session_start");
        }

        private void Start()
        {
            if (loadMainOnStart && !string.IsNullOrEmpty(mainSceneName))
                SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
        }

        private void Update() => Clock?.Advance(Time.deltaTime);

        private void OnApplicationPause(bool paused)
        {
            if (paused) Save?.Save(Data);
        }

        private void OnApplicationQuit() => Save?.Save(Data);
    }
}
