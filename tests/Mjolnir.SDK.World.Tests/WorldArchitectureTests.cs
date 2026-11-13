using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;

namespace Mjolnir.SDK.World.Tests;

/// <summary>
/// Guards the Mjolnir.SDK.World module:
/// <list type="bullet">
/// <item>its public signatures use only .NET, Unity and the SDK's types, never Core's or the game's;</item>
/// <item>it reaches Core only through Mjolnir.SDK, and needs no other module;</item>
/// <item>the event convention: EventHandler or EventHandler&lt;TArgs&gt;, no On prefix.</item>
/// </list>
/// </summary>
public class WorldArchitectureTests
{
    private static readonly Assembly World = typeof(WorldTime).Assembly;

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(World));

    [Fact]
    public void NoPublicSignatureUsesACoreType() => Assert.Empty(ApiSurface.Leaks(World, ApiSurface.IsCore));

    [Fact]
    public void NoPublicSignatureUsesAGameType() => Assert.Empty(ApiSurface.Leaks(World, ApiSurface.IsGame));

    [Fact]
    public void EventsFollowTheConvention() => Assert.Empty(ApiSurface.EventConventionBreaks(World));

    [Fact]
    public void TheModuleReachesCoreOnlyThroughTheSdk() =>
        Assert.DoesNotContain("Mjolnir.Core", World.GetReferencedAssemblies().Select(a => a.Name));

    [Theory]
    [InlineData("Mjolnir.SDK.UI")]
    [InlineData("Mjolnir.SDK.Player")]
    [InlineData("Mjolnir.SDK.Content")]
    [InlineData("Mjolnir.SDK.AI")]
    public void TheModuleNeedsNoOtherModule(string module) =>
        Assert.DoesNotContain(module, World.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void OnlyItsTestsSeeItsInternals() => Assert.Equal(["Mjolnir.SDK.World.Tests"], ApiSurface.FriendsOf(World));
}

/// <summary>The world facade is an SDK module: Mjolnir.SDK finds it next to itself and starts it.</summary>
public class SdkModuleTests
{
    // Discover only: EnsureStarted runs once per process and connects the game's clock.
    [Fact]
    public void TheSdkFindsTheWorldModuleNextToItself()
    {
        string folder = System.IO.Path.GetDirectoryName(typeof(WorldModule).Assembly.Location)!;
        var entries = SdkModules.Discover(folder);
        Assert.Contains(typeof(WorldModule), entries);
        Assert.True(typeof(ISdkModule).IsAssignableFrom(typeof(WorldModule)));
    }
}
