using System;
using Mjolnir.Core.Mods;

namespace Mjolnir.SDK;

/// <summary>
/// Marks the one <see cref="Mod"/> class of a mod assembly.
/// <para><paramref name="id"/> is the mod's stable identity: config file name, dependencies, multiplayer handshake.
/// Letters, digits, '.', '_' and '-' only, starting with a letter or digit; compared case-insensitively.</para>
/// <para><paramref name="version"/> is <c>Major[.Minor[.Patch[.Revision]]]</c>; a <c>-prerelease</c> or <c>+build</c>
/// suffix is allowed and ignored when comparing. An empty <paramref name="name"/> falls back to the id.</para>
/// <para>For the player: <see cref="Description"/>, <see cref="Author"/> and the icon show in the game's Mods window
/// (<c>[ModManifest("my.mod", "My Mod", "1.0", Description = "…", Author = "…")]</c>). The icon is an <c>icon.png</c>
/// next to the mod's .dll, as in a Thunderstore package, or the file <see cref="Icon"/> names.</para>
/// Invalid values are reported in LogOutput.log when the mod is loaded, and the mod is not loaded.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModManifestAttribute(string id, string name, string version) : Attribute, IModManifest
{
    private NetworkCompatibility _network = NetworkCompatibility.ClientOnly;

    public string Id { get; } = id ?? "";
    public string Name { get; } = name ?? "";
    public string Version { get; } = version ?? "";

    /// <summary>
    /// Who must have the mod in a multiplayer session. <see cref="NetworkCompatibility.ClientOnly"/> by default:
    /// a mod of one player's own (interface, camera, hotkeys, cheats) keeps nobody out of a session.
    /// <para>A mod that adds content (items, recipes, buildings, effects) needs
    /// <see cref="NetworkCompatibility.Everyone"/>: its ids travel between the players, so a client whose mods
    /// differ from the host's is told which ones and does not join, instead of playing a session that drifts apart.
    /// The platform raises such a mod itself when it adds its first content, so this is set only to choose another
    /// mode — and that choice stands, with a warning in the log.</para>
    /// </summary>
    public NetworkCompatibility Network
    {
        get => _network;
        set
        {
            _network = value;
            IsNetworkDeclared = true;
        }
    }

    /// <summary>Whether the mod chose <see cref="Network"/> itself, which the platform then leaves alone.</summary>
    internal bool IsNetworkDeclared { get; private set; }

    /// <summary>
    /// Which Mjolnir versions the mod works with, as a dependency's range: <c>"&gt;=0.4"</c>, <c>"&gt;=0.4 &lt;0.5"</c>.
    /// Any version when not given.
    /// <para>A mod is built against one platform version and may meet another in the game; without this it loads and
    /// fails later, on the first type or member that changed, and the player sees a mod that broke for no reason. With
    /// it the mod is not loaded, and the log and the Mods window say which version it needs and which is installed.</para>
    /// </summary>
    public string? Platform { get; set; }

    /// <summary>What the mod does, in a short line: the Mods window shows it under the mod's name (its id when none).</summary>
    public string? Description { get; set; }

    /// <summary>Who made the mod: the Mods window shows it next to the version.</summary>
    public string? Author { get; set; }

    /// <summary>
    /// The mod's picture for the Mods window, a PNG or JPG relative to the folder of the mod's .dll (<c>"art/logo.png"</c>).
    /// Without it, an <c>icon.png</c> there is used; without that, the window shows the name's first letter. Square
    /// pictures fit best; 256×256 is plenty.
    /// </summary>
    public string? Icon { get; set; }
}
