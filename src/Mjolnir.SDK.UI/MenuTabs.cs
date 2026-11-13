using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mjolnir.SDK.Threading;
using SandSailorStudio.UI;
using SSSGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameSettingsMenu = SSSGame.UI.SettingsMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The tabs of a game window with a tab bar on its top edge, <see cref="Menus.Settings"/> or <see cref="Menus.Journal"/>:
/// a mod adds tabs of its own after the game's. Each is the window's own icon tab, showing the item's
/// <see cref="UiTabItem.Icon"/> (else the first letter of its <see cref="UiTabItem.Header"/>), with the header and the
/// mod's name in its hover card, and a page of the window's own kind showing the item's <see cref="UiTabItem.Content"/>:
/// in the settings menu under the header, as the game's pages say what they hold; in the journal on its window with the
/// header as the page's title. The window switches to it as to its own tabs, by a click and with Q/E.
/// </summary>
/// <example><code>
/// Menus.Settings.Add(new UiTabItem
/// {
///     Header = "My mod",
///     Icon = Bitmap.FromFile(Path.Combine(dir, "tab.png")),
///     Content = new UiStackPanel { Children = { SettingControls.For(config.Speed), SettingControls.For(config.Hard) } },
/// });
/// </code></example>
public sealed partial class MenuTabs
{
    private readonly string _name;
    private readonly PageKind _kind;
    private readonly ButtonRegistry _tabs;
    private readonly List<TabsHost> _hosts = [];

    internal MenuTabs(string name, PageKind kind)
    {
        _name = name;
        _kind = kind;
        _tabs = new ButtonRegistry(name);
    }

    /// <summary>How the window draws a page: the settings menu's under its tab bar, the journal's on a window of its own.</summary>
    internal enum PageKind
    {
        Settings,
        Journal,
    }

    /// <summary>
    /// Adds a tab after the window's own tabs (and after those added before: mods in load order). Its header, icon,
    /// content and <see cref="Control.IsVisible"/> follow the item. Safe from <see cref="Mod.OnLoad"/>; if the window is
    /// open, the tab appears at once (the game makes the window anew each time it opens, and the SDK adds the tab to
    /// every one). The item belongs here only, not also to a <see cref="UiTabControl"/>. Returns the item, to take it out
    /// again with <see cref="Remove"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public UiTabItem Add(UiTabItem tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        GameUi.EnsureInstalled();
        Register(tab, CallingMod.Of(Assembly.GetCallingAssembly()));
        return tab;
    }

    /// <summary>The bookkeeping of <see cref="Add"/> (unit-tested without the game): an item added again stays one tab.</summary>
    internal void Register(UiTabItem tab, Assembly owner)
    {
        if (_tabs.Snapshot().Any(e => ReferenceEquals(e.Entry.Element, tab))) return;
        _tabs.Add(tab, owner);
        tab.PropertyChanged += Changed;
        OnMain(host => host.Place(_tabs.Snapshot()));
    }

    /// <summary>The tabs added, in order (for the unit tests).</summary>
    internal IReadOnlyList<ButtonEntry> Entries => _tabs.Snapshot().Select(e => e.Entry).ToArray();

    /// <summary>
    /// Takes a tab added with <see cref="Add"/> out of the window: out of an open one at once (the window's first tab
    /// shows if it was showing) and out of every later one. The item can be added again. False if it was not added.
    /// </summary>
    public bool Remove(UiTabItem tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!_tabs.Remove(tab)) return false;
        tab.PropertyChanged -= Changed;
        OnMain(host => host.Remove(tab));
        return true;
    }

    private void Changed(object? sender, MjolnirPropertyChangedEventArgs e)
    {
        if (sender is UiTabItem tab) OnMain(host => host.Refresh(tab));
    }

    // On the main thread (at once there, else on the next frame), for every instance of the window still there.
    private void OnMain(Action<TabsHost> action) => Dispatcher.Run(() =>
    {
        _hosts.RemoveAll(h => !h.IsAlive);
        foreach (var host in _hosts.ToArray()) GameUi.Guard($"{_name} tabs", () => action(host));
    });

    /// <summary>
    /// The hidden patches: the game opens the window. It makes a new instance each time (seen in game, 2026-10-25); one
    /// opened again keeps its host, whose tabs are placed again (idempotent).
    /// </summary>
    internal void Opened(Transform root, TabGroup group, Button?[] shared)
    {
        _hosts.RemoveAll(h => !h.IsAlive);
        if (_hosts.FirstOrDefault(h => h.Holds(root)) is { } open)
        {
            open.Place(_tabs.Snapshot());
            return;
        }
        // The window's own tabs, by the game's order: the page tabs of its main group (each also carries a hover-card tab).
        var own = group.GetComponentsInChildren<TabButton>(true)
                       .Where(b => b.group != null && b.group.Pointer == group.Pointer && !b.name.StartsWith("Mjolnir_", StringComparison.Ordinal))
                       .OrderBy(b => b.order)
                       .ToList();
        var template = own.LastOrDefault(b => b.gameObject.activeSelf) ?? own.LastOrDefault();
        var pages = own.Select(b => b.page).Where(p => p != null).ToList();
        if (template == null || pages.Count == 0)
        {
            SdkLog.Warn(GameUi.Source, $"{_name}: no tab or page of the game's to copy, mod tabs not shown");
            return;
        }
        Theme.Load();
        var host = new TabsHost(_name, _kind, root, group, template, pages, shared.Where(b => b != null).ToArray()!);
        _hosts.Add(host);
        host.Place(_tabs.Snapshot());
    }
}

/// <summary>Hidden patches behind <see cref="Menus.Settings"/> and <see cref="Menus.Journal"/>.</summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class MenuTabPatches
{
    // Every menu opens through MenuManager.OpenMenu, given the new instance, awake but before its tabs' Start, where they
    // join the tab group: the mod tabs made here join with them. OnSetup is not a hook: the game opens the settings menu
    // and the journal without parameters, and then does not call it (seen in game, 2026-10-25).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.OpenMenu))]
    private static void MenuOpened(Menu __0)
    {
        if (__0 == null) return;
        if (__0.TryCast<GameSettingsMenu>() is { } settings)
        {
            GameUi.Guard("settings menu tabs", () =>
            {
                var pages = settings.settingsPages;
                var first = pages != null && pages.Length > 0 ? pages[0] : null;
                Button?[] shared = first == null ? [] : [first.applyButton, first.discardButton, first.resetButton];
                Menus.Settings.Opened(settings.transform, settings.MainTabGroup, shared);
            });
        }
        else if (__0.TryCast<PlayerMenu>() is { } journal)
        {
            GameUi.Guard("journal tabs", () => Menus.Journal.Opened(journal.transform, journal.MainTabGroup, []));
        }
    }
}
