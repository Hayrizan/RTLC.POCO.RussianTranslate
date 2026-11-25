using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace RTLC.Poco.RussianTranslate
{
    internal sealed class TextureOverrides
    {
        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly AssetBundle bundle;
        private readonly HashSet<Texture2D> seen = new HashSet<Texture2D>();
        private readonly HashSet<Texture2D> texturesWithNewContours = new HashSet<Texture2D>();
        private readonly HashSet<Sprite> expandedSprites = new HashSet<Sprite>();
        private static readonly ushort[] QuadTriangles = { 0, 1, 2, 2, 3, 0 };
        private readonly ManualLogSource log;

        public TextureOverrides(AssetBundle bundle, ManualLogSource log)
        {
            this.bundle = bundle;
            this.log = log;
            foreach (var address in bundle.GetAllAssetNames())
            {
                if (address.StartsWith("textures/", StringComparison.Ordinal) &&
                    address.EndsWith(".png", StringComparison.Ordinal))
                    files.Add(Path.GetFileNameWithoutExtension(address), address);
            }
            log.LogInfo($"Indexed {files.Count} texture overrides from rtlc.bundle.");
        }

        public bool HasOverrides => files.Count > 0;

        public void ApplyLoaded(string context = "resource scan")
        {
            seen.RemoveWhere(texture => !texture);
            foreach (var texture in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (!texture || seen.Contains(texture))
                    continue;

                var qualifiedName = $"{texture.name}.{texture.width}x{texture.height}";
                if (!files.TryGetValue(qualifiedName, out var path) &&
                    !files.TryGetValue(texture.name, out path))
                    continue;

                seen.Add(texture);
                try
                {
                    Replace(texture, path);
                    if (texture.name == "Logo" || texture.name == "PigmentsText")
                        texturesWithNewContours.Add(texture);
                    log.LogInfo($"Replaced texture {texture.name} ({texture.width}x{texture.height}) from {Path.GetFileName(path)} [{context}].");
                }
                catch (Exception error)
                {
                    log.LogWarning($"Texture {texture.name}: {error.Message}");
                }
            }

            ExpandSpriteGeometry(context);
        }

        private void ExpandSpriteGeometry(string context)
        {
            texturesWithNewContours.RemoveWhere(texture => !texture);
            if (texturesWithNewContours.Count == 0)
                return;

            expandedSprites.RemoveWhere(sprite => !sprite);
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (!sprite || expandedSprites.Contains(sprite) || !texturesWithNewContours.Contains(sprite.texture))
                    continue;

                // Keep the original sprite, UV rectangle and pivot, but let the replacement's alpha define its outline.
                var size = sprite.rect.size;
                sprite.OverrideGeometry(new[] { Vector2.zero, new Vector2(0, size.y), size, new Vector2(size.x, 0) },
                    QuadTriangles);
                expandedSprites.Add(sprite);
                log.LogInfo($"Expanded {sprite.name} sprite to {size.x}x{size.y} [{context}].");
            }
        }

        private void Replace(Texture2D original, string path)
        {
            var asset = bundle.LoadAsset<TextAsset>(path);
            if (!asset)
                throw new InvalidDataException($"Missing PNG asset in rtlc.bundle: {path}.");

            var bytes = asset.bytes;
            var pngHeader = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
            if (bytes.Length < 24 || !bytes.Take(pngHeader.Length).SequenceEqual(pngHeader))
                throw new InvalidDataException("Expected a PNG with an IHDR chunk.");

            var width = ReadBigEndian(bytes, 16);
            var height = ReadBigEndian(bytes, 20);
            if (width != original.width || height != original.height)
            {
                if (original.name != "Logo" || original.width != 1265 || original.height != 544 ||
                    width != 1280 || height != 720 || path != "textures/logo.png")
                    throw new InvalidDataException($"Expected {original.width}x{original.height}, found {width}x{height}.");

                bytes = CropMenuLogo(bytes);
            }

            var readable = original.isReadable;
            if (!ImageConversion.LoadImage(original, bytes, markNonReadable: !readable))
                throw new InvalidDataException("Unity could not load the replacement into the original texture.");
        }

        private static byte[] CropMenuLogo(byte[] png)
        {
            // The 1280x720 logo uses the same artwork at (0, 94) from the top left.
            const int width = 1265;
            const int height = 544;
            const int bottom = 82;
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var cropped = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(source, png))
                    throw new InvalidDataException("Unity could not decode the 1280x720 logo.");

                var pixels = source.GetPixels32();
                var result = new Color32[width * height];
                for (var y = 0; y < height; y++)
                    Array.Copy(pixels, (y + bottom) * source.width, result, y * width, width);

                cropped.SetPixels32(result);
                return ImageConversion.EncodeToPNG(cropped);
            }
            finally
            {
                UnityEngine.Object.Destroy(cropped);
                UnityEngine.Object.Destroy(source);
            }
        }

        private static int ReadBigEndian(byte[] bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }
}
