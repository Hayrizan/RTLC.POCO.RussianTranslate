using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class PocoBundleBuilder
{
    public static void Build()
    {
        const string fontPath = "Assets/Content/Poco-Cyrillic-v1.004-Regular.ttf";
        const string catalogPath = "Assets/Content/Russian.bytes";
        var fontImporter = (TrueTypeFontImporter)AssetImporter.GetAtPath(fontPath);
        fontImporter.includeFontData = true;
        fontImporter.fontTextureCase = FontTextureCase.Dynamic;
        fontImporter.SaveAndReimport();

        var textures = Directory.GetFiles("Assets/Content/Textures", "*.png.bytes")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => path.Replace('\\', '/')).ToArray();
        var textureAddresses = textures.Select(path => "textures/" +
            Path.GetFileNameWithoutExtension(path).ToLowerInvariant()).ToArray();
        var paths = new[] { catalogPath, fontPath }.Concat(textures).ToArray();
        var addresses = new[] { "russian", "poco-cyrillic" }.Concat(textureAddresses).ToArray();
        var spec = new AssetBundleBuild
        {
            assetBundleName = "rtlc.bundle",
            assetNames = paths,
            addressableNames = addresses
        };

        Directory.CreateDirectory("Output");
        if (BuildPipeline.BuildAssetBundles("Output", new[] { spec },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64) == null)
            throw new InvalidOperationException("AssetBundle build failed.");

        var bundle = AssetBundle.LoadFromFile(Path.GetFullPath("Output/rtlc.bundle"));
        if (!bundle || bundle.GetAllAssetNames().Length != paths.Length)
            throw new InvalidDataException("Unexpected AssetBundle inventory.");

        for (var i = 0; i < paths.Length; i++)
        {
            if (paths[i] == fontPath)
                continue;
            var asset = bundle.LoadAsset<TextAsset>(addresses[i]);
            if (!asset || !asset.bytes.SequenceEqual(File.ReadAllBytes(paths[i])))
                throw new InvalidDataException($"AssetBundle payload mismatch: {paths[i]}.");
        }

        var font = bundle.LoadAsset<Font>("poco-cyrillic");
        if (!font || !font.dynamic || !"АЯаяЁё—«»".All(font.HasCharacter))
            throw new InvalidDataException("The bundled dynamic font is missing required glyphs.");

        bundle.Unload(true);
        Debug.Log($"Verified {textures.Length} PNG files, one catalog, and one dynamic Cyrillic font.");
    }
}
