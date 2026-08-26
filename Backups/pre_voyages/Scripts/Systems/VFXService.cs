using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// VFX facade (GDD §13). Spawns a one-shot effect prefab and auto-destroys it. No-op until effect
    /// prefabs are supplied via a VFXLibrary in a later content pass. (Pooling to be added with content.)
    /// </summary>
    public sealed class VFXService
    {
        public void Spawn(GameObject prefab, Vector3 position)
        {
            if (prefab == null) return;
            var go = Object.Instantiate(prefab, position, Quaternion.identity);
            Object.Destroy(go, 2f);
        }
    }
}
