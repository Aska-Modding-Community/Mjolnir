using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Mjolnir.SDK.UI;

/// <summary>A button, menu item or tab registered for a game menu, before and independent of any Unity object.</summary>
internal sealed record ButtonEntry(Control Element, Assembly? Owner, string ObjectName);

/// <summary>
/// The buttons and menu items (or the tabs, <see cref="MenuTabs"/>) mods registered for one game menu, in registration order
/// (= mod load order, dependencies first).
/// Pure bookkeeping, no Unity: names are deterministic per mod, and a disabled mod's buttons drop out.
/// </summary>
internal sealed class ButtonRegistry(string menu)
{
    private readonly object _lock = new();
    private readonly List<ButtonEntry> _entries = [];
    private readonly Dictionary<string, int> _perOwner = [];

    /// <param name="button">The button to register.</param>
    /// <param name="owner">The mod that added the button (its assembly), not the handler's: a handler may well be an SDK
    /// method such as <c>UiWindow.Show</c>.</param>
    /// <param name="first">Put it before the buttons added so far (the SDK's own Mods button goes first).</param>
    public ButtonEntry Add(Control button, Assembly? owner, bool first = false)
    {
        string ownerName = owner?.GetName().Name ?? "unknown";
        lock (_lock)
        {
            int n = _perOwner.GetValueOrDefault(ownerName);
            _perOwner[ownerName] = n + 1;
            var entry = new ButtonEntry(button, owner, $"Mjolnir_{menu}_{ownerName}_{n}");
            if (first) _entries.Insert(0, entry);
            else _entries.Add(entry);
            return entry;
        }
    }

    /// <summary>
    /// Drops every entry of <paramref name="button"/>. Its mod's counter keeps counting, so a button added later never
    /// takes the object name of one still being destroyed. False if it was not registered.
    /// </summary>
    public bool Remove(Control button)
    {
        lock (_lock) return _entries.RemoveAll(e => ReferenceEquals(e.Element, button)) > 0;
    }

    /// <summary>The buttons to show now: those of mods that are not disabled (the unit tests' view of the registry).</summary>
    public IReadOnlyList<ButtonEntry> Active() => Snapshot().Where(e => e.Active).Select(e => e.Entry).ToArray();

    /// <summary>Every button in order, and whether its mod is still enabled (a disabled mod's buttons get hidden).</summary>
    public IReadOnlyList<(ButtonEntry Entry, bool Active)> Snapshot()
    {
        lock (_lock) return _entries.Select(e => (e, e.Owner == null || !ModCall.IsDisabled(e.Owner))).ToArray();
    }
}
