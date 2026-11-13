using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Mjolnir.Core.Mods;
using Mjolnir.SDK;

[assembly: FrontEnd(typeof(SdkFrontEnd))]

namespace Mjolnir.SDK;

/// <summary>
/// The SDK as Core's front end: Core starts it after loading the mods, whether or not any mod loaded, so the modules (the
/// UI module's Mods window, where mods are turned back on) start even with every mod turned off.
/// </summary>
internal sealed class SdkFrontEnd : IFrontEnd
{
    public void Start() => SdkModules.EnsureStarted();
}

/// <summary>
/// Marks an assembly as a module of the SDK (Mjolnir.SDK.UI, later Player, Content…) and names its entry, which
/// <see cref="SdkModules"/> starts. Like ASP.NET's HostingStartup attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
internal sealed class SdkModuleAttribute(Type entry) : Attribute
{
    public Type Entry { get; } = entry;
}

/// <summary>The entry of an SDK module: started once, when the first mod attaches or, with no mod loaded, when Core starts the SDK.</summary>
internal interface ISdkModule
{
    /// <summary>
    /// Phase A, after Il2CppInterop started: Harmony patches and registrations only, no Unity objects. It must not
    /// rely on another module having started (modules start in name order).
    /// </summary>
    void Start();
}

/// <summary>
/// The SDK's modules: assemblies named Mjolnir.SDK.*.dll next to Mjolnir.SDK.dll (in game, Mjolnir/core) that carry an
/// <see cref="SdkModuleAttribute"/>. The SDK knows none of them by name: a module brings its own features (the UI
/// module, the Mods window). A module that fails is logged and skipped; nothing here throws into the loader, which
/// would disable the mod whose attach started it.
/// </summary>
internal static class SdkModules
{
    private const string Source = "SDK";
    private static int s_started;

    /// <summary>Starts every module once (called by each mod's attach and by <see cref="SdkFrontEnd"/>; the first call does the work).</summary>
    public static void EnsureStarted()
    {
        if (Interlocked.Exchange(ref s_started, 1) == 1) return;
        // The modules register their own commands and content: the platform's, whichever mod's attach starts them.
        using var platform = ModFaults.Enter(typeof(SdkModules).Assembly);
        try
        {
            // The main thread is recorded when the game is ready whatever the modules hook: until then every thread
            // counts as main (phase A), and after it only the game's.
            Threading.Dispatcher.Hook();
            string folder = Path.GetDirectoryName(typeof(SdkModules).Assembly.Location) ?? "";
            var entries = Discover(folder);
            if (entries.Count == 0) SdkLog.Warn(Source, $"No SDK module found in {folder}: no Mods window, no UI");
            foreach (var entry in entries) Start(entry);
        }
        catch (Exception e)
        {
            SdkLog.Error(Source, $"SDK modules not started: {e}");
        }
    }

    /// <summary>
    /// The entry types of the modules in <paramref name="folder"/>, in name order. Loaded into the SDK's own load context
    /// (the default one in game), where mods find them too, so there is one copy of each.
    /// </summary>
    internal static IReadOnlyList<Type> Discover(string folder)
    {
        var context = AssemblyLoadContext.GetLoadContext(typeof(SdkModules).Assembly) ?? AssemblyLoadContext.Default;
        var entries = new List<Type>();
        var files = Directory.EnumerateFiles(folder, "Mjolnir.SDK.*.dll")
                             .Where(path => !path.EndsWith(".Tests.dll", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        foreach (string path in files)
        {
            try
            {
                var assembly = context.LoadFromAssemblyName(AssemblyName.GetAssemblyName(path));
                if (assembly.GetCustomAttribute<SdkModuleAttribute>() is { } module) entries.Add(module.Entry);
            }
            catch (Exception e)
            {
                SdkLog.Error(Source, $"{Path.GetFileName(path)} is not a usable SDK module: {e.Message}");
            }
        }
        return entries;
    }

    private static void Start(Type entry)
    {
        string name = entry.Assembly.GetName().Name ?? entry.FullName ?? "?";
        try
        {
            ((ISdkModule)Activator.CreateInstance(entry, nonPublic: true)!).Start();
            SdkLog.Info(Source, $"SDK module {name} {entry.Assembly.GetName().Version} started");
        }
        catch (Exception e)
        {
            SdkLog.Error(Source, $"SDK module {name} failed to start: {e}");
        }
    }
}
