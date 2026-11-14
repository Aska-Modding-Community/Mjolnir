using System;
using System.Collections.Generic;

namespace Mjolnir.SDK;

/// <summary>
/// Who must have a mod in a multiplayer session, as the platform sees it. A module calls <see cref="NeedsEveryone"/>
/// when a mod adds something every player must have — content of its own, whose ids travel between the players — and a
/// mod that declared no mode of its own is raised to <see cref="NetworkCompatibility.Everyone"/>, so a session refuses a
/// player who lacks it instead of drifting apart.
/// </summary>
/// <remarks>
/// Mods are known here from the moment each is built, because a mod adds its content in <c>OnLoad</c>, before
/// <see cref="ModRegistry"/> holds it. An id nothing was tracked for is ignored: a test, or content added by code that
/// is no loaded mod.
/// </remarks>
internal static class ModNetwork
{
    private static readonly object s_lock = new();
    private static readonly Dictionary<string, ModManifest> s_byId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every mod as it is constructed, so its own <c>OnLoad</c> can already raise it.</summary>
    public static void Track(ModManifest manifest)
    {
        lock (s_lock) s_byId[manifest.Id] = manifest;
    }

    /// <summary>The mod <paramref name="modId"/> adds <paramref name="what"/> ("the item twine") that every player needs.</summary>
    public static void NeedsEveryone(string modId, string what)
    {
        ModManifest? manifest;
        lock (s_lock) manifest = s_byId.GetValueOrDefault(modId);
        manifest?.NeedsEveryone(what);
    }
}
