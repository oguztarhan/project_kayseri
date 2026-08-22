using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    /// <summary>
    /// Turns the island from midday into night and back on a 45/15 minute cycle.
    ///
    /// There are no extra lights in this. <c>Kayseri/IslandVertexLit</c> is a main-light-only toon
    /// shader — it ignores additional lights on purpose, so the street lamps on a map cannot be
    /// point lights; they would cost a fortune and illuminate nothing. Night is instead a handful of
    /// numbers moved together: the sun swings low and orange through a real sunset, then settles
    /// dim, cool and higher as moonlight; ambient and reflections fade down tinted toward the
    /// moon's blue; and the emission already authored into <c>lamp_glow</c>, <c>winlight</c>,
    /// <c>headlight</c> and <c>taillight</c> switches on across the middle of dusk through a shader
    /// global, the way a town's lights actually come on. The bloom in the scene's volume does the
    /// rest.
    ///
    /// Everything daytime is read off the scene in <see cref="Awake"/> rather than typed in here, so
    /// dropping this on any island — or on Main, which lights itself differently — costs nothing to
    /// set up and cannot drift out of step with the art. Only the night end of each pair is authored.
    ///
    /// The clock is the device clock, aligned to the real hour: minute 0–45 of every hour is day,
    /// 45–60 is night. Nothing to save, no drift, and every island agrees on what time it is.
    /// </summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        private static readonly int NightId = Shader.PropertyToID("_KayseriNight");
        private static readonly int NightTintId = Shader.PropertyToID("_KayseriNightTint");
        private static readonly int LightsOnId = Shader.PropertyToID("_KayseriLightsOn");

        [Header("Döngü")]
        [Tooltip("Gündüzün dakika cinsinden uzunluğu.")]
        [SerializeField] private float _dayMinutes = 45f;
        [Tooltip("Gecenin dakika cinsinden uzunluğu.")]
        [SerializeField] private float _nightMinutes = 15f;
        [Tooltip("Gün batımı / gün doğumu geçişinin saniye cinsinden uzunluğu.")]
        [SerializeField] private float _transitionSeconds = 90f;

        [Header("Sahne")]
        [Tooltip("Boş bırakılırsa sahnedeki yönlü ışık bulunur.")]
        [SerializeField] private Light _sun;
        [Tooltip("Gökyüzü rengini taşıyan kamera. Sadece arka planı düz renk olan kameralarda kullanılır.")]
        [SerializeField] private Camera _sky;

        [Header("Gece")]
        [SerializeField] private Color _nightSunColor = new Color(0.34f, 0.44f, 0.84f);
        [SerializeField] private float _nightSunIntensity = 0.72f;
        [Tooltip("Ayın geldiği yön (Euler). Güneşin gündüz yönü sahneden okunur, geçişte buraya süzülür.")]
        [SerializeField] private Vector3 _nightSunAngles = new Vector3(38f, 205f, 0f);
        [SerializeField] private Color _nightSkyColor = new Color(0.10f, 0.14f, 0.28f);
        [Tooltip("Gece ortam ışığı ve yansımalarının gündüze oranı.")]
        [Range(0f, 1f)][SerializeField] private float _nightAmbient = 0.52f;
        [Tooltip("Gece ortam ışığına karışan ay mavisi; kanal başına çarpan. Beyaz = eski davranış.")]
        [SerializeField] private Color _nightAmbientTint = new Color(0.75f, 0.85f, 1.3f);
        [Tooltip("Materyallerdeki sabit gölge ve rim renklerinin gecede düştüğü ton.")]
        // Was (0.31, 0.37, 0.60). Multiplying every surface on the island by 0.31 in red does not
        // read as night, it reads as the colour being broken — which is exactly how it was reported.
        // A night still has to show what the player built.
        [SerializeField] private Color _nightTint = new Color(0.58f, 0.65f, 0.86f);
        [Tooltip("Gece pozlaması, EV cinsinden. Gündüz değeri sahnedeki Volume'dan okunur.")]
        [SerializeField] private float _nightExposure = -1f;

        [Header("Sis")]
        // Aerial perspective, and it is very nearly free: both island shaders already carry
        // #pragma multi_compile_fog and call MixFog, so this costs one lerp per pixel on a code path
        // that is already compiled in. The scene has fog switched off in its RenderSettings; this
        // class already owns ambient and reflection there, so it owns fog too rather than the scene
        // needing an edit.
        //
        // What it buys: the far side of the island softens instead of ending in a hard silhouette,
        // the terrain plane's edge stops reading as a cut, and the archipelago gains depth. Density is
        // deliberately tiny — at 0.0016 the near districts are untouched and only the far ridge lifts.
        // The skybox is Kayseri/ToonSky, which has an _Exposure but no night handling of its own —
        // it was authored as a daytime sky and left unused while the scene ran a night skybox from a
        // demo pack at 0.16 exposure, in daylight. Now that it is actually in use, this is what takes
        // it down after dark instead.
        [SerializeField] private float _nightSkyExposure = 0.42f;

        [SerializeField] private bool _fog = true;
        [SerializeField] private float _fogDensity = 0.0016f;
        [SerializeField] private Color _dayFogColor = new Color(0.62f, 0.74f, 0.86f);
        [SerializeField] private Color _nightFogColor = new Color(0.05f, 0.07f, 0.13f);

        // Dusk is not the midpoint between day and night: the real sky detours through orange
        // before it gets anywhere near blue. These are that detour, blended on a weight that peaks
        // halfway through the crossfade and is zero at both ends, so full day and full night stay
        // exactly the authored values and a scene without this block renders as before.
        [Header("Gün batımı / şafak")]
        [SerializeField] private Color _duskSunColor = new Color(1f, 0.55f, 0.24f);
        [Tooltip("Alacakaranlığın ortasında güneşin mutlak parlaklığı.")]
        [SerializeField] private float _duskSunIntensity = 0.95f;
        [SerializeField] private Color _duskSkyColor = new Color(0.95f, 0.56f, 0.36f);
        [Tooltip("Materyallerdeki sabit gölge ve rim renklerinin alacakaranlıkta aldığı sıcak ton.")]
        [SerializeField] private Color _duskTint = new Color(1f, 0.74f, 0.55f);
        [Tooltip("Geçişin ortasında güneşin ufka doğru ekstra düşüşü (derece). Gölgeleri uzatır.")]
        [SerializeField] private float _duskSunDip = 24f;

        // Lamba yüzeyleri oyun kamerasında çok küçük: sokak lambasının başı ~4x9 piksel, far ~7x6,
        // stop lambası ~1x2. O boyutta bir yüzey, ne kadar parlak olursa olsun kenar yumuşatmada
        // eriyip kayboluyor. Çözüm parlaklığı artırmak DEĞİL, bloom ile yaymak: eşiği düşürüp
        // yoğunluğu artırınca 4 piksellik leke okunabilir bir hâleye dönüşüyor.
        [Header("Gece ışıkları")]
        [Tooltip("Gece lamba/far parlaklığı çarpanı. 1 = materyalde yazan değer.")]
        [SerializeField] private float _nightEmissionBoost = 4f;
        [Tooltip("Gece bloom yoğunluğu. Gündüz değeri Volume'dan okunur.")]
        [SerializeField] private float _nightBloomIntensity = 1.9f;
        // Raised, not lowered, now that the island itself goes properly dark: a low threshold on a
        // dark scene blooms the ground as well as the lamps and puts the haze straight back.
        [Tooltip("Gece bloom eşiği. Düşürmek küçük ışıkların hâle yapmasını sağlar.")]
        [SerializeField] private float _nightBloomThreshold = 0.8f;
        // Lamps do not fade up with the sky. A town's lights come on across a band in the middle
        // of dusk — none while the sun is still setting, all of them well before full dark.
        [Tooltip("Lambaların yandığı gece aralığı: x'te sönük, y'de tam yanık.")]
        [SerializeField] private Vector2 _lampSwitchOn = new Vector2(0.12f, 0.55f);

        [Header("Işıksız su")]
        [Tooltip("Suyun shader adı. Bu shader güneşi hiç okumadığı için renkleri elle karartılır.")]
        [SerializeField] private string _unlitWaterShader = "WaterUnlit";
        [Tooltip("Gece su renklerinin gündüze oranı.")]
        [Range(0f, 1f)][SerializeField] private float _nightWater = 0.17f;

        /// <summary>Saati ezme modu. <see cref="TimeOverride.Auto"/> cihaz saatini takip eder.</summary>
        public enum TimeOverride { Auto, Day, Night }

        [Header("Test")]
        [Tooltip("Ayarlar penceresindeki test düğmesi burayı değiştirir. Auto = cihaz saati.")]
        [SerializeField] private TimeOverride _timeOverride = TimeOverride.Auto;

        /// <summary>Reading it costs nothing; setting it snaps, which is what a test switch wants —
        /// waiting out a 90 second crossfade to check the night look would defeat the point.</summary>
        public TimeOverride Override
        {
            get => _timeOverride;
            set => _timeOverride = value;
        }

        /// <summary>0 tam gündüz, 1 tam gece.</summary>
        public float Night01 { get; private set; }
        public bool IsNight => Night01 >= 0.5f;

        /// <summary>
        /// Puts the island back into full daylight and then hands the night straight back — for the
        /// upgrade screen's model studio, which sits in this scene and so was lit by this clock. A
        /// building the player is about to buy has to be readable at three in the morning, and the
        /// studio has no light of its own to reach for: the sun, the ambient probe and the shader
        /// globals below are the only lighting on the island.
        ///
        /// Scoped around ONE camera's pass rather than left on, so the island behind the panel is
        /// still dark. Cheap in daylight, where <see cref="Apply"/> sees no change and returns.
        /// </summary>
        public void HoldDaylight(bool held)
        {
            if (held == _daylightHeld) return;
            if (held)
            {
                _heldNight = _applied < 0f ? Target() : _applied;
                _daylightHeld = true;
                Apply(0f);
                return;
            }
            _daylightHeld = false;
            Apply(_heldNight);
        }

        private float _cycleSeconds;
        private float _nightSeconds;
        private float _transition;
        private float _applied = -1f;
        private bool _daylightHeld;
        private float _heldNight;

        // Daytime, read off the scene once so nothing here has to be kept in step by hand.
        private Color _daySunColor;
        private float _daySunIntensity;
        private float _daySunPitch, _daySunYaw;
        private Color _daySkyColor;
        private Material _skyInstance;
        private float _daySkyExposure = 1f;
        private AmbientMode _ambientMode;
        private float _dayAmbientIntensity;
        private Color _dayAmbientSky, _dayAmbientEquator, _dayAmbientGround, _dayAmbientFlat;
        private float _dayReflection;

        private Renderer[] _waterRenderers;
        private Material _waterAsset;
        private Material _waterInstance;
        private int[] _waterProps;
        private Color[] _waterDay;

        private UnityEngine.Rendering.Universal.ColorAdjustments _grade;
        private float _dayExposure;
        private UnityEngine.Rendering.Universal.Bloom _bloom;
        private float _dayBloomIntensity, _dayBloomThreshold;

        private void Awake()
        {
            _cycleSeconds = Mathf.Max(1f, (_dayMinutes + _nightMinutes) * 60f);
            _nightSeconds = Mathf.Clamp(_nightMinutes * 60f, 0f, _cycleSeconds);

            // The two crossfades are centred on the boundaries, so between them they eat half a
            // transition off each side of both day and night. Anything longer than that and dusk
            // would still be fading in when dawn started.
            float shortest = Mathf.Min(_nightSeconds, _cycleSeconds - _nightSeconds);
            _transition = Mathf.Clamp(_transitionSeconds, 0.01f, shortest);

            if (_sun == null)
            {
                foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                {
                    if (light.type != LightType.Directional) continue;
                    _sun = light;
                    break;
                }
            }
            if (_sky == null) _sky = Camera.main;

            if (_sun != null)
            {
                _daySunColor = _sun.color;
                _daySunIntensity = _sun.intensity;
                Vector3 angles = _sun.transform.eulerAngles;
                _daySunPitch = angles.x;
                _daySunYaw = angles.y;
            }
            if (_sky != null) _daySkyColor = _sky.backgroundColor;

            var sharedSky = RenderSettings.skybox;
            if (sharedSky != null && sharedSky.HasProperty("_Exposure"))
            {
                _daySkyExposure = sharedSky.GetFloat("_Exposure");
                _skyInstance = new Material(sharedSky);
                RenderSettings.skybox = _skyInstance;
            }

            _ambientMode = RenderSettings.ambientMode;
            _dayAmbientIntensity = RenderSettings.ambientIntensity;
            _dayAmbientSky = RenderSettings.ambientSkyColor;
            _dayAmbientEquator = RenderSettings.ambientEquatorColor;
            _dayAmbientGround = RenderSettings.ambientGroundColor;
            _dayAmbientFlat = RenderSettings.ambientLight;
            _dayReflection = RenderSettings.reflectionIntensity;

            BuildPost();
            BuildWater();
        }

        /// <summary>
        /// Night has to reach the exposure as well as the lights. Neutral tonemapping lifts the low
        /// end hard, so a scene that is merely dimly lit still reads as an overcast afternoon — and
        /// exposure is the one lever that also catches the surfaces nothing else can touch, like
        /// the unlit water. <c>Volume.profile</c> rather than <c>sharedProfile</c> on purpose: it
        /// hands back a runtime clone, so the profile asset on disk is never written to.
        /// </summary>
        private void BuildPost()
        {
            Volume best = null;
            foreach (var volume in FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
            {
                if (!volume.isGlobal) continue;
                if (best == null || volume.priority > best.priority) best = volume;
            }
            if (best == null || best.sharedProfile == null) return;

            var profile = best.profile;
            if (profile.TryGet(out _grade)) _dayExposure = _grade.postExposure.value;
            if (profile.TryGet(out _bloom))
            {
                _dayBloomIntensity = _bloom.intensity.value;
                _dayBloomThreshold = _bloom.threshold.value;

                // The only place in the game that holds the bloom override, so it is the only place
                // that can pay for it by device. See QualityService.HighQualityBloomAllowed.
                bool richBloom = Game.Systems.QualityService.HighQualityBloomAllowed;
                _bloom.highQualityFiltering.value = richBloom;
                // maxIterations, not the obsolete skipIterations, and the sense is the other way
                // round: this is how many mips the pyramid is allowed, so FEWER is cheaper. Six is
                // the URP default; four costs the widest, softest end of the glow and little else.
                _bloom.maxIterations.value = richBloom ? 6 : 4;
            }
        }

        private void OnEnable()
        {
            // Apply before the first frame renders, otherwise a session started at 23:50 opens on
            // one frame of broad daylight.
            _applied = -1f;
            Apply(Target());
        }

        // Never inside a hold: the hold lives between one camera's begin and end callbacks, which is
        // after this runs, but a frame the preview camera never finished would otherwise stick.
        private void Update()
        {
            if (_daylightHeld) return;
            Apply(Target());
        }

        private float Target()
        {
            switch (_timeOverride)
            {
                case TimeOverride.Day: return 0f;
                case TimeOverride.Night: return 1f;
                default: return NightAt(SecondsIntoCycle());
            }
        }

        /// <summary>Puts everything back in daylight. Shader globals and the water clone outlive the
        /// play session — they only clear on a domain reload — so leaving them set would tint the
        /// editor after play mode ends.</summary>
        private void OnDisable()
        {
            _daylightHeld = false;
            Apply(0f);

            if (_waterInstance == null) return;
            for (int i = 0; i < _waterRenderers.Length; i++)
                if (_waterRenderers[i] != null) _waterRenderers[i].sharedMaterial = _waterAsset;
            Destroy(_waterInstance);
            _waterInstance = null;
        }

        /// <summary>
        /// The sea and the river run on an unlit shader graph: no sun, no ambient, so they stay at
        /// noon brightness while the rest of the island goes dark. Every water renderer shares one
        /// material, so one runtime clone covers all of them and keeps them batching — and the asset
        /// on disk is never written to. A property block would be the obvious tool here and does not
        /// work: the SRP Batcher ignores per-renderer overrides.
        /// </summary>
        private void BuildWater()
        {
            var found = new List<Renderer>();
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var material = renderer.sharedMaterial;
                if (material == null || material.shader == null) continue;
                if (!material.shader.name.Contains(_unlitWaterShader)) continue;
                if (_waterAsset == null) _waterAsset = material;
                if (material == _waterAsset) found.Add(renderer);
            }
            if (_waterAsset == null) return;

            var shader = _waterAsset.shader;
            var props = new List<int>();
            var day = new List<Color>();
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Color) continue;
                int id = shader.GetPropertyNameId(i);
                props.Add(id);
                day.Add(_waterAsset.GetColor(id));
            }

            _waterProps = props.ToArray();
            _waterDay = day.ToArray();
            _waterRenderers = found.ToArray();
            _waterInstance = new Material(_waterAsset);
            for (int i = 0; i < _waterRenderers.Length; i++) _waterRenderers[i].sharedMaterial = _waterInstance;
        }

        private float SecondsIntoCycle()
        {
            // Unix time starts on an exact hour, so for the default 60-minute cycle this is simply
            // how far into the current real hour we are.
            return (float)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % (long)_cycleSeconds);
        }

        /// <summary>Night weight at a point in the cycle, with the dusk crossfade centred on the end
        /// of the day and the dawn one on the end of the night.</summary>
        private float NightAt(float secondsIntoCycle)
        {
            float duskStart = (_cycleSeconds - _nightSeconds) - _transition * 0.5f;
            float x = secondsIntoCycle - duskStart;
            if (x < 0f) x += _cycleSeconds;

            float rise = Mathf.SmoothStep(0f, 1f, x / _transition);
            float fall = 1f - Mathf.SmoothStep(0f, 1f, (x - _nightSeconds) / _transition);
            return Mathf.Min(rise, fall);
        }

        private void Apply(float night)
        {
            // Fifty-five minutes of every hour nothing is moving; this is the whole per-frame cost
            // outside a crossfade.
            if (Mathf.Abs(night - _applied) < 0.001f) return;
            _applied = night;
            Night01 = night;

            // Two shapes drive everything below. Dusk peaks halfway through the crossfade and is
            // zero at both ends, so the sunset only exists while the light is actually turning.
            // Dark lags night on purpose: a sunset is bright, so exposure and the water hold their
            // daylight through the orange phase and only drop once the sky has gone blue.
            float dusk = Mathf.Sin(night * Mathf.PI);
            dusk *= dusk;
            float dark = night * night;

            if (_sun != null)
            {
                _sun.color = Color.Lerp(Color.Lerp(_daySunColor, _nightSunColor, night),
                                        _duskSunColor, dusk);
                _sun.intensity = Mathf.Lerp(Mathf.Lerp(_daySunIntensity, _nightSunIntensity, night),
                                            _duskSunIntensity, dusk);

                // The sun genuinely sets: it swings from its daytime place toward the moon's, and
                // in the middle of the crossfade dips toward the horizon so the shadows stretch the
                // way an evening's do. The floor keeps it above the horizon, where the whole island
                // would fall into its own shadow for the rest of the fade.
                float pitch = Mathf.LerpAngle(_daySunPitch, _nightSunAngles.x, night) - _duskSunDip * dusk;
                float yaw = Mathf.LerpAngle(_daySunYaw, _nightSunAngles.y, night);
                _sun.transform.rotation = Quaternion.Euler(Mathf.Max(pitch, 8f), yaw, _nightSunAngles.z);
            }

            // Main clears to a skybox and barely shows any of it; the island scenes clear to a flat
            // colour, which is the whole sky there and has to follow.
            if (_sky != null && _sky.clearFlags == CameraClearFlags.SolidColor)
                _sky.backgroundColor = Color.Lerp(Color.Lerp(_daySkyColor, _nightSkyColor, night),
                                                  _duskSkyColor, dusk);

            switch (_ambientMode)
            {
                case AmbientMode.Trilight:
                    RenderSettings.ambientSkyColor = Dim(_dayAmbientSky, night);
                    RenderSettings.ambientEquatorColor = Dim(_dayAmbientEquator, night);
                    RenderSettings.ambientGroundColor = Dim(_dayAmbientGround, night);
                    break;
                case AmbientMode.Flat:
                    RenderSettings.ambientLight = Dim(_dayAmbientFlat, night);
                    break;
                default:
                    RenderSettings.ambientIntensity =
                        Mathf.Lerp(_dayAmbientIntensity, _dayAmbientIntensity * _nightAmbient, night);
                    break;
            }

            // A RUNTIME INSTANCE, never the shared asset. Writing _Exposure straight onto
            // RenderSettings.skybox mutates the material asset on disk — in the editor that survives
            // leaving play mode, so one session that happened to end at night would leave the sky
            // permanently dark and look exactly like a bug in the art.
            if (_skyInstance != null)
                _skyInstance.SetFloat("_Exposure", Mathf.Lerp(_daySkyExposure, _nightSkyExposure, night));

            if (_fog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = _fogDensity;
                // Follows the sky rather than staying one colour, or the island would sit in a pale
                // haze at midnight — which is exactly what fog looks like when nobody wired it up.
                RenderSettings.fogColor = Color.Lerp(_dayFogColor, _nightFogColor, night);
            }

            // Without this the glossy props keep mirroring a midday skybox after dark.
            RenderSettings.reflectionIntensity =
                Mathf.Lerp(_dayReflection, _dayReflection * _nightAmbient, night);

            if (_grade != null)
                _grade.postExposure.value = Mathf.Lerp(_dayExposure, _nightExposure, dark);

            if (_bloom != null)
            {
                _bloom.intensity.value = Mathf.Lerp(_dayBloomIntensity, _nightBloomIntensity, night);
                _bloom.threshold.value = Mathf.Lerp(_dayBloomThreshold, _nightBloomThreshold, night);
            }

            if (_waterInstance != null)
            {
                // The sea keeps its evening light as long as the sky does, then follows it down.
                float water = Mathf.Lerp(1f, _nightWater, dark);
                for (int i = 0; i < _waterProps.Length; i++)
                {
                    Color day = _waterDay[i];
                    // Alpha carries foam masks, not brightness — scaling it would erase them.
                    _waterInstance.SetColor(_waterProps[i],
                        new Color(day.r * water, day.g * water, day.b * water, day.a));
                }
            }

            Shader.SetGlobalFloat(NightId, night);
            // The constant shading terms warm up with the sunset before they cool into the night
            // blue — the same detour the sun itself takes.
            Shader.SetGlobalColor(NightTintId, Color.Lerp(_nightTint, _duskTint, dusk));
            // Deliberately allowed above 1: the shader multiplies the authored emission by this, so
            // it doubles as the night brightness of every lamp without editing a single material.
            float lampsOn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_lampSwitchOn.x, _lampSwitchOn.y, night));
            Shader.SetGlobalFloat(LightsOnId, lampsOn * _nightEmissionBoost);
        }

        /// <summary>Ambient at night is not just dimmer, it is bluer — moonlight — so the dim end
        /// is tinted per channel as well as scaled. White tint is exactly the old behaviour.</summary>
        private Color Dim(Color day, float night)
        {
            var dark = new Color(day.r * _nightAmbient * _nightAmbientTint.r,
                                 day.g * _nightAmbient * _nightAmbientTint.g,
                                 day.b * _nightAmbient * _nightAmbientTint.b, day.a);
            return Color.Lerp(day, dark, night);
        }
    }
}
