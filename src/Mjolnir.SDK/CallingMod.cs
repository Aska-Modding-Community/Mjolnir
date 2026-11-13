using System.Reflection;
using Mjolnir.Core.Mods;

namespace Mjolnir.SDK;

/// <summary>
/// Which mod called a public SDK method that acts for "the calling mod" (its strings, its content, its commands). Such a
/// method is <c>[MethodImpl(MethodImplOptions.NoInlining)]</c> and passes <c>Assembly.GetCallingAssembly()</c> to
/// <see cref="Of"/>, never uses it on its own.
/// </summary>
/// <remarks>
/// The stack alone loses the mod in hot code: after tiering up the JIT may inline a mod's function into the platform
/// method that invokes its delegate, so the caller seen is the platform; and a tail call in the mod's own code
/// (<c>string Title() =&gt; Localization.Get("title")</c>) drops the mod's frame, so the caller seen is whoever called the
/// mod. So a platform caller falls back to the thread's current owner, the mod whose code the platform is running
/// (<see cref="ModFaults.CurrentOwner"/>: set around every handler, mod function and mod load). Platform code that calls
/// such a method on its own behalf while mod code runs would be taken for that mod: it uses the internal overload with an
/// explicit owner instead.
/// </remarks>
internal static class CallingMod
{
    /// <summary>
    /// <paramref name="calling"/> when it is a mod's assembly (not the platform's load context), else the thread's current
    /// owner when that is a mod's, else <paramref name="calling"/> itself (the platform, or a test).
    /// </summary>
    public static Assembly Of(Assembly calling)
    {
        if (!ModFaults.IsPlatform(calling)) return calling;
        var owner = ModFaults.CurrentOwner;
        return owner != null && !ModFaults.IsPlatform(owner) ? owner : calling;
    }
}
