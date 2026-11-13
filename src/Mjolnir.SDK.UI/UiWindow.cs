using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using SandSailorStudio.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Il2CppInterop.Runtime;
using GameContextMenu = SSSGame.UI.ContextMenu;
using Object = UnityEngine.Object;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A mod window that is a real game menu: while it is open the game blocks the player's input,
/// shows the cursor and stacks it with its own menus; Esc closes it. It wears the game's look: the frame of its main
/// windows (or its dialogs', <see cref="WindowStyle"/>), the settings menu's rows and scroll view; content taller than the window
/// scrolls. A <see cref="UiTabControl"/> content with its tabs on top shows them on the window's top edge, as the game's
/// settings menu.
/// <para>Derive to make a window of your own, build the elements in the constructor and set <see cref="Content"/>
/// (a <see cref="UiStackPanel"/> column takes the window's whole width). Build it any time, even in
/// <see cref="Mod.OnLoad"/>; Unity objects are created on the first <see cref="Show"/>, which needs the running game.</para>
/// <para>Make a window once and show it again and again. A window made for one use is disposed after it
/// (<see cref="Dispose"/>), or its Unity objects stay for the rest of the game.</para>
/// <para>Sizes are reference pixels: the window is laid out like the game's menus, for 1920×1080 (Expand), and scales
/// with <see cref="InterfaceScale.Menus"/> over its whole range. It always stays inside the screen; content taller than it scrolls.
/// <see cref="SizeToContent"/> makes it as tall as its content.</para>
/// </summary>
/// <example><code>
/// var window = new UiWindow { Title = "My mod" };
/// var godMode = new UiToggleSwitch { Header = "God mode", Description = "Take no damage" };
/// godMode.Bind(UiToggleSwitch.IsCheckedProperty, config.GodMode);
/// var close = new UiButton { Content = "Close" };
/// close.Click += (s, e) => window.Close();
/// window.Content = new UiStackPanel { Children = { godMode, close } };
/// </code></example>
[SuppressMessage("ApiDesign", "RS0022", Justification = "Made to be derived by mods: its protected members are API")]
public partial class UiWindow : MjolnirObject, IDisposable
{
    public static readonly MjolnirProperty<string> TitleProperty =
        MjolnirProperty.Register<UiWindow, string>(nameof(Title), "", DirtyFlags.None, coerce: static (_, value) => value ?? "");

    /// <summary>Width in reference pixels (the game's UI is laid out for 1920×1080).</summary>
    public static readonly MjolnirProperty<double> WidthProperty =
        MjolnirProperty.Register<UiWindow, double>(nameof(Width), 760, DirtyFlags.None);

    public static readonly MjolnirProperty<double> HeightProperty =
        MjolnirProperty.Register<UiWindow, double>(nameof(Height), 440, DirtyFlags.None);

    public static readonly MjolnirProperty<double> MinWidthProperty =
        MjolnirProperty.Register<UiWindow, double>(nameof(MinWidth), 0, DirtyFlags.None);

    public static readonly MjolnirProperty<double> MinHeightProperty =
        MjolnirProperty.Register<UiWindow, double>(nameof(MinHeight), 0, DirtyFlags.None);

    public static readonly MjolnirProperty<double> MaxWidthProperty =
        MjolnirProperty.Register<UiWindow, double>(nameof(MaxWidth), double.PositiveInfinity, DirtyFlags.None);

    public static readonly MjolnirProperty<double> MaxHeightProperty =
        MjolnirProperty.Register<UiWindow, double>(nameof(MaxHeight), double.PositiveInfinity, DirtyFlags.None);

    public static readonly MjolnirProperty<SizeToContent> SizeToContentProperty =
        MjolnirProperty.Register<UiWindow, SizeToContent>(nameof(SizeToContent), SizeToContent.Manual, DirtyFlags.None);

    public static readonly MjolnirProperty<Control?> ContentProperty =
        MjolnirProperty.Register<UiWindow, Control?>(nameof(Content), null, DirtyFlags.None);

    public static readonly MjolnirProperty<Control?> FooterProperty =
        MjolnirProperty.Register<UiWindow, Control?>(nameof(Footer), null, DirtyFlags.None);

    /// <summary>The start of a window's object name, by which the interface scale tells mod windows from game menus.</summary>
    internal const string ObjectNamePrefix = "Mjolnir_Window_";

    /// <summary>The object's name after <see cref="ObjectNamePrefix"/>, when not the title (a title with rich text in it).</summary>
    internal string? ObjectName { get; init; }

    /// <summary>
    /// Whether the player moves the window by dragging its frame or title (what does not take a drag itself: a scroll view
    /// scrolls, a text field selects). It keeps its place while closed, inside the screen. The console's for now.
    /// </summary>
    internal bool Draggable { get; init; }

    /// <summary>Called when the player lets go of a drag (the console focuses its field again).</summary>
    internal Action? DragEnded { get; init; }

    private const float TitleTop = 24; // a dialog's title, clear of the frame's top band: at 12 it looked pressed against it
    private const float TitleHeight = 64;
    private const float HeaderHeight = 48; // a main window's title plate, centred on the top edge
    private const float TabsHeight = 80; // the journal's icon tabs, centred on the top edge
    private const float SubtitleHeight = 28; // the selected tab's name under the tabs (the settings menu's page subtitle)
    private const float Margin = 12; // around the content, inside the scroll view
    private const float ScrollBottom = 24; // the scroll view's distance from the window's bottom
    private const float FooterGap = 8; // between the scroll view and the footer
    private static readonly Color TitleColor = new(0.776f, 0.902f, 0.855f, 1f); // #C6E6DA
    private static readonly Color SubtitleColor = new(0.506f, 0.596f, 0.565f, 1f); // #819890

    private static readonly List<UiWindow> s_built = [];
    private static bool s_watching;

    private ModHandlers<EventHandler>? _opened;
    private ModHandlers<EventHandler>? _closed;
    private int _disposed;
    private bool _retiring; // main thread: disposed and built, waiting to be destroyed
    private GameObject? _root;
    private GameContextMenu? _menu;
    private RectTransform? _panel;
    private TextMeshProUGUI? _titleLabel;
    private ScrollRect? _scroll;
    private int _scrollToEnd;
    private Vector2 _offset; // where the player dragged the window, from the screen's middle
    private Vector2 _shownOffset;
    private RectTransform? _scrollArea;
    private RectTransform? _footerRect;
    private ContainerView? _footerView;
    private float _shownFooter = -1;
    private WindowHost? _host;
    private ContainerView? _contentView;
    private RectTransform? _canvasRect;
    private Vector2 _shownSize;
    private volatile bool _shown;
    // Main thread: whether the window should be open, and what was last asked of the game (it works opens and closes off
    // over the next frames, so the menu stack shows a request only later) and how many frames ago.
    private bool _wanted, _asked;
    private int _askedFrames;
    private bool _menuChrome; // built with the main windows' frame (Style = Menu and the game's parts found)
    private UiTabControl? _headerTabs; // the content's tabs, shown in the header
    private RectTransform? _plate, _tabsRect;
    private ContainerView? _tabsView;
    private TextMeshProUGUI? _subtitle;
    private string? _shownSubtitle;

    /// <summary>
    /// How the window is framed: as the game's main windows (<see cref="WindowStyle.Menu"/>, the default) or as its
    /// dialogs. Set it before the window is first shown.
    /// </summary>
    public WindowStyle WindowStyle { get; init; }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public double Width
    {
        get => GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    public double Height
    {
        get => GetValue(HeightProperty);
        set => SetValue(HeightProperty, value);
    }

    /// <summary>The narrowest the window gets, in reference pixels (the screen still wins).</summary>
    public double MinWidth
    {
        get => GetValue(MinWidthProperty);
        set => SetValue(MinWidthProperty, value);
    }

    /// <summary>The lowest the window gets, in reference pixels (the screen still wins).</summary>
    public double MinHeight
    {
        get => GetValue(MinHeightProperty);
        set => SetValue(MinHeightProperty, value);
    }

    /// <summary>The widest the window gets, in reference pixels; infinity (the default) for the screen's width.</summary>
    public double MaxWidth
    {
        get => GetValue(MaxWidthProperty);
        set => SetValue(MaxWidthProperty, value);
    }

    /// <summary>The tallest the window gets, in reference pixels; infinity (the default) for the screen's height.</summary>
    public double MaxHeight
    {
        get => GetValue(MaxHeightProperty);
        set => SetValue(MaxHeightProperty, value);
    }

    /// <summary>
    /// <see cref="UI.SizeToContent.Height"/>: the window is as tall as its title and content, up to <see cref="MaxHeight"/>
    /// and the screen (taller content scrolls), instead of <see cref="Height"/>.
    /// </summary>
    public SizeToContent SizeToContent
    {
        get => GetValue(SizeToContentProperty);
        set => SetValue(SizeToContentProperty, value);
    }

    /// <summary>What the window shows; the window has a 12 px margin inside its scroll view.</summary>
    public Control? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>
    /// What the window shows under its content and always in view, its buttons and notes, as wide as the content: the
    /// content scrolls above it. The window makes room for the footer's height, so keep that steady and the window keeps
    /// its size: a note that comes and goes is better hidden with <see cref="Control.Opacity"/> 0 than with
    /// <see cref="Control.IsVisible"/>. A row of buttons is a horizontal <see cref="UiStackPanel"/>; a stretched text in it
    /// takes the room the buttons leave.
    /// </summary>
    public Control? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    /// <summary>
    /// Whether the window is open (also while a dialog or another window covers it). Not <c>IsVisible</c>: that is an
    /// element's own setting (<see cref="Control.IsVisible"/>), this is the game's menu state.
    /// </summary>
    public bool IsOpen => _shown;

    /// <summary>Raised when the window opens (not again when a menu that covered it closes).</summary>
    public event EventHandler? Opened
    {
        add => ModHandlers<EventHandler>.Of(ref _opened, $"{this}.Opened").Add(value);
        remove => _opened?.Remove(value);
    }

    /// <summary>Raised when the window closes, however: Esc, <see cref="Close"/>, or the game closing all menus.</summary>
    public event EventHandler? Closed
    {
        add => ModHandlers<EventHandler>.Of(ref _closed, $"{this}.Closed").Add(value);
        remove => _closed?.Remove(value);
    }

    /// <summary>
    /// Opens the window on top of whatever menu is open. Needs the running game; from another thread it opens next frame.
    /// A disposed window cannot be shown again.
    /// </summary>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Dispatcher.Run(() =>
        {
            GameUi.EnsureInstalled();
            if (Volatile.Read(ref _disposed) != 0 || !GameUi.CanUseUnity($"{this}.Show")) return;
            GameUi.Guard($"{this}.Show", Open);
        });
    }

    /// <summary>Closes the window (Esc does the same).</summary>
    public void Close() => Dispatcher.Run(() =>
    {
        if (_menu == null || _retiring) return;
        GameUi.Guard($"{this}.Close", () =>
        {
            _wanted = false;
            Refresh();
        });
    });

    /// <summary>
    /// Closes the window if it is open and destroys its Unity objects once the game is done closing it (<see cref="Closed"/>
    /// is raised as usual). The content is taken off and can be shown elsewhere; the window itself cannot be shown again.
    /// Any thread; a second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        GC.SuppressFinalize(this);
        Dispatcher.Run(() =>
        {
            if (_menu == null) return; // never built: nothing in Unity
            _retiring = true;
            GameUi.Guard($"{this}.Dispose", Retire);
        });
    }

    /// <summary>
    /// Shows the end of the content (the console's newest line) for the next frames: the content's height follows a
    /// change of text only once its layout ran.
    /// </summary>
    internal void ScrollToEnd() => _scrollToEnd = 3;

    /// <summary>Built and not disposed: its bindings follow their sources (until <see cref="Dispose"/>).</summary>
    internal override bool IsLive => _menu != null;

    internal override bool IsOnScreen => _shown;

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        if (ReferenceEquals(e.Property, TitleProperty)) Dispatcher.Run(ShowTitle);
        else if (ReferenceEquals(e.Property, ContentProperty) || ReferenceEquals(e.Property, FooterProperty)) Dispatcher.Run(Remount);
        else Dispatcher.Run(Resize); // a size (checked every frame too)
    }

    // Stored values only, like Control.ToString: a bound title read here would read its source, which describes itself with this.
    public override string ToString() => $"UiWindow '{GetStored(TitleProperty)}'";

    // A window is open while it is in the game's menu stack: covering it with a dialog or another window keeps it
    // there, closing it any way (Esc, Close, the game closing all menus) takes it out. ContextMenu.OnClosed is no use:
    // MenuManager's close routine never calls it for these windows (seen in game, 2026-10-20).
    // Backwards: a window is destroyed (and removed) by its own Retire, and an event handler may dispose another one.
    private static void RefreshAll()
    {
        for (int i = s_built.Count - 1; i >= 0; i--)
        {
            if (i >= s_built.Count) continue;
            var window = s_built[i];
            window._askedFrames++;
            window.Refresh();
            if (window._retiring) GameUi.Guard($"{window}.Dispose", window.Retire);
            else if (window._shown) window.Resize(); // the screen, the scale or the content's height may have changed
        }
    }

    // Main thread, every frame until done. The game's menu routines run over several frames (MenuManager queues opens
    // and closes and works them off in a coroutine), so the window is closed first and destroyed only once it is out of
    // the menu stack and the menu manager is idle: destroying it earlier would leave the game a destroyed menu to work on.
    private void Retire()
    {
        var manager = MenuManager.Instance;
        if (_menu != null && manager != null)
        {
            if (manager._workRoutine != null) return; // opening or closing menus: maybe this one
            if (manager._menuStack.Contains(_menu))
            {
                _menu.Close();
                return;
            }
        }
        Refresh(); // raises Closed if it was open
        _retiring = false;
        s_built.Remove(this);
        _host?.UnmountAll(); // the content's copies go: its bindings let go of their sources
        if (_root != null) Object.Destroy(_root);
        _root = null;
        _menu = null;
        _panel = null;
        _titleLabel = null;
        _scroll = null;
        _scrollArea = null;
        _footerRect = null;
        _footerView = null;
        _shownFooter = -1;
        _host = null;
        _contentView = null;
        _canvasRect = null;
        _shownSize = default;
        if (_headerTabs != null)
        {
            _headerTabs.StripReplaced -= OnStripReplaced;
            _headerTabs.StripInHeader = false;
            _headerTabs = null;
        }
        _plate = _tabsRect = null;
        _tabsView = null;
        _subtitle = null;
        _shownSubtitle = null;
        SyncBindings(); // not live any more: the window's own bindings let go too
    }

    // Also asks the game for what is wanted, once the request before has landed: a Close right after a Show closes the
    // window once it opened, a Show while it closes opens it again once it closed.
    private void Refresh()
    {
        var manager = MenuManager.Instance;
        bool open = InMenuStack(manager);
        bool settled = manager == null || (manager._workRoutine == null && _askedFrames > 1);
        // The game's own change (Esc, all menus closed), or a request it did not take: what it shows now is what holds.
        if (open != _asked && (open != _shown || settled)) _wanted = _asked = open;
        if (_wanted != _asked && !_retiring && (open == _asked || settled))
        {
            Ask(_wanted);
            open = InMenuStack(manager);
        }
        ShowSubtitle();
        if (open == _shown) return;
        _shown = open;
        (open ? _opened : _closed)?.Raise(h => h(this, EventArgs.Empty));
    }

    private void ShowTitle()
    {
        if (_titleLabel != null) _titleLabel.text = Title;
    }

    // Under the tabs in the header: the selected tab's name.
    private void ShowSubtitle()
    {
        if (_subtitle == null || _headerTabs == null) return;
        string text = _headerTabs.SelectedItem?.Header ?? "";
        if (text != _shownSubtitle) _subtitle.text = _shownSubtitle = text;
    }

    // The scroll view's distance from the window's top: under a dialog's title, under the half of a main window's title
    // plate inside the window, or under its tabs and the selected tab's name.
    private float ScrollTop => _headerTabs != null ? TabsHeight / 2 + SubtitleHeight + 8
                             : _menuChrome ? HeaderHeight / 2 + 20
                             : TitleTop + TitleHeight + 4;

    private float Chrome => ScrollTop + ScrollBottom; // the window's height besides its content and footer

    // The size on screen (WindowFit): Width/Height or the content's height, within Min/Max and the screen; the scroll
    // view ends above the footer.
    private void Resize()
    {
        if (_panel == null) return;
        if (_scrollToEnd > 0 && _scroll != null)
        {
            _scroll.verticalNormalizedPosition = 0;
            _scrollToEnd--;
        }
        float footer = Footer != null && _footerRect != null ? _footerRect.rect.height : 0;
        float below = footer > 0 ? footer + FooterGap : 0;
        if (footer != _shownFooter && _scrollArea != null)
        {
            _shownFooter = footer;
            _scrollArea.offsetMin = new Vector2(_scrollArea.offsetMin.x, ScrollBottom + below);
        }
        var canvas = _canvasRect != null ? _canvasRect.rect.size : Vector2.zero;
        float content = SizeToContent == SizeToContent.Height && _scroll != null ? _scroll.content.rect.height : -1;
        var (width, height) = WindowFit.Size(Width, Height, (MinWidth, MinHeight), (MaxWidth, MaxHeight), SizeToContent, Chrome + below,
                                             content, (canvas.x, canvas.y));
        var size = new Vector2(width, height);
        if (size != _shownSize)
        {
            _shownSize = size;
            _panel.sizeDelta = size;
        }
        var (x, y) = WindowFit.Offset((_offset.x, _offset.y), (size.x, size.y), (canvas.x, canvas.y));
        _offset = new Vector2(x, y);
        if (_offset.x == _shownOffset.x && _offset.y == _shownOffset.y) return;
        _shownOffset = _offset;
        _panel.anchoredPosition = _offset;
    }

    private void Remount()
    {
        if (_host == null || _contentView == null) return;
        GameUi.Guard($"{this} content", () =>
        {
            _host.UnmountAll();
            UseHeaderTabs();
            MountAll();
        });
    }

    private void MountAll()
    {
        if (_host == null || _contentView == null) return;
        if (Content is { } content) _host.Mount(content, _contentView);
        if (Footer is { } footer && _footerView != null) _host.Mount(footer, _footerView);
        if (_headerTabs != null && _tabsView != null) _host.Mount(_headerTabs.Strip, _tabsView);
    }

    // A main window whose content is a tab control with its tabs on top shows them in its header, as the game's settings
    // menu: the tab control's layout lets the strip go, and the header holds it in the title plate's place.
    private void UseHeaderTabs()
    {
        var tabs = _menuChrome && Content is UiTabControl { TabStripPlacement: Dock.Top } content ? content : null;
        if (ReferenceEquals(tabs, _headerTabs)) return;
        if (_headerTabs != null)
        {
            _headerTabs.StripReplaced -= OnStripReplaced;
            _headerTabs.StripInHeader = false;
        }
        _headerTabs = tabs;
        _shownSubtitle = null;
        if (tabs != null)
        {
            tabs.StripInHeader = true;
            tabs.StripReplaced += OnStripReplaced;
        }
        ShowHeader();
    }

    private void OnStripReplaced(object? sender, EventArgs e) => Dispatcher.Run(Remount);

    // The header holds the title plate, or the content's tabs with the selected tab's name under them.
    private void ShowHeader()
    {
        bool tabs = _headerTabs != null;
        _plate?.gameObject.SetActive(!tabs);
        _tabsRect?.gameObject.SetActive(tabs);
        _subtitle?.gameObject.SetActive(tabs);
        if (_scrollArea != null) _scrollArea.offsetMax = new Vector2(_scrollArea.offsetMax.x, -ScrollTop);
        _shownSize = default; // the chrome's height changed
    }

    private bool InMenuStack(MenuManager? manager) => _menu != null && manager != null && manager._menuStack.Contains(_menu);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Open()
    {
        if (_menu == null) Build();
        _wanted = true;
        Refresh();
    }

    private void Ask(bool open)
    {
        if (open)
        {
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1; // every opening starts at the top
            MenuManager.Instance.OpenMenu(_menu, null);
        }
        else _menu!.Close();
        _asked = open;
        _askedFrames = 0;
    }

    private void Build()
    {
        Theme.Load();
        var root = new GameObject(ObjectNamePrefix + (ObjectName ?? Title));
        _root = root;
        root.SetActive(false);
        Object.DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = Theme.DialogSortingOrder - 1; // under the game's dialogs, so Dialog.* from a window shows on top
        // The game's menus: ScaleWithScreenSize for 1920×1080 with Expand.
        // InterfaceScale.Menus divides it when the window opens, as for every game menu (ScaleService).
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referenceResolution = new Vector2(UiScale.ReferenceWidth, UiScale.ReferenceHeight);
        _canvasRect = root.GetComponent<RectTransform>();
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<CanvasGroup>();

        var panel = ViewFactory.NewRect(root.transform, "Panel");
        _panel = panel;
        // The panel only takes the pointer (clicks never reach what is behind the window); the game's chrome is its look.
        var catcher = panel.gameObject.AddComponent<Image>();
        catcher.color = Color.clear;
        _shownOffset = new Vector2(float.NaN, float.NaN);
        if (Draggable) MakeDraggable(panel.gameObject, canvas);
        _menuChrome = WindowStyle == WindowStyle.Menu && Theme.MenuChrome != null;
        if (_menuChrome) BuildMenuChrome(root.transform, panel);
        else BuildDialogChrome(panel, catcher);

        _contentView = new ContainerView(BuildScrollView(panel));
        _footerView = new ContainerView(BuildFooter(panel));

        var menu = root.AddComponent<GameContextMenu>();
        menu.context = Theme.InputContext;
        menu.GroupsToClose = new Il2CppSystem.Collections.Generic.List<TabGroup>();
        menu.Stacked = true;
        menu.showMouseCursor = true;
        menu.DestroyWhenClosed = false;
        _menu = menu;
        if (!s_watching)
        {
            s_watching = true;
            GameLoop.Update += RefreshAll;
        }
        s_built.Add(this);

        SyncBindings(); // built: the window's own bindings start
        ShowTitle();
        Resize();
        _host = new WindowHost(this, root);
        UseHeaderTabs();
        MountAll();
    }

    /// <summary>A window's element tree: alive while its object exists (never destroyed), polled while the window is open.</summary>
    private sealed class WindowHost(UiWindow window, GameObject root) : ViewHost(UnityBackend.Instance)
    {
        public override bool IsAlive => root != null;

        public override bool IsActive => window.IsOpen;

        // A column as the whole content takes over the scroll content: no extra nesting, the window's margin kept.
        public override bool AdoptsRoot(Panel panel, IElementView? parent) =>
            ReferenceEquals(panel, window.Content) && panel is UiStackPanel { Orientation: Orientation.Vertical } &&
            parent == window._contentView;

        public override float RootPadding => Margin;
    }
}
