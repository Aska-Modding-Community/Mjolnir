using System;
using System.IO;
using System.Threading;
using Mjolnir.Core;
using Mjolnir.Core.Lifecycle;
using Mjolnir.Core.Mods;
using Mjolnir.SDK.Config;

namespace Mjolnir.SDK;

/// <summary>
/// The platform's own settings, Mjolnir/Config/mjolnir.toml (Core reserves the mod id "mjolnir", so no mod's config takes
/// that file). The SDK's modules bind their settings here (the UI module: [UI] Menus, [UI] Hud and [Console] Key; the animation module: [Kit]),
/// and the Mods window shows them on the page behind its footer's Settings button. Saved when that page closes and on quit, as
/// a mod's config. Part of the module contract.
/// </summary>
internal static class PlatformConfig
{
    private const string Source = "SDK";
    private static ModConfig? s_config;

    /// <summary>The settings, read on first use. Without the loader (unit tests) the file is in the temp folder.</summary>
    public static ModConfig Config => LazyInitializer.EnsureInitialized(ref s_config, Load);

    /// <summary>The config if anything used it (the Mods window's row asks before creating it).</summary>
    public static ModConfig? IfUsed => s_config;

    private static ModConfig Load()
    {
        string folder = Bootstrap.Paths?.ConfigDir ?? Path.Combine(Path.GetTempPath(), "mjolnir-platform", Environment.ProcessId.ToString());
        var config = ModConfig.Load(Path.Combine(folder, ModDiscovery.ReservedId + ".toml"), "Mjolnir", message => SdkLog.Warn(Source, message));
        GameEvents.ApplicationQuit += () => config.SaveIfChanged();
        return config;
    }
}
