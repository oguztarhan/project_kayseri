using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>Original UI exports, trimmed and meshed by the editor importer. No runtime texture copies.</summary>
    public sealed class PortraitUiArt : ScriptableObject
    {
        [SerializeField] private Sprite[] _sprites;
        [System.Serializable]
        public struct MeshData
        {
            public Sprite Sprite;
            public Vector2[] Vertices;
            public int[] Triangles;
        }
        [SerializeField] private MeshData[] _meshes;
        private static PortraitUiArt _instance;

        public static Sprite Get(string name)
        {
            if (_instance == null)
            {
                _instance = Resources.Load<PortraitUiArt>("UI/Portrait/PortraitUiArt");
            }
            if (_instance == null || _instance._sprites == null) return null;
            for (int i = 0; i < _instance._sprites.Length; i++)
                if (_instance._sprites[i] != null && _instance._sprites[i].name == name) return _instance._sprites[i];
            return null;
        }

        public static void Apply(Image image, Sprite sprite)
        {
            if (image == null || sprite == null) return;
            image.sprite = sprite;
            image.overrideSprite = null;
            image.enabled = true;
            image.overrideSprite = null;
            image.enabled = true;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.useSpriteMesh = false;
            image.color = Color.white;
            if (image.GetComponent<PortraitSpriteMesh>() == null) image.gameObject.AddComponent<PortraitSpriteMesh>();
        }

        public static bool MeshOf(Sprite sprite, out MeshData mesh)
        {
            if (_instance == null) Get(string.Empty);
            if (_instance != null && _instance._meshes != null)
                for (int i = 0; i < _instance._meshes.Length; i++)
                    if (_instance._meshes[i].Sprite == sprite) { mesh = _instance._meshes[i]; return true; }
            mesh = default; return false;
        }

        public static void Apply(Image image, string name) => Apply(image, Get(name));
    }
}
