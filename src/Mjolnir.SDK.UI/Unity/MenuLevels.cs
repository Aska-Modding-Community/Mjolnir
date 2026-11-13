using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SandSailorStudio.UI;
using UnityEngine;
using UnityEngine.UI;
using GameMainMenu = SSSGame.MainMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The levels a pause menu shows (no Unity: unit-tested): the menu's own buttons at the bottom, the levels opened on
/// top of them, the last one shown. Esc and «Back» go one level up; closing the menu goes back to its own buttons.
/// </summary>
internal sealed class LevelStack<T> where T : class
{
    private readonly List<T> _open = [];

    /// <summary>The level shown; null for the menu's own buttons.</summary>
    public T? Current => _open.Count > 0 ? _open[^1] : null;

    /// <summary>Opens a level on top of the one shown.</summary>
    public void Open(T level)
    {
        _open.Remove(level);
        _open.Add(level);
    }

    /// <summary>Goes one level up; false at the menu's own buttons (then Esc is the menu's).</summary>
    public bool Back()
    {
        if (_open.Count == 0) return false;
        _open.RemoveAt(_open.Count - 1);
        return true;
    }

    /// <summary>Back to the menu's own buttons.</summary>
    public void Clear() => _open.Clear();

    /// <summary>A level that is gone: it and the levels opened from it close.</summary>
    public void Forget(T level)
    {
        int at = _open.IndexOf(level);
        if (at >= 0) _open.RemoveRange(at, _open.Count - at);
    }
}

/// <summary>A level of a game menu: the column holding its buttons, and in the main menu the game's tab parts.</summary>
internal sealed class MenuLevel(RectTransform page)
{
    public RectTransform Page { get; } = page;
    public TabGroup? Group { get; init; }
    public TabPage? TabPage { get; init; }
    public TabButton? Self { get; init; }
    public GameObject? Back { get; set; }
}

/// <summary>
/// How one instance of a game menu shows the levels <see cref="UiMenuItem"/>s open: a page per level, laid out like the
/// menu's own button column, an opener that shows it, and «Back» at its end.
/// </summary>
internal abstract class MenuLevels(RectTransform column)
{
    /// <summary>The menu's own button column (the level mod items without a parent level sit in).</summary>
    public MenuLevel Root { get; protected init; } = new(column);

    /// <summary>A new page for a level, hidden.</summary>
    public abstract MenuLevel NewLevel(string name);

    /// <summary>Makes <paramref name="opener"/> (a button in <paramref name="parent"/>) show <paramref name="level"/>.</summary>
    public virtual void ConnectOpener(GameObject opener, MenuLevel parent, MenuLevel level) { }

    /// <summary>The player clicked an opener (after its item's handlers).</summary>
    public virtual void Opened(MenuLevel level) { }

    /// <summary>The «Back» at the end of <paramref name="level"/>: a clone of the menu's own button, in the game's words.</summary>
    public GameObject NewBack(MenuLevel level, MenuLevel parent, Button template)
    {
        var back = ViewFactory.CloneButton(template, level.Page, $"{level.Page.name}_Back", Theme.Loc("common.back", "Back"),
                                           () => GameUi.Guard("menu level back", () => GoBack(level, parent)));
        level.Back = back;
        Registered(back.GetComponent<Button>());
        return back;
    }

    /// <summary>Goes from <paramref name="level"/> up to <paramref name="parent"/> («Back»).</summary>
    protected abstract void GoBack(MenuLevel level, MenuLevel parent);

    /// <summary>A new «Back» exists (the main menu presses it on Esc).</summary>
    protected virtual void Registered(Button back) { }

    /// <summary>Esc while this menu is on top: one level up if a level is open (true), else nothing (false: the game's Esc).</summary>
    public virtual bool Back() => false;

    /// <summary>The menu closed: its own buttons show again next time.</summary>
    public virtual void Reset() { }

    /// <summary>
    /// A level whose item went: its page goes, and nothing of the game keeps a reference to it. When the game destroyed the
    /// menu (a world loading destroys the main menu), the page went with it.
    /// </summary>
    public virtual void Forget(MenuLevel level)
    {
        if (level.Page == null) return;
        level.Page.SetParent(Theme.Holder, false);
        Object.Destroy(level.Page.gameObject);
    }
}

/// <summary>
/// The main menu's levels are the game's own: a page beside
/// «SinglePlayerPage/Group» with a TabGroup, a TabPage and a TabButton on itself; the opener a TabButton of the parent
/// page's group; «Back» closes the page and selects the parent's own tab, as the game's «Back» does, and joins
/// <c>MainMenu.BackButtons</c>, so Esc presses it. The game's animations and sounds come with it.
/// </summary>
internal sealed class GameTabLevels : MenuLevels
{
    private readonly GameMainMenu _menu;

    public GameTabLevels(GameMainMenu menu, RectTransform column) : base(column)
    {
        _menu = menu;
        Root = new MenuLevel(column) { Group = column.GetComponent<TabGroup>(), TabPage = column.GetComponent<TabPage>(), Self = column.GetComponent<TabButton>() };
    }

    public override MenuLevel NewLevel(string name)
    {
        var column = Root.Page;
        var page = ViewFactory.NewRect(column.parent, name);
        page.gameObject.SetActive(false); // like the game's pages; the components wake when it opens
        page.anchorMin = column.anchorMin;
        page.anchorMax = column.anchorMax;
        page.pivot = column.pivot;
        page.anchoredPosition = column.anchoredPosition;
        page.sizeDelta = new Vector2(column.sizeDelta.x, 0);
        var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 5;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        page.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var group = page.gameObject.AddComponent<TabGroup>();
        group.buttons = new Il2CppSystem.Collections.Generic.List<TabButton>();
        var tabPage = page.gameObject.AddComponent<TabPage>();
        var self = page.gameObject.AddComponent<TabButton>();
        self.group = group;
        self.page = tabPage;
        return new MenuLevel(page) { Group = group, TabPage = tabPage, Self = self };
    }

    public override void ConnectOpener(GameObject opener, MenuLevel parent, MenuLevel level)
    {
        var tab = opener.AddComponent<TabButton>(); // joins the parent's group in its Start, as the game's buttons do
        tab.group = parent.Group;
        tab.page = level.TabPage;
    }

    protected override void GoBack(MenuLevel level, MenuLevel parent)
    {
        level.Group?.Close();
        parent.Group?.TabSelect(parent.Self);
    }

    // A managed array copied in, never new Il2CppReferenceArray<T>(int): an int binds to its IntPtr constructor (int →
    // nint is the better conversion), and the "array" at that address killed the CLR (seen in game, 2026-10-24).
    protected override void Registered(Button back)
    {
        var old = _menu.BackButtons;
        var buttons = new Button[old.Length + 1];
        for (int i = 0; i < old.Length; i++) buttons[i] = old[i];
        buttons[old.Length] = back;
        _menu.BackButtons = new Il2CppReferenceArray<Button>(buttons);
    }

    // The game presses every active one of BackButtons on Esc: a destroyed «Back» must leave the array first.
    public override void Forget(MenuLevel level)
    {
        if (_menu != null && level.Back != null)
        {
            var back = level.Back.GetComponent<Button>();
            var kept = Enumerable.Range(0, _menu.BackButtons.Length).Select(i => _menu.BackButtons[i])
                                 .Where(b => b != null && b.Pointer != back.Pointer).ToArray();
            _menu.BackButtons = new Il2CppReferenceArray<Button>(kept);
        }
        base.Forget(level);
    }
}

/// <summary>
/// The pause menu has no pages of its own: the SDK shows one level at a time in the place of the menu's button column,
/// which is faded out and let through while a level is open. Esc goes one level up (a prefix on the pause key), and
/// closing the menu goes back to its own buttons.
/// </summary>
internal sealed class SdkLevels(RectTransform column) : MenuLevels(column)
{
    private readonly LevelStack<MenuLevel> _stack = new();
    private readonly List<MenuLevel> _levels = [];

    public override MenuLevel NewLevel(string name)
    {
        var column = Root.Page;
        var page = ViewFactory.NewRect(column.parent, name);
        page.gameObject.SetActive(false);
        page.anchorMin = column.anchorMin;
        page.anchorMax = column.anchorMax;
        page.pivot = column.pivot;
        page.anchoredPosition = column.anchoredPosition;
        page.sizeDelta = column.sizeDelta;
        var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
        if (column.GetComponent<HorizontalOrVerticalLayoutGroup>() is { } own)
        {
            layout.spacing = own.spacing;
            layout.padding = own.padding;
            layout.childAlignment = own.childAlignment;
            layout.childControlWidth = own.childControlWidth;
            layout.childControlHeight = own.childControlHeight;
            layout.childForceExpandWidth = own.childForceExpandWidth;
            layout.childForceExpandHeight = own.childForceExpandHeight;
        }
        if (column.GetComponent<ContentSizeFitter>() is { } fit)
        {
            var copy = page.gameObject.AddComponent<ContentSizeFitter>();
            copy.horizontalFit = fit.horizontalFit;
            copy.verticalFit = fit.verticalFit;
        }
        var level = new MenuLevel(page);
        _levels.Add(level);
        return level;
    }

    public override void Opened(MenuLevel level)
    {
        _stack.Open(level);
        Show();
    }

    protected override void GoBack(MenuLevel level, MenuLevel parent) => Back();

    public override bool Back()
    {
        if (!_stack.Back()) return false;
        Show();
        return true;
    }

    public override void Reset()
    {
        _stack.Clear();
        Show();
    }

    public override void Forget(MenuLevel level)
    {
        _levels.Remove(level);
        _stack.Forget(level);
        base.Forget(level);
        Show();
    }

    // Only the current level shows; the menu's own column is faded out (not deactivated: the game sets its buttons
    // active itself) while a level is open.
    private void Show()
    {
        var current = _stack.Current;
        if (Root.Page != null)
        {
            var group = Root.Page.GetComponent<CanvasGroup>() ?? Root.Page.gameObject.AddComponent<CanvasGroup>();
            group.alpha = current == null ? 1 : 0;
            group.interactable = current == null;
            group.blocksRaycasts = current == null;
        }
        foreach (var level in _levels)
        {
            if (level.Page != null) level.Page.gameObject.SetActive(ReferenceEquals(level, current));
        }
    }
}
