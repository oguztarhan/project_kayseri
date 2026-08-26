namespace Game.Systems
{
    /// <summary>
    /// Cloud-save facade (GDD §14.6). Local stub now (no backend); real Google Play Games / custom
    /// backend sync swaps in at M5. Kept behind an interface so nothing else changes when it goes live.
    /// </summary>
    public interface ICloudSave
    {
        bool Available { get; }
        void Push(byte[] blob);
        byte[] Pull();
    }

    /// <summary>No-op dev stub — reports unavailable so the game stays fully local for now.</summary>
    public sealed class LocalCloudSaveStub : ICloudSave
    {
        public bool Available => false;
        public void Push(byte[] blob) { }
        public byte[] Pull() => null;
    }
}
