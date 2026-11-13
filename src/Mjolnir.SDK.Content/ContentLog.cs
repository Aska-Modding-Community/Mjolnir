using System;
using System.Collections.Generic;

namespace Mjolnir.SDK.Content;

/// <summary>
/// The content module's log (no Unity): a warning about a mod's content said once, however often the game builds its
/// database, and calls into the game that never throw into it.
/// </summary>
internal static class ContentLog
{
    public const string Source = GameLocalization.Source;

    private static readonly HashSet<string> s_warned = [];

    /// <summary>Warns once per text.</summary>
    public static void WarnOnce(string message) => WarnOnce(message, message);

    /// <summary>Warns once per <paramref name="key"/>, whatever the text says.</summary>
    public static void WarnOnce(string key, string message)
    {
        bool first;
        lock (s_warned) first = s_warned.Add(key);
        if (first) SdkLog.Warn(Source, message);
    }

    /// <summary>Runs <paramref name="action"/>; a fault is logged as "Items: <paramref name="what"/> failed: …".</summary>
    public static void Guard(string what, Action action) => GameGuard.Run(Source, $"Items: {what}", action);
}
