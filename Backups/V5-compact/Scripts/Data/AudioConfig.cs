using UnityEngine;

namespace Game.Data
{
    /// <summary>Audio mix (GDD §13). Volumes are designer-editable; clips are assigned in an AudioLibrary later.</summary>
    [CreateAssetMenu(fileName = "AudioConfig", menuName = "Ore Empire/Audio Config", order = 16)]
    public sealed class AudioConfig : ScriptableObject
    {
        [SerializeField, Range(0f, 1f)] private float master = 1f;
        [SerializeField, Range(0f, 1f)] private float music = 0.6f;
        [SerializeField, Range(0f, 1f)] private float sfx = 0.8f;

        public float Master => master;
        public float Music => music;
        public float Sfx => sfx;
    }
}
