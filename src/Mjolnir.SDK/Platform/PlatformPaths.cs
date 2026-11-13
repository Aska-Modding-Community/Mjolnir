using Mjolnir.Core;

namespace Mjolnir.SDK;

/// <summary>Where the loader lives (Mjolnir/ in the game's folder), for the modules' own files. Part of the module contract.</summary>
internal static class PlatformPaths
{
    /// <summary>The Mjolnir folder; null without the loader (unit tests).</summary>
    public static string? Loader => Bootstrap.Paths?.LoaderDir;
}
