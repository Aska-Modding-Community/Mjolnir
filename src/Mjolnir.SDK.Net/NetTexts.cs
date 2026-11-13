using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Mjolnir.SDK.Net;

/// <summary>
/// The network module's own texts in the player's language: lang/*.toml embedded in this assembly (English and Russian;
/// any other language shows the English), looked up through the SDK's <see cref="Localization"/> like a mod's strings.
/// </summary>
internal static class NetTexts
{
    private const string Id = "mjolnir.sdk.net";
    internal const string Source = "SDK.Net";
    private static readonly Lazy<ModStrings> s_strings = new(Load);

    /// <summary>The text of <paramref name="key"/> (<c>refusal.title</c>), formatted with <paramref name="args"/>.</summary>
    public static string Get(string key, params object?[] args) => Localization.Resolve(s_strings.Value, key, args);

    /// <summary>The dialog a refused client sees: a title naming what failed, then the differences, one per line.</summary>
    public static (string Title, string Message) Refusal(CompatibilityReport report) => Refusal(report, Get);

    internal static (string Title, string Message) Refusal(CompatibilityReport report, Func<string, object?[], string> text)
    {
        string T(string key, params object?[] args) => text(key, args);
        var body = new StringBuilder();
        void List(IEnumerable<string> items)
        {
            foreach (string item in items) body.Append("\n  • ").Append(item);
        }

        if (report.HostDidNotAnswer)
        {
            body.Append(T("refusal.no_answer"));
        }
        else if (report.HostWithoutMjolnir)
        {
            body.Append(T("refusal.vanilla"));
            List(report.MissingOnHost.Select(m => m.ToString()));
            body.Append("\n\n").Append(T("refusal.action"));
        }
        else
        {
            body.Append(T("refusal.differs"));
            if (report.PlatformMismatch) body.Append("\n\n").Append(T("refusal.platform", report.HostPlatform, report.ClientPlatform));
            if (report.MissingOnClient.Count > 0)
            {
                body.Append("\n\n").Append(T("refusal.missing"));
                List(report.MissingOnClient.Select(m => m.ToString()));
            }
            if (report.MissingOnHost.Count > 0)
            {
                body.Append("\n\n").Append(T("refusal.extra"));
                List(report.MissingOnHost.Select(m => m.ToString()));
            }
            if (report.VersionMismatches.Count > 0)
            {
                body.Append("\n\n").Append(T("refusal.versions"));
                List(report.VersionMismatches.Select(v => T("refusal.version", v.Name, v.HostVersion, v.ClientVersion)));
            }
            if (report.MissingOnClient.Count + report.MissingOnHost.Count + report.VersionMismatches.Count > 0)
                body.Append("\n\n").Append(T("refusal.action"));
        }
        return (T("refusal.title"), body.ToString());
    }

    private static ModStrings Load()
    {
        var assembly = typeof(NetTexts).Assembly;
        var files = new List<(string Language, string Name, string Text)>();
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ModStrings.FolderName + "/", StringComparison.Ordinal) || !resource.EndsWith(".toml", StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null) continue;
            using var reader = new StreamReader(stream);
            files.Add((GameLanguages.FromFileName(Path.GetFileNameWithoutExtension(resource)), resource, reader.ReadToEnd()));
        }
        return ModStrings.Parse(Id, files, message => SdkLog.Warn(Source, message));
    }
}
