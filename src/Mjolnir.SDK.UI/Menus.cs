using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using GameMainMenu = SSSGame.MainMenu;
// A global PauseMenu (Unity sample code in Assembly-CSharp) shadows the game's; the SDK never says "PauseMenu" publicly.
using GamePauseMenu = SSSGame.UI.PauseMenu;
using MenuShortcuts = SSSGame.UI.MenuShortcutsManager;
using SandSailorStudio.UI;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The game's menus: buttons and menu items with levels of their own (<see cref="UiMenuItem"/>) in its main menu and pause
/// menu, and tabs of a mod's own in its settings menu and journal (<see cref="MenuTabs"/>); learn when the main and pause
/// menus open. All may be added in <see cref="Mod.OnLoad"/>; the SDK puts them into every instance of the menu (the game
/// rebuilds them).
/// </summary>
/// <example><code>
/// var hello = new UiButton { Content = "My mod" };
/// hello.Click += (s, e) => Dialog.Message("My mod", "Hello");
/// Menus.Main.Add(hello);
/// </code></example>
public static class Menus
{
    /// <summary>The main menu: buttons go above "Quit to desktop".</summary>
    public static MenuButtons Main { get; } = new("Main");

    /// <summary>The pause menu (Esc in the world): buttons go below "Resume".</summary>
    public static MenuButtons Pause { get; } = new("Pause");

    /// <summary>The settings menu (Video, Audio, Controls, Gameplay): mod tabs go after the game's.</summary>
    public static MenuTabs Settings { get; } = new("Settings", MenuTabs.PageKind.Settings);

    /// <summary>The journal (Tutorials, Journal, Deeds and the settlement's pages): mod tabs go after the game's.</summary>
    public static MenuTabs Journal { get; } = new("Journal", MenuTabs.PageKind.Journal);
}

/// <summary>One game menu (<see cref="Menus.Main"/> or <see cref="Menus.Pause"/>).</summary>
public sealed partial class MenuButtons
{
    private readonly string _name;
    private readonly ButtonRegistry _buttons;
    private readonly ModHandlers<EventHandler> _opened;
    private readonly Dictionary<int, MenuHost> _hosts = [];
    private Placement? _last;

    internal MenuButtons(string name)
    {
        _name = name;
        _buttons = new ButtonRegistry(name);
        _opened = new ModHandlers<EventHandler>($"Menus.{name}.Opened");
    }

    /// <summary>
    /// Raised each time this menu becomes the active menu: when it opens, and again when a menu opened on top of it
    /// (settings, a dialog, a mod window) closes. The sender is this <see cref="MenuButtons"/>.
    /// </summary>
    public event EventHandler? Opened
    {
        add
        {
            GameUi.EnsureInstalled();
            _opened.Add(value);
        }
        remove => _opened.Remove(value);
    }

    /// <summary>
    /// Adds a button that looks like the game's own; its <see cref="UiButton.Content"/>, <see cref="Control.IsVisible"/> and
    /// <see cref="Control.IsEnabled"/> follow the element. Buttons appear in the order they were added (mods in load order).
    /// Safe from <see cref="Mod.OnLoad"/>; if the menu is open, the button appears at once. Returns the button, to take it
    /// out again with <see cref="Remove(UiButton)"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public UiButton Add(UiButton button)
    {
        ArgumentNullException.ThrowIfNull(button);
        Register(button, CallingMod.Of(Assembly.GetCallingAssembly()), first: false);
        return button;
    }

    /// <summary>
    /// Takes a button added with <see cref="Add(UiButton)"/> out of this menu: out of an open menu at once (on the next frame from
    /// another thread) and out of every later one. The button can be added again. False if it was not in this menu.
    /// </summary>
    public bool Remove(UiButton button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return Unregister(button);
    }

    private bool Unregister(Control button)
    {
        if (!_buttons.Remove(button)) return false;
        Dispatcher.Run(() =>
        {
            foreach (var host in _hosts.Values)
            {
                if (host.IsAlive) GameUi.Guard($"{_name} menu buttons", () => host.Unmount(button));
            }
        });
        return true;
    }

    /// <summary>
    /// Adds a menu item in the menu's own look (<see cref="UiMenuItem"/>): one with <see cref="UiMenuItem.Items"/> opens a
    /// level of its own, with the game's «Back» at its end; Esc goes one level up too. In the main menu the levels are the
    /// game's own pages (its animations and sounds); the pause menu, which has none, shows them in the place of its
    /// buttons. Items appear in the order they were added, after the buttons added before them; the item's text,
    /// <see cref="Control.IsVisible"/> and <see cref="Control.IsEnabled"/>, and its items, follow the element. Safe from
    /// <see cref="Mod.OnLoad"/>. Returns the item, to take it out again with <see cref="Remove(UiMenuItem)"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public UiMenuItem Add(UiMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Register(item, CallingMod.Of(Assembly.GetCallingAssembly()), first: false);
        return item;
    }

    /// <summary>Takes a menu item added with <see cref="Add(UiMenuItem)"/> out of this menu, levels and all. False if it was not in this menu.</summary>
    public bool Remove(UiMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Unregister(item);
    }

    /// <summary>
    /// The SDK's own button (Mods): first among the mod buttons, owned by the platform, so never hidden. Its owner is
    /// Mjolnir.SDK, whichever module adds it, so its name stays Mjolnir_&lt;menu&gt;_Mjolnir.SDK_0.
    /// </summary>
    internal UiButton AddPlatformButton(UiButton button)
    {
        Register(button, typeof(Mod).Assembly, first: true);
        return button;
    }

    private void Register(Control button, Assembly owner, bool first)
    {
        GameUi.EnsureInstalled();
        _buttons.Add(button, owner, first);
        Dispatcher.Run(() =>
        {
            if (_last is { } open && open.Root != null && GameUi.CanUseUnity($"Menus.{_name}.Add"))
                GameUi.Guard($"{_name} menu buttons", () => Place(open));
        });
    }

    /// <summary>Esc while this menu is on top (the hidden patches): one of its levels up, if one is open.</summary>
    internal bool Back() => _last is { } open && open.Root != null && HostFor(open).Levels.Back();

    /// <summary>The menu closed (the hidden patches): its own buttons show next time.</summary>
    internal void ResetLevels()
    {
        if (_last is { } open && open.Root != null) HostFor(open).Levels.Reset();
    }

    /// <summary>Called by the hidden patches when the game activates this menu.</summary>
    internal void Activated(Placement placement)
    {
        _last = placement;
        Theme.Load();
        Place(placement);
        _opened.Raise(h => h(this, EventArgs.Empty));
    }

    // Idempotent: a button already in this menu instance (found anywhere in its tree, not only among the direct
    // children: the game nests its buttons in groups) is kept, so re-activation never duplicates it.
    private void Place(Placement at)
    {
        if (at.Template == null)
        {
            SdkLog.Warn(GameUi.Source, $"{_name} menu: no button to copy, mod buttons not shown");
            return;
        }
        var parent = at.Template.transform.parent;
        var host = HostFor(at);
        var container = host.ContainerFor(parent);
        int index = at.After != null ? at.After.GetSiblingIndex() + 1
                  : at.Before != null && at.Before.parent == parent ? at.Before.GetSiblingIndex()
                  : parent.childCount;
        foreach (var (entry, active) in _buttons.Snapshot())
        {
            var existing = ViewFactory.FindDeep(at.Root, entry.ObjectName);
            if (!active)
            {
                existing?.gameObject.SetActive(false);
                continue;
            }
            if (existing != null)
            {
                index = existing.GetSiblingIndex() + 1;
                continue;
            }
            host.Mount(entry.Element, container, index++);
        }
    }

    // One host per menu instance (the game rebuilds the pause menu); a destroyed one is dropped by the UI tick.
    private MenuHost HostFor(Placement at)
    {
        int id = at.Root.GetInstanceID();
        if (_hosts.TryGetValue(id, out var host) && host.IsAlive) return host;
        foreach (int dead in _hosts.Where(h => !h.Value.IsAlive).Select(h => h.Key).ToList()) _hosts.Remove(dead);
        var levels = at.NewLevels(at.Template!.transform.parent.Cast<RectTransform>());
        return _hosts[id] = new MenuHost(at.Root, new MenuButtonBackend(at.Template!, NameOf, levels), levels);
    }

    private string NameOf(Control button) =>
        _buttons.Snapshot().Select(e => e.Entry).FirstOrDefault(e => ReferenceEquals(e.Element, button))?.ObjectName ?? "Mjolnir_Button";

    /// <summary>
    /// Where buttons go in one menu instance: a game button to copy, the neighbour to insert next to, and how the menu
    /// shows the levels of <see cref="UiMenuItem"/>s (made from the template's column).
    /// </summary>
    internal sealed record Placement(Transform Root, Button? Template, Transform? Before, Transform? After, Func<RectTransform, MenuLevels> NewLevels);
}

/// <summary>Hidden patches behind <see cref="Menus"/> (the hybrid Harmony rule: mods never see them).</summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class MenuPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMainMenu), nameof(GameMainMenu.OnActivate))]
    private static void MainMenuActivated(GameMainMenu __instance) => GameUi.Guard("main menu", () =>
    {
        var root = __instance.buttonsParent.transform;
        var template = FirstActiveButton(root);
        var quit = __instance.quitApplicationButton;
        Menus.Main.Activated(new MenuButtons.Placement(root, template, quit != null ? quit.transform : null, null,
                                                       column => new GameTabLevels(__instance, column)));
    });

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GamePauseMenu), nameof(GamePauseMenu.OnActivate))]
    private static void PauseMenuActivated(GamePauseMenu __instance) => GameUi.Guard("pause menu", () =>
    {
        var root = __instance.buttonsPanel.transform;
        var template = FirstActiveButton(root);
        Menus.Pause.Activated(new MenuButtons.Placement(root, template, null, template != null ? template.transform : null,
                                                        column => new SdkLevels(column)));
    });

    // Esc in the pause menu goes through the pause key, which closes it; with a mod level open (and no mod window or
    // dialog on top), it goes one level up instead. PauseMenu.OnBackPressed is not patched: its body is shared with three
    // methods. The pause menu is itself on the menu stack (seen in game, 2026-10-24): "on top" is the stack's top.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(MenuShortcuts), nameof(MenuShortcuts.Pause_started))]
    private static bool PauseKey()
    {
        bool handled = false;
        GameUi.Guard("pause menu levels", () => handled = PauseMenuOnTop() && Menus.Pause.Back());
        return !handled;
    }

    private static bool PauseMenuOnTop()
    {
        var stack = MenuManager.Instance?._menuStack;
        var pause = Menu.IsOpen<GamePauseMenu>() ? Menu.GetInstance<GamePauseMenu>() : null;
        return pause != null && stack != null && (stack.Count == 0 || stack.Peek()?.Pointer == pause.Pointer);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GamePauseMenu), nameof(GamePauseMenu.OnClosed))]
    private static void PauseMenuClosed() => GameUi.Guard("pause menu levels", Menus.Pause.ResetLevels);

    // The first visible game button (main menu: "Singleplayer", pause: "Resume"), skipping our own clones.
    private static Button? FirstActiveButton(Transform root) =>
        root.GetComponentsInChildren<Button>(false).FirstOrDefault(b => !b.name.StartsWith("Mjolnir_", StringComparison.Ordinal));
}
