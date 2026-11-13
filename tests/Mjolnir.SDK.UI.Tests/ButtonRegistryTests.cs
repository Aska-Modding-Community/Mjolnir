using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Mjolnir.SDK.UI;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>The bookkeeping behind Menus.Main/Pause.Add, which needs no game.</summary>
public class ButtonRegistryTests
{
    // Each test gets its own fake mod assemblies, so disabling one never leaks into another test.
    private static Assembly NewMod(string name) =>
        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);

    [Fact]
    public void KeepsRegistrationOrder()
    {
        var registry = new ButtonRegistry("Main");
        var a = NewMod("Order.A");
        var b = NewMod("Order.B");
        registry.Add(new UiButton { Content = "first" }, a);
        registry.Add(new UiButton { Content = "second" }, b);
        registry.Add(new UiButton { Content = "third" }, a);

        Assert.Equal(["first", "second", "third"], registry.Active().Select(e => ((UiButton)e.Element).Content));
    }

    [Fact]
    public void ThePlatformButtonGoesFirst()
    {
        var registry = new ButtonRegistry("Main");
        var mod = NewMod("First.Mod");
        registry.Add(new UiButton { Content = "mod button" }, mod);
        registry.Add(new UiButton { Content = "Mjolnir" }, typeof(Mod).Assembly, first: true); // the platform owner, as MenuHook.AddPlatformButton gives it

        Assert.Equal(["Mjolnir", "mod button"], registry.Active().Select(e => ((UiButton)e.Element).Content));
        Assert.Equal("Mjolnir_Main_Mjolnir.SDK_0", registry.Active()[0].ObjectName);
    }

    [Fact]
    public void NamesAreDeterministicPerModAndMenu()
    {
        var registry = new ButtonRegistry("Pause");
        var mod = NewMod("Names.Mod");
        var other = NewMod("Names.Other");
        registry.Add(new UiButton { Content = "x" }, mod);
        registry.Add(new UiButton { Content = "y" }, other);
        registry.Add(new UiButton { Content = "z" }, mod);

        Assert.Equal(["Mjolnir_Pause_Names.Mod_0", "Mjolnir_Pause_Names.Other_0", "Mjolnir_Pause_Names.Mod_1"],
                     registry.Active().Select(e => e.ObjectName));
    }

    [Fact]
    public void ARemovedButtonLeavesAndANewOneNeverReusesItsName()
    {
        var registry = new ButtonRegistry("Main");
        var mod = NewMod("Remove.Mod");
        var removed = new UiButton { Content = "removed" };
        registry.Add(new UiButton { Content = "kept" }, mod);
        registry.Add(removed, mod);

        Assert.True(registry.Remove(removed));
        Assert.False(registry.Remove(removed));
        registry.Add(new UiButton { Content = "added" }, mod);

        Assert.Equal(["kept", "added"], registry.Active().Select(e => ((UiButton)e.Element).Content));
        Assert.Equal(["Mjolnir_Main_Remove.Mod_0", "Mjolnir_Main_Remove.Mod_2"], registry.Active().Select(e => e.ObjectName));
    }

    [Fact]
    public void ButtonsOfADisabledModDropOut()
    {
        var registry = new ButtonRegistry("Main");
        var healthy = NewMod("Disabled.Healthy");
        var broken = NewMod("Disabled.Broken");
        registry.Add(new UiButton { Content = "kept" }, healthy);
        registry.Add(new UiButton { Content = "dropped" }, broken);

        ModCall.Disable(broken, "test");

        Assert.Equal(["kept"], registry.Active().Select(e => ((UiButton)e.Element).Content));
        Assert.Equal([true, false], registry.Snapshot().Select(e => e.Active));
    }
}
