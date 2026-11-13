using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Mjolnir.SDK.Content.Tests;

/// <summary>
/// Content whose id or asset name was taken first: items, recipes and buildings share the game's item database, which keys
/// them by id and finds them by asset name. The later one is left out of the game (logged), its mod goes on, and no id or
/// name is ever computed otherwise (a save keeps them).
/// </summary>
public class ClashTests
{
    private static readonly Assembly Caller = typeof(ClashTests).Assembly;

    // A registry whose calling mod is whatever the test sets last.
    private sealed class Mods
    {
        public string Id = "mjolnir.test";
        public ContentRegistry Registry { get; }

        public Mods() => Registry = new ContentRegistry(_ => (Id, null));
    }

    // Names of one mod whose ids of two kinds meet, found by trying long names outside the tests (short ones never met: the
    // hash of a short ending stays near its prefix's). The ids are the real ones, so the tests check them first.
    private const string ItemMeetingRecipe = "n201633ad0229", RecipeMeetingItem = "n7f78f69ee7fb";
    private const string ItemMeetingBuilding = "n022ee7abe37d", BuildingMeetingItem = "nf2a6260f6643";

    [Fact]
    public void AnAssetNameTakenByAnotherModIsLeftOut()
    {
        var mods = new Mods { Id = "a_b" };
        var registry = mods.Registry;
        var first = registry.AddItem(Caller, "c", "Item_Materials_BasicRope", null, null);
        mods.Id = "a";
        int updates = 0;
        registry.Updated += () => updates++;
        var second = registry.AddItem(Caller, "b_c", "Item_Wood_Sticks", null, null);
        Assert.Equal(first.AssetName, second.AssetName); // Item_a_b_c, both: the format stays as saves know it
        Assert.Equal([first], registry.Items);
        Assert.True(registry.IsLeftOut(second));
        Assert.Equal(0, updates); // nothing for the game side to apply
        Assert.Same(second, registry.AddItem(Caller, "b_c", "Item_Wood_Sticks", null, null)); // the same again is the same
        Assert.Null(registry.ItemById(second.Id));
        Assert.Same(first, registry.ItemById(first.Id));
    }

    [Fact]
    public void ABuildingOnAnAssetNameTakenIsLeftOut()
    {
        var mods = new Mods { Id = "a_b" };
        var item = mods.Registry.AddItem(Caller, "c", "Item_Materials_BasicRope", null, null);
        mods.Id = "a";
        var building = mods.Registry.AddBuilding(Caller, "b_c", "Item_Decoration_ArchFlimsy", null);
        Assert.Equal(item.AssetName, building.AssetName);
        Assert.Empty(mods.Registry.Buildings);
        Assert.Null(mods.Registry.BuildingById(building.Id));
    }

    [Fact]
    public void AnItemNamedAsARecipeIsLeftOut()
    {
        var registry = new Mods().Registry;
        var twine = registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null);
        var recipe = registry.AddRecipe(Caller, twine, 1, ModRecipe.DefaultBase);
        var named = registry.AddItem(Caller, "twine_Recipe", "Item_Materials_BasicRope", null, null);
        Assert.Equal(recipe.AssetName, named.AssetName);
        Assert.Equal([twine], registry.Items);
        Assert.Equal([recipe], registry.Recipes);
    }

    [Fact]
    public void ARecipeNamedAsAnItemIsLeftOut()
    {
        var registry = new Mods().Registry;
        var named = registry.AddItem(Caller, "twine_Recipe", "Item_Materials_BasicRope", null, null);
        var twine = registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null);
        var recipe = registry.AddRecipe(Caller, twine, 1, ModRecipe.DefaultBase);
        Assert.Equal(named.AssetName, recipe.AssetName);
        Assert.Empty(registry.Recipes);
        Assert.True(registry.IsLeftOut(recipe));
        Assert.Same(recipe, registry.AddRecipe(Caller, twine, 1, ModRecipe.DefaultBase));
    }

    [Fact]
    public void AnItemOnARecipesIdIsLeftOut()
    {
        var (item, result) = (ItemMeetingRecipe, RecipeMeetingItem);
        Assert.Equal(ContentIds.ItemId("mjolnir.test", item), ContentIds.RecipeId("mjolnir.test", result));
        var registry = new Mods().Registry;
        var made = registry.AddItem(Caller, result, "Item_Materials_BasicRope", null, null);
        var recipe = registry.AddRecipe(Caller, made, 1, ModRecipe.DefaultBase);
        var late = registry.AddItem(Caller, item, "Item_Materials_BasicRope", null, null);
        Assert.Equal(recipe.Id, late.Id);
        Assert.Equal([made], registry.Items); // the game side never finds a recipe under an item's id
        Assert.Equal([recipe], registry.Recipes);
    }

    [Fact]
    public void ABuildingOnAnItemsIdIsLeftOut()
    {
        var (item, building) = (ItemMeetingBuilding, BuildingMeetingItem);
        Assert.Equal(ContentIds.ItemId("mjolnir.test", item), ContentIds.BuildingId("mjolnir.test", building));
        var registry = new Mods().Registry;
        var first = registry.AddItem(Caller, item, "Item_Materials_BasicRope", null, null);
        var late = registry.AddBuilding(Caller, building, "Item_Decoration_ArchFlimsy", null);
        Assert.Equal(first.Id, late.Id);
        Assert.Empty(registry.Buildings);
        Assert.Same(first, registry.ItemById(first.Id));
    }

    [Fact]
    public void ARecipeOfAnItemLeftOutIsLeftOut()
    {
        var mods = new Mods { Id = "a_b" };
        mods.Registry.AddItem(Caller, "c", "Item_Materials_BasicRope", null, null);
        mods.Id = "a";
        var left = mods.Registry.AddItem(Caller, "b_c", "Item_Materials_BasicRope", null, null);
        var recipe = mods.Registry.AddRecipe(Caller, left, 1, ModRecipe.DefaultBase);
        Assert.Empty(mods.Registry.Recipes);
        Assert.True(mods.Registry.IsLeftOut(recipe));
    }

    [Fact]
    public void ARecipeIsOfItsResultNotOfItsName()
    {
        var mods = new Mods { Id = "mjolnir.other" };
        var theirs = mods.Registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null);
        mods.Id = "mjolnir.test";
        var ours = mods.Registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null);
        var forTheirs = mods.Registry.AddRecipe(Caller, theirs, 1, ModRecipe.DefaultBase);
        var forOurs = mods.Registry.AddRecipe(Caller, ours, 2, ModRecipe.DefaultBase);
        Assert.NotSame(forTheirs, forOurs); // another result: another recipe
        Assert.Same(ours, forOurs.Result);
        // Recipe ids come from the mod and the result's name, as saves know them: the second takes the first's, so it is left out.
        Assert.Equal(forTheirs.Id, forOurs.Id);
        Assert.Equal([forTheirs], mods.Registry.Recipes);
        Assert.Same(forOurs, mods.Registry.AddRecipe(Caller, ours, 2, ModRecipe.DefaultBase));
    }

    [Fact]
    public void ContentIsFoundByItsId()
    {
        var registry = new Mods().Registry;
        var twine = registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null);
        var arch = registry.AddBuilding(Caller, "arch", "Item_Decoration_ArchFlimsy", null);
        Assert.Same(twine, registry.ItemById(twine.Id));
        Assert.Same(arch, registry.BuildingById(arch.Id));
        Assert.Null(registry.ItemById(arch.Id));
        Assert.Null(registry.BuildingById(twine.Id));
        Assert.Null(registry.ItemById(999)); // the game's own
    }

    [Fact]
    public void AnEffectOnAnAssetNameTakenIsLeftOut()
    {
        var mods = new Mods { Id = "a_b" };
        var first = mods.Registry.AddEffect(Caller, "c", "Effect_Leisure", null);
        mods.Id = "a";
        var second = mods.Registry.AddEffect(Caller, "b_c", "Effect_Leisure", null);
        Assert.Equal(first.AssetName, second.AssetName);
        Assert.Equal([first], mods.Registry.Effects);
    }

    [Fact]
    public void AModsFileIsNullForANonMod()
    {
        Assert.Null(new ContentRegistry(_ => null).TryPathOf(Caller, "bundles/models", "Models.Spawn('Altar')"));
        Assert.Equal(System.IO.Path.GetFullPath(@"C:\Mods\Test\bundles\models"),
                     new ContentRegistry(_ => ("mjolnir.test", @"C:\Mods\Test")).TryPathOf(Caller, "bundles/models", "Models.Spawn('Altar')"));
    }

    [Fact]
    public void OnlyAModelChangeMakesTheWorldObjectAgain()
    {
        var item = new Mods().Registry.AddItem(Caller, "spool", "Item_Materials_BasicRope", null, null);
        int model = item.ModelVersion;
        item.WithAttribute("Property_Durability", 100);
        Assert.Equal(model, item.ModelVersion);
        item.WithModel("bundles/models", "Spool");
        Assert.Equal(model + 1, item.ModelVersion);
        item.WithModel("bundles/models", "Spool"); // the same: no change
        Assert.Equal(model + 1, item.ModelVersion);
    }
}
