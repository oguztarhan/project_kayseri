using System.Collections;
using System.Collections.Generic;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The loading screen for a swap between two scenes that are already in the game: island to market,
    /// market back to island.
    ///
    /// Both used to be a bare <c>LoadSceneAsync</c> with nothing over it. That is not a small omission on
    /// a phone: Main is the heavy scene — every island, all three phase roots — and for the second or two
    /// it takes to read, the player is looking at the yard they just asked to leave with a button that
    /// apparently did nothing. Half of them tap it again. The way out of the market has been reported as
    /// broken on slower devices for exactly this reason, and there was nothing wrong with the button.
    ///
    /// It is deliberately NOT <see cref="LoadingScreen"/>. That one is the boot splash: it is authored in
    /// the Bootstrap scene, holds eight island paintings, and deletes itself once Main is up so none of
    /// that stays resident. Keeping it alive to reuse here would keep the paintings alive with it. This is
    /// built from <see cref="UiBuild"/> and the wired <see cref="UiSkin"/> instead, so it costs nothing
    /// when it is not on screen — but it is the same shape on purpose: a full-bleed backdrop, the name of
    /// where you are going, one word underneath, and a bar along the bottom.
    ///
    /// Owns the input for its whole life. The backdrop is a raycast target over everything at sorting
    /// order 400, so the second tap that used to start a second load cannot reach anything.
    /// </summary>
    public sealed class SceneCurtain : MonoBehaviour
    {
        [Tooltip("Perdenin kapanma süresi. Sahne okuması bunun arkasında başlıyor, sırayla değil.")]
        [SerializeField, Min(0f)] private float fadeInSeconds = 0.22f;

        [Tooltip("Sahne hazır olsa bile perdenin en az bu kadar durması. Bir anda gelip giden " +
                 "yükleme ekranı, ekranın titremesi gibi duruyor.")]
        [SerializeField, Min(0f)] private float holdSeconds = 0.7f;

        [Tooltip("Yeni sahne kurulduktan sonraki açılma süresi.")]
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.35f;

        [Tooltip("Marketten hazır bekleyen adaya dönüşte yükleme görselinin ekranda kalacağı en az süre.")]
        [SerializeField, Min(0f)] private float returnScreenSeconds = 2f;

        [Tooltip("HUD 100, dünya haritası 150. Perde hepsinin üstünde olmalı.")]
        [SerializeField] private int sortingOrder = 400;

        private static SceneCurtain _live;

        private const string MarketScene = "Market";
        private const string MarketBackdropResource = "UI/Transitions/market_transition";

        // Main is expensive because the live operation builds its vehicles, tracks and dressing in
        // Start. Keep that already-built scene parked while the tiny market scene is open. Returning to
        // the island then wakes the existing objects instead of constructing the whole operation again.
        private static Scene _parkedIslandScene;
        private static readonly List<GameObject> ParkedIslandRoots = new List<GameObject>();

        private CanvasGroup _group;
        private RectTransform _fill;

        /// <summary>
        /// Covers the screen and loads <paramref name="sceneName"/> behind it. <paramref name="accent"/> is
        /// the ore colour of where the player is going, which is what the bar fills with — the same cue the
        /// map, the yard roofs and the floor arrows all use for the same islands.
        ///
        /// Returns true if the curtain took the job. False means one is already up, and that is the answer
        /// to a double tap: the caller has nothing left to do.
        /// </summary>
        public static bool Cover(string sceneName, Color accent, string caption)
        {
            if (string.IsNullOrEmpty(sceneName) || _live != null) return false;
            var go = new GameObject("SahneOrtusu");
            _live = go.AddComponent<SceneCurtain>();
            _live.Begin(sceneName, accent, caption);
            return true;
        }

        /// <summary>True while a swap is in flight, for anything that has to stop doing its job.</summary>
        public static bool Busy => _live != null;

        private void Begin(string sceneName, Color accent, string caption)
        {
            DontDestroyOnLoad(gameObject);
            Build(accent, caption);
            StartCoroutine(Run(sceneName));
        }

        private void Build(Color accent, string caption)
        {
            RectTransform canvas = UiBuild.Canvas(transform, "OrtuKanvas", sortingOrder);
            _group = canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            // From the first frame, before it is even visible. This is the half of the fix that stops a
            // second tap starting a second load — the guard in Cover is the other half.
            _group.blocksRaycasts = true;

            // Full bleed, and outside any safe area on purpose: a backdrop that stops at the notch is a
            // backdrop with a bright stripe of gameplay down one edge of it.
            // SceneCurtain is shared by both directions of the market trip. Using the same painting in
            // both directions makes the journey feel continuous instead of changing visual language at
            // the market door. If the resource is ever missing, the original colour curtain remains a
            // safe fallback.
            bool hasMarketBackdrop = AddMarketBackdrop(canvas);
            if (!hasMarketBackdrop)
            {
                UiBuild.Flat(canvas, "Zemin", new Color(0.045f, 0.055f, 0.08f, 1f), Vector2.zero, Vector2.one);
                // A wash of where you are going, so the two directions do not look like the same screen.
                UiBuild.Flat(canvas, "Vurgu", new Color(accent.r, accent.g, accent.b, 0.10f),
                             new Vector2(0f, 0f), new Vector2(1f, 0.42f));

                Label(canvas, "Baslik", caption, 74f, new Vector2(0.08f, 0.5f), new Vector2(0.92f, 0.62f),
                      new Color(1f, 1f, 1f, 0.96f));
                Label(canvas, "Durum", Loc.T("ortak.yukleniyor"), 40f,
                      new Vector2(0.08f, 0.44f), new Vector2(0.92f, 0.5f), new Color(1f, 1f, 1f, 0.55f));
            }
            else
            {
                // Keep the painting fully visible. The letters carry their own navy outline, so the
                // title stays readable without putting an opaque plate over the sky and mountains.
                Label(canvas, "Baslik", caption, 64f, new Vector2(0.21f, 0.815f), new Vector2(0.79f, 0.895f),
                      new Color(1f, 0.92f, 0.57f, 1f), true);
                Label(canvas, "Durum", Loc.T("ortak.yukleniyor"), 34f,
                      new Vector2(0.21f, 0.765f), new Vector2(0.79f, 0.825f), Color.white, true);
            }

            UiBuild.Bar(canvas, "Cubuk", new Color(1f, 1f, 1f, 0.10f),
                        new Color(accent.r, accent.g, accent.b, 0.95f),
                        new Vector2(0.08f, 0.085f), new Vector2(0.92f, 0.105f), out _fill);
            Progress(0f);
        }

        private static bool AddMarketBackdrop(Transform canvas)
        {
            Sprite sprite = Resources.Load<Sprite>(MarketBackdropResource);
            if (sprite == null) return false;

            var go = new GameObject("MarketGecisGorseli", typeof(RectTransform), typeof(Image),
                                    typeof(AspectRatioFitter));
            go.transform.SetParent(canvas, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = true;
            UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);
            var fitter = go.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            return true;
        }

        /// <summary>
        /// A centred line. TMP rather than <see cref="UiBuild.Label"/>'s legacy Text, because TMP's
        /// project default font IS the game's font — so a label built with no font wired comes out in
        /// Baloo2 like every authored screen, and one built the old way comes out in Arial.
        /// </summary>
        private static void Label(Transform parent, string name, string content, float size,
                                 Vector2 aMin, Vector2 aMax, Color colour, bool outlined = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = colour;
            text.fontStyle = FontStyles.Bold;
            if (outlined && text.font != null)
            {
                text.outlineColor = new Color32(5, 20, 48, 235);
                text.outlineWidth = 0.24f;
            }
            text.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        private void Progress(float t)
        {
            if (_fill == null) return;
            _fill.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
        }

        /// <summary>
        /// Fade in, read the scene behind the fade, hold long enough to be seen, swap, fade out, go away.
        ///
        /// Unscaled time throughout: a swap has to work from a paused game, and the market pauses nothing
        /// but the island's popups do.
        /// </summary>
        private IEnumerator Run(string sceneName)
        {
            float curtainShownAt = Time.unscaledTime;
            // Paint the curtain before asking Unity to read anything. Starting LoadSceneAsync in the
            // first coroutine slice can spend a long frame opening bundles before the canvas has ever
            // reached the GPU, which looks exactly like a frozen button and a frozen empty bar.
            for (float t = 0f; t < fadeInSeconds; t += Time.unscaledDeltaTime)
            {
                _group.alpha = fadeInSeconds > 0f ? t / fadeInSeconds : 1f;
                Progress((fadeInSeconds > 0f ? t / fadeInSeconds : 1f) * 0.06f);
                yield return null;
            }
            _group.alpha = 1f;
            Progress(0.06f);
            yield return new WaitForEndOfFrame();

            ThreadPriority previousPriority = Application.backgroundLoadingPriority;
            // Low priority gives the transition canvas a frame between loading slices on mobile. The
            // operation can take a little longer in wall-clock time, but it no longer locks animation.
            Application.backgroundLoadingPriority = ThreadPriority.Low;

            bool restoringParkedIsland = HasParkedIsland(sceneName);
            if (restoringParkedIsland)
                yield return RestoreParkedIsland();
            else
                yield return LoadScene(sceneName, sceneName == MarketScene);

            Application.backgroundLoadingPriority = previousPriority;

            // The cached island can wake almost instantly. Keep the requested two-second transition so
            // the return still reads as a deliberate journey rather than a single-frame flash.
            if (restoringParkedIsland)
            {
                while (Time.unscaledTime - curtainShownAt < returnScreenSeconds)
                {
                    float elapsed = Time.unscaledTime - curtainShownAt;
                    float t = returnScreenSeconds > 0f ? elapsed / returnScreenSeconds : 1f;
                    Progress(Mathf.Lerp(0.72f, 0.99f, Mathf.Clamp01(t)));
                    yield return null;
                }
            }
            Progress(1f);

            // One more frame so the destination's enabled objects and canvases have completed their
            // first layout before the curtain exposes them.
            yield return null;

            for (float t = 0f; t < fadeOutSeconds; t += Time.unscaledDeltaTime)
            {
                _group.alpha = 1f - (fadeOutSeconds > 0f ? t / fadeOutSeconds : 1f);
                yield return null;
            }

            _live = null;
            Destroy(gameObject);
        }

        private IEnumerator LoadScene(string sceneName, bool parkCurrentIsland)
        {
            Scene source = SceneManager.GetActiveScene();
            LoadSceneMode mode = parkCurrentIsland ? LoadSceneMode.Additive : LoadSceneMode.Single;
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, mode);
            if (op == null) yield break;
            op.allowSceneActivation = false;

            // Unity parks a loaded-but-not-activated scene at 0.9 and never reaches 1 until activation is
            // allowed, so waiting on isDone here would deadlock. Same trap the boot splash documents.
            float held = 0f;
            while (op.progress < 0.9f || held < holdSeconds)
            {
                held += Time.unscaledDeltaTime;
                // Whichever of the two is further behind, so the bar arrives full exactly as the loop lets
                // go. Raw progress alone would slam to the end on the first frame and sit there.
                float ready = Mathf.Min(op.progress / 0.9f,
                                        holdSeconds > 0f ? held / holdSeconds : 1f);
                Progress(Mathf.Lerp(0.06f, 0.93f, ready));
                yield return null;
            }
            Progress(0.94f);

            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;

            if (parkCurrentIsland)
            {
                Scene destination = SceneManager.GetSceneByName(sceneName);
                if (destination.IsValid() && destination.isLoaded)
                    SceneManager.SetActiveScene(destination);
                ParkIsland(source);
            }
        }

        private static void ParkIsland(Scene scene)
        {
            ParkedIslandRoots.Clear();
            if (!scene.IsValid() || !scene.isLoaded) return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                // The market scene builds its UI while Main's EventSystem still exists, so it correctly
                // decides that a second one is unnecessary. Parking this standalone root afterwards
                // would then leave the market with no touch, button or movement input at all.
                if (roots[i].GetComponent<EventSystem>() != null) continue;

                // Remember only roots that were live. The seven inactive island roots must stay
                // inactive when Main wakes again.
                if (!roots[i].activeSelf) continue;
                ParkedIslandRoots.Add(roots[i]);
                roots[i].SetActive(false);
            }
            _parkedIslandScene = scene;
        }

        private static bool HasParkedIsland(string sceneName)
        {
            if (_parkedIslandScene.IsValid() && _parkedIslandScene.isLoaded &&
                _parkedIslandScene.name == sceneName && ParkedIslandRoots.Count > 0)
                return true;

            // Domain reloads and interrupted play sessions can leave static references behind.
            _parkedIslandScene = default;
            ParkedIslandRoots.Clear();
            return false;
        }

        private IEnumerator RestoreParkedIsland()
        {
            Scene market = SceneManager.GetActiveScene();
            Scene island = _parkedIslandScene;

            SceneManager.SetActiveScene(island);
            for (int i = 0; i < ParkedIslandRoots.Count; i++)
                if (ParkedIslandRoots[i] != null) ParkedIslandRoots[i].SetActive(true);

            _parkedIslandScene = default;
            ParkedIslandRoots.Clear();
            Progress(0.72f);
            yield return null;

            // Market is intentionally lightweight. Unloading it releases its generated yard without
            // touching the already-built island operation underneath the curtain.
            if (market.IsValid() && market.isLoaded && market != island)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(market);
                if (unload != null)
                {
                    while (!unload.isDone)
                    {
                        Progress(Mathf.Lerp(0.72f, 0.98f, unload.progress));
                        yield return null;
                    }
                }
            }
        }

        /// <summary>A hot reload or a second Bootstrap must not leave the static handle pointing at a
        /// curtain that no longer exists — nothing would ever be able to cover a swap again.</summary>
        private void OnDestroy()
        {
            if (_live == this) _live = null;
        }
    }
}
