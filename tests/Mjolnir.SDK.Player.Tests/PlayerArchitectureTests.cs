using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;

namespace Mjolnir.SDK.Player.Tests;

/// <summary>
/// Guards the Mjolnir.SDK.Player module:
/// <list type="bullet">
/// <item>its public signatures use only .NET types and the SDK's, never Core's or the game's (no PlayerCharacter, no
/// DrainStaminaUsage: <see cref="StaminaUse"/> is the module's own);</item>
/// <item>it reaches Core only through Mjolnir.SDK, so Core's friends stay the SDK and the tests;</item>
/// <item>the event convention: EventHandler or EventHandler&lt;TArgs&gt;, no On prefix.</item>
/// </list>
/// </summary>
public class PlayerArchitectureTests
{
    private static readonly Assembly Player = typeof(LocalPlayer).Assembly;

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(Player));

    [Fact]
    public void NoPublicSignatureUsesACoreType() => Assert.Empty(ApiSurface.Leaks(Player, ApiSurface.IsCore));

    [Fact]
    public void NoPublicSignatureUsesAGameType() => Assert.Empty(ApiSurface.Leaks(Player, ApiSurface.IsGame));

    [Fact]
    public void EventsFollowTheConvention() => Assert.Empty(ApiSurface.EventConventionBreaks(Player));

    [Fact]
    public void TheModuleReachesCoreOnlyThroughTheSdk() =>
        Assert.DoesNotContain("Mjolnir.Core", Player.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void TheModuleDoesNotNeedTheUi() =>
        Assert.DoesNotContain("Mjolnir.SDK.UI", Player.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void OnlyItsTestsSeeItsInternals() => Assert.Equal(["Mjolnir.SDK.Player.Tests"], ApiSurface.FriendsOf(Player));
}

/// <summary>The player facade is an SDK module: Mjolnir.SDK finds it next to itself and starts it.</summary>
public class SdkModuleTests
{
    // Discover only: EnsureStarted runs once per process and patches game methods.
    [Fact]
    public void TheSdkFindsThePlayerModuleNextToItself()
    {
        string folder = System.IO.Path.GetDirectoryName(typeof(PlayerModule).Assembly.Location)!;
        var entries = SdkModules.Discover(folder);
        Assert.Contains(typeof(PlayerModule), entries);
        Assert.True(typeof(ISdkModule).IsAssignableFrom(typeof(PlayerModule)));
    }
}
