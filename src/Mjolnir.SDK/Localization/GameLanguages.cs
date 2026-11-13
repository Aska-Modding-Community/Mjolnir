using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mjolnir.SDK;

/// <summary>
/// The game's languages: Steam's language names, which the game uses for its own language enum,
/// with the codes its code list gives them. A mod's language file may be named by either (<c>russian.toml</c>,
/// <c>ru-RU.toml</c>), or by a code's language part where only one language has it (<c>ru.toml</c>).
/// </summary>
internal static class GameLanguages
{
    /// <summary>The language every mod string falls back to, and the game's own fallback.</summary>
    public const string Fallback = "english";

    // Name, then its codes; the first code names its culture.
    private static readonly (string Name, string[] Codes)[] s_languages =
    [
        ("english", ["en-US", "en-GB", "en"]),
        ("englishUS", ["en-US"]),
        ("romanian", ["ro-RO", "ro"]),
        ("german", ["de-DE", "de"]),
        ("spanish", ["es-ES", "es-419", "es"]),
        ("french", ["fr-FR", "fr-CA", "fr"]),
        ("italian", ["it-IT", "it"]),
        ("japanese", ["ja-JP", "ja"]),
        ("schinese", ["zh-Hans"]),
        ("tchinese", ["zh-Hant"]),
        ("polish", ["pl-PL", "pl"]),
        ("brazilian", ["pt-BR", "pt-PT", "pt"]),
        ("russian", ["ru-RU", "ru"]),
        ("ukrainian", ["uk-UA", "uk"]),
    ];

    private static readonly Dictionary<string, string> s_byFileName = BuildFileNames();
    private static readonly Dictionary<string, CultureInfo> s_cultures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The language a file name means: a language name or code, case aside (en-US is english, the game's code for it).
    /// A name the table does not know is taken as a language name the game may report (lower case).
    /// </summary>
    public static string FromFileName(string fileName)
    {
        string name = fileName.Trim();
        return s_byFileName.TryGetValue(name, out var language) ? language : name.ToLowerInvariant();
    }

    /// <summary>Whether the table knows the language (a file named otherwise is still read, under that name).</summary>
    public static bool IsKnown(string language) => Array.Exists(s_languages, l => l.Name.Equals(language, StringComparison.OrdinalIgnoreCase));

    /// <summary>The culture that formats numbers and dates in the language (ru-RU for russian); invariant for one the table does not know.</summary>
    public static CultureInfo CultureOf(string language)
    {
        lock (s_cultures)
        {
            if (s_cultures.TryGetValue(language, out var culture)) return culture;
            culture = CultureInfo.InvariantCulture;
            foreach (var (name, codes) in s_languages)
            {
                if (!name.Equals(language, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    culture = CultureInfo.GetCultureInfo(codes[0]);
                }
                catch (CultureNotFoundException)
                {
                    // the runtime has no data for it: numbers stay invariant
                }
                break;
            }
            s_cultures[language] = culture;
            return culture;
        }
    }

    private static Dictionary<string, string> BuildFileNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in s_languages) map[name] = name;
        // Codes after names, the first language to claim a code keeping it: en-US stays english, as in the game's list.
        foreach (var (name, codes) in s_languages)
            foreach (string code in codes)
                map.TryAdd(code, name);
        return map;
    }
}
