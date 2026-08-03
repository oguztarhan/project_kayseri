using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Puts a translated line into the label it sits on, and puts it there again when the language
    /// changes. This is for text the player reads but code never touches — titles, button captions,
    /// offer copy. A label whose content is computed (a price, a countdown, a day number) must NOT carry
    /// this: the two would overwrite each other and which one won would depend on frame order. Those go
    /// through <see cref="Loc"/> at the point the code builds the string instead.
    ///
    /// Works with both TMP and legacy uGUI text, because the editor-authored screens use TMP and the
    /// runtime-built ones use <see cref="UiBuild"/>'s legacy labels.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalizedText : MonoBehaviour
    {
        [Tooltip("Resources/Diller/metinler.txt içindeki satırın anahtarı.")]
        [SerializeField] private string key;

        private TMP_Text _tmp;
        private UnityEngine.UI.Text _legacy;
        private LocalizationService _loc;

        /// <summary>Retarget at another line — the store's cloned cells do this as they are built.</summary>
        public void SetKey(string newKey)
        {
            key = newKey;
            Apply();
        }

        private void Awake()
        {
            _tmp = GetComponent<TMP_Text>();
            if (_tmp == null) _legacy = GetComponent<UnityEngine.UI.Text>();
        }

        private void OnEnable()
        {
            if (_loc == null) _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += Apply;
            Apply();
        }

        private void OnDisable()
        {
            if (_loc != null) _loc.Changed -= Apply;
        }

        private void Apply()
        {
            if (string.IsNullOrEmpty(key)) return;
            string v = Loc.T(key);
            if (_tmp != null) _tmp.text = v;
            else if (_legacy != null) _legacy.text = v;
        }
    }
}
