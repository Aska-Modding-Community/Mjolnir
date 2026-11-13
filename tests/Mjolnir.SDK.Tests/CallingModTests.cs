using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mjolnir.Core.Mods;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// Which mod called an SDK method (<see cref="CallingMod"/>): a mod's assembly on the stack wins, a platform caller falls
/// back to the thread's current owner, the mod whose code the platform is running (<see cref="ModFaults.CurrentOwner"/>),
/// which <see cref="ModCall"/> and <see cref="ModHandlers{T}"/> set around mod code, nested and restored after a throw.
/// The mods are assemblies made here in load contexts of their own, as a mod's is in game; this test assembly is the
/// platform (the default load context), so a call from it is what the stack shows once the JIT inlined a mod's function
/// into the platform or a tail call dropped the mod's frame.
/// </summary>
[Collection("MainThread")]
public sealed class CallingModTests
{
    private static readonly FakeMod s_a = new("CallingMod.A");
    private static readonly FakeMod s_b = new("CallingMod.B");

    [Fact]
    public void FakeModsAreNotPlatformCode()
    {
        Assert.False(ModFaults.IsPlatform(s_a.Assembly));
        Assert.True(ModFaults.IsPlatform(typeof(CallingModTests).Assembly));
    }

    [Fact]
    public void AModOnTheStackIsTheCaller()
    {
        Assert.Same(s_a.Assembly, s_a.Who());
        using (ModFaults.Enter(s_b.Assembly)) Assert.Same(s_a.Assembly, s_a.Who());
    }

    [Fact]
    public void AModFrameLostToATailCallIsFoundByItsOwner()
    {
        Assert.True(ModCall.Evaluate(s_a.WhoTail, "test", out var seen));
        Assert.Same(s_a.Assembly, seen);

        var handlers = new ModHandlers<Func<Assembly>>("test");
        handlers.Add(s_b.WhoTail);
        handlers.Raise(h => seen = h());
        Assert.Same(s_b.Assembly, seen);
    }

    [Fact]
    public void APlatformCallerFallsBackToTheCurrentOwner()
    {
        var platform = typeof(CallingModTests).Assembly;
        Assert.Null(ModFaults.CurrentOwner);
        Assert.Same(platform, CallerProbe.Who());

        using (ModFaults.Enter(s_a.Assembly)) Assert.Same(s_a.Assembly, CallerProbe.Who());
        using (ModFaults.Enter(typeof(Mod).Assembly)) Assert.Same(platform, CallerProbe.Who());
        Assert.Null(ModFaults.CurrentOwner);
    }

    [Fact]
    public void ModCodeRunsAsItsMod()
    {
        Assert.True(ModCall.Evaluate(s_a.AmbientFunc, "test", out var during));
        Assert.Same(s_a.Assembly, during);
        Assert.True(ModCall.Evaluate(s_a.AmbientOf, 1, "test", out during));
        Assert.Same(s_a.Assembly, during);
        Assert.Null(ModFaults.CurrentOwner);

        // Platform code running for a mod (a wrapper, or a mod function the JIT inlined into it) is taken for that mod.
        Assembly? seen = null;
        ModCall.Run(s_a.Assembly, "test", () => seen = CallerProbe.Who());
        Assert.Same(s_a.Assembly, seen);
        Assert.True(ModCall.TryRun(s_b.Assembly, "test", CallerProbe.Who, out seen));
        Assert.Same(s_b.Assembly, seen);
        Assert.True(ModCall.Run(s_a.Assembly, "test", _ => seen = CallerProbe.Who(), 1));
        Assert.Same(s_a.Assembly, seen);
        Assert.True(ModCall.TryRun(s_b.Assembly, "test", _ => CallerProbe.Who(), 1, out seen));
        Assert.Same(s_b.Assembly, seen);
        Assert.Null(ModFaults.CurrentOwner);
    }

    [Fact]
    public void HandlersRunAsTheirMods()
    {
        Assembly? seen = null;
        var handlers = new ModHandlers<Func<Assembly?>>("test");
        handlers.Add(s_a.AmbientFunc);
        handlers.Raise(h => seen = h());
        Assert.Same(s_a.Assembly, seen);
        Assert.Null(ModFaults.CurrentOwner);
    }

    [Fact]
    public void OwnersNest()
    {
        Assembly? inner = null, outer = null;
        ModCall.Run(s_a.Assembly, "test", () =>
        {
            ModCall.Run(s_b.Assembly, "test", () => inner = CallerProbe.Who());
            outer = CallerProbe.Who();
        });
        Assert.Same(s_b.Assembly, inner);
        Assert.Same(s_a.Assembly, outer);
        Assert.Null(ModFaults.CurrentOwner);
    }

    [Fact]
    public void AThrowRestoresTheOwnerBefore()
    {
        var thrower = new FakeMod("CallingMod.Thrower");
        using (ModFaults.Enter(s_a.Assembly))
        {
            ModCall.Run(thrower.Assembly, "test", () => throw new InvalidOperationException("mod fault"));
            Assert.Same(s_a.Assembly, ModFaults.CurrentOwner);
            Assert.True(ModCall.IsDisabled(thrower.Assembly));

            Assert.False(ModCall.TryRun<int>(typeof(Mod).Assembly, "test", () => throw new InvalidOperationException("platform fault"), out _));
            Assert.Same(s_a.Assembly, ModFaults.CurrentOwner);

            var handlers = new ModHandlers<Action>("test");
            handlers.Add(() => throw new InvalidOperationException("handler fault"));
            handlers.Raise(h => h());
            Assert.Same(s_a.Assembly, ModFaults.CurrentOwner);
        }
        Assert.Null(ModFaults.CurrentOwner);
    }

    [Fact]
    public void ACallerBasedApiFollowsTheOwner()
    {
        DevCommand? command = null;
        try
        {
            ModCall.Run(s_a.Assembly, "test", () => command = DevConsole.Add("callingmodprobe", "test", _ => { }));
            Assert.Equal("CallingMod.A", command!.ModId);
        }
        finally
        {
            if (command != null) DevConsole.Remove(command);
        }
    }
}

/// <summary>What an SDK method acting for the calling mod does, for the fake mods to call.</summary>
public static class CallerProbe
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Assembly Who() => CallingMod.Of(Assembly.GetCallingAssembly());

    public static Assembly? Ambient() => ModFaults.CurrentOwner;
}

/// <summary>A mod made in memory, in a load context of its own, whose methods call <see cref="CallerProbe"/>.</summary>
internal sealed class FakeMod
{
    public FakeMod(string name)
    {
        var context = new AssemblyLoadContext(name, isCollectible: true);
        using (context.EnterContextualReflection())
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.RunAndCollect);
            var type = assembly.DefineDynamicModule(name).DefineType("Mod", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var who = typeof(CallerProbe).GetMethod(nameof(CallerProbe.Who))!;
            Calling(type, "Who", who, [], MethodImplAttributes.NoOptimization);
            Calling(type, "WhoTail", who, []);
            Calling(type, "Ambient", typeof(CallerProbe).GetMethod(nameof(CallerProbe.Ambient))!, []);
            Calling(type, "AmbientOf", typeof(CallerProbe).GetMethod(nameof(CallerProbe.Ambient))!, [typeof(int)]);
            var made = type.CreateType();
            Assembly = made.Assembly;
            Who = made.GetMethod("Who")!.CreateDelegate<Func<Assembly>>();
            WhoTail = made.GetMethod("WhoTail")!.CreateDelegate<Func<Assembly>>();
            AmbientFunc = made.GetMethod("Ambient")!.CreateDelegate<Func<Assembly?>>();
            AmbientOf = made.GetMethod("AmbientOf")!.CreateDelegate<Func<int, Assembly?>>();
        }
    }

    public Assembly Assembly { get; }

    /// <summary>The mod calling <see cref="CallerProbe.Who"/> itself, its own frame on the stack.</summary>
    public Func<Assembly> Who { get; }

    /// <summary>
    /// The same as a tail call, as <c>Assembly Who() =&gt; CallerProbe.Who()</c> compiles: optimised (this assembly is
    /// collectible, so never at a lower tier), the mod's frame is gone and the stack shows whoever called it.
    /// </summary>
    public Func<Assembly> WhoTail { get; }

    /// <summary>The owner current while the mod's function runs.</summary>
    public Func<Assembly?> AmbientFunc { get; }

    public Func<int, Assembly?> AmbientOf { get; }

    private static void Calling(TypeBuilder type, string name, MethodInfo target, Type[] parameters,
                                MethodImplAttributes flags = MethodImplAttributes.IL)
    {
        var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, target.ReturnType, parameters);
        method.SetImplementationFlags(flags);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
    }
}
