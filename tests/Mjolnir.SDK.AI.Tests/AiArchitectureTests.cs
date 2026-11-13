using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;

namespace Mjolnir.SDK.AI.Tests;

/// <summary>
/// Guards the Mjolnir.SDK.AI module:
/// <list type="bullet">
/// <item>its public signatures use only .NET, Unity and the SDK's types, never Core's or the game's (no Villager, no
/// Quest: <see cref="Resident"/> and <see cref="ResidentJob"/> are the module's own);</item>
/// <item>it reaches Core only through Mjolnir.SDK, and needs no other module;</item>
/// <item>the event convention: EventHandler or EventHandler&lt;TArgs&gt;, no On prefix.</item>
/// </list>
/// </summary>
public class AiArchitectureTests
{
    private static readonly Assembly Ai = typeof(Residents).Assembly;

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(Ai));

    [Fact]
    public void NoPublicSignatureUsesACoreType() => Assert.Empty(ApiSurface.Leaks(Ai, ApiSurface.IsCore));

    [Fact]
    public void NoPublicSignatureUsesAGameType() => Assert.Empty(ApiSurface.Leaks(Ai, ApiSurface.IsGame));

    [Fact]
    public void EventsFollowTheConvention() => Assert.Empty(ApiSurface.EventConventionBreaks(Ai));

    [Fact]
    public void TheModuleReachesCoreOnlyThroughTheSdk() =>
        Assert.DoesNotContain("Mjolnir.Core", Ai.GetReferencedAssemblies().Select(a => a.Name));

    [Theory]
    [InlineData("Mjolnir.SDK.UI")]
    [InlineData("Mjolnir.SDK.Player")]
    [InlineData("Mjolnir.SDK.Content")]
    public void TheModuleNeedsNoOtherModule(string module) =>
        Assert.DoesNotContain(module, Ai.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void OnlyItsTestsSeeItsInternals() => Assert.Equal(["Mjolnir.SDK.AI.Tests"], ApiSurface.FriendsOf(Ai));
}

/// <summary>The villager facade is an SDK module: Mjolnir.SDK finds it next to itself and starts it.</summary>
public class SdkModuleTests
{
    // Discover only: EnsureStarted runs once per process and patches game methods.
    [Fact]
    public void TheSdkFindsTheAiModuleNextToItself()
    {
        string folder = System.IO.Path.GetDirectoryName(typeof(AiModule).Assembly.Location)!;
        var entries = SdkModules.Discover(folder);
        Assert.Contains(typeof(AiModule), entries);
        Assert.True(typeof(ISdkModule).IsAssignableFrom(typeof(AiModule)));
    }
}
