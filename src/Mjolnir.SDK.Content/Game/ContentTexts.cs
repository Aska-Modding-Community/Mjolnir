using System.Collections.Generic;
using System.Linq;
using SandSailorStudio.Inventory;
using SandSailorStudio.Localization;
using GameStringDictionary = Il2CppSystem.Collections.Generic.Dictionary<string, string>;

namespace Mjolnir.SDK.Content;

/// <summary>
/// The texts of the mods' content in the game's dictionaries: under the keys the game makes for an item, a building or an
/// effect, the mod's <c>[&lt;section&gt;.&lt;name&gt;] &lt;key&gt;</c> in its language files, in the player's language or
/// else in English. A language loaded later gets them from the localization postfix, one loaded already at once.
/// </summary>
internal static class ContentTexts
{
    // The game's item tokens and the mod's keys under [item.<name>] for them; a building's under [building.<name>].
    private static readonly (string Token, string Key)[] Texts = [("name", "name"), ("desc", "description"), ("lore", "lore")];

    private static readonly List<(ModStrings? Strings, string Section, string Name, (string Key, string GameKey)[] Keys)> s_texts = [];

    /// <summary>
    /// The game loaded a language into <paramref name="strings"/>: the mods' item and building texts go in too (the
    /// localization postfix calls this), in <paramref name="language"/> or else in English.
    /// </summary>
    public static int AddTexts(GameStringDictionary strings, string language, Il2CppSystem.Collections.Generic.HashSet<string>? keys)
    {
        int count = 0;
        foreach (var (owner, section, name, texts) in s_texts)
        {
            for (int i = 0; i < texts.Length; i++)
            {
                string text = TextOf(owner, section, name, texts[i].Key, language);
                // A text the mod does not give is empty, never left out: the game shows a missing key as it is (the
                // inventory an item's lore, the build menu a building's description; seen in game). A missing name is the
                // content's own name.
                strings[texts[i].GameKey] = text.Length > 0 || i > 0 ? text : name;
                keys?.Add(texts[i].GameKey);
                count++;
            }
        }
        return count;
    }

    /// <summary>Other content's texts into the same dictionaries; a missing one is an empty text.</summary>
    public static void Add(ModStrings? owner, string section, string name, (string Key, string GameKey)[] texts)
    {
        s_texts.Add((owner, section, name, texts));
        LoadedLanguages();
    }

    // The keys the game makes for the item's or building's texts (item.<group>_<asset>_<token>) get the mod's texts,
    // [<section>.<name>] <key> in its language files.
    public static void Add(ModStrings? owner, string section, string name, ItemInfo info)
    {
        s_texts.Add((owner, section, name, Texts.Select(t => (t.Key, info.GetKey(t.Token))).ToArray()));
        LoadedLanguages();
    }

    private static void LoadedLanguages()
    {
        var manager = LocalizationManager.Instance;
        if (manager == null) return;
        // A language already loaded (the item is made after it): its dictionaries get the texts now.
        if (manager._localizedStrings != null) AddTexts(manager._localizedStrings, Localization.Language, LocalizationManager._allKeys);
        if (manager._localizedFallbackStrings != null) AddTexts(manager._localizedFallbackStrings, GameLanguages.Fallback, LocalizationManager._allKeys);
        // A key the game looked up before its text was here stays as it found it in the manager's cache (the fallback's
        // English, or the key itself).
        manager._cache?.Clear();
    }

    private static string TextOf(ModStrings? owner, string section, string name, string key, string language) =>
        owner != null && owner.TryGet($"{section}.{name}.{key}", language, out string text) ? text : "";
}
