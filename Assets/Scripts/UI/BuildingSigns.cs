using Game.Core;
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
        [Header("Görünürlük")]
        [Tooltip("Dikey tersanedeki beş oyuncu istasyonunu ekran üstünde adlandırır.")]
        [SerializeField] private bool showBuildingNames = true;
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
        [SerializeField] private float _screenDrop = 72f;
        [Tooltip("HUD 100. Tabelalar harita rozetlerinin üstünde, sabit HUD'nin altında kalır.")]
        [SerializeField] private int _sortingOrder = 99;

        // The drop above is measured down the screen, so a station low in frame lands under the
        // bottom HUD row and a station at the edge slides under the left and right icon rails.
        // These keep every sign inside the band the HUD leaves free, whatever the camera is doing.
        [Header("HUD payı")]
        [Tooltip("Üstteki para/elmas satırının kapladığı ekran oranı.")]
        [SerializeField] private float _hudTopFraction = 0.10f;
        [Tooltip("Alttaki buton satırının kapladığı ekran oranı.")]
        [SerializeField] private float _hudBottomFraction = 0.20f;
        [Tooltip("Sol ve sağdaki ikon raylarının kapladığı ekran oranı.")]
        [SerializeField] private float _hudSideFraction = 0.09f;

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
        [Tooltip("Tabelanın zemini — MaviSet/panel_beyaz. Doluyken tabela oyunun geri kalanıyla " +
                 "aynı beyaz panele oturuyor ve gece/gündüz boyaması devre dışı kalıyor: beyaz " +
                 "panelin üstünde gece yanan sarı yazı okunmuyor.")]
        [SerializeField] private Sprite _plateArt;
        [Tooltip("Beyaz panelin üstündeki yazı rengi.")]
        [SerializeField] private Color _plateText = new Color(0.09f, 0.14f, 0.24f, 1f);

        // Tabelalar UZAKTAN işe yarar: hangi binanın ne olduğunu söylerler. Yakın planda bina zaten
        // tanınıyor ve her çatının üstünde ayrıca yükseltme rozeti ile tamir anahtarı duruyor — üç
        // katman üst üste binince ada okunmaz oluyor. Kamera içeri girdikçe tabelalar siliniyor,
        // yerlerini binanın yanında duran ustanın kendisi alıyor.
        // Ölçüm: açılış karesi bu bandın ALT çeyreğinde oturuyor — kamera mesafesi
        // groundToCentre + 0.8925*defaultZoomFraction*survey, yani ZoomT sahnedeki 0.21 ile ~0.26,
        // yeni 0.14 ile ~0.12. Band bilerek ikisinin de üstünde başlıyor: açılışta tabela YOK,
        // oyuncu ada geneline bakmak için geri çekildiğinde geliyor. Bandı 0.26'nın altına
        // indirirseniz tabelalar varsayılan karede geri gelir ve temiz görünüm bozulur.
        [Header("Yakınlaşınca sönme")]
        [Tooltip("Bu yakınlık oranının altında tabelalar tamamen görünmez (0 = tam yakın, 1 = tam uzak).")]
        [SerializeField] private float _fadeInStartT = 0f;
        [Tooltip("Bu oranın üstünde tamamen görünür.")]
        [SerializeField] private float _fadeInEndT = 0.08f;

        [Header("Bağlanma")]
        [Tooltip("Hangi operasyonun canlı olduğuna bu sıklıkta bakılır.")]
        [SerializeField] private float _rebindSeconds = 0.5f;

        private static readonly int NightId = Shader.PropertyToID("_KayseriNight");

        private Camera _camera;
        private CameraController _rig;
        private CanvasGroup _fade;
        private CoalOperation _operation;
        private RectTransform _canvas;

        private RectTransform[] _signs;
        private Image[] _plates;
        private TextMeshProUGUI[] _labels;
        private int[] _stations;
        private int _count;

        private float _rebindIn;
        private float _appliedNight = -1f;
        private LocalizationService _loc;

        private void Awake()
        {
            if (!showBuildingNames)
            {
                enabled = false;
                return;
            }
            _camera = Camera.main;

            if (_modernStyle)
            {
                _dayPlate = new Color(0.055f, 0.105f, 0.20f, 0.96f);
                _dayText = new Color(0.98f, 0.98f, 1f, 1f);
                _nightPlate = new Color(0.025f, 0.055f, 0.12f, 0.97f);
                _nightText = new Color(1f, 0.82f, 0.38f, 1f);
            }

            var go = new GameObject("BinaTabelalariKanvas", typeof(Canvas), typeof(CanvasScaler),
                                    typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = _sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvas = (RectTransform)go.transform;
            _fade = go.GetComponent<CanvasGroup>();
            // Signs never take a tap; without this a faded-out sign would still swallow one.
            _fade.blocksRaycasts = false;
            _fade.interactable = false;

            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
        }

        private void OnDestroy()
        {
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        /// <summary>
        /// Tabela adları yerelleştirilmiş ve plaka yazının genişliğine göre ölçülüyor, yani metni
        /// yerinde değiştirmek yetmez — levhanın yeniden ölçülmesi gerekir. Bağlantıyı düşürmek
        /// <see cref="Rebind"/>'in ada değiştiğinde zaten kullandığı yolu tetikler ve <see cref="Build"/>
        /// her şeyi yeni dille kurar. Gecikme bir saniyeye kadar; dil değişimi nadir bir eylem.
        /// </summary>
        private void OnLanguageChanged()
        {
            _operation = null;
            _rebindIn = 0f;
        }

        private void Update()
        {
            Rebind();
            if (_count == 0) return;

            FadeWithZoom();
            if (_fade != null && _fade.alpha <= 0.001f) return;   // invisible: nothing to lay out

            Paint(Shader.GetGlobalFloat(NightId));
            Place();
        }

        /// <summary>
        /// Fades the whole board in as the camera pulls back. No rig to ask (the market and sea scenes
        /// have their own cameras) means full opacity — never a silent blank board.
        /// </summary>
        private void FadeWithZoom()
        {
            if (_fade == null) return;
            if (_rig == null) { _fade.alpha = 1f; return; }

            float t = Mathf.InverseLerp(_fadeInStartT, _fadeInEndT, _rig.ZoomT);
            _fade.alpha = Mathf.Clamp01(t);
        }

        /// <summary>Travelling to another island enables a different <see cref="CoalOperation"/>, so
        /// the binding is re-checked on a slow timer rather than taken once.</summary>
        private void Rebind()
        {
            _rebindIn -= Time.unscaledDeltaTime;
            if (_rebindIn > 0f) return;
            _rebindIn = Mathf.Max(0.1f, _rebindSeconds);

            if (_camera == null) _camera = Camera.main;
            // The pan/pinch rig lives on the island camera itself. Re-read alongside the camera so a
            // scene swap picks up the new one rather than fading against a destroyed rig.
            if (_camera != null && _rig == null) _rig = _camera.GetComponent<CameraController>();
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
            plate.sprite = _plateArt != null ? _plateArt : UiSkin.Pill;
            plate.type = Image.Type.Sliced;
            plate.color = _plateArt != null ? Color.white : _borderColor;
            plate.raycastTarget = false;
            if (_plateArt != null)
            {
                // panel_beyaz'ın köşe payı 44; 52 boyundaki bir tabelaya ham hâliyle basılınca
                // üst ve alt dilim üst üste biniyor ve kenar iki kat çiziliyor.
                float border = Mathf.Max(_plateArt.border.y, _plateArt.border.w);
                if (border > 0f) plate.pixelsPerUnitMultiplier = Mathf.Max(1f, border / 16f);
            }

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
            // Beyaz panel tek parça: çerçeve + iç zemin iki katmanken panelin kendi kenarı
            // ikinci bir kenarın altında kalıyordu. İç zemin kapanıyor, boyayacak bir şey de
            // kalmıyor — Paint() beyaz tabelaya hiç dokunmuyor.
            if (_plateArt != null) plate.enabled = false;

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
            label.text = PlayerStationTitle(station);

            // The plate is sized to the text rather than to a number typed in here: the names are
            // localised, and "POWER PLANT" and its Turkish are not the same width.
            label.ForceMeshUpdate();
            Vector2 text = label.GetRenderedValues(false);
            // Beyaz panelin kenarında kendi yumuşak halesi var; 44'lük payla yazı o halenin
            // üstüne oturuyor. Panelli tabela her yönden daha geniş bir pay istiyor — ama 86/46
            // fazlaydı: adaya bakarken tabelalar binaların kendisi kadar yer kaplıyordu.
            rect.sizeDelta = _plateArt != null
                ? new Vector2(text.x + 58f, Mathf.Max(62f, text.y + 30f))
                : new Vector2(text.x + 44f, Mathf.Max(52f, text.y + 22f));

            return rect;
        }

        private static string PlayerStationTitle(int station)
        {
            switch (station)
            {
                case IslandEconomy.Mine: return Loc.T("shipyard.mine");
                case IslandEconomy.Storage: return Loc.T("shipyard.deposit");
                case IslandEconomy.Smelter: return Loc.T("shipyard.refinery");
                case IslandEconomy.Market: return Loc.T("shipyard.market");
                case IslandEconomy.Power: return Loc.T("shipyard.port");
                default: return "";
            }
        }

        /// <summary>Day to night. Only touched when the value actually moves — fifty-five minutes of
        /// every hour this is one float compare.</summary>
        private void Paint(float night)
        {
            if (Mathf.Abs(night - _appliedNight) < 0.002f) return;
            _appliedNight = night;

            var plate = Color.Lerp(_dayPlate, _nightPlate, night);
            var text = _plateArt != null ? _plateText : Color.Lerp(_dayText, _nightText, night);
            // Beyaz panelin üstünde gece lambası yakmıyoruz: sıcak sarı bir yazı beyaz kâğıtta
            // okunmuyor, glow da panelin kenarına bulaşıyor.
            float glow = _plateArt != null ? 0f : night;

            for (int i = 0; i < _count; i++)
            {
                if (_plates[i] != null && _plates[i].enabled) _plates[i].color = plate;
                if (_labels[i] == null) continue;
                _labels[i].color = text;
                // A lit sign is not just a brighter colour, it spills. The glow rides the night
                // value so the lettering comes up with the lamps rather than snapping on at dusk.
                _labels[i].fontMaterial.SetColor(ShaderUtilities.ID_GlowColor, text);
                _labels[i].fontMaterial.SetFloat(ShaderUtilities.ID_GlowPower, glow * 0.7f);
                _labels[i].fontMaterial.SetFloat(ShaderUtilities.ID_GlowOuter, glow * 0.35f);
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

                float w = _canvas.rect.width, h = _canvas.rect.height;
                float halfW = sign.sizeDelta.x * 0.5f, halfH = sign.sizeDelta.y * 0.5f;
                float x = (screen.x / Screen.width - 0.5f) * w;
                float y = (screen.y / Screen.height - 0.5f) * h - _screenDrop;

                float side = w * _hudSideFraction;
                float loX = -w * 0.5f + side + halfW, hiX = w * 0.5f - side - halfW;
                float loY = -h * 0.5f + h * _hudBottomFraction + halfH;
                float hiY = h * 0.5f - h * _hudTopFraction - halfH;
                if (loX < hiX) x = x < loX ? loX : (x > hiX ? hiX : x);
                if (loY < hiY) y = y < loY ? loY : (y > hiY ? hiY : y);

                // Deposit, Market and Port all meet at the harbour end of this portrait map. When
                // their anchors touch the bottom-safe edge, fan them around the contextual market
                // and sail actions instead of hiding their names underneath those buttons.
                if (y <= loY + 1f)
                {
                    if (_stations[i] == IslandEconomy.Storage)
                        x = Mathf.Min(hiX, x + halfW + 72f);
                    else if (_stations[i] == IslandEconomy.Market)
                        y = Mathf.Min(hiY, y + sign.sizeDelta.y + 16f);
                }

                sign.anchoredPosition = new Vector2(x, y);
            }

            SeparateOverlappingSigns();
        }

        /// <summary>
        /// Several lower-terrace anchors can reach the HUD-safe edge at the same time. Keep their
        /// labels in a small readable stack instead of drawing Deposit and Market on top of one
        /// another. Positions are rebuilt from their world anchors every frame, so this pass is
        /// deterministic and cannot accumulate drift while the camera moves.
        /// </summary>
        private void SeparateOverlappingSigns()
        {
            if (_canvas == null) return;

            float h = _canvas.rect.height;
            for (int i = 0; i < _count; i++)
            {
                RectTransform current = _signs[i];
                if (current == null || !current.gameObject.activeSelf) continue;

                Vector2 p = current.anchoredPosition;
                for (int j = 0; j < i; j++)
                {
                    RectTransform previous = _signs[j];
                    if (previous == null || !previous.gameObject.activeSelf) continue;

                    Vector2 q = previous.anchoredPosition;
                    float xGap = (current.sizeDelta.x + previous.sizeDelta.x) * 0.5f + 10f;
                    float yGap = (current.sizeDelta.y + previous.sizeDelta.y) * 0.5f + 10f;
                    if (Mathf.Abs(p.x - q.x) >= xGap || Mathf.Abs(p.y - q.y) >= yGap) continue;
                    p.y = q.y + yGap;
                }

                float halfH = current.sizeDelta.y * 0.5f;
                float hiY = h * 0.5f - h * _hudTopFraction - halfH;
                p.y = Mathf.Min(p.y, hiY);
                current.anchoredPosition = p;
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
