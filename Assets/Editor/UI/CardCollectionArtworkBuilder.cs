using System.Collections.Generic;
using System.IO;
using Game.UI;
using UnityEditor;
using UnityEngine;

/// <summary>Collection-only outline import. Source PNG pixels and proportions are retained.</summary>
public static class CardCollectionArtworkBuilder
{
    private const string Source = "Assets/UI DESİGNS/kart koleksiyonu";
    private const string Output = "Assets/Resources/UI/Portrait/";

    [MenuItem("Tools/UI/Prepare Card Collection Artwork")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play mode before importing artwork.");
        var kit = AssetDatabase.LoadAssetAtPath<PortraitUiArt>(Output + "PortraitUiArt.asset");
        var meshField = typeof(PortraitUiArt).GetField("_meshes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var meshes = (PortraitUiArt.MeshData[])meshField.GetValue(kit);
        foreach (string file in Directory.GetFiles(Source, "*.png"))
        {
            string path = file.Replace('\\', '/');
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool wasReadable = importer.isReadable;
            try
            {
                importer.isReadable = true; importer.SaveAndReimport();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                bool[] matte = Exterior(texture);
                var targets = new List<Sprite>();
                targets.Add(AssetDatabase.LoadAssetAtPath<Sprite>(Output + "collection-" + Path.GetFileNameWithoutExtension(path) + ".asset"));
                string prefix = path.EndsWith("/card-states-strip.png") ? "card-state-" : path.EndsWith("/empty-notification-states.png") ? "empty-state-" : null;
                if (prefix != null)
                {
                    var columns = new List<Vector2Int>();
                    int start = -1;
                    for (int x = 0; x <= texture.width; x++)
                    {
                        int count = 0;
                        if (x < texture.width)
                            for (int y = 0; y < texture.height; y++) if (!matte[y * texture.width + x]) count++;
                        bool occupied = count > 12;
                        if (occupied && start < 0) start = x;
                        if (!occupied && start >= 0) { columns.Add(new Vector2Int(start, x)); start = -1; }
                    }
                    int expected = prefix == "card-state-" ? 6 : 5;
                    if (columns.Count != expected) throw new System.InvalidOperationException(path + ": expected " + expected + " separate shapes; found " + columns.Count);
                    for (int i = 0; i < columns.Count; i++)
                    {
                        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(Output + prefix + i + ".asset");
                        Rect bounds = Bounds(matte, texture.width, texture.height, columns[i].x, columns[i].y);
                        var replacement = Sprite.Create(texture, bounds, Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect);
                        replacement.name = existing.name;
                        EditorUtility.CopySerialized(replacement, existing); Object.DestroyImmediate(replacement);
                        targets.Add(existing);
                    }
                }
                foreach (var sprite in targets)
                {
                    var vertices = new List<Vector2>(); var triangles = new List<int>();
                    var rect = sprite.rect;
                    int x0 = Mathf.RoundToInt(rect.x), y0 = Mathf.RoundToInt(rect.y);
                    int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
                    // Each run is independent, so gaps between icons cannot retain the checkerboard.
                    // Two-pixel strips preserve the contour while keeping the UI mesh inexpensive.
                    for (int y = 0; y < h; y += 2)
                    {
                        int sample = Mathf.Min(y + 1, h - 1), start = -1;
                        for (int x = 0; x <= w; x++)
                        {
                            bool ink = x < w && !matte[(y0 + sample) * texture.width + x0 + x];
                            if (ink && start < 0) start = x;
                            if (ink || start < 0) continue;
                            int n = vertices.Count;
                            float a = (start - w * 0.5f) / 100f, b = (x - w * 0.5f) / 100f;
                            float c = (y - h * 0.5f) / 100f, d = (Mathf.Min(y + 2, h) - h * 0.5f) / 100f;
                            vertices.Add(new Vector2(a, c)); vertices.Add(new Vector2(a, d));
                            vertices.Add(new Vector2(b, d)); vertices.Add(new Vector2(b, c));
                            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
                            triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 3); start = -1;
                        }
                    }
                    if (vertices.Count > 65000) throw new System.InvalidOperationException(sprite.name + " exceeds UI mesh budget");
                    for (int i = 0; i < meshes.Length; i++)
                    {
                        if (meshes[i].Sprite != sprite) continue;
                        meshes[i] = new PortraitUiArt.MeshData { Sprite = sprite, Vertices = vertices.ToArray(), Triangles = triangles.ToArray() };
                        break;
                    }
                    EditorUtility.SetDirty(sprite); AssetDatabase.SaveAssetIfDirty(sprite);
                }
            }
            finally { importer.isReadable = wasReadable; importer.SaveAndReimport(); }
        }
        meshField.SetValue(kit, meshes); EditorUtility.SetDirty(kit); AssetDatabase.SaveAssetIfDirty(kit);
        Debug.Log("Card Collection: all 12 source assets prepared; state icons have complete outlines.");
    }

    private static Rect Bounds(bool[] matte, int width, int height, int left, int right)
    {
        int bottom = height, top = 0;
        for (int y = 0; y < height; y++) for (int x = left; x < right; x++)
            if (!matte[y * width + x]) { bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y); }
        return new Rect(left, bottom, right - left, top - bottom + 1);
    }

    private static bool[] Exterior(Texture2D texture)
    {
        int w = texture.width, h = texture.height;
        var pixels = texture.GetPixels32(); var exterior = new bool[pixels.Length];
        var queue = new Queue<int>();
        System.Action<int> visit = p =>
        {
            if (exterior[p]) return;
            var c = pixels[p];
            int hi = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), lo = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            if (c.a > 12 && !(hi - lo < Mathf.Max(30, hi * 0.40f) && lo > 25)) return;
            exterior[p] = true; queue.Enqueue(p);
        };
        for (int x = 0; x < w; x++) { visit(x); visit((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { visit(y * w); visit(y * w + w - 1); }
        // A small matte pocket enclosed between the three touching filter controls.
        if (texture.name == "sort-filter-panel") visit(Mathf.RoundToInt(h * 0.51f) * w + Mathf.RoundToInt(w * 0.749f));
        while (queue.Count > 0)
        {
            int p = queue.Dequeue(), x = p % w, y = p / w;
            if (x > 0) visit(p - 1); if (x + 1 < w) visit(p + 1);
            if (y > 0) visit(p - w); if (y + 1 < h) visit(p + w);
        }
        return exterior;
    }
}
