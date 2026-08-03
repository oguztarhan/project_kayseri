using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// The clip behind every <see cref="SoundId"/>, plus the three things that decide whether a sound
    /// wears out: how loud it is, how much its pitch wanders, and how often it may repeat.
    ///
    /// The repeat gate matters more than it looks. This is an idle game — at a high level the operation
    /// sells several times a second, and the same 240 ms clip fired that often stops being a sound and
    /// becomes a texture. <see cref="Entry.minInterval"/> is what keeps it a sound.
    /// </summary>
    [CreateAssetMenu(fileName = "AudioLibrary", menuName = "Ore Empire/Audio Library", order = 17)]
    public sealed class AudioLibrary : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public SoundId id;
            public AudioClip clip;
            [Range(0f, 2f)] public float volume;
            [Tooltip("± perde sapması. Üst üste binen kopyaları doğal gösterir; 0 = hep aynı.")]
            [Range(0f, 0.3f)] public float pitchJitter;
            [Tooltip("Aynı sesin iki çalışı arasındaki en kısa süre (sn). Sık çalan sesler için şart.")]
            [Range(0f, 2f)] public float minInterval;
        }

        [SerializeField] private Entry[] entries;

        [Header("Ortam yatağı (müzik sürgüsüne bağlı)")]
        [Tooltip("Sürekli dönen 2B ortam sesi. Boşken ortam sessizdir.")]
        [SerializeField] private AudioClip ambience;
        [SerializeField, Range(0f, 2f)] private float ambienceVolume = 1f;

        private Entry[] _byId;

        public AudioClip Ambience => ambience;
        public float AmbienceVolume => ambienceVolume;

        /// <summary>The entry for <paramref name="id"/>, or false when it has no clip wired.</summary>
        public bool TryGet(SoundId id, out Entry entry)
        {
            if (_byId == null) BuildIndex();
            int i = (int)id;
            if (i < 0 || i >= _byId.Length || _byId[i].clip == null) { entry = default; return false; }
            entry = _byId[i];
            return true;
        }

        private void OnEnable() => BuildIndex();

        private void OnValidate() => BuildIndex();

        /// <summary>
        /// Flattens the authored list into a lookup by enum value, so playing a sound is an array index
        /// rather than a search. Rebuilt on validate as well, so editing the list in play mode takes.
        /// </summary>
        private void BuildIndex()
        {
            int size = 1;
            foreach (SoundId v in System.Enum.GetValues(typeof(SoundId)))
                if ((int)v >= size) size = (int)v + 1;

            _byId = new Entry[size];
            if (entries == null) return;

            for (int i = 0; i < entries.Length; i++)
            {
                int slot = (int)entries[i].id;
                if (slot <= 0 || slot >= size) continue;
                Entry e = entries[i];
                if (e.volume <= 0f) e.volume = 1f;
                _byId[slot] = e;
            }
        }
    }
}
