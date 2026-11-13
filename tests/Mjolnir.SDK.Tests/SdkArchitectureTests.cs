using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// Guards Mjolnir.SDK, the main library every mod references:
/// <list type="bullet">
/// <item>the Core/SDK split: no public signature exposes a Core type (implemented Core contracts
/// are internal interfaces and do not count);</item>
/// <item>insulation from the game: no public signature uses a game type;</item>
/// <item>the module split (2026-10-22): the SDK references no game, UGUI or TextMeshPro assembly and none of its modules
/// (Mjolnir.SDK.UI…), which plug in through SdkModules; only its own modules and test projects see its internals;</item>
/// <item>the event convention: EventHandler or EventHandler&lt;TArgs&gt;, no On prefix.</item>
/// </list>
/// </summary>
public class SdkArchitectureTests
{
    private static readonly Assembly Sdk = typeof(Mod).Assembly;

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(Sdk));

    [Fact]
    public void ARawCallingAssemblyIsFound()
    {
        var uses = ApiSurface.RawCallingAssemblyUses(typeof(SdkArchitectureTests).Assembly);
        Assert.Contains($"{typeof(RawCaller).FullName}.{nameof(RawCaller.Raw)}", uses);
        Assert.DoesNotContain(uses, u => u.StartsWith(typeof(CallerProbe).FullName!, StringComparison.Ordinal));
    }

    private static class RawCaller
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static Assembly Raw() => Assembly.GetCallingAssembly();
    }

    [Fact]
    public void NoPublicSignatureUsesACoreType() => Assert.Empty(ApiSurface.Leaks(Sdk, ApiSurface.IsCore));

    [Fact]
    public void NoPublicSignatureUsesAGameType() => Assert.Empty(ApiSurface.Leaks(Sdk, ApiSurface.IsGame));

    [Fact]
    public void EventsFollowTheConvention() => Assert.Empty(ApiSurface.EventConventionBreaks(Sdk));

    [Fact]
    public void TheConventionAsksAChangeForItsValuesAndAnEventBeforehandForACancel()
    {
        var breaks = ApiSurface.EventConventionBreaks([typeof(BadEvents), typeof(GoodEvents)]).Select(b => b[..b.IndexOf(':')]);
        Assert.Equal(["BadEvents.Changed", "BadEvents.Closing", "BadEvents.CountChanged"], breaks.Order());
    }

#pragma warning disable CS0067 // never raised: only their types are looked at
    public sealed class BadEvents
    {
        public int Count { get; set; }
        public event EventHandler? Changed;
        public event EventHandler? CountChanged;
        public event EventHandler<EventArgs>? Closing;
        public event EventHandler? SelectionChanged; // no property is named Selection: not a value's change
    }

    public sealed class GoodEvents
    {
        public bool IsOn { get; set; }
        public event EventHandler<ValueChangedEventArgs<bool>>? IsOnChanged;
        public event EventHandler<CancelEventArgs>? Closing;
    }
#pragma warning restore CS0067

    [Theory]
    [InlineData("Assembly-CSharp")]
    [InlineData("SandSailorStudio")]
    [InlineData("Unity.TextMeshPro")]
    [InlineData("UnityEngine.UI")]
    [InlineData("UnityEngine.UIModule")]
    [InlineData("Mjolnir.SDK.UI")]
    [InlineData("Mjolnir.SDK.Player")]
    [InlineData("Mjolnir.SDK.Content")]
    [InlineData("Mjolnir.SDK.AI")]
    [InlineData("Mjolnir.SDK.World")]
    [InlineData("Mjolnir.SDK.Stats")]
    [InlineData("Mjolnir.SDK.Net")]
    [InlineData("Mjolnir.SDK.Animation")]
    [InlineData("Mjolnir.SDK.Combat")]
    [InlineData("Mjolnir.SDK.Camera")]
    [InlineData("Mjolnir.SDK.Terrain")]
    [InlineData("Cinemachine")]
    [InlineData("UnityEngine.AnimationModule")]
    [InlineData("Fusion.Runtime")]
    public void TheSdkReferencesNoGameUiOrModuleAssembly(string assembly) =>
        Assert.DoesNotContain(assembly, Sdk.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void OnlyTheSdksModulesAndTestsSeeItsInternals() =>
        Assert.All(ApiSurface.FriendsOf(Sdk), friend =>
            Assert.True(friend.StartsWith("Mjolnir.SDK.", StringComparison.Ordinal), $"{friend} sees the SDK's internals"));

    /// <summary>Core starts the SDK as its front end even when no mod loaded, so the Mods window is there with every mod off.</summary>
    [Fact]
    public void TheSdkIsCoresFrontEnd()
    {
        var front = Sdk.GetCustomAttribute<Mjolnir.Core.Mods.FrontEndAttribute>();
        Assert.NotNull(front);
        Assert.True(typeof(Mjolnir.Core.Mods.IFrontEnd).IsAssignableFrom(front.Entry));
        Assert.NotNull(front.Entry.GetConstructor(Type.EmptyTypes));
    }
}
