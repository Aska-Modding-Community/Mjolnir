using System;
using System.Reflection;
using Mjolnir.Core.Mods;
using Xunit;

namespace Mjolnir.Core.Tests;

/// <summary>
/// The thread's current owner (<see cref="ModFaults.CurrentOwner"/>), which the SDK's caller lookup falls back to: set to
/// a handler's assembly while <see cref="ModFaults.Invoke{T}"/> runs it, nested, restored after a throw, and per thread.
/// </summary>
public sealed class ModFaultsTests
{
    private static readonly Assembly s_tests = typeof(ModFaultsTests).Assembly;
    private static readonly Assembly s_coreLib = typeof(object).Assembly;

    [Fact]
    public void AHandlerRunsAsItsAssembly()
    {
        Assert.Null(ModFaults.CurrentOwner);
        Assembly? during = null;
        ModFaults.Invoke<Action>(() => during = ModFaults.CurrentOwner, "test", h => h());
        Assert.Same(s_tests, during);
        Assert.Null(ModFaults.CurrentOwner);

        // The handler's own assembly, not the code that calls it: here a method of the runtime's library (never called).
        ModFaults.Invoke<Action>(GC.Collect, "test", _ => during = ModFaults.CurrentOwner);
        Assert.Same(s_coreLib, during);
    }

    [Fact]
    public void OwnersNestAndComeBack()
    {
        Assembly? inner = null, after = null;
        using (ModFaults.Enter(s_coreLib))
        {
            ModFaults.Invoke<Action>(() =>
            {
                using (ModFaults.Enter(typeof(ModFaults).Assembly)) inner = ModFaults.CurrentOwner;
                after = ModFaults.CurrentOwner;
            }, "test", h => h());
            Assert.Same(s_coreLib, ModFaults.CurrentOwner);
        }
        Assert.Same(typeof(ModFaults).Assembly, inner);
        Assert.Same(s_tests, after);
        Assert.Null(ModFaults.CurrentOwner);
    }

    [Fact]
    public void AThrowingHandlerRestoresTheOwnerBefore()
    {
        using (ModFaults.Enter(s_coreLib))
        {
            ModFaults.Invoke<Action>(() => throw new InvalidOperationException("handler fault"), "test", h => h());
            Assert.Same(s_coreLib, ModFaults.CurrentOwner);
        }
        Assert.Null(ModFaults.CurrentOwner);
    }

    [Fact]
    public void TheOwnerIsPerThread()
    {
        using (ModFaults.Enter(s_coreLib))
        {
            Assembly? elsewhere = s_tests;
            var thread = new System.Threading.Thread(() => elsewhere = ModFaults.CurrentOwner);
            thread.Start();
            thread.Join();
            Assert.Null(elsewhere);
        }
    }
}
