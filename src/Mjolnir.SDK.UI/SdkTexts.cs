using System;
using System.Collections.Generic;
using System.IO;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The UI module's own texts in the player's language: lang/*.toml embedded in this assembly (English and Russian; any
/// other language shows the English), looked up through the SDK's <see cref="Localization"/> like a mod's strings. No
/// Unity here: model code (setting descriptions) uses it too.
/// </summary>
internal static class SdkTexts
{
    private const string Id = "mjolnir.sdk.ui";
    private static readonly Lazy<ModStrings> s_strings = new(Load);

    /// <summary>The module's strings themselves: the platform's settings page takes its setting names from them (<c>[config.UI.Scale]</c>).</summary>
    public static ModStrings Strings => s_strings.Value;

    /// <summary>The text of <paramref name="key"/> (<c>mods.reset</c>), formatted with <paramref name="args"/>.</summary>
    public static string Get(string key, params object?[] args) => Localization.Resolve(s_strings.Value, key, args);

    private static ModStrings Load()
    {
        var assembly = typeof(SdkTexts).Assembly;
        var files = new List<(string Language, string Name, string Text)>();
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ModStrings.FolderName + "/", StringComparison.Ordinal) || !resource.EndsWith(".toml", StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null) continue;
            using var reader = new StreamReader(stream);
            files.Add((GameLanguages.FromFileName(Path.GetFileNameWithoutExtension(resource)), resource, reader.ReadToEnd()));
        }
        return ModStrings.Parse(Id, files, message => SdkLog.Warn(GameUi.Source, message));
    }
}
