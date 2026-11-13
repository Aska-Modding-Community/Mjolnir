using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;

namespace Mjolnir.SDK.Camera.Tests;

/// <summary>
/// Guards the Mjolnir.SDK.Camera module:
/// <list type="bullet">
/// <item>its public signatures use only .NET, Unity and the SDK's types, never Core's or the game's (Cinemachine's neither);</item>
/// <item>it reaches Core only through Mjolnir.SDK, and needs no other module;</item>
/// <item>the event convention: EventHandler or EventHandler&lt;TArgs&gt;, no On prefix.</item>
/// </list>
/// </summary>
public class CameraArchitectureTests
{
    private static readonly Assembly Camera = typeof(GameCamera).Assembly;

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(Camera));

    [Fact]
    public void NoPublicSignatureUsesACoreType() => Assert.Empty(ApiSurface.Leaks(Camera, ApiSurface.IsCore));

    [Fact]
    public void NoPublicSignatureUsesAGameType() => Assert.Empty(ApiSurface.Leaks(Camera, ApiSurface.IsGame));

    [Fact]
    public void NoPublicSignatureUsesACinemachineType() =>
        Assert.Empty(ApiSurface.Leaks(Camera, t => t.Assembly.GetName().Name == "Cinemachine"));

    [Fact]
    public void EventsFollowTheConvention() => Assert.Empty(ApiSurface.EventConventionBreaks(Camera));

    [Fact]
    public void TheModuleReachesCoreOnlyThroughTheSdk() =>
        Assert.DoesNotContain("Mjolnir.Core", Camera.GetReferencedAssemblies().Select(a => a.Name));

    [Theory]
    [InlineData("Mjolnir.SDK.UI")]
    [InlineData("Mjolnir.SDK.Player")]
    [InlineData("Mjolnir.SDK.Content")]
    [InlineData("Mjolnir.SDK.AI")]
    [InlineData("Mjolnir.SDK.World")]
    public void TheModuleNeedsNoOtherModule(string module) =>
        Assert.DoesNotContain(module, Camera.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void OnlyItsTestsSeeItsInternals() => Assert.Equal(["Mjolnir.SDK.Camera.Tests"], ApiSurface.FriendsOf(Camera));
}

/// <summary>The camera facade is an SDK module: Mjolnir.SDK finds it next to itself and starts it.</summary>
public class SdkModuleTests
{
    // Discover only: EnsureStarted runs once per process and patches the game.
    [Fact]
    public void TheSdkFindsTheCameraModuleNextToItself()
    {
        string folder = System.IO.Path.GetDirectoryName(typeof(CameraModule).Assembly.Location)!;
        var entries = SdkModules.Discover(folder);
        Assert.Contains(typeof(CameraModule), entries);
        Assert.True(typeof(ISdkModule).IsAssignableFrom(typeof(CameraModule)));
    }
}
