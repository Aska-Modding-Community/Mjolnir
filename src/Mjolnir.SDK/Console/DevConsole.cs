using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Mjolnir.Core.Mods;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// The developer console: commands the player types into the console window, for testing and debugging. The UI module
/// opens the window with a key (<c>`</c> by default, the platform's setting in the Mjolnir window); Mjolnir's modules
/// add <c>help</c>, <c>clear</c>, <c>give</c>, <c>items</c>, <c>pos</c> and <c>tp</c>, and a mod adds its own with
/// <see cref="Add"/>. Commands run on the main thread.
/// </summary>
/// <example><code>
/// DevConsole.Add("heal", "Sets the player's health to its maximum.", context =>
/// {
///     LocalPlayer.Health.Value = LocalPlayer.Health.Maximum;
///     context.Print("Healed.");
/// });
/// DevConsole.Add("wood", "Gives logs.", context => Items.Give("Item_Wood_Logs", context.GetInt(0, 10)), "[count]");
/// </code></example>
public static class DevConsole
{
    /// <summary>The most lines the console keeps; older ones go.</summary>
    internal const int MaxLines = 500;

    /// <summary>The owner of the platform's own commands.</summary>
    internal const string PlatformOwner = "mjolnir";

    private const string Source = "Console";
    private static readonly object s_lock = new();
    private static readonly List<DevCommand> s_commands = [];
    private static readonly List<ConsoleLine> s_lines = [];

    static DevConsole()
    {
        var platform = typeof(DevConsole).Assembly;
        AddFor(platform, "help", "Lists the commands, or describes one.", Help, "[command]");
        AddFor(platform, "clear", "Clears the console.", _ => Clear());
    }

    /// <summary>
    /// Adds a command. Its name is what the player types first (letters, digits, <c>_</c> and <c>-</c>; any case). If
    /// another command took the name, yours answers to its full name, <c>&lt;mod id&gt;:&lt;name&gt;</c>, which every
    /// command has. <paramref name="syntax"/> describes what follows the name (<c>"&lt;item&gt; [count]"</c>) for
    /// <c>help</c> and for the usage line shown after a <see cref="DevCommandException"/>. A command that throws anything
    /// else disables its mod, as any mod code does.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static DevCommand Add(string name, string description, Action<DevCommandContext> run, string? syntax = null) =>
        AddFor(CallingMod.Of(Assembly.GetCallingAssembly()), name, description, run, syntax);

    /// <summary>Takes a command away (the console no longer knows it). Any thread.</summary>
    public static void Remove(DevCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (s_lock) s_commands.Remove(command);
    }

    /// <summary>Every command, by name.</summary>
    public static IReadOnlyList<DevCommand> Commands
    {
        get
        {
            lock (s_lock) return [.. s_commands.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.FullName, StringComparer.Ordinal)];
        }
    }

    /// <summary>Adds a line to the console (and to the log). Any thread.</summary>
    public static void Print(string text) => AddLine(text ?? "", ConsoleLineKind.Output);

    /// <summary>Runs a line as if the player typed it: on the main thread now, from another thread on the next frame.</summary>
    public static void Execute(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Dispatcher.Run(() => Run(line));
    }

    /// <summary>The lines shown, oldest first.</summary>
    internal static IReadOnlyList<ConsoleLine> Lines
    {
        get
        {
            lock (s_lock) return [.. s_lines];
        }
    }

    /// <summary>A line was added or the console cleared (on the thread that did it).</summary>
    internal static event Action? LinesChanged;

    internal static void AddLine(string text, ConsoleLineKind kind)
    {
        foreach (string part in text.Replace("\r\n", "\n").Split('\n'))
        {
            lock (s_lock)
            {
                s_lines.Add(new ConsoleLine(part, kind));
                if (s_lines.Count > MaxLines) s_lines.RemoveRange(0, s_lines.Count - MaxLines);
            }
            string logged = kind == ConsoleLineKind.Input ? $"> {part}" : part;
            if (kind == ConsoleLineKind.Error) SdkLog.Warn(Source, logged);
            else SdkLog.Info(Source, logged);
        }
        LinesChanged?.Invoke();
    }

    internal static void Clear()
    {
        lock (s_lock) s_lines.Clear();
        LinesChanged?.Invoke();
    }

    /// <summary>The command a name finds: its own name (the first command that took it), else a full name.</summary>
    internal static DevCommand? Find(string name)
    {
        lock (s_lock)
        {
            return s_commands.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? s_commands.FirstOrDefault(c => string.Equals(c.FullName, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static DevCommand AddFor(Assembly owner, string name, string description, Action<DevCommandContext> run, string? syntax = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!IsValidName(name)) throw new ArgumentException($"A command's name is letters, digits, '_' and '-': '{name}'", nameof(name));
        string ownerId = OwnerOf(owner);
        var command = new DevCommand(name, description ?? "", syntax, ownerId, owner, run);
        lock (s_lock)
        {
            if (s_commands.Any(c => string.Equals(c.FullName, command.FullName, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"The command {command.FullName} is there already", nameof(name));
            if (s_commands.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is { } taken)
                SdkLog.Warn(Source, $"{ownerId}: the command '{name}' is {taken.FullName}'s; yours answers to {command.FullName}");
            s_commands.Add(command);
        }
        return command;
    }

    internal static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    /// <summary>The line's words: spaces part them, and double quotes keep spaces in one (<c>give "Item name" 3</c>).</summary>
    internal static IReadOnlyList<string> Split(string line)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any) words.Add(word.ToString());
                word.Clear();
                any = false;
            }
            else
            {
                word.Append(c);
                any = true;
            }
        }
        if (any) words.Add(word.ToString());
        return words;
    }

    /// <summary>Unit tests: no lines, and only the platform's own commands.</summary>
    internal static void ResetForTests()
    {
        var platform = typeof(DevConsole).Assembly;
        lock (s_lock)
        {
            s_lines.Clear();
            s_commands.RemoveAll(c => c.OwnerAssembly != platform);
        }
    }

    private static void Run(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return;
        AddLine(line, ConsoleLineKind.Input);
        var words = Split(line);
        if (words.Count == 0) return;
        var command = Find(words[0]);
        if (command == null)
        {
            AddLine($"Unknown command '{words[0]}'. Type help for the list.", ConsoleLineKind.Error);
            return;
        }
        if (ModCall.IsDisabled(command.OwnerAssembly))
        {
            AddLine($"{command.FullName}: its mod was disabled after an error.", ConsoleLineKind.Error);
            return;
        }
        var context = new DevCommandContext(command, line, [.. words.Skip(1)]);
        ModCall.Run(command.OwnerAssembly, $"Console command {command.FullName}", () =>
        {
            try
            {
                command.Run(context);
            }
            catch (DevCommandException e)
            {
                context.Error(e.Message);
                context.Error($"Usage: {command.Usage}");
            }
            catch (Exception e)
            {
                context.Error($"{command.Name} failed: {e.Message}");
                throw;
            }
        });
    }

    private static void Help(DevCommandContext context)
    {
        if (context.Arguments.Count > 0)
        {
            var command = Find(context.Arguments[0]) ?? throw new DevCommandException($"No command '{context.Arguments[0]}'.");
            context.Print($"{command.Usage}  ({command.FullName})");
            if (command.Description.Length > 0) context.Print($"  {command.Description}");
            return;
        }
        foreach (var command in Commands) context.Print($"{command.Usage} - {command.Description}");
    }

    /// <summary>The id a command's owner goes by (unit tests name their own owners).</summary>
    internal static Func<Assembly, string> OwnerOf { get; set; } = OwnerId;

    private static string OwnerId(Assembly owner)
    {
        if (ModStringsRegistry.ForAssembly(owner) is { } strings) return strings.ModId;
        return ModFaults.IsPlatform(owner) || owner == typeof(DevConsole).Assembly ? PlatformOwner : owner.GetName().Name ?? "unknown";
    }
}
