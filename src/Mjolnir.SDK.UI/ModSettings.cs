using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Mjolnir.SDK.UI;

/// <summary>
/// Your mod's own page behind its Settings button in the Mods window.
/// <para>
/// Without one, the window builds the page from your config: a header per section and a bound control per setting. That
/// is the right page for most mods. Set your own when the settings need more shape than a flat list — a mod that carries
/// other mods' settings, a page with its own rows, buttons or pages of its own:
/// </para>
/// <code>
/// ModSettings.SetPage(() => new UiStackPanel
/// {
///     Children = { new UiSectionHeader { Text = "Speed" }, SettingControls.For(_speed) },
/// });
/// </code>
/// <para>
/// The window around it stays the platform's: the title, and a footer with Reset, Close and the restart note. Reset
/// still puts every setting of your <see cref="Mjolnir.SDK.Config.ModConfig"/> back to its default, and the file is
/// written when the page closes, so a control you bind with <see cref="SettingControls"/> behaves as it does on the
/// built page. The builder runs once, the first time the player opens the page.
/// </para>
/// </summary>
public static class ModSettings
{
    private static readonly ConcurrentDictionary<Assembly, Func<Control>> Pages = [];

    /// <summary>
    /// Sets the calling mod's page, in place of the one built from its config. <paramref name="build"/> runs the first
    /// time the player opens the page, on the main thread.
    /// </summary>
    /// <param name="build">Makes the page's content.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SetPage(Func<Control> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        Pages[CallingMod.Of(Assembly.GetCallingAssembly())] = build;
    }

    /// <summary>Takes the calling mod's page away; its Settings button goes back to the page built from its config.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ClearPage() => Pages.TryRemove(CallingMod.Of(Assembly.GetCallingAssembly()), out _);

    /// <summary>The mod's own page builder, or null for the page built from its config.</summary>
    internal static Func<Control>? PageOf(Assembly mod) => Pages.TryGetValue(mod, out var build) ? build : null;
}
