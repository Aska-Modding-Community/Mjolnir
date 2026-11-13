using System;

namespace Mjolnir.Core.Mods;

// What the loader needs from a mod, without knowing the SDK: Mjolnir.SDK implements
// these internal interfaces (InternalsVisibleTo) and Core never references the SDK. Another front end, such
// as a scripting host, could implement them too.

/// <summary>A mod's manifest attribute; Mjolnir.SDK's <c>ModManifestAttribute</c>.</summary>
internal interface IModManifest
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
}

/// <summary>A dependency attribute; Mjolnir.SDK's <c>ModDependencyAttribute</c>.</summary>
internal interface IModDependencyDeclaration
{
    string Id { get; }
    string VersionRange { get; }
    bool Optional { get; }
}

/// <summary>A mod object; Mjolnir.SDK's <c>Mod</c> base class implements it explicitly.</summary>
internal interface IModEntry
{
    /// <summary>Phase A, once, in dependency order. Throwing disables the mod.</summary>
    void Load();

    /// <summary>After a successful <see cref="Load"/>, only when Il2CppInterop runs: hook the phase B callbacks.</summary>
    void Attach();
}

/// <summary>
/// Marks a front end of the mod API (Mjolnir.SDK's assembly) and names its entry, a class implementing
/// <see cref="IFrontEnd"/> with a public parameterless constructor. Core finds it by this attribute, never by name.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class FrontEndAttribute(Type entry) : Attribute
{
    public Type Entry { get; } = entry;
}

/// <summary>
/// A front end's entry (<see cref="FrontEnds"/>). Core calls <see cref="Start"/> once in phase A, after the mods loaded and
/// only when Il2CppInterop runs, whether or not any mod loaded: the front end's own UI (the SDK's Mods window, where mods
/// are turned back on) must exist with every mod turned off. Throwing is logged and costs only the front end.
/// </summary>
internal interface IFrontEnd
{
    void Start();
}

/// <summary>
/// What the loader injects into a mod while it is being constructed. The mod base class reads
/// <see cref="Creating"/> in its constructor, so a mod's manifest, log and config work there already.
/// </summary>
internal sealed class ModContext(ModDescriptor mod, string configPath)
{
    [ThreadStatic] private static ModContext? t_creating;

    /// <summary>The context of the mod being constructed on this thread, or null outside <see cref="Create"/>.</summary>
    internal static ModContext? Creating => t_creating;

    public ModDescriptor Mod { get; } = mod;

    /// <summary>Mjolnir/Config/&lt;id&gt;.toml.</summary>
    public string ConfigPath { get; } = configPath;

    internal static IModEntry Create(Type type, ModContext context)
    {
        t_creating = context;
        try
        {
            return (IModEntry)Activator.CreateInstance(type)!;
        }
        finally
        {
            t_creating = null;
        }
    }
}
