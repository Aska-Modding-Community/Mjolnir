using System;
using System.Collections.Generic;
using System.Linq;

namespace Mjolnir.SDK.Net;

/// <summary>A mod as one player has it, for the handshake: what it is, its version and who needs it.</summary>
internal sealed record ModStamp(string Id, string Name, string Version, NetworkCompatibility Network)
{
    /// <summary>Every player in the session needs it with the same version (content, mechanics).</summary>
    public bool IsShared => Network == NetworkCompatibility.Everyone;

    public override string ToString() => $"{Name} {Version}";
}

/// <summary>A mod both players have with different versions.</summary>
internal sealed record VersionMismatch(string Name, string HostVersion, string ClientVersion);

/// <summary>
/// How a client's mods differ from the host's. Only mods every player needs (<see cref="NetworkCompatibility.Everyone"/>)
/// count; a mod of one player's own (<see cref="NetworkCompatibility.ClientOnly"/>, <see cref="NetworkCompatibility.ServerOnly"/>)
/// never keeps anyone out. The platforms must agree in their major and minor version.
/// </summary>
internal sealed record CompatibilityReport(
    string HostPlatform,
    string ClientPlatform,
    bool PlatformMismatch,
    bool HostWithoutMjolnir,
    bool HostDidNotAnswer,
    IReadOnlyList<ModStamp> MissingOnClient,
    IReadOnlyList<ModStamp> MissingOnHost,
    IReadOnlyList<VersionMismatch> VersionMismatches)
{
    public bool IsCompatible =>
        !PlatformMismatch && !HostWithoutMjolnir && !HostDidNotAnswer && MissingOnClient.Count == 0 && MissingOnHost.Count == 0 && VersionMismatches.Count == 0;

    /// <summary>One line for the log.</summary>
    public string Summary =>
        IsCompatible ? "compatible" : string.Join("; ", new[]
        {
            PlatformMismatch ? $"Mjolnir {HostPlatform} on the host, {ClientPlatform} on the client" : null,
            HostWithoutMjolnir ? "the host has no Mjolnir" : null,
            HostDidNotAnswer ? "the host did not answer the handshake" : null,
            MissingOnClient.Count > 0 ? "missing on the client: " + string.Join(", ", MissingOnClient) : null,
            MissingOnHost.Count > 0 ? "not on the host: " + string.Join(", ", MissingOnHost) : null,
            VersionMismatches.Count > 0 ? "versions: " + string.Join(", ", VersionMismatches.Select(v => $"{v.Name} {v.HostVersion}/{v.ClientVersion}")) : null,
        }.Where(s => s != null));

    /// <summary>The host's mods against the client's.</summary>
    public static CompatibilityReport Compare(string hostPlatform, IReadOnlyCollection<ModStamp> host,
                                              string clientPlatform, IReadOnlyCollection<ModStamp> client)
    {
        var hostShared = SharedById(host);
        var clientShared = SharedById(client);
        var missingOnClient = hostShared.Values.Where(m => !clientShared.ContainsKey(m.Id)).OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var missingOnHost = clientShared.Values.Where(m => !hostShared.ContainsKey(m.Id)).OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var versions = hostShared.Values
            .Where(m => clientShared.TryGetValue(m.Id, out var c) && !SameVersion(m.Version, c.Version))
            .Select(m => new VersionMismatch(m.Name, m.Version, clientShared[m.Id].Version))
            .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new CompatibilityReport(hostPlatform, clientPlatform, !SamePlatform(hostPlatform, clientPlatform), false, false,
                                       missingOnClient, missingOnHost, versions);
    }

    // The mods every player needs by their id. A list that names an id twice came from no Mjolnir (each side lists its
    // loaded mods, one per id), but it comes from another machine: the first of an id counts.
    private static Dictionary<string, ModStamp> SharedById(IEnumerable<ModStamp> mods)
    {
        var byId = new Dictionary<string, ModStamp>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
            if (mod.IsShared) byId.TryAdd(mod.Id, mod);
        return byId;
    }

    /// <summary>A host without Mjolnir: the client's shared mods cannot be played with it.</summary>
    public static CompatibilityReport HostHasNoMjolnir(string clientPlatform, IReadOnlyCollection<ModStamp> client) =>
        new("", clientPlatform, false, true, false, [], client.Where(m => m.IsShared).OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), []);

    /// <summary>A host that announced Mjolnir but never answered the client's mods.</summary>
    public static CompatibilityReport NoAnswer(string hostPlatform, string clientPlatform) =>
        new(hostPlatform, clientPlatform, false, false, true, [], [], []);

    // A mod's version as its manifest writes it, compared by its four components ("1.0" is "1.0.0"); a text that is no
    // version is compared as it is.
    internal static bool SameVersion(string a, string b) =>
        Version.TryParse(Core(a), out var va) && Version.TryParse(Core(b), out var vb)
            ? Normal(va) == Normal(vb) && Suffix(a) == Suffix(b)
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Mjolnir's own versions keep the API within a minor version, so only those two parts must agree.
    internal static bool SamePlatform(string a, string b) =>
        Version.TryParse(Core(a), out var va) && Version.TryParse(Core(b), out var vb)
            ? va.Major == vb.Major && va.Minor == vb.Minor
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Core(string version) => version.Split('-', '+')[0];

    private static string Suffix(string version) => version.Length > Core(version).Length ? version[Core(version).Length..].ToLowerInvariant() : "";

    private static Version Normal(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
