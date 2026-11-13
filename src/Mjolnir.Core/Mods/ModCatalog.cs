using System;
using System.Collections.Generic;

namespace Mjolnir.Core.Mods;

/// <summary>What became of a mod found at start.</summary>
internal enum ModOutcome
{
    /// <summary>Its Load succeeded (it may be disabled after an error later: <see cref="ModFaults"/>).</summary>
    Loaded,
    /// <summary>The player turned it off (<see cref="ModList"/>).</summary>
    TurnedOff,
    /// <summary>Skipped before loading: <see cref="ModRecord.Skip"/> says why.</summary>
    NotLoaded,
    /// <summary>Its creation or Load threw.</summary>
    Failed,
}

/// <summary>One mod found at start: what the loader read, its entry class, and what became of it.</summary>
internal sealed record ModRecord(ModDescriptor Mod, Type EntryType, ModOutcome Outcome, ModSkip? Skip = null);

/// <summary>
/// Every mod found at start, loaded or not, filled by <see cref="ModLoader"/> in phase A: the SDK's Mods window lists
/// them, turned-off and skipped ones included, and turns mods on and off through <see cref="ModList"/>.
/// </summary>
internal static class ModCatalog
{
    private static IReadOnlyList<ModRecord> s_mods = [];
    private static IReadOnlySet<string> s_turnedOff = new HashSet<string>();

    /// <summary>Loaded mods in load order, then the rest in discovery order.</summary>
    public static IReadOnlyList<ModRecord> Mods => s_mods;

    /// <summary>Mjolnir/Config/mod-list.json; empty before the loader ran.</summary>
    public static string ListPath { get; private set; } = "";

    /// <summary>The ids that were turned off at this start, as the file listed them (ids of absent mods included).</summary>
    public static IReadOnlySet<string> TurnedOffAtStart => s_turnedOff;

    /// <summary>Whether the loader is done with every mod found (each loaded or not): false while mods are still loading.</summary>
    public static bool IsFilled { get; private set; }

    internal static void Set(IReadOnlyList<ModRecord> mods, string listPath, IReadOnlySet<string> turnedOff)
    {
        s_mods = mods;
        IsFilled = true;
        ListPath = listPath;
        s_turnedOff = turnedOff;
    }
}
