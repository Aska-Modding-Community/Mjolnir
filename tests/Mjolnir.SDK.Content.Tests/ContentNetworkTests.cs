using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Mjolnir.SDK.Content.Tests;

/// <summary>
/// Every kind of content tells the platform that its mod is needed by every player of a session (what the platform does
/// with it is the SDK's own test): the ids of items, recipes, buildings and effects travel between the players, so a mod
/// that chose no mode is compared when a player joins instead of letting the session drift apart.
/// </summary>
public class ContentNetworkTests
{
    private static readonly Assembly Caller = typeof(ContentNetworkTests).Assembly;

    private readonly List<(string ModId, string What)> _told = [];
    private readonly ContentRegistry _registry;

    public ContentNetworkTests() =>
        _registry = new ContentRegistry(_ => ("mjolnir.test", null), (modId, what) => _told.Add((modId, what)));

    [Fact]
    public void AnItemIsNeededByEveryPlayer()
    {
        _registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null);
        Assert.Equal(("mjolnir.test", "the item twine"), Assert.Single(_told));
    }

    [Fact]
    public void ARecipeIsNeededByEveryPlayer()
    {
        var twine = _registry.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null);
        _registry.AddRecipe(Caller, twine, 1, ModRecipe.DefaultBase);
        Assert.Equal(("mjolnir.test", "a recipe for twine"), _told[1]);
    }

    [Fact]
    public void ABuildingIsNeededByEveryPlayer()
    {
        _registry.AddBuilding(Caller, "altar", "Item_Decoration_ArchFlimsy", null);
        Assert.Equal(("mjolnir.test", "the building altar"), Assert.Single(_told));
    }

    [Fact]
    public void AnEffectIsNeededByEveryPlayer()
    {
        _registry.AddEffect(Caller, "blessing", "Effect_Leisure", null);
        Assert.Equal(("mjolnir.test", "the effect blessing"), Assert.Single(_told));
    }

    [Fact]
    public void ContentOfNoModTellsNothing()
    {
        var ownerless = new ContentRegistry(_ => null, (modId, what) => _told.Add((modId, what)));
        Assert.Throws<InvalidOperationException>(() => ownerless.AddItem(Caller, "twine", "Item_Materials_BasicRope", null, null));
        Assert.Empty(_told);
    }
}
