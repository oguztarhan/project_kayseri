using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The island's own noise: a looping source parked on each district that should be audible when the
    /// camera comes near it — the furnace roar at the refinery, surf at the port, hammer and belt at the
    /// mine — plus the global ambience bed that plays everywhere.
    ///
    /// Positions come from the art, the same way the smoke sources do, so a district that rebuilds into a
    /// bigger phase drags its sound to the new centre instead of leaving it behind. Sources are created
    /// once and moved, never respawned.
    ///
    /// Volume follows <see cref="AudioService.MixChanged"/> rather than being polled, so the settings
    /// slider reaches the island with no per-frame work here at all: this component has no Update.
    /// </summary>
    public sealed class IslandAmbience : MonoBehaviour
    {
        [System.Serializable]
        private struct Source
        {
            [Tooltip("Ada sanatındaki bölge adı: Refinery, Port, Mine, Depot, Market ...")]
            public string district;
            public AudioClip clip;
            [Range(0f, 2f)] public float volume;
            [Tooltip("Bu yarıçapın içinde tam sesle duyulur.")]
            public float minDistance;
            [Tooltip("Bu mesafenin ötesinde tamamen susar.")]
            public float maxDistance;
            [Tooltip("Bölgenin taban merkezinden yukarı kaydırma.")]
            public float heightOffset;
        }

        [SerializeField] private Kayseri.Island.IslandPhaseController phases;
        [SerializeField] private Source[] sources;

        private AudioSource[] _live;
        private AudioService _audio;
        private Coroutine _phasePlacement;

        private void Start()
        {
            _audio = ServiceLocator.Get<AudioService>();
            if (_audio == null) return;

            _audio.StartAmbience();
            _audio.MixChanged += ApplyVolumes;

            if (phases == null) phases = GetComponentInChildren<Kayseri.Island.IslandPhaseController>(true);
            if (phases != null) phases.PhaseRefreshCompleted += OnPhaseRefreshCompleted;

            Build();
            Place();
            ApplyVolumes();
            StartCoroutine(Settle());
        }

        /// <summary>
        /// Places again over the first few seconds. The mine yard is not authored where it ends up —
        /// <see cref="CoalOperation"/> lays it out at runtime, which is why that district's renderers are
        /// the only ones not static-batched — and there is no guarantee this Start runs after that one.
        /// Placed once at Start the mine's sound lands ~85 m off, out on bare ground. After this the only
        /// thing that moves a district is a phase change, which is handled.
        /// </summary>
        private System.Collections.IEnumerator Settle()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(i == 0 ? 0.5f : 1f);
                Place();
            }
        }

        private void OnDestroy()
        {
            if (_audio != null) _audio.MixChanged -= ApplyVolumes;
            if (phases != null) phases.PhaseRefreshCompleted -= OnPhaseRefreshCompleted;
        }

        private void OnPhaseRefreshCompleted()
        {
            if (_phasePlacement == null) _phasePlacement = StartCoroutine(PlaceAfterPhase());
        }

        private System.Collections.IEnumerator PlaceAfterPhase()
        {
            // Bounds collection walks every renderer below each audible district. Let lighting take
            // the first two follow-up frames and move the audio sources after those scans.
            yield return null;
            yield return null;
            yield return null;
            Place();
            _phasePlacement = null;
        }

        private void Build()
        {
            if (sources == null) return;
            _live = new AudioSource[sources.Length];

            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i].clip == null) continue;

                var go = new GameObject("Amb_" + sources[i].district);
                go.transform.SetParent(transform, false);

                var s = go.AddComponent<AudioSource>();
                s.clip = sources[i].clip;
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.dopplerLevel = 0f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = sources[i].minDistance;
                s.maxDistance = sources[i].maxDistance;
                s.bypassReverbZones = true;
                _live[i] = s;

                // Aynı anda başlayan döngüler faz içinde kilitlenip tek bir ses gibi atar; her birini
                // kendi uzunluğunun farklı bir yerinden başlatınca ada tek kaynak gibi duyulmaz.
                s.Play();
                s.time = s.clip.length * (i * 0.37f % 1f);
            }
        }

        /// <summary>Moves each source onto its district's current art. Districts a phase does not build
        /// simply go quiet until one does.</summary>
        private void Place()
        {
            if (_live == null || phases == null) return;

            for (int i = 0; i < _live.Length; i++)
            {
                if (_live[i] == null) continue;

                Transform district = phases.ActiveDistrict(sources[i].district);
                if (district == null) { _live[i].mute = true; continue; }

                var rends = district.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) { _live[i].mute = true; continue; }

                Bounds b = rends[0].bounds;
                for (int r = 1; r < rends.Length; r++) b.Encapsulate(rends[r].bounds);

                _live[i].mute = false;
                _live[i].transform.position = new Vector3(b.center.x, b.min.y + sources[i].heightOffset, b.center.z);
            }
        }

        private void ApplyVolumes()
        {
            if (_live == null || _audio == null) return;
            float mix = _audio.SfxVolume;
            for (int i = 0; i < _live.Length; i++)
                if (_live[i] != null) _live[i].volume = mix * sources[i].volume;
        }
    }
}
