using System;
using System.Collections.Generic;

namespace Mjolnir.SDK;

/// <summary>
/// Calls into the game from the SDK's modules that must never throw into a mod or the game: a fault is logged as an error
/// under the module's log source and the call goes on (or answers its fallback). Part of the module contract.
/// </summary>
/// <remarks>
/// The <c>…LogOnce</c> forms log a fault of the same <c>source</c> and <c>what</c> once per process, for calls made every
/// frame, whose fault would otherwise fill the log. Mod code is never called through these: it goes through
/// <see cref="ModCall"/>, which disables the mod that threw.
/// </remarks>
internal static class GameGuard
{
    private static readonly HashSet<(string Source, string What)> s_logged = [];

    /// <summary>Runs <paramref name="action"/>; a fault is logged as "<paramref name="what"/> failed: …".</summary>
    public static void Run(string source, string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            SdkLog.Error(source, $"{what} failed: {e}");
        }
    }

    /// <summary>As <see cref="Run"/>, a fault of this <paramref name="what"/> logged only the first time.</summary>
    public static void RunLogOnce(string source, string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            LogOnce(source, what, e);
        }
    }

    /// <summary>The answer of <paramref name="func"/>, or <paramref name="fallback"/> when it threw (logged).</summary>
    public static T Try<T>(string source, string what, Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (Exception e)
        {
            SdkLog.Error(source, $"{what} failed: {e}");
            return fallback;
        }
    }

    /// <summary>As <see cref="Try{T}"/>, a fault of this <paramref name="what"/> logged only the first time.</summary>
    public static T TryLogOnce<T>(string source, string what, Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (Exception e)
        {
            LogOnce(source, what, e);
            return fallback;
        }
    }

    private static void LogOnce(string source, string what, Exception e)
    {
        bool first;
        lock (s_logged) first = s_logged.Add((source, what));
        if (first) SdkLog.Error(source, $"{what} failed (later faults of it are not logged): {e}");
    }

    /// <summary>Forgets what was logged, between unit tests.</summary>
    internal static void ResetForTests()
    {
        lock (s_logged) s_logged.Clear();
    }

    /// <summary>Whether a fault of this <paramref name="what"/> was logged already, for unit tests.</summary>
    internal static bool WasLogged(string source, string what)
    {
        lock (s_logged) return s_logged.Contains((source, what));
    }
}
