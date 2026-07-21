using UnityEditor;
using UnityEditor.SceneManagement;

namespace Game.EditorTools
{
    /// <summary>
    /// Forces Play Mode to always start from the <b>Bootstrap</b> scene — which registers every service
    /// (Wallet, Economy, GameClock, SaveData, …) and then loads Main. Runs on every editor load / domain
    /// reload via [InitializeOnLoad], so it <b>survives editor restarts</b>. Without this, restarting Unity
    /// resets <c>EditorSceneManager.playModeStartScene</c> to null, so pressing Play in Main boots straight
    /// into the game with NO services — a null Wallet then freezes WorldMapUI/HudUGUI (no pill text, no
    /// selection card, can't enter any island) and no economy ticks. Editor-only; not shipped in builds.
    /// </summary>
    [InitializeOnLoad]
    public static class BootstrapStartScene
    {
        private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";

        static BootstrapStartScene()
        {
            // Defer: the AssetDatabase isn't guaranteed ready inside the static constructor during a reload.
            EditorApplication.delayCall += Arm;
        }

        private static void Arm()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapPath);
            if (scene != null) EditorSceneManager.playModeStartScene = scene;
        }
    }
}
