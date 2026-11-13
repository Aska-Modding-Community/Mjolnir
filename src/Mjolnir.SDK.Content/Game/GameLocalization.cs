using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mjolnir.SDK.Threading;
using SandSailorStudio.Localization;
using GameStringDictionary = Il2CppSystem.Collections.Generic.Dictionary<string, string>;

namespace Mjolnir.SDK.Content;

/// <summary>
/// The game side of the SDK's <see cref="Localization"/>: its hidden patch on the game's language loading, the game's
/// strings, and its log. Nothing here throws into the game or into a mod: SDK faults are logged as
/// [SDK.Content].
/// </summary>
internal static class GameLocalization
{
    public const string Source = "SDK.Content";

    /// <summary>The module's start (phase A: Harmony only, no Unity objects).</summary>
    public static void Start()
    {
        try
        {
            Dispatcher.Hook(); // records the main thread at GameReady: the game's strings are read only there
            Localization.Game = new GameStrings();
            SdkLog.Info(Source, $"Localization started: {Install()} game method patched");
        }
        catch (Exception e)
        {
            SdkLog.Error(Source, $"Localization not started: {e}");
        }
    }

    // Harmony and game types are touched only here, after Il2CppInterop is up. The count
    // catches a patch class Harmony skipped silently (it did once: no class-level [HarmonyPatch]).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Install() =>
        new Harmony("mjolnir.sdk.content").CreateClassProcessor(typeof(LocalizationPatches)).Patch()?.Count ?? 0;
}

/// <summary>
/// The game's own strings: <c>LocalizationManager.KeyExists</c>, then <c>Loc</c>, which falls
/// back to English for an empty translation. On the main thread only, once the manager exists (the loader scene).
/// </summary>
internal sealed class GameStrings : IStringsSource
{
    public bool TryGet(string key, out string text)
    {
        text = "";
        return GameLoop.IsGameReady && Dispatcher.IsMainThread && Read(key, ref text);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Read(string key, ref string text)
    {
        var manager = LocalizationManager.Instance;
        if (manager == null || !manager.KeyExists(key)) return false;
        text = manager.Loc(key, false);
        return true;
    }
}

/// <summary>
/// Every language the game loads (LocalizationManager._LoadLanguage): its main dictionary at the
/// start (the player's language) and on SetLanguage, the English fallback dictionary right after the first. Each load
/// gets the mods' strings for that language, also in the manager's key set, so KeyExists finds them; the game's key
/// indexes (sent over the network) are left alone. A load of the main dictionary sets the language.
/// </summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class LocalizationPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager._LoadLanguage))]
    private static void LanguageLoaded(LocalizationManager __instance, Language lang, ref GameStringDictionary localizedStrings)
    {
        try
        {
            if (localizedStrings == null) return;
            bool main = __instance._localizedStrings != null && localizedStrings.Pointer == __instance._localizedStrings.Pointer;
            string language = lang.ToString();
            var keys = LocalizationManager._allKeys;
            int count = 0;
            foreach (var strings in ModStringsRegistry.All())
            {
                foreach (var (key, text) in strings.EntriesFor(language))
                {
                    localizedStrings[key] = text;
                    keys?.Add(key);
                    count++;
                }
            }
            count += ContentTexts.AddTexts(localizedStrings, language, keys); // the mods' item names, under the game's keys
            // The manager's cache of looked-up texts (cleared by the game as a load starts) may hold a mod's key looked up
            // in between, before its text was here: a building's name stayed English in a Russian game.
            __instance._cache?.Clear();
            SdkLog.Info(GameLocalization.Source, $"Game loaded {language} ({(main ? "the player's language" : "the fallback")}): {count} mod string(s) added");
            if (main) Localization.LanguageLoaded(language);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameLocalization.Source, $"Adding the mods' strings to {lang} failed: {e}");
        }
    }

    /// <summary>
    /// The English name the game's analytics send (a building placed, built, dismantled; an item crafted…): the game puts it
    /// into the manager's cache of looked-up texts under the same key the player's language uses, so the menus and the
    /// building's card showed a mod building's English name until the cache let it go (seen in game). For mods' content
    /// the name comes from the English dictionary without the cache; the game's own content is left as it is.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(LocalizedAssetExtensions), nameof(LocalizedAssetExtensions.FallbackLocName))]
    private static bool EnglishName(ILocalizedAsset localizedAsset, ref string __result)
    {
        try
        {
            var info = localizedAsset?.TryCast<SandSailorStudio.Inventory.ItemInfo>();
            if (info == null || info.id >= 0) return true; // mods' ids are negative (ContentIds)
            string key = info.GetKey("name");
            var english = LocalizationManager.Instance?.FallbackLocalizedStrings;
            __result = english != null && english.TryGetValue(key, out string text) && !string.IsNullOrEmpty(text) ? text : info.name;
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
