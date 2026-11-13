using System.Linq;
using Xunit;

namespace Mjolnir.SDK.Content.Tests;

/// <summary>
/// What the console's give and items find among the game's items: an asset name with or without Item_, the English
/// name (an item before the recipes named after it), then every item containing the words, in any case; and give's words
/// (a name of several words, a count after it).
/// </summary>
public sealed class ItemSearchTests
{
    private static readonly ItemEntry[] Items =
    [
        new("Item_Wood_Sticks", "Sticks", false),
        new("Item_Wood_Logs", "Logs", false),
        new("Item_Materials_BasicRope", "Rope", false),
        new("Item_Binders_BasicRope", "Rope", true),
        new("Item_Binders_5xBasicRope", "Rope", true),
        new("Item_mjolnir.itemsexample_twine", "Twine", false),
        new("Item_mjolnir.itemsexample_twine_Recipe", "Twine", true),
        new("Item_Food_RawFish", "Fish", false),
        new("Item_Food_RawFish_Big", "Fish", false),
        new("Item_Decoration_JotunBP", "Stone Jotun", true),
        new("Item_Tools_StoneBlade", "Stone Blade", false),
    ];

    [Theory]
    [InlineData("Item_Wood_Sticks")]
    [InlineData("item_wood_sticks")]
    [InlineData("Wood_Sticks")]
    [InlineData("STICKS")]
    public void AnAssetNameOrTheEnglishNameFindsOneItem(string query) =>
        Assert.Equal(["Item_Wood_Sticks"], Assets(query));

    [Fact]
    public void TheAssetNameWinsOverTheSameNameOnARecipe() =>
        Assert.Equal(["Item_Materials_BasicRope"], Assets("Materials_BasicRope"));

    [Theory]
    [InlineData("rope", "Item_Materials_BasicRope")]
    [InlineData("Twine", "Item_mjolnir.itemsexample_twine")]
    public void TheItemWinsOverTheRecipesNamedAfterIt(string query, string asset) => Assert.Equal([asset], Assets(query));

    [Fact]
    public void ARecipeIsFoundByItsAssetNameOrItsOwnName()
    {
        Assert.Equal(["Item_Binders_BasicRope"], Assets("Binders_BasicRope"));
        Assert.Equal(["Item_Decoration_JotunBP"], Assets("stone jotun"));
    }

    [Fact]
    public void ANameSeveralItemsShareFindsThemAll() =>
        Assert.Equal(["Item_Food_RawFish", "Item_Food_RawFish_Big"], Assets("fish"));

    [Fact]
    public void OtherwiseEveryItemContainingTheWords() =>
        Assert.Equal(["Item_Wood_Sticks", "Item_Wood_Logs"], Assets("wood"));

    [Fact]
    public void ItemsListsEveryItemContainingTheWords()
    {
        Assert.Equal(["Item_Materials_BasicRope", "Item_Binders_BasicRope", "Item_Binders_5xBasicRope"], ItemSearch.Containing(Items, " rope ").Select(i => i.Asset));
        Assert.Empty(ItemSearch.Containing(Items, ""));
    }

    [Fact]
    public void AnEnglishNameOfSeveralWords() => Assert.Equal(["Item_Tools_StoneBlade"], Assets("stone blade"));

    [Fact]
    public void NothingOrBlankFindsNothing()
    {
        Assert.Empty(Assets("iron"));
        Assert.Empty(Assets("  "));
    }

    [Fact]
    public void GiveTakesANameOfSeveralWordsAndACountAfterIt()
    {
        Assert.Equal(("Stone blade", 5), ItemSearch.GiveArguments(["Stone", "blade", "5"]));
        Assert.Equal(("Wood_Sticks", 1), ItemSearch.GiveArguments(["Wood_Sticks"]));
        Assert.Equal(("5", 1), ItemSearch.GiveArguments(["5"]));
    }

    private static string[] Assets(string query) => [.. ItemSearch.Find(Items, query).Select(i => i.Asset)];
}
