using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Systems
{
    /// <summary>
    /// Covers the Bootstrap → Main handover with the island the player last stood on: quit on the coal
    /// island and you come back to the coal splash, quit on copper and you come back to copper's.
    ///
    /// It lives in Game.Systems rather than Game.UI on purpose. Game.UI already references Game.Systems,
    /// so a boot component the bootstrapper has to hold a reference to cannot live there without making
    /// the two assemblies circular. This is part of the boot sequence, not part of the game's interface.
    ///
    /// Survives the scene swap via DontDestroyOnLoad and deletes itself once Main is up, so nothing here
    /// is resident during play.
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        // WorldIslands records the live island's ladder index under this id (SaveLevel("worldactive", i)).
        // Duplicated across the assembly boundary rather than shared, because the alternative is
        // Game.Gameplay and Game.Systems agreeing on a constant neither of them owns.
        private const string ActiveIslandKey = "worldactive";

        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image background;

        [Tooltip("Ada sırasıyla: kömür, bakır, demir, gümüş, altın, yakut, zümrüt, elmas.")]
        [SerializeField] private Sprite[] islandBackgrounds;

        [Tooltip("Alttaki çubuğun dolan parçası. Sola dayalı gerili olmalı — genişliği anchorMax.x " +
                 "ile sürülüyor. Boşken çubuk hiç çizilmez, ekranın gerisi aynı çalışır.")]
        [SerializeField] private RectTransform barFill;

        [Tooltip("Çubuğun altındaki tek kelime. Metni buradan yazılıyor çünkü dil servisi sahne " +
                 "kurulurken değil, Begin çağrıldığında hazır oluyor.")]
        [SerializeField] private TMP_Text barLabel;

        [Tooltip("Ekran en az bu kadar durur. Yükleme daha erken biterse bile göz görsün diye.")]
        [SerializeField, Min(0f)] private float minimumSeconds = 1.4f;

        [Tooltip("Main hazır olduktan sonraki açılma süresi.")]
        [SerializeField, Min(0f)] private float fadeSeconds = 0.45f;

        private void Awake() => DontDestroyOnLoad(gameObject);

        /// <summary>Paints the splash for the saved island, then loads <paramref name="sceneName"/> behind it.</summary>
        public void Begin(string sceneName, SaveData data)
        {
            Paint(data);
            if (barLabel != null) barLabel.text = Loc.T("ortak.yukleniyor");
            StartCoroutine(Run(sceneName));
        }

        private void Paint(SaveData data)
        {
            if (background == null || islandBackgrounds == null || islandBackgrounds.Length == 0) return;
            int index = ActiveIsland(data);
            // A save written before an island was added, or a cleared "worldactive", both land here.
            if (index < 0 || index >= islandBackgrounds.Length) index = 0;
            if (islandBackgrounds[index] != null) background.sprite = islandBackgrounds[index];
        }

        /// <summary>Drives the fill's right edge; the track is whatever rect the fill sits in.</summary>
        private void Progress(float t)
        {
            if (barFill == null) return;
            Vector2 max = barFill.anchorMax;
            barFill.anchorMax = new Vector2(Mathf.Clamp01(t), max.y);
        }

        private static int ActiveIsland(SaveData data)
        {
            if (data == null || data.islandLevels == null) return 0;
            for (int i = 0; i < data.islandLevels.Count; i++)
                if (data.islandLevels[i].id == ActiveIslandKey) return data.islandLevels[i].level;
            return 0;
        }

        private IEnumerator Run(string sceneName)
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            op.allowSceneActivation = false;

            // Unity parks a scene that is loaded but not yet activated at 0.9 — it never reaches 1
            // until activation is allowed, so waiting for isDone here would deadlock.
            float held = 0f;
            Progress(0f);
            while (op.progress < 0.9f || held < minimumSeconds)
            {
                held += Time.unscaledDeltaTime;
                // Whichever of the two is further behind is what the bar shows, so it arrives at full
                // exactly when this loop lets go. Drawing raw progress instead would throw the bar to
                // the end on the first frame and leave it sitting there — Main loads in well under the
                // minimum hold — and a bar driven by the clock alone would lie on a slow device.
                Progress(Mathf.Min(op.progress / 0.9f,
                                   minimumSeconds > 0f ? held / minimumSeconds : 1f));
                yield return null;
            }
            Progress(1f);

            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;

            // One more frame so Main's Start methods have run and its canvases are laid out. Fading off
            // a half-built HUD looks worse than never having shown a splash.
            yield return null;

            if (group != null && fadeSeconds > 0f)
            {
                for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
                {
                    group.alpha = 1f - (t / fadeSeconds);
                    yield return null;
                }
            }

            Destroy(gameObject);
            // The seven islands we did not paint were pulled in with the array — let them go.
            Resources.UnloadUnusedAssets();
        }
    }
}
