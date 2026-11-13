using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace Mjolnir.Core.Mods;

/// <summary>
/// Mods disabled after an exception, and the one way to call mod code from platform code. A disabled mod's
/// handlers are skipped from then on; Harmony patches it already applied stay in place, since they cannot be
/// reliably reverted (hot unload is unsupported by design).
/// </summary>
internal static class ModFaults
{
    private static readonly object s_lock = new();
    private static readonly HashSet<Assembly> s_disabled = [];

    [ThreadStatic] private static Assembly? t_owner;

    /// <summary>
    /// The assembly whose code the platform is running on this thread: the declaring assembly of the handler being invoked
    /// (<see cref="Invoke{T}"/>), of a mod being constructed and loaded, or whatever the SDK entered around mod code it
    /// calls (<see cref="Enter"/>). Null outside such a call. It may be a platform assembly (a platform handler).
    /// </summary>
    /// <remarks>
    /// The SDK's "which mod called me" falls back to it when the stack names the platform: the JIT may inline a hot mod
    /// function into the platform method that invokes its delegate, or drop the mod's frame through a tail call, and then
    /// <see cref="Assembly.GetCallingAssembly"/> sees the platform instead of the mod.
    /// </remarks>
    public static Assembly? CurrentOwner => t_owner;

    /// <summary>
    /// Makes <paramref name="owner"/> the <see cref="CurrentOwner"/> until the scope is disposed, which restores the one before
    /// (so calls nest, and a throw restores it too when the scope is a <c>using</c>).
    /// </summary>
    public static OwnerScope Enter(Assembly? owner)
    {
        var previous = t_owner;
        t_owner = owner;
        return new OwnerScope(previous);
    }

    /// <summary>Restores the <see cref="CurrentOwner"/> that was current before <see cref="Enter"/>.</summary>
    public readonly struct OwnerScope : IDisposable
    {
        private readonly Assembly? _previous;

        internal OwnerScope(Assembly? previous) => _previous = previous;

        public void Dispose() => t_owner = _previous;
    }

    public static bool IsDisabled(Assembly assembly)
    {
        lock (s_lock) return s_disabled.Contains(assembly);
    }

    public static void Disable(Assembly assembly, string reason)
    {
        bool first;
        lock (s_lock) first = s_disabled.Add(assembly);
        if (first) Log.Error("Mods", $"{assembly.GetName().Name} disabled: {reason}");
    }

    /// <summary>
    /// Platform code (Core and SDK) lives in the Default load context, every mod in its own one. Platform code
    /// is never disabled: a fault there is only logged.
    /// </summary>
    public static bool IsPlatform(Assembly assembly) => AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default;

    /// <summary>
    /// Calls a handler that may belong to a mod: skipped if that mod is disabled, and an exception disables the
    /// handler's mod. Core's GameEvents and the SDK's facades (whose hidden Harmony patches raise mod events)
    /// call mods only through this, so a fault is blamed on the mod, never on the platform.
    /// </summary>
    public static void Invoke<T>(T handler, string source, Action<T> call) where T : Delegate
    {
        var owner = handler.Method.DeclaringType?.Assembly;
        if (owner != null && IsDisabled(owner)) return;
        var scope = Enter(owner);
        try
        {
            call(handler);
        }
        catch (Exception e)
        {
            string what = $"{source} handler {handler.Method.DeclaringType?.FullName}.{handler.Method.Name} threw: {e}";
            if (owner == null || IsPlatform(owner)) Log.Error("Core", what);
            else Disable(owner, what);
        }
        finally
        {
            scope.Dispose();
        }
    }
}
