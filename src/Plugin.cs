using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RTLC.Poco.RussianTranslate
{
    [BepInPlugin("rtlc.poco.russiantranslate", "Poco Russian Translate", "0.1.0")]
    [BepInProcess("Poco.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private static Plugin instance;
        private static readonly Regex WiggleTags = new Regex(@"</?w>");
        private readonly Dictionary<string, string> dialogue = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> ui = new Dictionary<string, string>(StringComparer.Ordinal);
        private Font font;
        private TMP_FontAsset regularFont;
        private TMP_FontAsset pocoFont;
        private ConfigEntry<bool> translatePoco;
        private AssetBundle bundle;
        private TextureOverrides textures;

        private void Awake()
        {
            instance = this;
            translatePoco = Config.Bind("Dialogue", "TranslatePoco", false, "Show Poco's lines in Russian using the standard game font.");
            regularFont = Resources.Load<TMP_FontAsset>("Poco-Regular SDF");
            pocoFont = Resources.Load<TMP_FontAsset>("Poco_jinglejangle-Regular SDF");
            var directory = Path.GetDirectoryName(Info.Location);
            var bundlePath = Path.Combine(directory, "rtlc.bundle");
            bundle = File.Exists(bundlePath) ? AssetBundle.LoadFromFile(bundlePath) : null;
            if (!bundle)
            {
                Logger.LogError($"Could not load the localization AssetBundle: {bundlePath}");
                return;
            }

            LoadCatalog(bundle.LoadAsset<TextAsset>("russian"));
            try
            {
                LoadFont(bundle.LoadAsset<Font>("poco-cyrillic"));
            }
            catch (Exception error)
            {
                Logger.LogError($"Cyrillic font could not be loaded: {error}");
            }

            var harmony = new Harmony(Info.Metadata.GUID);
            harmony.PatchAll(typeof(Plugin));
            Logger.LogInfo($"Installed {harmony.GetPatchedMethods().Count()} Harmony patches.");
            Logger.LogInfo($"Loaded {dialogue.Count} dialogue and {ui.Count} UI translations.");

            textures = new TextureOverrides(bundle, Logger);
            if (textures.HasOverrides)
            {
                SceneManager.sceneLoaded += ApplySceneTextures;
                StartCoroutine(ScanTextures());
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= ApplySceneTextures;
            if (bundle)
                bundle.Unload(false);
        }

        private void ApplySceneTextures(Scene scene, LoadSceneMode mode) =>
            textures.ApplyLoaded($"scene load: {scene.name}");

        private void LoadFont(Font bundledFont)
        {
            if (!bundledFont || !bundledFont.dynamic)
                throw new InvalidDataException("The AssetBundle is missing the dynamic Cyrillic font.");

            font = bundledFont;
            var regularFallback = CreateFontFallback(regularFont);

            TMP_Settings.fallbackFontAssets?.Insert(0, regularFallback);
        }

        private TMP_FontAsset CreateFontFallback(TMP_FontAsset original)
        {
            if (!original)
                throw new InvalidOperationException("A Poco TextMeshPro font asset is missing.");

            var face = original.faceInfo;
            var fallback = TMP_FontAsset.CreateFontAsset(font, face.pointSize, original.atlasPadding,
                original.atlasRenderMode, original.atlasWidth, original.atlasHeight);
            if (!fallback)
                throw new InvalidOperationException($"TextMeshPro could not create a fallback for {original.name}.");

            fallback.name = $"{original.name} Cyrillic";
            fallback.TryAddCharacters("АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя—«»",
                out var missing);

            // Match the parent atlas, including baseline, line height and the game's custom scale.
            fallback.faceInfo = face;
            if (original.fallbackFontAssetTable == null)
                original.fallbackFontAssetTable = new List<TMP_FontAsset>();
            original.fallbackFontAssetTable.Insert(0, fallback);

            Logger.LogInfo($"Registered {fallback.name}: {face.pointSize} pt, scale {face.scale}, " +
                $"missing Russian letters or punctuation: {(string.IsNullOrEmpty(missing) ? "none" : missing)}.");
            return fallback;
        }

        private void LoadCatalog(TextAsset asset)
        {
            try
            {
                if (!asset)
                    throw new InvalidDataException("The AssetBundle is missing the Russian catalog.");

                using var stream = new MemoryStream(asset.bytes, writable: false);
                var catalog = (Catalog)new DataContractJsonSerializer(typeof(Catalog)).ReadObject(stream);
                if (catalog == null)
                    throw new InvalidDataException("Expected a JSON object.");

                AddEntries(catalog.dialogue, dialogue, preserveWiggle: true);
                AddEntries(catalog.ui, ui, preserveWiggle: false);
            }
            catch (Exception error)
            {
                Logger.LogWarning($"Could not read the Russian catalog from rtlc.bundle: {error.Message}");
            }
        }

        private void AddEntries(Entry[] entries, Dictionary<string, string> destination, bool preserveWiggle)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.en) || string.IsNullOrWhiteSpace(entry.ru))
                    continue;

                if (preserveWiggle && !WiggleTags.Matches(entry.en).Cast<Match>().Select(match => match.Value)
                        .SequenceEqual(WiggleTags.Matches(entry.ru).Cast<Match>().Select(match => match.Value)))
                {
                    Logger.LogWarning($"Ignored dialogue with changed <w> tags: {entry.en}");
                    continue;
                }

                if (!destination.TryAdd(entry.en, entry.ru))
                    Logger.LogWarning($"Duplicate translation ignored: {entry.en}");
            }
        }

        private IEnumerator ScanTextures()
        {
            while (true)
            {
                textures.ApplyLoaded();
                yield return new WaitForSecondsRealtime(1);
            }
        }

        private string Translate(string value) =>
            value != null && ui.TryGetValue(value, out var translated) ? translated : value;

        private static bool HasCyrillic(string value) =>
            value != null && value.Any(character => character >= '\u0400' && character <= '\u052f');

        [HarmonyPostfix, HarmonyPatch(typeof(WorldController), nameof(WorldController.GetDialogueInfo))]
        private static void TranslateQuote(string[] __result)
        {
            if (__result[1] == "0" && !instance.translatePoco.Value)
                return;

            // Translate before the game calculates the indices for the <w> effect.
            if (instance.dialogue.TryGetValue(__result[0], out var translated))
                __result[0] = translated;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(WorldController), "ReadDialogueOption")]
        private static void TranslateOption(string[] __result)
        {
            // Element zero is the branch designation used by WorldController.
            if (__result.Length > 1 && instance.dialogue.TryGetValue(__result[1], out var translated))
                __result[1] = translated;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.text), MethodType.Setter)]
        private static void TranslateTmpSetter(TMP_Text __instance, ref string __0)
        {
            if (instance.pocoFont && __instance.font == instance.pocoFont)
            {
                if (instance.translatePoco.Value && instance.regularFont)
                    __instance.font = instance.regularFont;
                return;
            }

            __0 = instance.Translate(__0);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(WorldController), "Start")]
        private static void AddPocoSetting(WorldController __instance)
        {
            var panel = __instance.transform.Find("PauseMenu/SettingsFillPanel/SettingsPanel");
            var source = panel?.Find("AlwaysRunToggle")?.GetComponent<Toggle>();
            if (!source || panel.Find("TranslatePocoToggle"))
                return;

            var toggle = Instantiate(source, panel);
            toggle.name = "TranslatePocoToggle";
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.group = null;
            toggle.SetIsOnWithoutNotify(instance.translatePoco.Value);

            var rect = (RectTransform)toggle.transform;
            const float width = 210f;
            rect.anchoredPosition += new Vector2((width - rect.sizeDelta.x) * rect.pivot.x * rect.localScale.x, -60f);
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);

            var textCrawl = panel.Find("TextCrawlToggle") as RectTransform;
            if (textCrawl)
            {
                var alwaysRun = (RectTransform)source.transform;
                var middle = alwaysRun.anchoredPosition;
                middle.y = (textCrawl.anchoredPosition.y + rect.anchoredPosition.y) * 0.5f;
                alwaysRun.anchoredPosition = middle;
            }

            toggle.transform.Find("Label").GetComponent<TMP_Text>().text = "Перевод реплик Поко";
            toggle.onValueChanged.AddListener(enabled => instance.translatePoco.Value = enabled);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TextMeshProUGUI), "OnEnable")]
        private static void TranslateSerializedTmp(TMP_Text __instance)
        {
            var original = __instance.text;
            var translated = instance.Translate(original);
            if (translated != original)
            {
                if (__instance.name == "Credits" && __instance.gameObject.scene.name == "Credits")
                    translated = FitCredits(__instance, translated);
                __instance.text = translated;
            }
        }

        private static string FitCredits(TMP_Text text, string translation)
        {
            // The scroll animation is timed for one rendered line per source line.
            text.enableWordWrapping = false;
            var width = text.rectTransform.rect.width - text.margin.x - text.margin.z;
            if (width <= 0f)
                return translation;

            var lines = Regex.Split(translation, @"\r\n|\n\r|\r|\n");
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0)
                    continue;

                var preferredWidth = text.GetPreferredValues(lines[i], Mathf.Infinity, Mathf.Infinity).x;
                if (preferredWidth <= width)
                    continue;

                var percent = Mathf.FloorToInt(98f * width / preferredWidth);
                lines[i] = $"<size={percent}%>{lines[i]}</size>";
            }

            return string.Join("\n", lines);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TextMeshPro), "OnEnable")]
        private static void TranslateWorldTmp(TextMeshPro __instance) => TranslateSerializedTmp(__instance);

        [HarmonyPrefix, HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
        private static void TranslateLegacySetter(Text __instance, ref string __0)
        {
            __0 = instance.Translate(__0);
            if (instance.font && HasCyrillic(__0) && __instance.font != instance.font)
                __instance.font = instance.font;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Text), "OnEnable")]
        private static void TranslateSerializedLegacy(Text __instance)
        {
            var original = __instance.text;
            var translated = original;
            TranslateLegacySetter(__instance, ref translated);
            if (translated != original)
                __instance.text = translated;
        }

        [DataContract]
        private sealed class Catalog
        {
            [DataMember]
            public Entry[] dialogue = null;
            [DataMember]
            public Entry[] ui = null;
        }

        [DataContract]
        private sealed class Entry
        {
            [DataMember]
            public string en = null;
            [DataMember]
            public string ru = null;
        }
    }
}
