using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using Mjolnir.Core;
using Mjolnir.Core.Mods;

namespace Mjolnir.SDK;

/// <summary>
/// Handlers of an SDK event, invoked one by one through <see cref="ModFaults.Invoke{T}"/>: a throwing handler disables
/// its own mod, the other handlers still run. A combined delegate (<c>+= a + b</c>) is kept as its parts, so each part
/// is blamed on its own mod. Copy-on-write, so raising never locks.
/// </summary>
internal sealed class ModHandlers<T>(string eventName) where T : Delegate
{
    private readonly object _lock = new();
    private T[] _items = [];

    public int Count => Volatile.Read(ref _items).Length;

    /// <summary>The handlers of an event field created on first use (<c>add => ModHandlers&lt;T&gt;.Of(ref _x, "…").Add(value)</c>).</summary>
    public static ModHandlers<T> Of(ref ModHandlers<T>? field, string eventName)
    {
        var existing = Volatile.Read(ref field);
        if (existing != null) return existing;
        Interlocked.CompareExchange(ref field, new ModHandlers<T>(eventName), null);
        return field!;
    }

    public void Add(T? handler)
    {
        if (handler == null) return;
        lock (_lock) _items = [.. _items, .. handler.GetInvocationList().Cast<T>()];
    }

    public void Remove(T? handler)
    {
        if (handler == null) return;
        lock (_lock)
        {
            foreach (var part in handler.GetInvocationList().Cast<T>().Reverse())
            {
                int i = Array.LastIndexOf(_items, part);
                if (i >= 0) _items = [.. _items.AsSpan(0, i), .. _items.AsSpan(i + 1)];
            }
        }
    }

    public void Raise(Action<T> invoke)
    {
        foreach (var handler in Volatile.Read(ref _items)) ModFaults.Invoke(handler, eventName, invoke);
    }
}

/// <summary>
/// Calls mod code from SDK code with the loader's fault isolation: a throwing delegate disables the mod that declared it
/// (platform code is only logged), and the delegates of a disabled mod are skipped.
/// </summary>
internal static class ModCall
{
    /// <summary>Every delegate of a (multicast) handler on its own, so one mod's fault never stops another's handler.</summary>
    public static void InvokeEach(Action? handler, string what)
    {
        if (handler == null) return;
        foreach (var single in handler.GetInvocationList()) ModFaults.Invoke((Action)single, what, static h => h());
    }

    public static void InvokeEach<T>(Action<T>? handler, T value, string what)
    {
        if (handler == null) return;
        foreach (var single in handler.GetInvocationList()) ModFaults.Invoke((Action<T>)single, what, h => h(value));
    }

    public static void InvokeEach<TArgs>(EventHandler<TArgs>? handler, object? sender, TArgs args, string what)
    {
        if (handler == null) return;
        foreach (var single in handler.GetInvocationList()) ModFaults.Invoke((EventHandler<TArgs>)single, what, h => h(sender, args));
    }

    /// <summary>
    /// A mod function's result, without allocating on success (it is polled every frame). False if it threw (its mod is
    /// then disabled) or its mod is already disabled.
    /// </summary>
    // The methods below that call mod code themselves make its mod the thread's current owner while it runs
    // (ModFaults.Enter), as ModFaults.Invoke does for handlers: optimised, the JIT may inline a hot mod function into them,
    // and then an SDK method asking who called it sees this assembly instead of the mod (CallingMod falls back to the owner).
    public static bool Evaluate<T>(Func<T> func, string what, out T result)
    {
        var owner = Owner(func);
        if (owner != null && ModFaults.IsDisabled(owner))
        {
            result = default!;
            return false;
        }
        using var scope = ModFaults.Enter(owner);
        try
        {
            result = func();
            return true;
        }
        catch (Exception e)
        {
            Fault(owner, func, what, e);
            result = default!;
            return false;
        }
    }

    public static bool Evaluate<TArg, T>(Func<TArg, T> func, TArg arg, string what, out T result)
    {
        var owner = Owner(func);
        if (owner != null && ModFaults.IsDisabled(owner))
        {
            result = default!;
            return false;
        }
        using var scope = ModFaults.Enter(owner);
        try
        {
            result = func(arg);
            return true;
        }
        catch (Exception e)
        {
            Fault(owner, func, what, e);
            result = default!;
            return false;
        }
    }

    /// <summary>Runs code a mod supplied indirectly (a custom IBindable's setter): a throw blames <paramref name="owner"/>.</summary>
    public static void Run(Assembly? owner, string what, Action action)
    {
        if (owner != null && ModFaults.IsDisabled(owner)) return;
        using var scope = ModFaults.Enter(owner);
        try
        {
            action();
        }
        catch (Exception e)
        {
            Fault(owner, action, what, e);
        }
    }

    /// <summary>
    /// Runs a mod's delegate with an argument, without allocating (a camera's modifiers run every frame): a throw blames
    /// <paramref name="owner"/>. False if it threw or its mod is disabled.
    /// </summary>
    public static bool Run<TArg>(Assembly? owner, string what, Action<TArg> action, TArg arg)
    {
        if (owner != null && ModFaults.IsDisabled(owner)) return false;
        using var scope = ModFaults.Enter(owner);
        try
        {
            action(arg);
            return true;
        }
        catch (Exception e)
        {
            Fault(owner, action, what, e);
            return false;
        }
    }

    /// <summary>A value's text: its ToString may be mod code, so a throw blames the value's mod and gives "".</summary>
    public static string TextOf(object? value, string what) =>
        value != null && TryRun(value.GetType().Assembly, what, value.ToString, out string? text) ? text ?? "" : "";

    /// <summary>The items of a mod's sequence: enumerating it runs the collection's code, so a throw blames its mod and gives none.</summary>
    public static object?[] ItemsOf(IEnumerable? source, string what) =>
        source != null && TryRun(source.GetType().Assembly, what, () => source.Cast<object?>().ToArray(), out var items) ? items : [];

    /// <summary>The result of code a mod supplied indirectly (a custom IBindable's getter): a throw blames <paramref name="owner"/>.</summary>
    public static bool TryRun<T>(Assembly? owner, string what, Func<T> func, out T result)
    {
        if (owner != null && ModFaults.IsDisabled(owner))
        {
            result = default!;
            return false;
        }
        using var scope = ModFaults.Enter(owner);
        try
        {
            result = func();
            return true;
        }
        catch (Exception e)
        {
            Fault(owner, func, what, e);
            result = default!;
            return false;
        }
    }

    /// <summary>
    /// The result of a mod's function of one argument, without a wrapper of the platform's around it: a throw blames
    /// <paramref name="owner"/>. False if it threw or its mod is disabled.
    /// </summary>
    public static bool TryRun<TArg, T>(Assembly? owner, string what, Func<TArg, T> func, TArg arg, out T result)
    {
        if (owner != null && ModFaults.IsDisabled(owner))
        {
            result = default!;
            return false;
        }
        using var scope = ModFaults.Enter(owner);
        try
        {
            result = func(arg);
            return true;
        }
        catch (Exception e)
        {
            Fault(owner, func, what, e);
            result = default!;
            return false;
        }
    }

    /// <summary>Whether the mod of <paramref name="owner"/> was disabled after an error (its UI and handlers are skipped).</summary>
    public static bool IsDisabled(Assembly owner) => ModFaults.IsDisabled(owner);

    /// <summary>Disables the mod of <paramref name="owner"/>, logged with the reason (its UI and handlers are skipped from now on).</summary>
    public static void Disable(Assembly owner, string reason) => ModFaults.Disable(owner, reason);

    private static Assembly? Owner(Delegate d) => d.Method.DeclaringType?.Assembly;

    private static void Fault(Assembly? owner, Delegate d, string what, Exception e)
    {
        string message = $"{what} {d.Method.DeclaringType?.FullName}.{d.Method.Name} threw: {e}";
        if (owner == null || ModFaults.IsPlatform(owner)) Log.Error("Core", message);
        else Disable(owner, message);
    }
}
