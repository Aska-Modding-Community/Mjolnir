namespace Mjolnir.SDK.UI;

/// <summary>
/// What the Mods window shows for a mod's settings, in the player's language when the mod's lang files have it (no
/// Unity: unit-tested). The keys, in <c>lang/&lt;language&gt;.toml</c> (English is the fallback, as for every string):
/// <code>
/// [mod]
/// name = "…"            # the mod's row in the list
/// description = "…"
///
/// [sections]
/// HUD = "…"             # a section's header
///
/// [config.HUD.ShowText] # a setting's row: [config.&lt;section&gt;.&lt;key&gt;]
/// name = "…"
/// description = "…"
/// </code>
/// Without them: the manifest's name and description, the section's name, the setting's display name (else its key) and
/// description.
/// </summary>
internal static class SettingTexts
{
    public static string NameOf(string section, string key, string? displayName, ModStrings? strings) =>
        Find(strings, $"config.{section}.{key}.name") ?? displayName ?? key;

    public static string? DescriptionOf(string section, string key, string? description, ModStrings? strings) =>
        Find(strings, $"config.{section}.{key}.description") ?? description;

    public static string SectionOf(string section, ModStrings? strings) => Find(strings, $"sections.{section}") ?? section;

    public static string ModNameOf(string name, ModStrings? strings) => Find(strings, "mod.name") ?? name;

    public static string? ModDescriptionOf(string? description, ModStrings? strings) => Find(strings, "mod.description") ?? description;

    /// <summary>The object name of a setting's row (<c>Setting HUD.ShowText</c>), the same in every language.</summary>
    public static string RowName(string section, string key) => $"Setting {section}.{key}";

    private static string? Find(ModStrings? strings, string key) =>
        strings != null && strings.TryGet(key, Localization.Language, out string text) ? text : null;
}
