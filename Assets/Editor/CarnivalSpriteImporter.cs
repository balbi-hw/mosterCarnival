using UnityEditor;
using UnityEngine;

public class CarnivalSpriteImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/CarnivalArt/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 16;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
    }
}

[InitializeOnLoad]
public static class CarnivalArtSetup
{
    static CarnivalArtSetup()
    {
        EditorApplication.delayCall += EnsureSpriteSettings;
    }

    static void EnsureSpriteSettings()
    {
        string[] names = { "player", "chaser", "ranger", "shield", "ogre", "boss" };
        foreach (string name in names)
        {
            string path = "Assets/Resources/CarnivalArt/" + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || (importer.textureType == TextureImporterType.Sprite && importer.spritePixelsPerUnit == 16)) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}
