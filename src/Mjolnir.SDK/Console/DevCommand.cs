using System;
using System.Reflection;

namespace Mjolnir.SDK;

/// <summary>A console command (<see cref="DevConsole.Add"/>).</summary>
public sealed class DevCommand
{
    internal DevCommand(string name, string description, string? syntax, string owner, Assembly ownerAssembly, Action<DevCommandContext> run)
    {
        Name = name;
        Description = description;
        Syntax = syntax;
        ModId = owner;
        OwnerAssembly = ownerAssembly;
        Run = run;
    }

    /// <summary>The name the player types.</summary>
    public string Name { get; }

    /// <summary><c>&lt;owner&gt;:&lt;name&gt;</c>: the command's name that no other command takes.</summary>
    public string FullName => $"{ModId}:{Name}";

    /// <summary>What it does, one line (shown by <c>help</c>).</summary>
    public string Description { get; }

    /// <summary>What follows the name, such as <c>"&lt;item&gt; [count]"</c>; null without arguments.</summary>
    public string? Syntax { get; }

    /// <summary>The id of the mod that added it; <c>mjolnir</c> for the platform's own.</summary>
    public string ModId { get; }

    /// <summary>The name and its arguments, as <c>help</c> shows them.</summary>
    public string Usage => Syntax is { Length: > 0 } a ? $"{Name} {a}" : Name;

    internal Assembly OwnerAssembly { get; }

    internal Action<DevCommandContext> Run { get; }

    public override string ToString() => FullName;
}
