using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Mjolnir.SDK.Content.Tests;

/// <summary>
/// Mods' status effects without the game: their short ids and asset names, the registry. What the game makes of them is checked
/// in game (UiLab).
/// </summary>
public class EffectsTests
{
    private static readonly Assembly Caller = typeof(EffectsTests).Assembly;

    private static ContentRegistry Registry(string modId = "mjolnir.test", string? folder = @"C:\Mods\Test") =>
        new(_ => (modId, folder));

    [Fact]
    public void AnIdComesFromTheModAndTheNameAndFitsTheNetwork()
    {
        int id = ContentIds.EffectId("mjolnir.test", "blessing");
        Assert.Equal(id, ContentIds.EffectId("mjolnir.test", "blessing"));
        Assert.InRange(id, 20000, short.MaxValue); // above the game's own, and the network has 16 bits for it
        Assert.NotEqual(id, ContentIds.EffectId("mjolnir.other", "blessing"));
        Assert.NotEqual(id, ContentIds.EffectId("mjolnir.test", "curse"));
    }

    [Fact]
    public void EffectsAddStaysOutOfLineToFindItsMod()
    {
        // Effects.Add is a static extension of the main library's Effects: a mod's call lands in this method, and only
        // a method that is never inlined sees the mod as its caller.
        var add = typeof(EffectsExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "Add");
        Assert.True(add.MethodImplementationFlags.HasFlag(MethodImplAttributes.NoInlining));
        Assert.Equal(typeof(ModEffect), add.ReturnType);
    }

    [Fact]
    public void EveryIdFitsTheNetwork()
    {
        foreach (int i in Enumerable.Range(0, 2000)) Assert.InRange(ContentIds.EffectId("mod", $"effect{i}"), 20000, short.MaxValue);
    }

    [Fact]
    public void AnIdDoesNotDependOnTheProcess() => Assert.Equal(30205, ContentIds.EffectId("a", "b")); // 20000 + FNV-1a of "a/effect/b" with the sign bit, modulo 12768

    [Fact]
    public void AnAssetNameLooksLikeTheGames() =>
        Assert.Equal("Effect_mjolnir.test_blessing", ContentIds.EffectAssetName("mjolnir.test", "blessing"));

    [Fact]
    public void AnEffectKeepsWhatItWasGiven()
    {
        var effect = Registry().AddEffect(Caller, "blessing", "Effect_Leisure", "items/blessing.png");
        Assert.Equal("blessing", effect.Name);
        Assert.Equal("mjolnir.test", effect.ModId);
        Assert.Equal("Effect_Leisure", effect.BasedOn);
        Assert.EndsWith(System.IO.Path.Combine("Test", "items", "blessing.png"), effect.IconPath, StringComparison.Ordinal);
        Assert.Equal(ContentIds.EffectId("mjolnir.test", "blessing"), effect.Id);
        Assert.Equal("Effect_mjolnir.test_blessing", effect.AssetName);
    }

    [Fact]
    public void TheNameWithoutTextsIsItsKey() => Assert.Equal("blessing", Registry().AddEffect(Caller, "blessing", "Effect_Leisure", null).DisplayName);

    [Fact]
    public void ChangesAreKeptInOrder()
    {
        var effect = Registry().AddEffect(Caller, "blessing", "Effect_Leisure", null)
                               .Modifies("Attribute_Strength", 3)
                               .Modifies("Attribute_Stamina", 0.5f, ModifierKind.Percent)
                               .PerSecond("VariableAttribute_Health", 1);
        Assert.Equal([("Attribute_Strength", 3f, ModifierKind.Add), ("Attribute_Stamina", 0.5f, ModifierKind.Percent)], effect.Modifiers);
        Assert.Equal([("VariableAttribute_Health", 1f)], effect.Rates);
    }

    [Fact]
    public void AnEffectAddedAgainIsTheSame()
    {
        var registry = Registry();
        var effect = registry.AddEffect(Caller, "blessing", "Effect_Leisure", null);
        Assert.Same(effect, registry.AddEffect(Caller, "blessing", "Effect_Leisure", null));
        Assert.Single(registry.Effects);
    }

    [Fact]
    public void ItCannotChangeItsBase()
    {
        var registry = Registry();
        registry.AddEffect(Caller, "blessing", "Effect_Leisure", null);
        Assert.Throws<ArgumentException>(() => registry.AddEffect(Caller, "blessing", "Effect_Sleep", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("blessing!")]
    public void ANameIsLettersDigitsAndUnderscores(string name) =>
        Assert.Throws<ArgumentException>(() => Registry().AddEffect(Caller, name, "Effect_Leisure", null));

    [Fact]
    public void AnEffectOnAnIdTakenIsLeftOutAndItsModGoesOn()
    {
        var registry = Registry();
        var taken = Enumerable.Range(0, 20000).Select(i => $"effect{i}").GroupBy(n => ContentIds.EffectId("mjolnir.test", n)).First(g => g.Count() > 1).ToList();
        var first = registry.AddEffect(Caller, taken[0], "Effect_Leisure", null);
        int updates = 0;
        registry.Updated += () => updates++;
        var second = registry.AddEffect(Caller, taken[1], "Effect_Leisure", null);
        Assert.Equal([first], registry.Effects);
        Assert.Same(second, registry.AddEffect(Caller, taken[1], "Effect_Leisure", null));
        Assert.Equal(0, updates);
    }

    [Fact]
    public void ANewEffectIsAnnounced()
    {
        var registry = Registry();
        int updates = 0;
        registry.Updated += () => updates++;
        registry.AddEffect(Caller, "blessing", "Effect_Leisure", null);
        Assert.Equal(1, updates);
    }

    [Fact]
    public void AChangeOfAnEffectIsAnnouncedWithANewVersion()
    {
        var registry = Registry();
        var effect = registry.AddEffect(Caller, "blessing", "Effect_Leisure", null);
        int updates = 0, version = registry.Version, changes = effect.Version;
        registry.Updated += () => updates++;
        effect.Modifies("Attribute_Strength", 3).PerSecond("VariableAttribute_Health", 1);
        Assert.Equal(2, updates);
        Assert.Equal(version + 2, registry.Version);
        Assert.Equal(changes + 2, effect.Version);
    }
}
