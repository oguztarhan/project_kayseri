using Game.Data;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// The one place sound comes out of (GDD §13). Callers name a moment — <see cref="SoundId.Upgrade"/>,
    /// <see cref="SoundId.Denied"/> — and this decides whether it is allowed to be heard right now.
    ///
    /// Everything plays through a small round-robin pool of 2D sources on one hidden object that survives
    /// scene loads, so a sound never allocates and never cuts off the one before it. World sounds are not
    /// here: those are looping 3D sources placed on the island by <c>IslandAmbience</c>, which follows
    /// <see cref="MixChanged"/> to stay in step with the SFX slider.
    ///
    /// The mix is split the way the settings screen presents it: the ambience bed rides the music slider,
    /// everything else rides the SFX slider, and both sit under master.
    /// </summary>
    public sealed class AudioService
    {
        private const int PoolSize = 10;

        private readonly AudioLibrary _library;
        private readonly float[] _nextAllowed;

        private GameObject _host;
        private AudioSource[] _pool;
        private AudioSource _bed;
        private int _next;

        private float _master, _music, _sfx;

        /// <summary>Raised whenever a volume changes. World loop sources follow this instead of polling.</summary>
        public event System.Action MixChanged;

        public float Master { get { return _master; } set { _master = value; ApplyMix(); } }
        public float Music { get { return _music; } set { _music = value; ApplyMix(); } }
        public float Sfx { get { return _sfx; } set { _sfx = value; ApplyMix(); } }

        /// <summary>What a world-placed loop should multiply its own volume by.</summary>
        public float SfxVolume => _master * _sfx;

        public AudioService(float master, float music, float sfx) : this(master, music, sfx, null) { }

        public AudioService(float master, float music, float sfx, AudioLibrary library)
        {
            _master = master; _music = music; _sfx = sfx;
            _library = library;

            int size = 1;
            foreach (SoundId v in System.Enum.GetValues(typeof(SoundId)))
                if ((int)v >= size) size = (int)v + 1;
            _nextAllowed = new float[size];
        }

        /// <summary>Plays a sound, unless its repeat gate says it is too soon.</summary>
        public void Play(SoundId id)
        {
            if (id == SoundId.None || _library == null || !EnsureHost()) return;

            AudioLibrary.Entry e;
            if (!_library.TryGet(id, out e)) return;

            int slot = (int)id;
            if (slot < _nextAllowed.Length)
            {
                float now = Time.unscaledTime;
                if (now < _nextAllowed[slot]) return;
                _nextAllowed[slot] = now + e.minInterval;
            }

            AudioSource s = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            s.clip = e.clip;
            s.volume = SfxVolume * e.volume;
            s.pitch = e.pitchJitter > 0f ? 1f + Random.Range(-e.pitchJitter, e.pitchJitter) : 1f;
            s.Play();
        }

        /// <summary>Starts the looping ambience bed. Idempotent — safe to call on every scene load.</summary>
        public void StartAmbience() => Bed(_library != null ? _library.Ambience : null);

        /// <summary>
        /// Moves the bed between the island's outdoors and the market's indoors.
        ///
        /// One bed, swapped, rather than two crossfading. The market is a scene load away behind a
        /// curtain — the player never hears the join — and a second looping source would be a second
        /// source running for the whole session to cover a transition nobody is listening to.
        ///
        /// Falls back to the island bed if no market clip is wired, so the room going quiet is never
        /// the failure mode.
        /// </summary>
        public void SetMarketAmbience(bool inMarket)
        {
            if (_library == null) return;
            Bed(inMarket && _library.MarketAmbience != null
                ? _library.MarketAmbience : _library.Ambience);
        }

        /// <summary>Puts one clip on the looping bed, building the bed the first time.</summary>
        private void Bed(AudioClip clip)
        {
            if (clip == null || !EnsureHost()) return;

            if (_bed == null)
            {
                _bed = _host.AddComponent<AudioSource>();
                _bed.playOnAwake = false;
                _bed.loop = true;
                _bed.spatialBlend = 0f;
            }
            ApplyMix();
            // Restarted only when the clip actually changes. Re-assigning the same clip and calling
            // Play again would jump the bed back to its first sample every time a scene loads, which
            // on a twenty-second loop is audible.
            if (_bed.clip != clip)
            {
                _bed.clip = clip;
                _bed.Play();
            }
            else if (!_bed.isPlaying) _bed.Play();
        }

        /// <summary>
        /// Builds the hidden host on first use. Lazy because the service is constructed in Bootstrap's
        /// Awake, and because a domain reload leaves the old host destroyed behind a live reference.
        /// </summary>
        private bool EnsureHost()
        {
            if (_host != null) return true;
            if (!Application.isPlaying) return false;

            _host = new GameObject("~Audio");
            Object.DontDestroyOnLoad(_host);

            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var s = _host.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0f;
                s.bypassReverbZones = true;
                _pool[i] = s;
            }
            _next = 0;
            _bed = null;
            return true;
        }

        private void ApplyMix()
        {
            if (_bed != null)
                _bed.volume = _master * _music * (_library != null ? _library.AmbienceVolume : 1f);
            if (MixChanged != null) MixChanged();
        }
    }
}
