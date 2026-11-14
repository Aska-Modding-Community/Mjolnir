namespace Mjolnir.Core.Mods;

/// <summary>Why a mod was not loaded; the SDK shows each kind in the player's language.</summary>
internal enum ModSkipKind
{
    /// <summary>The player turned it off in the Mods window (<see cref="ModList"/>).</summary>
    TurnedOff,
    /// <summary>Another mod already has its id: <see cref="ModSkip.Other"/> the id, <see cref="ModSkip.Detail"/> the other's file.</summary>
    Duplicate,
    /// <summary>A required dependency is not installed: <see cref="ModSkip.Other"/> its id, <see cref="ModSkip.Range"/> the range.</summary>
    Missing,
    /// <summary>A dependency's version is outside the range: <see cref="ModSkip.Detail"/> the installed version.</summary>
    WrongVersion,
    /// <summary>
    /// The mod was built for another Mjolnir: <see cref="ModSkip.Range"/> the platform range it declared,
    /// <see cref="ModSkip.Detail"/> the installed platform version.
    /// </summary>
    WrongPlatform,
    /// <summary>A required dependency is installed but not loaded (it was skipped itself).</summary>
    DependencyNotLoaded,
    /// <summary>A required dependency is turned off.</summary>
    DependencyTurnedOff,
    /// <summary>A required dependency threw while loading.</summary>
    DependencyFailed,
    SelfDependency,
    /// <summary>The mod sits in or behind a dependency cycle: <see cref="ModSkip.Other"/> the mods in it.</summary>
    Cycle,
}

/// <summary>
/// Why a mod was not loaded: the kind and what it names. <see cref="ToString"/> is the log's English text; the SDK turns
/// the parts into the player's language for the Mods window.
/// </summary>
internal sealed record ModSkip(ModSkipKind Kind, string? Other = null, string? Range = null, string? Detail = null)
{
    public override string ToString() => Kind switch
    {
        ModSkipKind.TurnedOff => "turned off in the Mods window",
        ModSkipKind.Duplicate => $"duplicate mod id '{Other}', already used by {Detail}",
        ModSkipKind.Missing => $"needs {Other} {Range}, which is not installed",
        ModSkipKind.WrongVersion => $"needs {Other} {Range}, but {Detail} is installed",
        ModSkipKind.WrongPlatform => $"needs Mjolnir {Range}, but {Detail} is installed: rebuild the mod against it",
        ModSkipKind.DependencyNotLoaded => $"needs {Other}, which is not loaded",
        ModSkipKind.DependencyTurnedOff => $"needs {Other}, which is turned off",
        ModSkipKind.DependencyFailed => $"its dependency {Other} failed to load",
        ModSkipKind.SelfDependency => "depends on itself",
        ModSkipKind.Cycle => "dependency cycle among " + Other,
        _ => Kind.ToString(),
    };
}
