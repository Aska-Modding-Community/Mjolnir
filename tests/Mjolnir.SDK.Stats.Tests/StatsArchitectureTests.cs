using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;

namespace Mjolnir.SDK.Stats.Tests;

/// <summary>
/// Guards the Mjolnir.SDK.Stats module: it has no public API of its own (mods use Mjolnir.SDK's CharacterStats), it reaches
/// Core only through Mjolnir.SDK, and it needs no other module.
/// </summary>
public class StatsArchitectureTests
{
    private static readonly Assembly Stats = typeof(StatsModule).Assembly;

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(Stats));

    [Fact]
    public void ItHasNoPublicType() => Assert.Empty(Stats.GetExportedTypes());

    [Fact]
    public void TheModuleReachesCoreOnlyThroughTheSdk() =>
        Assert.DoesNotContain("Mjolnir.Core", Stats.GetReferencedAssemblies().Select(a => a.Name));

    [Theory]
    [InlineData("Mjolnir.SDK.UI")]
    [InlineData("Mjolnir.SDK.Player")]
    [InlineData("Mjolnir.SDK.Content")]
    [InlineData("Mjolnir.SDK.AI")]
    [InlineData("Mjolnir.SDK.World")]
    public void TheModuleNeedsNoOtherModule(string module) =>
        Assert.DoesNotContain(module, Stats.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void OnlyItsTestsSeeItsInternals() => Assert.Equal(["Mjolnir.SDK.Stats.Tests"], ApiSurface.FriendsOf(Stats));
}

/// <summary>The stats module is an SDK module: Mjolnir.SDK finds it next to itself and starts it.</summary>
public class SdkModuleTests
{
    // Discover only: EnsureStarted runs once per process and connects the game.
    [Fact]
    public void TheSdkFindsTheStatsModuleNextToItself()
    {
        string folder = System.IO.Path.GetDirectoryName(typeof(StatsModule).Assembly.Location)!;
        var entries = SdkModules.Discover(folder);
        Assert.Contains(typeof(StatsModule), entries);
        Assert.True(typeof(ISdkModule).IsAssignableFrom(typeof(StatsModule)));
    }
}
