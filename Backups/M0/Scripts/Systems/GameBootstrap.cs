using Game.Core;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// Entry point. Lives on a persistent object in the Bootstrap scene. Registers services
    /// (facade-first: dev stubs now, real SDKs at M5), creates the simulation clock, and loads
    /// (or starts) the save. Drives the GameClock each frame — the sim is decoupled from render
    /// (GDD §14.5). Later milestones hang systems off the clock and load the Main scene here.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float ticksPerSecond = 8f;

        public GameClock Clock { get; private set; }
        public SaveService Save { get; private set; }
        public SaveData Data { get; private set; }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            ServiceLocator.Register<IAnalytics>(new DevAnalyticsService());
            ServiceLocator.Register<IConsent>(new DevConsentService());
            ServiceLocator.Register<IRemoteConfig>(new LocalRemoteConfigService());

            Save = new SaveService();
            ServiceLocator.Register(Save);

            Data = Save.TryLoad(out SaveData loaded) ? loaded : new SaveData();

            Clock = new GameClock(ticksPerSecond);

            ServiceLocator.Get<IAnalytics>()?.Log("session_start");
        }

        private void Update()
        {
            Clock?.Advance(Time.deltaTime);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && Data != null) Save?.Save(Data);
        }

        private void OnApplicationQuit()
        {
            if (Data != null) Save?.Save(Data);
        }
    }
}
