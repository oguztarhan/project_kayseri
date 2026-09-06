using UnityEngine.SceneManagement;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// The single policy boundary for the portrait shipyard rollout.
    ///
    /// The value itself lives on <see cref="SaveData"/> so a rollback is a save-preserving setting
    /// change rather than a build-only decision. Callers use this class instead of reading the field
    /// directly, which keeps scene routing, service registration, and legacy entry points consistent.
    /// </summary>
    public static class ShipyardFeatureSwitch
    {
        public const string PortraitSceneName = "Shipyard";
        public const string LegacySceneName = "Main";

        /// <summary>Null means an isolated preview or an editor probe, where the feature is on.</summary>
        public static bool IsEnabled(SaveData data)
            => data == null || data.UsePortraitShipyard;

        public static bool Set(SaveData data, bool enabled)
        {
            if (data == null || data.UsePortraitShipyard == enabled) return false;
            data.UsePortraitShipyard = enabled;
            return true;
        }

        public static string PresentationScene(SaveData data, string portraitScene, string legacyScene)
        {
            string preferred = IsEnabled(data) ? portraitScene : legacyScene;
            if (!string.IsNullOrEmpty(preferred) && Application.CanStreamedLevelBeLoaded(preferred))
                return preferred;

            // Keep the switch safe if a build profile forgot to include one presentation scene. The
            // legacy scene is the least surprising fallback and is also what old builds know.
            if (!string.IsNullOrEmpty(legacyScene) && Application.CanStreamedLevelBeLoaded(legacyScene))
                return legacyScene;
            return preferred;
        }

        public static bool AllowsLegacyMarket(SaveData data)
            => !IsEnabled(data);

        public static bool IsCurrentPresentation(SaveData data, string portraitScene, string legacyScene)
        {
            string expected = PresentationScene(data, portraitScene, legacyScene);
            return SceneManager.GetActiveScene().name == expected;
        }
    }
}
