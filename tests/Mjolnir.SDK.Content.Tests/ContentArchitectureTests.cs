using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;

namespace Mjolnir.SDK.Content.Tests;

/// <summary>
/// Guards the Mjolnir.SDK.Content module:
/// <list type="bullet">
/// <item>its public signatures use only .NET types and the SDK's, never Core's or the game's (today it has none: the
/// localization API it connects to the game is the SDK's);</item>
/// <item>it reaches Core only through Mjolnir.SDK and needs no other module;</item>
/// <item>the event convention: EventHandler or EventHandler&lt;TArgs&gt;, no On prefix.</item>
/// </list>
/// </summary>
public class ContentArchitectureTests
{
    private static readonly Assembly Content = typeof(ContentModule).Assembly;

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(Content));

    [Fact]
    public void NoPublicSignatureUsesACoreType() => Assert.Empty(ApiSurface.Leaks(Content, ApiSurface.IsCore));

    [Fact]
    public void NoPublicSignatureUsesAGameType() => Assert.Empty(ApiSurface.Leaks(Content, ApiSurface.IsGame));

    [Fact]
    public void EventsFollowTheConvention() => Assert.Empty(ApiSurface.EventConventionBreaks(Content));

    [Fact]
    public void TheModuleReachesCoreOnlyThroughTheSdk() =>
        Assert.DoesNotContain("Mjolnir.Core", Content.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void TheModuleNeedsNoOtherModule() =>
        Assert.DoesNotContain(Content.GetReferencedAssemblies(), a => a.Name is "Mjolnir.SDK.UI" or "Mjolnir.SDK.Player");

    [Fact]
    public void OnlyItsTestsSeeItsInternals() => Assert.Equal(["Mjolnir.SDK.Content.Tests"], ApiSurface.FriendsOf(Content));
}

/// <summary>The content facade is an SDK module: Mjolnir.SDK finds it next to itself and starts it.</summary>
public class SdkModuleTests
{
    // Discover only: EnsureStarted runs once per process and patches game methods.
    [Fact]
    public void TheSdkFindsTheContentModuleNextToItself()
    {
        string folder = System.IO.Path.GetDirectoryName(typeof(ContentModule).Assembly.Location)!;
        Assert.Contains(typeof(ContentModule), SdkModules.Discover(folder));
    }
}
