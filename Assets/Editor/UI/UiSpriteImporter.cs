using UnityEditor;

namespace Game.EditorTools
{
    /// <summary>
    /// Imports anything dropped into <c>Assets/Resources/UI/</c> as a Sprite.
    ///
    /// WHY THIS HAS TO EXIST. The UI art in this project is generated, not drawn by hand: a dozen
    /// scripts under <c>Tools/ui/</c> write PNGs straight into that folder. But the project is in 3D
    /// behaviour mode (<c>EditorSettings.m_DefaultBehaviorMode: 0</c>), so a new PNG imports as a plain
    /// texture — and every one of those files is fetched with <c>Resources.Load&lt;Sprite&gt;</c>, which
    /// answers null for a texture. The symptom is a button that draws as a blank coloured plate, which
    /// is exactly what the league opener did, and it looks like a missing file rather than a wrong
    /// import setting.
    ///
    /// IT ONLY TOUCHES A FIRST IMPORT. <see cref="AssetImporter.importSettingsMissing"/> is true only
    /// when there is no .meta yet, so this decides the default for a newly generated file and never
    /// overrides a setting anybody has chosen since — the sea set's Repeat wrap, for one, has to
    /// survive a reimport untouched.
    /// </summary>
    public sealed class UiSpriteImporter : AssetPostprocessor
    {
        private const string UiResources = "Assets/Resources/UI/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiResources, System.StringComparison.Ordinal)) return;

            var importer = (TextureImporter)assetImporter;
            if (!importer.importSettingsMissing) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
        }
    }
}
