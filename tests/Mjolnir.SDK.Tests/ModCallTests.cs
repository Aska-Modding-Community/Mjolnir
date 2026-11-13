using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// How the SDK and its modules call mod code: each handler on its own, a throw contained (a mod's throw disables that
/// mod; the test assembly is platform code, so it is only logged), and a disabled mod skipped.
/// </summary>
public sealed class ModCallTests
{
    [Fact]
    public void EveryDelegateOfAHandlerRunsEvenIfOneThrows()
    {
        int count = 0;
        Action throwing = () => throw new InvalidOperationException("first");
        Action counting = () => count++;
        ModCall.InvokeEach(throwing + counting + counting, "test");
        Assert.Equal(2, count);
    }

    [Fact]
    public void ACombinedHandlerIsKeptAsItsParts()
    {
        int count = 0;
        Action throwing = () => throw new InvalidOperationException("first");
        Action counting = () => count++;
        var handlers = new ModHandlers<Action>("test");
        handlers.Add(throwing + counting);
        Assert.Equal(2, handlers.Count);

        handlers.Raise(h => h());
        Assert.Equal(1, count);

        handlers.Remove(throwing + counting);
        Assert.Equal(0, handlers.Count);
    }

    [Fact]
    public void TextAndItemsOfThrowingModCodeAreEmpty()
    {
        Assert.Equal("", ModCall.TextOf(new BadText(), "test"));
        Assert.Equal("5", ModCall.TextOf(5, "test"));
        Assert.Empty(ModCall.ItemsOf(new BadItems(), "test"));
        Assert.Equal([1, 2], ModCall.ItemsOf(new[] { 1, 2 }, "test"));
    }

    [Fact]
    public void ADisabledModsCodeIsSkipped()
    {
        var mod = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ModCall.Disabled"), AssemblyBuilderAccess.Run);
        Assert.False(ModCall.IsDisabled(mod));
        ModCall.Disable(mod, "test");
        Assert.True(ModCall.IsDisabled(mod));

        bool ran = false;
        ModCall.Run(mod, "test", () => ran = true);
        Assert.False(ran);
        Assert.False(ModCall.TryRun(mod, "test", () => 1, out _));
    }

    private sealed class BadText
    {
        public override string ToString() => throw new InvalidOperationException("no text");
    }

    private sealed class BadItems : IEnumerable
    {
        public IEnumerator GetEnumerator() => throw new InvalidOperationException("no items");
    }
}
