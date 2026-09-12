using System.Collections.Generic;
using System.IO;
using Game.UI;
using UnityEditor;
using UnityEngine;

public static class PortraitUiAssetBuilder
{
    private const string Output = "Assets/Resources/UI/Portrait";
    private const string Source = "Assets/UI DESİGNS/";
    private static readonly List<PortraitUiArt.MeshData> Meshes = new List<PortraitUiArt.MeshData>();

    [MenuItem("Tools/UI/Import Portrait UI Artwork")]
    public static void Build()
    {
        Directory.CreateDirectory(Output);
        AssetDatabase.Refresh();
        var sprites = new List<Sprite>();
        Meshes.Clear();
        foreach (string folder in new[] { "settings-ui-hud-standard", "ui assets general", "kart koleksiyonu" })
        {
            foreach (string file in Directory.GetFiles(Source + folder, "*.png"))
            {
                string path = file.Replace('\\', '/');
                string prefix = folder == "settings-ui-hud-standard" ? "settings-" : folder == "ui assets general" ? "general-" : "collection-";
                sprites.Add(Import(path, prefix + Path.GetFileNameWithoutExtension(path), new Rect(0, 0, 1, 1)));
            }
        }
        // Sprite-sheet cells are separate controls, not a single image stretched across a layout.
        for (int i = 0; i < 6; i++)
            sprites.Add(Import(Source + "kart koleksiyonu/card-states-strip.png", "card-state-" + i,
                new Rect(i / 6f, 0, 1f / 6f, 1)));
        for (int i = 0; i < 5; i++)
            sprites.Add(Import(Source + "kart koleksiyonu/empty-notification-states.png", "empty-state-" + i,
                new Rect(i / 5f, 0, 1f / 5f, 1)));
        for (int i = 0; i < 3; i++)
            sprites.Add(Import(Source + "kart koleksiyonu/collection-set-tabs.png", "set-tab-" + i,
                new Rect(i / 3f, 0, 1f / 3f, 1)));
        var kit = AssetDatabase.LoadAssetAtPath<PortraitUiArt>(Output + "/PortraitUiArt.asset");
        if (kit == null) { kit = ScriptableObject.CreateInstance<PortraitUiArt>(); AssetDatabase.CreateAsset(kit, Output + "/PortraitUiArt.asset"); }
        var so = new SerializedObject(kit);
        var array = so.FindProperty("_sprites"); array.arraySize = sprites.Count;
        for (int i = 0; i < sprites.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        var meshes = so.FindProperty("_meshes"); meshes.arraySize = Meshes.Count;
        for (int i = 0; i < Meshes.Count; i++)
        {
            var entry = meshes.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("Sprite").objectReferenceValue = Meshes[i].Sprite;
            var v = entry.FindPropertyRelative("Vertices"); v.arraySize = Meshes[i].Vertices.Length;
            for (int k = 0; k < v.arraySize; k++) v.GetArrayElementAtIndex(k).vector2Value = Meshes[i].Vertices[k];
            var t = entry.FindPropertyRelative("Triangles"); t.arraySize = Meshes[i].Triangles.Length;
            for (int k = 0; k < t.arraySize; k++) t.GetArrayElementAtIndex(k).intValue = Meshes[i].Triangles[k];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log("Portrait UI artwork ready: " + sprites.Count + " proportion-preserving sprites.");
    }

    private static Sprite Import(string path, string name, Rect region)
    {
        string output = Output + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(output);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool readable = importer.isReadable;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.isReadable = true;
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        int x0 = Mathf.RoundToInt(region.x * texture.width), y0 = Mathf.RoundToInt(region.y * texture.height);
        int w = Mathf.Min(Mathf.RoundToInt(region.width * texture.width), texture.width - x0);
        int h = Mathf.Min(Mathf.RoundToInt(region.height * texture.height), texture.height - y0);
        Color32[] all = texture.GetPixels32();
        var exterior = new bool[w * h];
        var queue = new Queue<int>();
        // Only boundary-connected neutral export matte is excluded. Enclosed white artwork remains intact.
        System.Action<int> visit = p =>
        {
            if (exterior[p]) return;
            Color32 c = all[(y0 + p / w) * texture.width + x0 + p % w];
            int hi = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), lo = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            if (c.a > 12 && !(hi - lo < 28 && lo > 55)) return;
            exterior[p] = true; queue.Enqueue(p);
        };
        for (int x = 0; x < w; x++) { visit(x); visit((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { visit(y * w); visit(y * w + w - 1); }
        while (queue.Count > 0)
        {
            int p = queue.Dequeue(), x = p % w, y = p / w;
            if (x > 0) visit(p - 1); if (x + 1 < w) visit(p + 1);
            if (y > 0) visit(p - w); if (y + 1 < h) visit(p + w);
        }
        int left = w, bottom = h, right = 0, top = 0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            if (!exterior[y * w + x]) { left = Mathf.Min(left, x); right = Mathf.Max(right, x); bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y); }
        if (right <= left || top <= bottom) { left = bottom = 0; right = w - 1; top = h - 1; }
        var rect = new Rect(x0 + left, y0 + bottom, right - left + 1, top - bottom + 1);
        var sprite = Sprite.Create(texture, rect, Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        var vertices = new List<Vector2>(); var triangles = new List<ushort>();
        // Horizontal mesh strips clip the external matte, leaving the original texture untouched.
        const int step = 3;
        for (int y = bottom; y <= top; y += step)
        {
            int lo = right + 1, hi = left - 1;
            int sample = Mathf.Min(y + step / 2, top);
            for (int x = left; x <= right; x++) if (!exterior[sample * w + x]) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
            if (hi < lo) continue;
            ushort n = (ushort)vertices.Count;
            float a = (lo - left - rect.width * 0.5f) / 100f, b = (hi + 1 - left - rect.width * 0.5f) / 100f;
            float c = (y - bottom - rect.height * 0.5f) / 100f, d = (Mathf.Min(y + step, top + 1) - bottom - rect.height * 0.5f) / 100f;
            vertices.Add(new Vector2(a, c)); vertices.Add(new Vector2(a, d)); vertices.Add(new Vector2(b, d)); vertices.Add(new Vector2(b, c));
            triangles.Add(n); triangles.Add((ushort)(n + 1)); triangles.Add((ushort)(n + 2));
            triangles.Add(n); triangles.Add((ushort)(n + 2)); triangles.Add((ushort)(n + 3));
        }
        if (vertices.Count > 0) sprite.OverrideGeometry(vertices.ToArray(), triangles.ToArray());
        if (existing != null) { EditorUtility.CopySerialized(sprite, existing); Object.DestroyImmediate(sprite); sprite = existing; }
        else AssetDatabase.CreateAsset(sprite, output);
        var meshTriangles = new int[triangles.Count];
        for (int i = 0; i < meshTriangles.Length; i++) meshTriangles[i] = triangles[i];
        Meshes.Add(new PortraitUiArt.MeshData { Sprite = sprite, Vertices = vertices.ToArray(), Triangles = meshTriangles });
        EditorUtility.SetDirty(sprite); AssetDatabase.SaveAssetIfDirty(sprite);
        importer.isReadable = readable; importer.SaveAndReimport();
        return sprite;
    }
}
