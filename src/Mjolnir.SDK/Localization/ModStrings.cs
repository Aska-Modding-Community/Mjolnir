using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tomlyn;
using Tomlyn.Model;

namespace Mjolnir.SDK;

/// <summary>
/// One mod's strings, from the files in its <c>lang</c> folder: <c>lang/&lt;language&gt;.toml</c>, each key a string.
/// Tables nest with dots (<c>[hud] title = "…"</c> is the key <c>hud.title</c>); the table <c>game</c> holds overrides
/// of the game's own strings by their keys (<c>[game] "common.settings" = "…"</c>). Immutable once read.
/// </summary>
internal sealed class ModStrings
{
    public const string FolderName = "lang";
    private const string GameTable = "game";
    private static readonly TomlSerializerOptions s_toml = new();

    // language → key → text, for the mod's own keys and for overrides of the game's.
    private readonly Dictionary<string, Dictionary<string, string>> _texts;
    private readonly Dictionary<string, Dictionary<string, string>> _overrides;

    private ModStrings(string modId, Dictionary<string, Dictionary<string, string>> texts, Dictionary<string, Dictionary<string, string>> overrides)
    {
        ModId = modId;
        _texts = texts;
        _overrides = overrides;
    }

    public string ModId { get; }

    /// <summary>The prefix of the mod's keys in the game's dictionaries: <c>&lt;mod id&gt;.</c>.</summary>
    public string KeyPrefix => ModId + ".";

    public IEnumerable<string> Languages => _texts.Keys.Union(_overrides.Keys);

    /// <summary>A mod without a lang folder: it has no strings of its own.</summary>
    public static ModStrings Empty(string modId) => new(modId, [], []);

    /// <summary>Reads <c>lang/*.toml</c> under <paramref name="modFolder"/>; a file that does not parse is skipped with a warning.</summary>
    public static ModStrings Load(string modId, string? modFolder, Action<string> warn)
    {
        string folder = Path.Combine(modFolder ?? "", FolderName);
        if (modFolder == null || !Directory.Exists(folder)) return Empty(modId);
        var files = new List<(string Language, string Name, string Text)>();
        foreach (string path in Directory.EnumerateFiles(folder, "*.toml").OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                files.Add((GameLanguages.FromFileName(Path.GetFileNameWithoutExtension(path)), Path.GetFileName(path), File.ReadAllText(path)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                warn($"{FolderName}/{Path.GetFileName(path)} not read: {e.Message}");
            }
        }
        return Parse(modId, files, warn);
    }

    /// <summary>The strings of files already read: (language, file name, its text).</summary>
    public static ModStrings Parse(string modId, IEnumerable<(string Language, string Name, string Text)> files, Action<string> warn)
    {
        var texts = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (language, name, text) in files)
        {
            TomlTable table;
            try
            {
                table = TomlSerializer.Deserialize<TomlTable>(text, s_toml) ?? new TomlTable();
            }
            catch (TomlException e)
            {
                warn($"{FolderName}/{name} skipped: line {e.Line}, column {e.Column}: {e.Message}");
                continue;
            }
            if (!GameLanguages.IsKnown(language))
                warn($"{FolderName}/{name}: '{language}' is not one of the game's languages; its strings are used only if the game reports that name");
            var own = Of(texts, language);
            foreach (var (key, value) in Flatten(table, ""))
            {
                if (value is not string s)
                {
                    warn($"{FolderName}/{name}: '{key}' is not a string; skipped");
                    continue;
                }
                if (key.StartsWith(GameTable + ".", StringComparison.Ordinal)) Of(overrides, language)[key[(GameTable.Length + 1)..]] = s;
                else own[key] = s;
            }
        }
        return new ModStrings(modId, texts, overrides);
    }

    /// <summary>The mod's text for <paramref name="key"/> in <paramref name="language"/>, else in English.</summary>
    public bool TryGet(string key, string language, out string text) =>
        TryIn(_texts, language, key, out text) || TryIn(_texts, GameLanguages.Fallback, key, out text);

    /// <summary>The mod's override of a game string in exactly <paramref name="language"/> (the game's own translation stays otherwise).</summary>
    public bool TryGetOverride(string gameKey, string language, out string text) => TryIn(_overrides, language, gameKey, out text);

    /// <summary>
    /// What goes into the game's dictionary of <paramref name="language"/>: every key of the mod with its prefix, in that
    /// language or else in English, and the mod's overrides in that language.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> EntriesFor(string language)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        if (_texts.TryGetValue(language, out var own)) keys.UnionWith(own.Keys);
        if (_texts.TryGetValue(GameLanguages.Fallback, out var english)) keys.UnionWith(english.Keys);
        foreach (string key in keys)
            if (TryGet(key, language, out string text)) yield return new(KeyPrefix + key, text);
        if (_overrides.TryGetValue(language, out var overrides))
            foreach (var (key, text) in overrides.OrderBy(o => o.Key, StringComparer.Ordinal))
                yield return new(key, text);
    }

    private static Dictionary<string, string> Of(Dictionary<string, Dictionary<string, string>> byLanguage, string language)
    {
        if (!byLanguage.TryGetValue(language, out var strings)) byLanguage[language] = strings = new(StringComparer.Ordinal);
        return strings;
    }

    private static bool TryIn(Dictionary<string, Dictionary<string, string>> byLanguage, string language, string key, out string text)
    {
        if (byLanguage.TryGetValue(language, out var strings) && strings.TryGetValue(key, out var found))
        {
            text = found;
            return true;
        }
        text = "";
        return false;
    }

    private static IEnumerable<(string Key, object? Value)> Flatten(TomlTable table, string prefix)
    {
        foreach (var (key, value) in table)
        {
            string full = prefix + key;
            if (value is TomlTable inner)
            {
                foreach (var entry in Flatten(inner, full + ".")) yield return entry;
            }
            else
            {
                yield return (full, value);
            }
        }
    }
}
