using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mjolnir.Core.Mods;

/// <summary>
/// A set of version bounds, all of which must hold: <c>"&gt;=1.2.0 &lt;2.0.0"</c> (space or comma separated).
/// Operators: <c>&gt;=</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&lt;</c>, <c>=</c> (or <c>==</c>); a bare version means
/// <c>&gt;=</c> (a minimum). Empty or <c>*</c> accepts any version. Missing components count as 0, so 1.2 = 1.2.0.
/// </summary>
internal sealed class VersionRange
{
    private enum Op { Ge, Gt, Le, Lt, Eq }

    private readonly (Op Op, Version Version)[] _bounds;
    private readonly string _text;

    public static readonly VersionRange Any = new([], "*");

    private VersionRange((Op, Version)[] bounds, string text)
    {
        _bounds = bounds;
        _text = text;
    }

    public static VersionRange Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "*") return Any;
        var bounds = new List<(Op, Version)>();
        foreach (string token in text.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            (Op op, string rest) = token switch
            {
                _ when token.StartsWith(">=", StringComparison.Ordinal) => (Op.Ge, token[2..]),
                _ when token.StartsWith("<=", StringComparison.Ordinal) => (Op.Le, token[2..]),
                _ when token.StartsWith("==", StringComparison.Ordinal) => (Op.Eq, token[2..]),
                _ when token.StartsWith('>') => (Op.Gt, token[1..]),
                _ when token.StartsWith('<') => (Op.Lt, token[1..]),
                _ when token.StartsWith('=') => (Op.Eq, token[1..]),
                _ => (Op.Ge, token),
            };
            if (!ModVersion.TryParse(rest, out var version))
                throw new FormatException($"invalid version range '{text}': '{token}' is not a version");
            bounds.Add((op, version));
        }
        return new VersionRange([.. bounds], text.Trim());
    }

    public bool Contains(Version version)
    {
        var v = ModVersion.Normalize(version);
        return _bounds.All(b =>
        {
            int c = v.CompareTo(b.Version);
            return b.Op switch
            {
                Op.Ge => c >= 0,
                Op.Gt => c > 0,
                Op.Le => c <= 0,
                Op.Lt => c < 0,
                _ => c == 0,
            };
        });
    }

    public override string ToString() => _text;
}

/// <summary>Mod version strings: <c>Major[.Minor[.Patch[.Revision]]]</c> with an ignored -prerelease/+build suffix.</summary>
internal static class ModVersion
{
    /// <summary>Parses into a 4-component version (missing parts are 0), so that comparisons are well defined.</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        string core = text.Trim();
        int suffix = core.IndexOfAny(['-', '+']);
        if (suffix >= 0) core = core[..suffix];

        string[] parts = core.Split('.');
        if (parts.Length is < 1 or > 4) return false;
        var numbers = new int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }
        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    public static Version Normalize(Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
