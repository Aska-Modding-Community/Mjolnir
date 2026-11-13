using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Mjolnir.SDK.Content.Tests;

/// <summary>
/// Items of a kind and what makes a weapon, armour or food the mod's own (<see cref="ItemKind"/>, <see cref="ModItem.WithDamage"/>,
/// <see cref="ModItem.WithAttribute"/>, the <c>Applies…</c> effects): the model a mod fills. What the game makes of them is
/// checked in game (UiLab).
/// </summary>
public class EquipmentTests
{
    private static readonly Assembly Caller = typeof(EquipmentTests).Assembly;

    private static ContentRegistry Registry() => new(_ => ("mjolnir.test", @"C:\Mods\Test"));

    private static ModEffect Blessing(ContentRegistry registry) => registry.AddEffect(Caller, "blessing", "Effect_Leisure", null);

    [Fact]
    public void EveryKindStandsOnAGameItem()
    {
        foreach (var kind in Enum.GetValues<ItemKind>()) Assert.StartsWith("Item_", ItemKinds.BaseOf(kind));
        Assert.Equal(Enum.GetValues<ItemKind>().Length, Enum.GetValues<ItemKind>().Select(ItemKinds.BaseOf).Distinct().Count());
        Assert.Equal("Item_OneHanded_1HIronSword", ItemKinds.BaseOf(ItemKind.OneHandedWeapon));
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemKinds.BaseOf((ItemKind)99));
    }

    [Fact]
    public void AnItemOfAKindKnowsItAndItsBase()
    {
        var registry = Registry();
        var sword = registry.AddItem(Caller, "rune_sword", ItemKinds.BaseOf(ItemKind.OneHandedWeapon), null, null, ItemKind.OneHandedWeapon);
        Assert.Equal((ItemKind.OneHandedWeapon, "Item_OneHanded_1HIronSword"), (sword.Kind, sword.BasedOn));
        Assert.Null(registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null).Kind);
        // The same name again is the same item.
        Assert.Same(sword, registry.AddItem(Caller, "rune_sword", ItemKinds.BaseOf(ItemKind.OneHandedWeapon), null, null, ItemKind.OneHandedWeapon));
    }

    [Fact]
    public void AWeaponsDamage()
    {
        var registry = Registry();
        var sword = registry.AddItem(Caller, "rune_sword", ItemKinds.BaseOf(ItemKind.OneHandedWeapon), null, null, ItemKind.OneHandedWeapon);
        int updates = 0;
        registry.Updated += () => updates++;
        Assert.Equal((null, null), (sword.MinimumDamage, sword.MaximumDamage));
        Assert.Same(sword, sword.WithDamage(14, 18));
        Assert.Equal((14f, 18f, 1), (sword.MinimumDamage, sword.MaximumDamage, updates));
        sword.WithDamage(14, 18); // the same: no change
        Assert.Equal(1, updates);
        Assert.Throws<ArgumentOutOfRangeException>(() => sword.WithDamage(-1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => sword.WithDamage(10, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => sword.WithDamage(1, float.PositiveInfinity));
        // Only weapons deal damage; an item made from a named game item is checked in the game instead.
        var hood = registry.AddItem(Caller, "hood", ItemKinds.BaseOf(ItemKind.Helmet), null, null, ItemKind.Helmet);
        Assert.Throws<InvalidOperationException>(() => hood.WithDamage(1, 2));
        Assert.Equal(3f, registry.AddItem(Caller, "rope_whip", "Item_Materials_BasicRope", null, null).WithDamage(3, 4).MinimumDamage);
    }

    [Fact]
    public void AnItemsAttributesReplaceTheirValues()
    {
        var registry = Registry();
        var hood = registry.AddItem(Caller, "hood", ItemKinds.BaseOf(ItemKind.Helmet), null, null, ItemKind.Helmet);
        int updates = 0;
        registry.Updated += () => updates++;
        hood.WithArmor(40).WithAttribute("Attribute_ColdResistance", 60).WithDurability(900).WithArmor(50);
        Assert.Equal([new("Attribute_Armor", 50f), new("Attribute_ColdResistance", 60f), new KeyValuePair<string, float>("Property_Durability", 900f)], hood.Attributes);
        Assert.Equal(4, updates);
        hood.WithArmor(50); // the same: no change
        Assert.Equal(4, updates);
        Assert.Throws<ArgumentException>(() => hood.WithAttribute(" ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => hood.WithAttribute("Attribute_Armor", float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => hood.WithDurability(0));
    }

    [Fact]
    public void EffectsOnHitWhileEquippedAndWhenEaten()
    {
        var registry = Registry();
        var blessing = Blessing(registry);
        var sword = registry.AddItem(Caller, "rune_sword", ItemKinds.BaseOf(ItemKind.OneHandedWeapon), null, null, ItemKind.OneHandedWeapon);
        sword.AppliesOnHit(blessing, TimeSpan.FromSeconds(4), 0.25f).AppliesWhileEquipped(blessing);
        Assert.Equal([new ItemEffect(blessing.AssetName, TimeSpan.FromSeconds(4), 0.25f)], sword.HitEffects);
        Assert.Equal([new ItemEffect(blessing.AssetName, null, 1f)], sword.EquippedEffects);
        int updates = 0;
        registry.Updated += () => updates++;
        sword.AppliesOnHit(blessing, TimeSpan.FromSeconds(4), 0.25f); // the same: no second
        Assert.Equal((1, 0), (sword.HitEffects.Count, updates));
        Assert.Throws<ArgumentOutOfRangeException>(() => sword.AppliesOnHit(blessing, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => sword.AppliesOnHit(blessing, TimeSpan.FromSeconds(1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => sword.AppliesOnHit(blessing, TimeSpan.FromSeconds(1), 1.5f));
        Assert.Throws<ArgumentNullException>(() => sword.AppliesWhileEquipped((EffectInfo)null!));
        Assert.Throws<ArgumentException>(() => sword.AppliesWhileEquipped(" "));
        // A game effect by asset name: the game knows its effects only once it runs, after OnLoad.
        sword.AppliesOnHit("Effect_Burning", TimeSpan.FromSeconds(5));
        Assert.Equal(new ItemEffect("Effect_Burning", TimeSpan.FromSeconds(5), 1f), sword.HitEffects[1]);
        Assert.Throws<InvalidOperationException>(() => sword.AppliesWhenEaten(blessing, TimeSpan.FromSeconds(1)));

        var cake = registry.AddItem(Caller, "honey_cake", ItemKinds.BaseOf(ItemKind.Food), null, null, ItemKind.Food);
        cake.AppliesWhenEaten(blessing, TimeSpan.FromMinutes(5));
        Assert.Equal([new ItemEffect(blessing.AssetName, TimeSpan.FromMinutes(5), 1f)], cake.EatenEffects);
        Assert.Throws<InvalidOperationException>(() => cake.AppliesWhileEquipped(blessing));
        Assert.Throws<InvalidOperationException>(() => cake.AppliesOnHit(blessing, TimeSpan.FromSeconds(1)));

        // A bow shoots, it does not strike; a helmet is worn.
        var bow = registry.AddItem(Caller, "rune_bow", ItemKinds.BaseOf(ItemKind.Bow), null, null, ItemKind.Bow);
        Assert.Throws<InvalidOperationException>(() => bow.AppliesOnHit(blessing, TimeSpan.FromSeconds(1)));
        registry.AddItem(Caller, "hood", ItemKinds.BaseOf(ItemKind.Helmet), null, null, ItemKind.Helmet).AppliesWhileEquipped(blessing);
    }

    [Fact]
    public void KindsByWhatTheyDo()
    {
        Assert.True(ItemKinds.IsWeapon(ItemKind.Arrow));
        Assert.False(ItemKinds.Strikes(ItemKind.Arrow));
        Assert.True(ItemKinds.Strikes(ItemKind.Shield));
        Assert.True(ItemKinds.IsWorn(ItemKind.Cape));
        Assert.True(ItemKinds.IsEquipment(ItemKind.Bow));
        Assert.False(ItemKinds.IsEquipment(ItemKind.Food));
        Assert.False(ItemKinds.IsEquipment(ItemKind.Material));
    }
}
