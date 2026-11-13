using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mjolnir.SDK;
using SandSailorStudio.Localization;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.Localization;
using UnityEngine;
using GameSettingsMenu = SSSGame.UI.SettingsMenu;
using Il2CppStrings = Il2CppSystem.Collections.Generic.Dictionary<string, string>;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "localization" scenario (autopilot.once containing "localization"), the research behind the SDK's localization:
/// <list type="bullet">
/// <item>when the game loads a language and into which dictionary (<see cref="LocalizationProbes"/>, installed only for
/// this scenario, which also adds a test key on every load);</item>
/// <item>the manager's state: the language and where it comes from, the fallback, the packs and language codes;</item>
/// <item>what Loc gives for a missing key, for a key only the fallback has, and for an empty translation;</item>
/// <item>a key added after loading, and a changed game string behind the manager's cache;</item>
/// <item>SetLanguage to English and back: the probes, OnLanguageChanged, the game's texts (screenshots);</item>
/// <item>a language row in the game's settings, by the keys of its texts;</item>
/// <item>the SDK's Localization (connected to the game by Mjolnir.SDK.Content) against all that: the language it follows,
/// examples/Mjolnir.UiExample's strings in the game's dictionaries and through Localization.Get, a bound text and
/// LanguageChanged across the switch.</item>
/// </list>
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> LocalizationScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 2f;
        var manager = LocalizationManager.Instance;
        if (manager == null)
        {
            Log("Localization: no LocalizationManager");
            if (QuitWhenDone) Application.Quit();
            yield break;
        }
        Log($"Localization state: {DescribeLocalization(manager)}");

        // Languages: which have a pack and which codes name them.
        var packs = manager.GetDatabase()?.packs;
        Log($"Localization database: {packs?.Length ?? 0} pack(s): " +
            (packs == null ? "" : string.Join(", ", packs.Select(p => $"{p.language} ({p.pack?.data?.Length ?? -1} strings)"))));
        foreach (Language language in Enum.GetValues<Language>())
        {
            var codes = manager.GetLanguageCodes(language);
            var pack = manager.GetLanguagePack(language);
            Log($"Localization language {language} ({(int)language}): supported {manager.IsLanguageSupported(language)}, " +
                $"pack {(pack == null ? "none" : $"{pack.data?.Length ?? -1} strings")}, codes [{(codes == null ? "" : string.Join(", ", codes.ToArray()))}]");
        }
        var codeList = manager.languageCodes?.languageCodes;
        Log($"Localization code list: {(codeList == null ? "none" : string.Join(", ", codeList.ToArray().Select(c => $"{c.lang}={c.code}")))}");

        // Missing, fallback-only and empty keys.
        const string missing = "mjolnir.research.missing";
        Log($"Localization missing key: Loc '{manager.Loc(missing, false)}', Loc(fallback) '{manager.Loc(missing, true)}', " +
            $"LocFallback '{manager.LocFallback(missing)}', KeyExists {manager.KeyExists(missing)}, index {manager.GetKeyIndex(missing)}");
        var local = manager.LocalizedStrings;
        var fallback = manager.FallbackLocalizedStrings;
        string? fallbackOnly = null, empty = null;
        int fallbackOnlyCount = 0, emptyCount = 0;
        foreach (var pair in fallback)
        {
            if (!local.ContainsKey(pair.Key))
            {
                fallbackOnlyCount++;
                fallbackOnly ??= pair.Key;
            }
            else if (string.IsNullOrEmpty(local[pair.Key]) && !string.IsNullOrEmpty(pair.Value))
            {
                emptyCount++;
                empty ??= pair.Key;
            }
        }
        Log($"Localization fallback-only keys: {fallbackOnlyCount}" +
            (fallbackOnly == null ? "" : $", e.g. '{fallbackOnly}': Loc '{manager.Loc(fallbackOnly, false)}', fallback text '{fallback[fallbackOnly]}'"));
        Log($"Localization keys with an empty translation: {emptyCount}" +
            (empty == null ? "" : $", e.g. '{empty}': Loc '{manager.Loc(empty, false)}', fallback text '{fallback[empty]}'"));
        Log($"Localization key indexes: common.settings {manager.GetKeyIndex("common.settings")}, common.on {manager.GetKeyIndex("common.on")}");

        // A key added after the language was loaded, and a game string changed behind the cache.
        local["mjolnir.research.runtime"] = "added after loading";
        Log($"Localization key added after loading: Loc '{manager.Loc("mjolnir.research.runtime", false)}', " +
            $"KeyExists {manager.KeyExists("mjolnir.research.runtime")} (not in _allKeys), index {manager.GetKeyIndex("mjolnir.research.runtime")}");
        string settings = manager.Loc("common.settings", false);
        local["common.settings"] = "Settings (changed by a mod)";
        string cached = manager.Loc("common.settings", false);
        manager._cache.Clear();
        string afterClear = manager.Loc("common.settings", false);
        local["common.settings"] = settings;
        manager._cache.Clear();
        Log($"Localization changed game string: before '{settings}', changed but cached '{cached}', after _cache.Clear '{afterClear}', restored '{manager.Loc("common.settings", false)}'");

        // Mjolnir.SDK.Content: UiExample's strings (needs -Examples), the game's key, a missing key, a bound text.
        const string exampleKey = "mjolnir.uiexample.section.form";
        Log($"Content: {DescribeContent(manager, exampleKey)}; game key '{Localization.Get("common.settings")}', missing '{Localization.Get("no.such.key")}', " +
            $"fallback dictionary '{(fallback.ContainsKey(exampleKey) ? fallback[exampleKey] : "none")}', formatted '{Localization.Get("{0:0.0} / {1}", 1.5, "x")}'");
        var bound = Localization.GetBindable(exampleKey);
        bound.Changed += (_, e) => Log($"Content: bound text changed '{e.OldValue}' → '{e.NewValue}' at frame {Time.frameCount}");
        Localization.LanguageChanged += (_, e) => Log($"Content: LanguageChanged {e.OldValue} → {e.NewValue} at frame {Time.frameCount}");

        // Switching the language: the probes, OnLanguageChanged, the game's texts.
        var original = manager.SystemLanguage;
        int changed = 0;
        var handler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Language>>(new Action<Language>(language =>
        {
            changed++;
            Log($"Localization OnLanguageChanged({language}) at frame {Time.frameCount}");
        }))!;
        manager.OnLanguageChanged = manager.OnLanguageChanged == null
            ? handler
            : Il2CppSystem.Delegate.Combine(manager.OnLanguageChanged, handler).Cast<Il2CppSystem.Action<Language>>();
        LocalizationProbes.Injected["mjolnir.research.hello"] = "Hello from a mod";
        Log($"Localization texts before the switch: {SampleTexts()}");
        Shot("menu-" + original);
        yield return AfterShot;

        manager.SetLanguage(Language.english);
        yield return 1f;
        Log($"Localization after SetLanguage(english): {DescribeLocalization(manager)}; OnLanguageChanged {changed} time(s); " +
            $"Loc common.settings '{manager.Loc("common.settings", false)}', injected '{manager.Loc("mjolnir.research.hello", false)}' " +
            $"KeyExists {manager.KeyExists("mjolnir.research.hello")}, runtime key '{manager.Loc("mjolnir.research.runtime", false)}'");
        Log($"Localization texts in English: {SampleTexts()}");
        Log($"Content in English: {DescribeContent(manager, exampleKey)}; bound text '{bound}'");
        Shot("menu-english");
        yield return AfterShot;

        manager.SetLanguage(original);
        yield return 1f;
        Log($"Localization after SetLanguage({original}): {DescribeLocalization(manager)}; OnLanguageChanged {changed} time(s); " +
            $"Loc common.settings '{manager.Loc("common.settings", false)}', injected '{manager.Loc("mjolnir.research.hello", false)}'");
        Log($"Localization texts back in {original}: {SampleTexts()}");
        Log($"Content back in {original}: {DescribeContent(manager, exampleKey)}; bound text '{bound}'");
        manager.OnLanguageChanged = Il2CppSystem.Delegate.Remove(manager.OnLanguageChanged, handler)?.Cast<Il2CppSystem.Action<Language>>();

        // A language row in the game's settings: its texts' keys, and the game's keys that mention a language.
        var settingsPrefab = MenuManager.Instance?.GetPrefab<GameSettingsMenu>();
        var keys = settingsPrefab == null ? [] : settingsPrefab.GetComponentsInChildren<LocalizedText>(true).Select(t => t.key).Where(k => !string.IsNullOrEmpty(k)).ToList();
        Log($"Localization settings menu: {keys.Count} localized text(s); with 'lang': [{string.Join(", ", keys.Where(k => k.Contains("lang", StringComparison.OrdinalIgnoreCase)))}]");
        var languageKeys = new List<string>();
        foreach (var pair in local)
            if (pair.Key.Contains("language", StringComparison.OrdinalIgnoreCase)) languageKeys.Add($"{pair.Key}='{pair.Value}'");
        Log($"Localization game keys with 'language': {languageKeys.Count}: {string.Join("; ", languageKeys.Take(20))}");

        Log("Localization scenario done");
        if (QuitWhenDone) Application.Quit();
    }

    internal static string DescribeLocalization(LocalizationManager manager) =>
        $"language field {manager.language}, forceLanguage {manager.forceLanguage}, game language {manager._gameLanguage} (set {manager._gameLanguageSet}), " +
        $"fallback {manager._fallbackLanguage}, SystemLanguage {manager.SystemLanguage}, strings {manager._localizedStrings?.Count ?? -1}, " +
        $"fallback strings {manager._localizedFallbackStrings?.Count ?? -1}, all keys {LocalizationManager._allKeys?.Count ?? -1}, " +
        $"key indexes {manager._keyIndexes?.Count ?? -1}";

    private static string DescribeContent(LocalizationManager manager, string key) =>
        $"Language {Localization.Language} ({Localization.Culture.Name}), '{key}': Get '{Localization.Get(key)}', game Loc '{manager.Loc(key, false)}', " +
        $"KeyExists {manager.KeyExists(key)}";

    // A few texts of the main menu as the game shows them, with their keys.
    private static string SampleTexts() =>
        string.Join(" | ", Object.FindObjectsByType<LocalizedText>(FindObjectsSortMode.None)
                                 .Where(t => t.isActiveAndEnabled && !string.IsNullOrEmpty(t.key))
                                 .Take(6)
                                 .Select(t => $"{t.key}='{t.GetComponent<TMPro.TMP_Text>()?.text}'"));
}

/// <summary>
/// Probes for the "localization" scenario: every language the game loads, and into which of its two dictionaries; each
/// load also gets the keys in <see cref="Injected"/>, marked with the language, as a mod's strings would.
/// </summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class LocalizationProbes
{
    public static readonly Dictionary<string, string> Injected = [];

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager._LoadLanguage))]
    private static void LanguageLoaded(LocalizationManager __instance, Language lang, ref Il2CppStrings localizedStrings)
    {
        string which = localizedStrings == null ? "null"
            : __instance._localizedStrings != null && localizedStrings.Pointer == __instance._localizedStrings.Pointer ? "main"
            : __instance._localizedFallbackStrings != null && localizedStrings.Pointer == __instance._localizedFallbackStrings.Pointer ? "fallback"
            : "other";
        foreach (var (key, text) in Injected)
        {
            localizedStrings![key] = $"{text} ({lang})";
            LocalizationManager._allKeys.Add(key);
        }
        UiLab.Instance.Log.Info($"Probe LocalizationManager._LoadLanguage({lang}) at frame {Time.frameCount}: {which} dictionary, " +
                                $"{localizedStrings?.Count ?? -1} strings, {Injected.Count} injected");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.SetLanguage))]
    private static void LanguageSet(Language language) =>
        UiLab.Instance.Log.Info($"Probe LocalizationManager.SetLanguage({language}) at frame {Time.frameCount}");
}
