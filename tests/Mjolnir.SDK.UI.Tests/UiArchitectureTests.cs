using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mjolnir.Tests.Shared;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>
/// Guards the Mjolnir.SDK.UI module:
/// <list type="bullet">
/// <item>its public signatures use only .NET, Unity and TextMeshPro types, never Core's or the game's;</item>
/// <item>it reaches Core only through Mjolnir.SDK (2026-10-22), so Core's friends stay the SDK and the tests;</item>
/// <item>the naming rules: the Ui prefix on the controls a mod shows, plain names on everything else;</item>
/// <item>the event convention: EventHandler or EventHandler&lt;TArgs&gt;, no On prefix.</item>
/// </list>
/// </summary>
public class UiArchitectureTests
{
    private static readonly Assembly Ui = typeof(Control).Assembly;

    [Fact]
    public void TheCallingModIsFoundThroughTheHelper() => Assert.Empty(ApiSurface.RawCallingAssemblyUses(Ui));

    [Fact]
    public void NoPublicSignatureUsesACoreType() => Assert.Empty(ApiSurface.Leaks(Ui, ApiSurface.IsCore));

    [Fact]
    public void NoPublicSignatureUsesAGameType() => Assert.Empty(ApiSurface.Leaks(Ui, ApiSurface.IsGame));

    [Fact]
    public void EventsFollowTheConvention() => Assert.Empty(ApiSurface.EventConventionBreaks(Ui));

    [Fact]
    public void TheModuleReachesCoreOnlyThroughTheSdk() =>
        Assert.DoesNotContain("Mjolnir.Core", Ui.GetReferencedAssemblies().Select(a => a.Name));

    [Fact]
    public void OnlyItsTestsSeeItsInternals() => Assert.Equal(["Mjolnir.SDK.UI.Tests"], ApiSurface.FriendsOf(Ui));

    /// <summary>
    /// The controls a mod creates and shows (UiButton, UiSlider, UiImage…, and UiWindow) carry the Ui prefix: bare Button,
    /// Slider, Image, Toggle would clash with UnityEngine.UI and UIElements in every mod that also uses raw Unity UI
    /// (decided with the user, 2026-10-21).
    /// </summary>
    [Fact]
    public void ControlsHaveTheUiPrefix() =>
        Assert.Empty(UiTypes().Where(IsControl).Where(t => !t.Name.StartsWith("Ui", StringComparison.Ordinal)).Select(t => t.FullName));

    /// <summary>
    /// Everything else has a plain name (decided with the user, 2026-10-22): base classes
    /// (Control, HeaderedControl, Panel), the property system (MjolnirObject, MjolnirProperty, BindingMode), enums and
    /// structs (Orientation, Thickness), event args, templates, commands.
    /// </summary>
    [Fact]
    public void NothingElseHasTheUiPrefix() =>
        Assert.Empty(UiTypes().Where(t => !IsControl(t))
                              .Where(t => t.Name.StartsWith("Ui", StringComparison.Ordinal) || t.Name.StartsWith("IUi", StringComparison.Ordinal))
                              .Select(t => t.FullName));

    private static IEnumerable<Type> UiTypes() => Ui.GetExportedTypes().Where(t => t.Namespace == typeof(Control).Namespace);

    private static bool IsControl(Type type) =>
        !type.IsAbstract && (typeof(Control).IsAssignableFrom(type) || typeof(UiWindow).IsAssignableFrom(type));
}

/// <summary>The UI is an SDK module: Mjolnir.SDK finds it next to itself and starts it (it adds the Mods window).</summary>
public class SdkModuleTests
{
    // Discover only: EnsureStarted runs once per process and patches game methods.
    [Fact]
    public void TheSdkFindsTheUiModuleNextToItself()
    {
        string folder = System.IO.Path.GetDirectoryName(typeof(UiModule).Assembly.Location)!;
        var entries = SdkModules.Discover(folder);
        Assert.Equal([typeof(UiModule)], entries);
        Assert.True(typeof(ISdkModule).IsAssignableFrom(entries[0]));
    }
}
