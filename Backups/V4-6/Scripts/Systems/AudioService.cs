using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// Audio facade (GDD §13). Plays SFX at the configured mix. Silent until clips are supplied via an
    /// AudioLibrary in a later content pass — the interface is here so callers don't change.
    /// </summary>
    public sealed class AudioService
    {
        public float Master { get; set; }
        public float Music { get; set; }
        public float Sfx { get; set; }

        public AudioService(float master, float music, float sfx)
        {
            Master = master; Music = music; Sfx = sfx;
        }

        public void PlaySfx(AudioClip clip, Vector3 position)
        {
            if (clip == null) return;
            AudioSource.PlayClipAtPoint(clip, position, Master * Sfx);
        }
    }
}
