using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;

namespace Mjolnir.SDK.Content.Tests;

/// <summary>
/// The item catalog on a fake database: what a player can carry and what is left out, one object per item and per category,
/// the categories as a tree of those holding items (one among its own parents a top one), names in the player's language,
/// a mod's items, giving, nothing before the game has a database, other threads.
/// </summary>
[Collection("MainThread")]
public sealed class CatalogTests : IDisposable
{
    private const int Resources = 16777216, Food = 16793600, Wood = 16830464, Armor = 4194304, Helmets = 4210688, Blueprints = 12582912;

    private readonly FakeCatalog _game = new();
    private readonly ItemCatalog _catalog;

    public CatalogTests()
    {
        Dispatcher.ResetForTests();
        _catalog = new ItemCatalog(id => id == Twine ? "mjolnir.itemsexample" : null) { Source = _game };
        _game.Categories.AddRange([
            new CatalogCategory(Resources, "Categ_Resources", 0), new CatalogCategory(Food, "Categ_Resources_Food", Resources),
            new CatalogCategory(Wood, "Categ_Resources_Wood", Resources), new CatalogCategory(Armor, "Categ_Armor", 0),
            new CatalogCategory(Helmets, "Categ_Armor_Helmets", Armor), new CatalogCategory(Blueprints, "Categ_Blueprints", Blueprints),
        ]);
        _game.Entries.AddRange([
            new CatalogEntry(Sticks, "Item_Wood_Sticks", false, "SmallItem", 20, Wood),
            new CatalogEntry(Berries, "Item_Food_Berries", false, "SmallItem", 10, Food),
            new CatalogEntry(Helmet, "Item_Helmets_Leather", false, "HeadArmor", 1, Helmets),
            new CatalogEntry(3001, "Item_Binders_BasicRope", true, "SmallItem", 1, Blueprints), // a recipe
            new CatalogEntry(3002, "Item_Wood_Fir1", false, ItemCatalog.WorldStorage, 1, Wood), // a tree of the world
            new CatalogEntry(3003, "Item_Misc_BiomeMarkerCave", false, null, 1, Resources), // a mark on the map
            new CatalogEntry(3004, "Harvest_Wood_DwarfSpruce", false, "SmallItem", 0, Wood), // nothing to stack
            new CatalogEntry(Twine, "Item_mjolnir.itemsexample_twine", false, "SmallItem", 50, Wood),
        ]);
        _game.Names[Sticks] = "Палки";
        _game.CategoryNames[Wood] = "Древесина";
    }

    private const int Sticks = 1001, Berries = 1002, Helmet = 1003, Twine = -410912958;

    public void Dispose() => Dispatcher.ResetForTests();

    [Fact]
    public void OnlyWhatAPlayerCarriesIsAnItem()
    {
        Assert.Equal(["Item_Wood_Sticks", "Item_Food_Berries", "Item_Helmets_Leather", "Item_mjolnir.itemsexample_twine"],
                     _catalog.All.Select(i => i.AssetName));
        Assert.Equal(20, _catalog.Find("Item_Wood_Sticks")!.StackSize);
    }

    [Fact]
    public void ItemsAreOneObjectEachAndFollowTheDatabase()
    {
        var sticks = _catalog.Find("Item_Wood_Sticks");
        Assert.Same(_catalog.All, _catalog.All); // nothing read again while the database stands
        _game.Entries.Add(new CatalogEntry(1004, "Item_Food_Carrot", false, "SmallItem", 10, Food));
        Assert.Equal(4, _catalog.All.Count);

        _game.Stamp++; // built again, with an item more
        Assert.Equal(5, _catalog.All.Count);
        Assert.Same(sticks, _catalog.All[0]);
        Assert.Same(sticks, _catalog.Find("item_wood_sticks")); // any case
        Assert.Null(_catalog.Find("Item_Wood_Fir1"));
    }

    [Fact]
    public void NamesAreTheGamesInThePlayersLanguageElseTheAssetName()
    {
        var sticks = _catalog.Find("Item_Wood_Sticks")!;
        Assert.Equal("Палки", sticks.DisplayName);
        _game.Names[Sticks] = "Sticks"; // another language
        Assert.Equal("Sticks", sticks.DisplayName);
        Assert.Equal("Item_Food_Berries", _catalog.Find("Item_Food_Berries")!.DisplayName);
        Assert.Equal("", sticks.Description);
        Assert.Equal("Древесина", sticks.Category!.DisplayName);
        Assert.Equal("Categ_Resources", sticks.Category.Parent!.DisplayName);
    }

    [Fact]
    public void CategoriesAreATreeOfThoseHoldingItems()
    {
        Assert.Equal(["Categ_Armor", "Categ_Resources"], _catalog.Roots.Select(c => c.AssetName)); // by id, the game's order
        var resources = _catalog.Roots[1];
        Assert.Equal(["Categ_Resources_Food", "Categ_Resources_Wood"], resources.Children.Select(c => c.AssetName));
        var sticks = _catalog.Find("Item_Wood_Sticks")!;
        Assert.Same(resources.Children[1], sticks.Category);
        Assert.Same(resources, sticks.Category!.Root);
        Assert.True(resources.Contains(sticks.Category));
        Assert.False(_catalog.Roots[0].Contains(sticks.Category));
        Assert.Empty(sticks.Category.Children);

        _game.Stamp++;
        Assert.Same(resources, _catalog.Roots[1]); // one object per category too
    }

    [Fact]
    public void ACategoryAmongItsOwnParentsIsATopOne()
    {
        _game.Categories.Add(new CatalogCategory(70, "Categ_A", 71));
        _game.Categories.Add(new CatalogCategory(71, "Categ_B", 70));
        _game.Entries.Add(new CatalogEntry(1005, "Item_Loop", false, "SmallItem", 1, 70));
        var loop = _catalog.Find("Item_Loop")!;
        Assert.Null(loop.Category!.Parent);
        Assert.Same(loop.Category, loop.Category.Root);
        Assert.Contains(loop.Category, _catalog.Roots);
    }

    [Fact]
    public void AModsItemNamesItsMod()
    {
        Assert.Equal("mjolnir.itemsexample", _catalog.Find("Item_mjolnir.itemsexample_twine")!.ModId);
        Assert.Null(_catalog.Find("Item_Wood_Sticks")!.ModId);
    }

    [Fact]
    public void GivingGoesToTheGame()
    {
        var sticks = _catalog.Find("Item_Wood_Sticks")!;
        _game.Room = 7;
        Assert.Equal(7, _catalog.Give(sticks, 10)); // what went in
        Assert.Equal(0, _catalog.Give(sticks, 0));
        Assert.Equal([(Sticks, 10)], _game.Given);
        Assert.False(_catalog.CanGive);
        _game.CanGive = true;
        Assert.True(_catalog.CanGive);
    }

    [Fact]
    public void NothingBeforeTheGameHasADatabase()
    {
        var catalog = new ItemCatalog(_ => null);
        Assert.Empty(catalog.All);
        Assert.Empty(catalog.Roots);
        Assert.Null(catalog.Find("Item_Wood_Sticks"));
        Assert.False(catalog.CanGive);
    }

    [Fact]
    public void AnotherThreadSeesWhatTheMainThreadReadLast()
    {
        Dispatcher.MarkMainThread();
        var sticks = _catalog.Find("Item_Wood_Sticks")!;
        Assert.Equal("Палки", sticks.DisplayName);
        _game.Names[Sticks] = "Sticks";
        _game.Stamp++;
        _game.Entries.Add(new CatalogEntry(1004, "Item_Food_Carrot", false, "SmallItem", 10, Food));

        var (count, name, given) = OnAnotherThread(() => (_catalog.All.Count, sticks.DisplayName, _catalog.Give(sticks, 3)));
        Assert.Equal(4, count); // the list read on the main thread, not the game's new one
        Assert.Equal("Палки", name);
        Assert.Equal(0, given); // the game is asked on the main thread only
        Assert.Empty(_game.Given);
    }

    private static T OnAnotherThread<T>(Func<T> read)
    {
        T result = default!;
        var worker = new Thread(() => result = read());
        worker.Start();
        worker.Join();
        return result;
    }

    private sealed class FakeCatalog : IItemCatalogSource
    {
        public List<CatalogEntry> Entries { get; } = [];
        public List<CatalogCategory> Categories { get; } = [];
        public Dictionary<int, string> Names { get; } = [];
        public Dictionary<int, string> CategoryNames { get; } = [];
        public List<(int Id, int Count)> Given { get; } = [];
        public int Room { get; set; }
        public long Stamp { get; set; } = 1;
        public bool CanGive { get; set; }

        IReadOnlyList<CatalogEntry> IItemCatalogSource.Entries() => [.. Entries];

        IReadOnlyList<CatalogCategory> IItemCatalogSource.Categories() => [.. Categories];

        public string? DisplayName(int id) => Names.GetValueOrDefault(id);

        public string? Description(int id) => null;

        public Sprite? Icon(int id) => null;

        public string? CategoryName(int id) => CategoryNames.GetValueOrDefault(id);

        public int Give(int id, int count)
        {
            Given.Add((id, count));
            return Math.Min(count, Room);
        }
    }
}

/// <summary>Tests that mark the main thread run alone: the dispatcher's main thread is process-wide.</summary>
[CollectionDefinition("MainThread", DisableParallelization = true)]
public sealed class MainThreadCollection;
