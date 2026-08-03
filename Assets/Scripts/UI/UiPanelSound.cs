using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// One panel's whole sound: a click under every button it contains, and a whoosh when it opens and
    /// closes. Carrying it means a screen needs no other audio wiring at all.
    ///
    /// Buttons are hooked in <see cref="OnEnable"/>, which is what makes cards work. The screens here
    /// build their rows once and then switch the panel on; hooking at open therefore catches whatever was
    /// built since, with no per-frame scan and no rebuild plumbing. Buttons already hooked are remembered,
    /// so opening a panel twice does not stack a second listener on them.
    ///
    /// <b>Attach it AFTER the panel has been switched off.</b> Every screen in this project ends its Start
    /// with <c>panelRoot.SetActive(false)</c>; adding the component to an object that is still active runs
    /// OnEnable immediately and the player hears a panel open and close during boot. Added to an inactive
    /// object, the first sound it makes is the first time the player actually opens the thing.
    ///
    /// Always-on surfaces — the HUD, the map badges — take <see cref="SoundId.None"/> for open and close
    /// and keep only the button click.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiPanelSound : MonoBehaviour
    {
        [SerializeField] private SoundId buttonSound = SoundId.Tap;
        [SerializeField] private SoundId openSound = SoundId.PanelOpen;
        [SerializeField] private SoundId closeSound = SoundId.PanelClose;

        private readonly HashSet<Button> _hooked = new HashSet<Button>();
        private UnityEngine.Events.UnityAction _click;
        private AudioService _audio;
        private HapticService _haptic;

        /// <summary>Adds the hook to a panel root if it has none.</summary>
        public static void Attach(GameObject root)
        {
            Add(root, SoundId.PanelOpen, SoundId.PanelClose);
        }

        /// <summary>For surfaces that are never opened or closed — buttons click, nothing whooshes.</summary>
        public static void AttachButtonsOnly(GameObject root)
        {
            Add(root, SoundId.None, SoundId.None);
        }

        private static void Add(GameObject root, SoundId open, SoundId close)
        {
            if (root == null || root.GetComponent<UiPanelSound>() != null) return;
            var s = root.AddComponent<UiPanelSound>();
            s.openSound = open;
            s.closeSound = close;
        }

        private void OnEnable()
        {
            if (_click == null) _click = OnButton;

            // Panel açılışında bir kez, kare başına değil.
            var found = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < found.Length; i++)
                if (_hooked.Add(found[i]))
                    found[i].onClick.AddListener(_click);

            Play(openSound);
        }

        private void OnDisable()
        {
            // Sahne yıkılırken de OnDisable gelir. Prestij sahneyi yeniden yüklüyor, yani buradan
            // korumasız çıkmak her panelin kapanış sesini aynı anda patlatır — ve o sırada ses
            // sunucusu çoktan yok edilmiş olduğu için AudioService onu yeniden kurmaya kalkar,
            // yani yıkım sırasında yeni GameObject doğar. Unity bunu hata olarak bildiriyor.
            if (_quitting || !gameObject.scene.isLoaded) return;
            Play(closeSound);
        }

        private void OnApplicationQuit() => _quitting = true;

        private static bool _quitting;

        private void OnButton()
        {
            Play(buttonSound);
            Bind();
            if (_haptic != null) _haptic.Light();
        }

        private void Play(SoundId id)
        {
            if (id == SoundId.None) return;
            Bind();
            if (_audio != null) _audio.Play(id);
        }

        private void Bind()
        {
            if (_audio == null) _audio = ServiceLocator.Get<AudioService>();
            if (_haptic == null) _haptic = ServiceLocator.Get<HapticService>();
        }
    }
}
