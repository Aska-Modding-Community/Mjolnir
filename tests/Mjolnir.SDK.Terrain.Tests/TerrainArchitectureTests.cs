using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;

namespace Mjolnir.SDK.Terrain.Tests;

/// <summary>
/// Guards the Mjolnir.SDK.Terrain module:
/// <list type="bullet">
/// <item>its public signatures use only .NET, Unity and the SDK's types, never Core's or the game's;</item>
/// <item>it reaches Core only through Mjolnir.SDK, and needs no other module;</item>
/// <item>the event convention: EventHandler or EventHandler&lt;TArgs&gt;, no On prefix, an …ing event cancellable.</item>
/// </list>
/// </summary>
public class TerrainArchitectureTests
{
    private static readonly Assembly Terrain = typeof(Landscape).Assembly;

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(Terrain));

    [Fact]
    public void NoPublicSignatureUsesACoreType() => Assert.Empty(ApiSurface.Leaks(Terrain, ApiSurface.IsCore));

    [Fact]
    public void NoPublicSignatureUsesAGameType() => Assert.Empty(ApiSurface.Leaks(Terrain, ApiSurface.IsGame));

    [Fact]
    public void NoPublicSignatureUsesTheVegetationSystem() =>
        Assert.Empty(ApiSurface.Leaks(Terrain, t => t.Assembly.GetName().Name == "AwesomeTechnologies.VegetationStudioPro.Runtime"));

    [Fact]
    public void EventsFollowTheConvention() => Assert.Empty(ApiSurface.EventConventionBreaks(Terrain));

    [Fact]
    public void TheModuleReachesCoreOnlyThroughTheSdk() =>
        Assert.DoesNotContain("Mjolnir.Core", Terrain.GetReferencedAssemblies().Select(a => a.Name));

    [Theory]
    [InlineData("Mjolnir.SDK.UI")]
    [InlineData("Mjolnir.SDK.Player")]
    [InlineData("Mjolnir.SDK.Content")]
    [InlineData("Mjolnir.SDK.AI")]
    [InlineData("Mjolnir.SDK.World")]
    [InlineData("Mjolnir.SDK.Camera")]
    public void TheModuleNeedsNoOtherModule(string module) =>
        Assert.DoesNotContain(module, Terrain.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void OnlyItsTestsSeeItsInternals() => Assert.Equal(["Mjolnir.SDK.Terrain.Tests"], ApiSurface.FriendsOf(Terrain));
}

/// <summary>The terrain facade is an SDK module: Mjolnir.SDK finds it next to itself and starts it.</summary>
public class SdkModuleTests
{
    // Discover only: EnsureStarted runs once per process and patches the game.
    [Fact]
    public void TheSdkFindsTheTerrainModuleNextToItself()
    {
        string folder = System.IO.Path.GetDirectoryName(typeof(TerrainModule).Assembly.Location)!;
        var entries = SdkModules.Discover(folder);
        Assert.Contains(typeof(TerrainModule), entries);
        Assert.True(typeof(ISdkModule).IsAssignableFrom(typeof(TerrainModule)));
    }
}
