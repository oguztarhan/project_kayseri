using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>Renders the original sprite through its imported outline, including UI clipping and tint.</summary>
    [RequireComponent(typeof(Image))]
    public sealed class PortraitSpriteMesh : BaseMeshEffect
    {
        private Image _image;
        protected override void Awake() { base.Awake(); _image = GetComponent<Image>(); }

        public override void ModifyMesh(VertexHelper helper)
        {
            if (!IsActive()) return;
            if (_image == null) _image = GetComponent<Image>();
            Sprite sprite = _image.sprite;
            if (sprite == null || !PortraitUiArt.MeshOf(sprite, out PortraitUiArt.MeshData mesh)) return;
            if (mesh.Vertices == null || mesh.Triangles == null) return;
            Rect box = _image.GetPixelAdjustedRect();
            float width = box.width, height = box.height;
            if (_image.preserveAspect)
            {
                float ratio = sprite.rect.width / sprite.rect.height;
                if (width / height > ratio) width = height * ratio; else height = width / ratio;
            }
            Vector2 origin = box.center - new Vector2(width, height) * 0.5f;
            helper.Clear();
            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                Vector2 px = mesh.Vertices[i] * sprite.pixelsPerUnit + sprite.pivot;
                var v = UIVertex.simpleVert;
                v.position = origin + new Vector2(px.x / sprite.rect.width * width, px.y / sprite.rect.height * height);
                v.uv0 = new Vector2((sprite.rect.x + px.x) / sprite.texture.width, (sprite.rect.y + px.y) / sprite.texture.height);
                v.color = _image.color;
                helper.AddVert(v);
            }
            for (int i = 0; i < mesh.Triangles.Length; i += 3)
                helper.AddTriangle(mesh.Triangles[i], mesh.Triangles[i + 1], mesh.Triangles[i + 2]);
        }
    }
}
