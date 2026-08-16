using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Names every building on the island, on a sign that reads as a painted board by day and a lit
    /// one after dark.
    ///
    /// The buildings are the game's own, not a guess at the art: <see cref="CoalOperation"/> knows
    /// which of its stations own a structure — the mine, the depot, the refinery, the market and the
    /// power plant, as against the train and the two truck fleets, which own no building and whose
    /// sign would hang over open grass. The name comes from the localisation table, so the sign says
    /// what the upgrade screen's title says in whatever language is set.
    ///
    /// One screen-space canvas holds every sign rather than a world-space canvas each. That keeps
    /// them to one batch, and it keeps them legible: pinned to the world they would shrink to
    /// nothing at the far end of the zoom range, which is exactly when the player is looking at the
    /// whole island trying to work out what is where. Same reasoning, and the same shape, as
    /// <see cref="UpgradeReadyMarkers"/> — and the sign deliberately sits UNDER that badge's
    /// sorting order, because when both are up the thing worth tapping should be on top.
    ///
    /// Day and night are one lerp on <c>_KayseriNight</c>, the global <see cref="DayNightCycle"/>
    /// already writes for the shaders. By day the board is pale with dark lettering, the way a sign
    /// looks in daylight; by night it goes dark and the lettering lights up.
    /// </summary>
    public sealed class BuildingSigns : MonoBehaviour
    {
        [Header("Yazı")]
        [Tooltip("Tabelanın yazı tipi. Baloo2-ExtraBold SDF.")]
        [SerializeField] private TMP_FontAsset _font;
        [Tooltip("Yazı boyu, referans çözünürlükte piksel.")]
        [SerializeField] private float _fontSize = 30f;
        [Tooltip("Tabelanın binanın tepesinden ne kadar yukarıda duracağı, dünya birimi.")]
        [SerializeField] private float _worldLift = 2f;
        // The upgrade badge hovers over the same buildings, off the same anchor. Dropping the sign
        // a fixed distance on screen keeps the two apart at every zoom level, which a world-space
        // offset would not: at the far end of the zoom they would converge into one another.
        [Tooltip("Tabelanın yükseltme rozetinin altında kalması için ekranda kaydırılacağı piksel.")]
        [SerializeField] private float _screenDrop = 124f;
        [Tooltip("HUD 100, yükseltme rozetleri 92. Tabela rozetin altında durmalı.")]
        [SerializeField] private int _sortingOrder = 88;

        [Header("Gündüz")]
        [SerializeField] private Color _dayPlate = new Color(0.97f, 0.95f, 0.90f, 0.94f);
        [SerializeField] private Color _dayText = new Color(0.16f, 0.15f, 0.19f, 1f);

        [Header("Gece")]
        [SerializeField] private Color _nightPlate = new Color(0.09f, 0.08f, 0.12f, 0.92f);
        [Tooltip("Gece yanan yazı rengi. Sokak lambalarıyla aynı sarıya yakın durmalı.")]
        [SerializeField] private Color _nightText = new Color(1f, 0.83f, 0.50f, 1f);

        [Header("Modern tabela")]
        [SerializeField] private bool _modernStyle = true;
        [SerializeField] private Color _borderColor = new Color(1f, 0.66f, 0.12f, 1f);

        [Header("Bağlanma")]
        [Tooltip("Hangi operasyonun canlı olduğuna bu sıklıkta bakılır.")]
        [SerializeField] private float _rebindSeconds = 0.5f;

        private static readonly int NightId = Shader.PropertyToID("_KayseriNight");

        private Camera _camera;
        private CoalOperation _operation;
        private RectTransform _canvas;

        private RectTransform[] _signs;
        private Image[] _plates;
        private TextMeshProUGUI[] _labels;
        private int[] _stations;
        private int _count;

        private float _rebindIn;
        private float _appliedNight = -1f;

        private void Awake()
        {
            _camera = Camera.main;

            if (_modernStyle)
            {
                _dayPlate = new Color(0.055f, 0.105f, 0.20f, 0.96f);
                _dayText = new Color(0.98f, 0.98f, 1f, 1f);
                _nightPlate = new Color(0.025f, 0.055f, 0.12f, 0.97f);
                _nightText = new Color(1f, 0.82f, 0.38f, 1f);
            }

            var go = new GameObject("BinaTabelalariKanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = _sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvas = (RectTransform)go.transform;
        }

        private void Update()
        {
            Rebind();
            if (_count == 0) return;

            Paint(Shader.GetGlobalFloat(NightId));
            Place();
        }

        /// <summary>Travelling to another island enables a different <see cref="CoalOperation"/>, so
        /// the binding is re-checked on a slow timer rather than taken once.</summary>
        private void Rebind()
        {
            _rebindIn -= Time.unscaledDeltaTime;
            if (_rebindIn > 0f) return;
            _rebindIn = Mathf.Max(0.1f, _rebindSeconds);

            if (_camera == null) _camera = Camera.main;
            if (_operation != null && _operation.enabled) return;

            CoalOperation live = null;
            foreach (var candidate in FindObjectsByType<CoalOperation>(FindObjectsInactive.Exclude))
                if (candidate.enabled) { live = candidate; break; }

            if (live == null || live == _operation) return;
            _operation = live;
            Build();
        }

        private void Build()
        {
            if (_signs != null)
                for (int i = 0; i < _signs.Length; i++)
                    if (_signs[i] != null) Destroy(_signs[i].gameObject);

            int total = _operation.StationCount;
            _signs = new RectTransform[total];
            _plates = new Image[total];
            _labels = new TextMeshProUGUI[total];
            _stations = new int[total];
            _count = 0;

            for (int station = 0; station < total; station++)
            {
                if (!_operation.StationHasBody(station)) continue;
                _stations[_count] = station;
                _signs[_count] = BuildSign(station, out _plates[_count], out _labels[_count]);
                _count++;
            }

            _appliedNight = -1f;
        }

        private RectTransform BuildSign(int station, out Image plate, out TextMeshProUGUI label)
        {
            var go = new GameObject("Tabela_" + _operation.StationName(station),
                                    typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_canvas, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            plate = go.GetComponent<Image>();
            plate.sprite = UiSkin.Pill;
            plate.type = Image.Type.Sliced;
            plate.color = _borderColor;
            plate.raycastTarget = false;

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.01f, 0.025f, 0.07f, 0.75f);
            shadow.effectDistance = new Vector2(0f, -5f);
            shadow.useGraphicAlpha = true;

            var innerGo = new GameObject("IcZemin", typeof(RectTransform), typeof(Image));
            var innerRect = (RectTransform)innerGo.transform;
            innerRect.SetParent(rect, false);
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(3f, 3f);
            innerRect.offsetMax = new Vector2(-3f, -3f);
            plate = innerGo.GetComponent<Image>();
            plate.sprite = UiSkin.Pill;
            plate.type = Image.Type.Sliced;
            plate.raycastTarget = false;

            var textGo = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)textGo.transform;
            textRect.SetParent(innerRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 7f);
            textRect.offsetMax = new Vector2(-18f, -7f);

            label = textGo.GetComponent<TextMeshProUGUI>();
            if (_font != null) label.font = _font;
            label.fontSize = _fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Max(20f, _fontSize * 0.72f);
            label.fontSizeMax = _fontSize;
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
            label.text = Loc.Id("istasyon", _operation.StationName(station));

            // The plate is sized to the text rather than to a number typed in here: the names are
            // localised, and "POWER PLANT" and its Turkish are not the same width.
            label.ForceMeshUpdate();
            Vector2 text = label.GetRenderedValues(false);
            rect.sizeDelta = new Vector2(text.x + 44f, Mathf.Max(52f, text.y + 22f));

            return rect;
        }

        /// <summary>Day to night. Only touched when the value actually moves — fifty-five minutes of
        /// every hour this is one float compare.</summary>
        private void Paint(float night)
        {
            if (Mathf.Abs(night - _appliedNight) < 0.002f) return;
            _appliedNight = night;

            var plate = Color.Lerp(_dayPlate, _nightPlate, night);
            var text = Color.Lerp(_dayText, _nightText, night);

            for (int i = 0; i < _count; i++)
            {
                if (_plates[i] != null) _plates[i].color = plate;
                if (_labels[i] == null) continue;
                _labels[i].color = text;
                // A lit sign is not just a brighter colour, it spills. The glow rides the night
                // value so the lettering comes up with the lamps rather than snapping on at dusk.
                _labels[i].fontMaterial.SetColor(ShaderUtilities.ID_GlowColor, text);
                _labels[i].fontMaterial.SetFloat(ShaderUtilities.ID_GlowPower, night * 0.7f);
                _labels[i].fontMaterial.SetFloat(ShaderUtilities.ID_GlowOuter, night * 0.35f);
            }
        }

        private void Place()
        {
            if (_camera == null) return;

            for (int i = 0; i < _count; i++)
            {
                var sign = _signs[i];
                if (sign == null) continue;

                if (!Anchor(_stations[i], out Vector3 world))
                {
                    if (sign.gameObject.activeSelf) sign.gameObject.SetActive(false);
                    continue;
                }

                Vector3 screen = _camera.WorldToScreenPoint(world + Vector3.up * _worldLift);
                bool visible = screen.z > 0f;
                if (sign.gameObject.activeSelf != visible) sign.gameObject.SetActive(visible);
                if (!visible) continue;

                sign.anchoredPosition = new Vector2(
                    (screen.x / Screen.width - 0.5f) * _canvas.rect.width,
                    (screen.y / Screen.height - 0.5f) * _canvas.rect.height - _screenDrop);
            }
        }

        /// <summary>
        /// The same point the upgrade badge hangs off — the top of the station's own silhouette —
        /// so that the screen drop above is measured against something the badge is measured against
        /// too. Hung off the district box instead, the two are no longer comparable: the refinery's
        /// district reaches well above the refinery, the sign starts that much higher, and the drop
        /// lands it straight back on the badge it was supposed to clear.
        /// </summary>
        private bool Anchor(int station, out Vector3 world)
        {
            if (_operation.StationAnchor(station, out world)) return true;
            if (!_operation.StationFocus(station, out Bounds area)) return false;
            world = new Vector3(area.center.x, area.max.y, area.center.z);
            return true;
        }
    }
}
