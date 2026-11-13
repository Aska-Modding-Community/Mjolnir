using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Mjolnir.SDK.Content.Tests;

/// <summary>
/// Mods' items and recipes without the game: their ids and asset names (ContentIds), and the registry the public API
/// fills (ContentRegistry) with a stand-in for the mod lookup. What the game makes of them is checked in game (UiLab).
/// </summary>
public class ItemsTests
{
    private static readonly Assembly Caller = typeof(ItemsTests).Assembly;

    private static ContentRegistry Registry(string modId = "mjolnir.test", string? folder = @"C:\Mods\Test") =>
        new(_ => (modId, folder));

    [Fact]
    public void IdsComeFromTheModAndTheNameAndAreNegative()
    {
        int id = ContentIds.ItemId("mjolnir.test", "twine");
        Assert.Equal(id, ContentIds.ItemId("mjolnir.test", "twine")); // the same for every player and start
        Assert.True(id < 0); // the game's own ids are 999 to 126 607 971
        Assert.NotEqual(id, ContentIds.ItemId("mjolnir.other", "twine"));
        Assert.NotEqual(id, ContentIds.ItemId("mjolnir.test", "rope"));
        Assert.NotEqual(id, ContentIds.RecipeId("mjolnir.test", "twine")); // a recipe is an item of its own
    }

    [Fact]
    public void AnIdDoesNotDependOnTheProcess()
    {
        // FNV-1a of "mjolnir.test/item/twine" with the sign bit: string.GetHashCode would change per process.
        Assert.Equal(unchecked((int)0xCE193218), ContentIds.ItemId("mjolnir.test", "twine"));
    }

    [Fact]
    public void ABuildingsNetworkGuidComesFromTheModAndTheName()
    {
        // The MD5 of "mjolnir.altarexample/building/altar": the guid the altar's prefab had in game (UiLab netprefab research).
        Assert.Equal("d1bb04bed7f8b5a29869c130a8e962c2", ContentIds.BuildingGuid("mjolnir.altarexample", "altar"));
        Assert.NotEqual(ContentIds.BuildingGuid("mjolnir.altarexample", "altar"), ContentIds.BuildingGuid("mjolnir.other", "altar"));
    }

    [Fact]
    public void AssetNamesAreNamedAsTheGamesAre()
    {
        Assert.Equal("Item_mjolnir.test_twine", ContentIds.ItemAssetName("mjolnir.test", "twine"));
        Assert.Equal("Item_mjolnir.test_twine_Recipe", ContentIds.RecipeAssetName("mjolnir.test", "twine"));
    }

    [Theory]
    [InlineData("twine", true)]
    [InlineData("Twine_2", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("two words", false)]
    [InlineData("dot.ted", false)]
    [InlineData("ёлка", false)]
    public void NamesAreLettersDigitsAndUnderscores(string? name, bool valid) => Assert.Equal(valid, ContentIds.IsValidName(name));

    [Fact]
    public void AnItemKnowsItsModIdNameAndPicture()
    {
        var item = Registry().AddItem(Caller, "twine", "Item_Materials_BasicRope", "items/twine.png", 20);
        Assert.Equal(("mjolnir.test", "twine", "Item_Materials_BasicRope", 20), (item.ModId, item.Name, item.BasedOn, item.StackSize));
        Assert.Equal(ContentIds.ItemId("mjolnir.test", "twine"), item.Id);
        Assert.Equal("Item_mjolnir.test_twine", item.AssetName);
        Assert.Equal(System.IO.Path.GetFullPath(@"C:\Mods\Test\items\twine.png"), item.IconPath); // relative to the mod's folder
        Assert.Equal("mjolnir.test.twine", item.ToString());
    }

    [Fact]
    public void AnItemHasItsBasesModelUntilItGetsOneOfItsOwn()
    {
        var registry = Registry();
        var spool = registry.AddItem(Caller, "twine_spool", "Item_Materials_BasicRope", null, null);
        Assert.Null(spool.ModelPath);
        Assert.Null(spool.ModelAsset);
        int updates = 0, version = spool.Version;
        registry.Updated += () => updates++;
        Assert.Same(spool, spool.WithModel("bundles/models", "TwineSpool"));
        Assert.Equal(System.IO.Path.GetFullPath(@"C:\Mods\Test\bundles\models"), spool.ModelPath); // relative to the mod's folder
        Assert.Equal("TwineSpool", spool.ModelAsset);
        Assert.Equal((1, version + 1), (updates, spool.Version)); // the game side gives an item made already its model
        spool.WithModel("bundles/models", "TwineSpool");
        Assert.Equal((1, version + 1), (updates, spool.Version)); // the same model again changes nothing
        spool.WithModel("bundles/models", "BigSpool");
        Assert.Equal(("BigSpool", 2), (spool.ModelAsset, updates));
    }

    [Theory]
    [InlineData("", "TwineSpool")]
    [InlineData("bundles/models", " ")]
    public void AModelNeedsABundleAndAPrefab(string bundle, string asset) =>
        Assert.Throws<ArgumentException>(() => Registry().AddItem(Caller, "twine_spool", "Item_Materials_BasicRope", null, null).WithModel(bundle, asset));

    [Fact]
    public void AnItemAddedAgainIsTheSameItem()
    {
        var registry = Registry();
        var first = registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null);
        Assert.Same(first, registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null));
        Assert.Single(registry.Items);
        // The same name made from another game item is a mistake.
        Assert.Throws<ArgumentException>(() => registry.AddItem(Caller, "twine", "Item_Wood_Sticks", null, null));
    }

    [Fact]
    public void BadItemsAreRefused()
    {
        var registry = Registry();
        Assert.Throws<ArgumentException>(() => registry.AddItem(Caller, "two words", "Item_Materials_BasicRope", null, null));
        Assert.Throws<ArgumentException>(() => registry.AddItem(Caller, "twine", " ", null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, 0));
        Assert.Empty(registry.Items);
    }

    [Fact]
    public void OnlyAModAddsContent()
    {
        var registry = new ContentRegistry(_ => null);
        Assert.Throws<InvalidOperationException>(() => registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null));
    }

    [Fact]
    public void ARecipeTakesItsPartsInOrder()
    {
        var registry = Registry();
        int updates = 0;
        registry.Updated += () => updates++;
        var twine = registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null);
        var recipe = registry.AddRecipe(Caller, twine, 2, ModRecipe.DefaultBase)
                             .Needs("Item_Wood_Sticks", 2)
                             .Needs(twine, 1);
        Assert.Equal((twine, 2, "Item_Binders_BasicRope"), (recipe.Result, recipe.Quantity, recipe.BasedOn));
        Assert.Equal([new RecipePart("Item_Wood_Sticks", 2), new RecipePart("Item_mjolnir.test_twine", 1)], recipe.Parts);
        Assert.Equal(ContentIds.RecipeId("mjolnir.test", "twine"), recipe.Id);
        Assert.Equal(4, updates); // the item, the recipe, each part: the game side applies what came later
        Assert.Same(recipe, registry.AddRecipe(Caller, twine, 2, ModRecipe.DefaultBase));
        Assert.Throws<ArgumentOutOfRangeException>(() => recipe.Needs("Item_Wood_Sticks", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.AddRecipe(Caller, twine, 0, ModRecipe.DefaultBase));
    }

    [Fact]
    public void ARecipeIsMadeWhereItsBaseIsUnlessPlacesAreNamed()
    {
        var registry = Registry();
        int updates = 0;
        var spool = registry.AddItem(Caller, "twine_spool", "Item_Materials_BasicRope", null, null);
        var recipe = registry.AddRecipe(Caller, spool, 1, ModRecipe.DefaultBase);
        registry.Updated += () => updates++;
        Assert.Empty(recipe.Places); // the base recipe's places: the game side reads them from its lists
        Assert.Same(recipe, recipe.MadeAt("Item_Structures_WorkshopL0").MadeAt(ModRecipe.ByHand).MadeAt("Item_Structures_WorkshopL0"));
        Assert.Equal(["Item_Structures_WorkshopL0", "hand"], recipe.Places);
        Assert.Equal(2, updates); // each new place: a recipe already in the game moves
        Assert.Throws<ArgumentException>(() => recipe.MadeAt(" "));
        Assert.Throws<ArgumentNullException>(() => recipe.MadeAt(null!));
    }

    [Fact]
    public void ABuildingIsNamedAsAnItemWithAnIdOfItsOwn()
    {
        var registry = Registry();
        var arch = registry.AddBuilding(Caller, "twine_arch", "Item_Decoration_ArchFlimsy", "items/twine_arch.png");
        Assert.Equal(("mjolnir.test", "twine_arch", "Item_Decoration_ArchFlimsy"), (arch.ModId, arch.Name, arch.BasedOn));
        Assert.Equal("Item_mjolnir.test_twine_arch", arch.AssetName);
        Assert.Equal(ContentIds.BuildingId("mjolnir.test", "twine_arch"), arch.Id);
        Assert.True(arch.Id < 0);
        Assert.NotEqual(ContentIds.ItemId("mjolnir.test", "twine_arch"), arch.Id); // a building is no item
        Assert.Equal(System.IO.Path.GetFullPath(@"C:\Mods\Test\items\twine_arch.png"), arch.IconPath);
        Assert.Same(arch, registry.AddBuilding(Caller, "twine_arch", "Item_Decoration_ArchFlimsy", null));
        Assert.Throws<ArgumentException>(() => registry.AddBuilding(Caller, "twine_arch", "Item_Decoration_IvyArch", null));
        Assert.Equal(["mjolnir.test.twine_arch"], registry.Buildings.Select(b => b.ToString()));
    }

    [Fact]
    public void ItemsAndBuildingsShareTheModsNames()
    {
        var registry = Registry();
        registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null);
        registry.AddBuilding(Caller, "arch", "Item_Decoration_ArchFlimsy", null);
        Assert.Throws<ArgumentException>(() => registry.AddBuilding(Caller, "twine", "Item_Decoration_ArchFlimsy", null));
        Assert.Throws<ArgumentException>(() => registry.AddItem(Caller, "arch", "Item_Materials_BasicRope", null, null));
        Assert.Throws<ArgumentException>(() => registry.AddBuilding(Caller, "an arch", "Item_Decoration_ArchFlimsy", null));
        Assert.Throws<InvalidOperationException>(() => new ContentRegistry(_ => null).AddBuilding(Caller, "arch", "Item_Decoration_ArchFlimsy", null));
    }

    [Fact]
    public void ABuildingCostsItsBasesUnlessPartsAreGiven()
    {
        var registry = Registry();
        var twine = registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null);
        var arch = registry.AddBuilding(Caller, "twine_arch", "Item_Decoration_ArchFlimsy", null);
        int updates = 0;
        registry.Updated += () => updates++;
        Assert.Empty(arch.Parts);
        Assert.Same(arch, arch.Needs("Item_Wood_Sticks", 5).Needs(twine, 4));
        Assert.Equal([new RecipePart("Item_Wood_Sticks", 5), new RecipePart("Item_mjolnir.test_twine", 4)], arch.Parts);
        Assert.Equal(2, updates); // each part: a building already in the game gets its new cost
        Assert.Throws<ArgumentOutOfRangeException>(() => arch.Needs("Item_Wood_Sticks", 0));
        Assert.Throws<ArgumentNullException>(() => arch.Needs((ModItem)null!, 1));
    }

    [Fact]
    public void EachStageCostsItsOwnParts()
    {
        var registry = Registry();
        var house = registry.AddBuilding(Caller, "house", "Item_Structures_HouseL1", null);
        Assert.Equal(0, house.StagesWithParts);
        house.Needs("Item_Stone_Raw", 4).Needs("Item_Wood_Thatch", 10, 2).Needs("Item_Wood_Sticks", 2);
        Assert.Equal([new RecipePart("Item_Stone_Raw", 4), new RecipePart("Item_Wood_Sticks", 2)], house.PartsAt(0));
        Assert.Empty(house.PartsAt(1)); // a stage without parts is only built
        Assert.Equal([new RecipePart("Item_Wood_Thatch", 10)], house.PartsAt(2));
        Assert.Equal(3, house.StagesWithParts);
        Assert.Equal(3, house.Parts.Count); // every stage's, in the order given
        Assert.Throws<ArgumentOutOfRangeException>(() => house.Needs("Item_Stone_Raw", 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => house.PartsAt(-1));
    }

    [Fact]
    public void ABuildingsSettingsAndTheirChanges()
    {
        var registry = Registry();
        var altar = registry.AddBuilding(Caller, "altar", "Item_Decoration_DecorativeBirch", null);
        int updates = 0;
        registry.Updated += () => updates++;
        Assert.Equal((null, null, null, null, null), (altar.ModelAsset, altar.ConstructionModelAsset, altar.Category, altar.BuildEffort, altar.LevelsGround));
        int version = altar.Version;
        altar.WithModel("bundles/models", "Altar").WithConstructionModel("bundles/models", "AltarFrame")
             .WithCategory("Categ_Blueprints_Totems").WithBuildEffort(20).WithGroundLevelling();
        Assert.Equal(System.IO.Path.GetFullPath(@"C:\Mods\Test\bundles\models"), altar.ConstructionModelPath);
        Assert.Equal(("Altar", "AltarFrame", "Categ_Blueprints_Totems", 20f, true),
                     (altar.ModelAsset, altar.ConstructionModelAsset, altar.Category, altar.BuildEffort, altar.LevelsGround));
        Assert.Equal(5, updates);
        Assert.True(altar.Version > version);
        altar.WithCategory("Categ_Blueprints_Totems").WithBuildEffort(20).WithGroundLevelling(true); // the same: no change
        Assert.Equal(5, updates);
        Assert.Throws<ArgumentOutOfRangeException>(() => altar.WithBuildEffort(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => altar.WithBuildEffort(float.NaN));
        Assert.Throws<ArgumentException>(() => altar.WithCategory(" "));
    }

    [Fact]
    public void ABuildingMayStandOnAModsBuilding()
    {
        var registry = Registry();
        var altar = registry.AddBuilding(Caller, "altar", "Item_Decoration_DecorativeBirch", null)
                            .WithModel("bundles/models", "Altar").WithCategory("Categ_Blueprints_Totems").Needs("Item_Stone_Raw", 4);
        var big = registry.AddBuilding(Caller, "big_altar", altar.AssetName, null);
        Assert.Equal("Item_Decoration_DecorativeBirch", big.GameBase);
        Assert.Same(altar, big.BaseBuilding);
        // What it does not give is its base's.
        Assert.Equal(("Altar", "Categ_Blueprints_Totems"), (big.ModelAsset, big.Category));
        Assert.Equal([new RecipePart("Item_Stone_Raw", 4)], big.Parts);
        big.WithModel("bundles/models", "BigAltar").Needs("Item_Stone_Raw", 8);
        Assert.Equal("BigAltar", big.ModelAsset);
        Assert.Equal([new RecipePart("Item_Stone_Raw", 8)], big.Parts);
        Assert.Equal("Altar", altar.ModelAsset);
        Assert.Throws<ArgumentException>(() => registry.AddBuilding(Caller, "self", "Item_mjolnir.test_self", null));
    }

    [Fact]
    public void ABuildingsStagesHaveModelsOfTheirOwn()
    {
        var registry = Registry();
        var lodge = registry.AddBuilding(Caller, "lodge", "Item_Structures_BuilderHutL1", null);
        int updates = 0;
        registry.Updated += () => updates++;
        Assert.Empty(lodge.StageLooks);
        Assert.False(lodge.HasOwnLook);
        // Given in any order, they come by stage; a stage may have a model, a construction look, or both.
        Assert.Same(lodge, lodge.WithModel("bundles/lodge", "Roof", stage: 2));
        lodge.WithModel("bundles/lodge", "Frame", stage: 0).WithConstructionModel("bundles/lodge", "RoofScaffolding", stage: 2)
             .WithConstructionModel("bundles/lodge", "WallsScaffolding", stage: 1);
        string bundle = System.IO.Path.GetFullPath(@"C:\Mods\Test\bundles\lodge");
        Assert.Equal([new StageLook(0, bundle, "Frame", null, null), new StageLook(1, null, null, bundle, "WallsScaffolding"),
                      new StageLook(2, bundle, "Roof", bundle, "RoofScaffolding")], lodge.StageLooks);
        Assert.True(lodge.HasOwnLook);
        Assert.Null(lodge.ModelPath); // no model for every stage
        Assert.Equal(4, updates);
        lodge.WithModel("bundles/lodge", "Frame", stage: 0); // the same: no change
        Assert.Equal(4, updates);
        lodge.WithModel("bundles/lodge", "BigFrame", stage: 0); // another: replaces it
        Assert.Equal(("BigFrame", 5), (lodge.StageLooks[0].ModelAsset, updates));
        Assert.Throws<ArgumentOutOfRangeException>(() => lodge.WithModel("bundles/lodge", "Frame", stage: -1));
        Assert.Throws<ArgumentException>(() => lodge.WithConstructionModel("bundles/lodge", " ", stage: 1));
        // A building on it takes its stages' looks until it gives its own.
        var big = registry.AddBuilding(Caller, "big_lodge", lodge.AssetName, null);
        Assert.Equal(lodge.StageLooks, big.StageLooks);
        big.WithModel("bundles/lodge", "BigRoof", stage: 2);
        Assert.Equal([new StageLook(2, bundle, "BigRoof", null, null)], big.StageLooks);
    }

    [Fact]
    public void ModsKeepTheirOwnNames()
    {
        string modId = "mjolnir.a";
        var registry = new ContentRegistry(_ => (modId, null));
        var a = registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", "twine.png", null);
        modId = "mjolnir.b";
        var b = registry.AddItem(Caller, "twine", "Item_Wood_Sticks", null, null);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(["mjolnir.a.twine", "mjolnir.b.twine"], registry.Items.Select(i => i.ToString()));
        Assert.Equal("twine.png", a.IconPath); // no folder: as given
    }
}
