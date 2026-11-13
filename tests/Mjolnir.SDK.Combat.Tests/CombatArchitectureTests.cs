using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;

namespace Mjolnir.SDK.Combat.Tests;

/// <summary>
/// Guards the Mjolnir.SDK.Combat module: it has no public API of its own (mods use Mjolnir.SDK's Combatant and Combatants),
/// it reaches Core only through Mjolnir.SDK, and it needs no other module.
/// </summary>
public class CombatArchitectureTests
{
    private static readonly Assembly Combat = typeof(CombatModule).Assembly;

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(Combat));

    [Fact]
    public void ItHasNoPublicType() => Assert.Empty(Combat.GetExportedTypes());

    [Fact]
    public void TheModuleReachesCoreOnlyThroughTheSdk() =>
        Assert.DoesNotContain("Mjolnir.Core", Combat.GetReferencedAssemblies().Select(a => a.Name));

    [Theory]
    [InlineData("Mjolnir.SDK.UI")]
    [InlineData("Mjolnir.SDK.Player")]
    [InlineData("Mjolnir.SDK.Content")]
    [InlineData("Mjolnir.SDK.AI")]
    [InlineData("Mjolnir.SDK.World")]
    [InlineData("Mjolnir.SDK.Stats")]
    [InlineData("Mjolnir.SDK.Net")]
    [InlineData("Mjolnir.SDK.Animation")]
    public void TheModuleNeedsNoOtherModule(string module) =>
        Assert.DoesNotContain(module, Combat.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void OnlyItsTestsSeeItsInternals() => Assert.Equal(["Mjolnir.SDK.Combat.Tests"], ApiSurface.FriendsOf(Combat));
}

/// <summary>The combat module is an SDK module: Mjolnir.SDK finds it next to itself and starts it.</summary>
public class SdkModuleTests
{
    // Discover only: EnsureStarted runs once per process and connects the game.
    [Fact]
    public void TheSdkFindsTheCombatModuleNextToItself()
    {
        string folder = System.IO.Path.GetDirectoryName(typeof(CombatModule).Assembly.Location)!;
        var entries = SdkModules.Discover(folder);
        Assert.Contains(typeof(CombatModule), entries);
        Assert.True(typeof(ISdkModule).IsAssignableFrom(typeof(CombatModule)));
    }
}
