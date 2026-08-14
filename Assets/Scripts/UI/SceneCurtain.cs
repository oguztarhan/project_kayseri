using System.Collections;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        [Tooltip("HUD 100, dünya haritası 150. Perde hepsinin üstünde olmalı.")]
        [SerializeField] private int sortingOrder = 400;

        private static SceneCurtain _live;

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
            UiBuild.Flat(canvas, "Zemin", new Color(0.045f, 0.055f, 0.08f, 1f), Vector2.zero, Vector2.one);
            // A wash of where you are going, so the two directions do not look like the same screen.
            UiBuild.Flat(canvas, "Vurgu", new Color(accent.r, accent.g, accent.b, 0.10f),
                         new Vector2(0f, 0f), new Vector2(1f, 0.42f));

            Label(canvas, "Baslik", caption, 74f, new Vector2(0.08f, 0.5f), new Vector2(0.92f, 0.62f),
                  new Color(1f, 1f, 1f, 0.96f));
            Label(canvas, "Durum", Loc.T("ortak.yukleniyor"), 40f,
                  new Vector2(0.08f, 0.44f), new Vector2(0.92f, 0.5f), new Color(1f, 1f, 1f, 0.55f));

            UiBuild.Bar(canvas, "Cubuk", new Color(1f, 1f, 1f, 0.10f),
                        new Color(accent.r, accent.g, accent.b, 0.95f),
                        new Vector2(0.08f, 0.085f), new Vector2(0.92f, 0.105f), out _fill);
            Progress(0f);
        }

        /// <summary>
        /// A centred line. TMP rather than <see cref="UiBuild.Label"/>'s legacy Text, because TMP's
        /// project default font IS the game's font — so a label built with no font wired comes out in
        /// Baloo2 like every authored screen, and one built the old way comes out in Arial.
        /// </summary>
        private static void Label(Transform parent, string name, string content, float size,
                                 Vector2 aMin, Vector2 aMax, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = colour;
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
            // Started before the fade rather than after it, so the read and the fade overlap. Sequencing
            // them would add the whole fade to every swap for no reason.
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            op.allowSceneActivation = false;

            for (float t = 0f; t < fadeInSeconds; t += Time.unscaledDeltaTime)
            {
                _group.alpha = fadeInSeconds > 0f ? t / fadeInSeconds : 1f;
                Progress(op.progress / 0.9f * 0.35f);
                yield return null;
            }
            _group.alpha = 1f;

            // Unity parks a loaded-but-not-activated scene at 0.9 and never reaches 1 until activation is
            // allowed, so waiting on isDone here would deadlock. Same trap the boot splash documents.
            float held = 0f;
            while (op.progress < 0.9f || held < holdSeconds)
            {
                held += Time.unscaledDeltaTime;
                // Whichever of the two is further behind, so the bar arrives full exactly as the loop lets
                // go. Raw progress alone would slam to the end on the first frame and sit there.
                Progress(Mathf.Min(op.progress / 0.9f, holdSeconds > 0f ? held / holdSeconds : 1f));
                yield return null;
            }
            Progress(1f);

            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;

            // One more frame so the new scene's Start methods have run and its canvases are laid out.
            // Opening onto a half-built HUD looks worse than never having covered the swap.
            yield return null;

            for (float t = 0f; t < fadeOutSeconds; t += Time.unscaledDeltaTime)
            {
                _group.alpha = 1f - (fadeOutSeconds > 0f ? t / fadeOutSeconds : 1f);
                yield return null;
            }

            _live = null;
            Destroy(gameObject);
        }

        /// <summary>A hot reload or a second Bootstrap must not leave the static handle pointing at a
        /// curtain that no longer exists — nothing would ever be able to cover a swap again.</summary>
        private void OnDestroy()
        {
            if (_live == this) _live = null;
        }
    }
}
