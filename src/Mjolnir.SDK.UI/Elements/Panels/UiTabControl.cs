using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>Where a <see cref="UiTabControl"/> puts its tabs.</summary>
public enum Dock
{
    Left,
    Bottom,
    Right,
    Top,
}

/// <summary>
/// A tab of a <see cref="UiTabControl"/>, or of the game's settings menu or journal (<see cref="Menus.Settings"/>,
/// <see cref="Menus.Journal"/>): its <see cref="Header"/>, an <see cref="Icon"/>, and the <see cref="Content"/> shown while
/// it is selected.
/// </summary>
public sealed class UiTabItem : Control
{
    public static readonly MjolnirProperty<string> HeaderProperty =
        MjolnirProperty.Register<UiTabItem, string>(nameof(Header), "", DirtyFlags.None, coerce: static (_, value) => value ?? "");

    public static readonly MjolnirProperty<Bitmap?> IconProperty =
        MjolnirProperty.Register<UiTabItem, Bitmap?>(nameof(Icon), null, DirtyFlags.None);

    public static readonly MjolnirProperty<Control?> ContentProperty =
        MjolnirProperty.Register<UiTabItem, Control?>(nameof(Content), null, DirtyFlags.None);

    /// <summary>The tab's name: the hover card of an icon tab, the text of a tab in a list.</summary>
    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>The tab's picture: what an icon tab shows (tabs on top or at the bottom), left of the name in a list.</summary>
    public Bitmap? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>What the tab shows while it is selected.</summary>
    public Control? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        base.Changed(e, dirty);
        if (Parent is UiTabControl tabs) Dispatcher.Run(() => tabs.ItemChanged(this));
    }

    public override string ToString() => $"UiTabItem '{GetStored(HeaderProperty)}'";
}

/// <summary>
/// Pages behind tabs: one tab per <see cref="UiTabItem"/> of <see cref="Items"/>, the selected
/// one's content beside them. On top (the default) or at the bottom, the tabs are the game journal's icon tabs, their
/// name in its hover card; on the left or right, the journal's list of categories with icon and name. Q and E switch tabs
/// while the window holding the tab control is on top, as in the game's journal. Select with <see cref="SelectedIndex"/>
/// or <see cref="SelectedItem"/>; <see cref="SelectionChanged"/> follows.
/// </summary>
public sealed class UiTabControl : Control
{
    /// <summary>0 by default: the first tab.</summary>
    public static readonly MjolnirProperty<int> SelectedIndexProperty =
        MjolnirProperty.Register<UiTabControl, int>(nameof(SelectedIndex), 0, DirtyFlags.None, BindingMode.TwoWay,
                                                   coerce: static (tabs, index) => Math.Clamp(index, -1, tabs.Tabs.Count - 1));

    public static readonly MjolnirProperty<Dock> TabStripPlacementProperty =
        MjolnirProperty.Register<UiTabControl, Dock>(nameof(TabStripPlacement), Dock.Top, DirtyFlags.None);

    private readonly UiBorder _content = new();
    private Control _layout;
    private UiStackPanel _strip = new();
    private ModHandlers<EventHandler<SelectionChangedEventArgs>>? _selectionChanged;
    private UiTabItem? _reported;
    private bool _stripInHeader;

    public UiTabControl()
    {
        Items = new Controls(this);
        _layout = Arrange();
        Adopt(_layout);
    }

    /// <summary>The tabs, in order (only <see cref="UiTabItem"/>s are shown). Change them on the main thread.</summary>
    public Controls Items { get; }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>The selected tab (null without tabs).</summary>
    public UiTabItem? SelectedItem
    {
        get => SelectedIndex is var index && index >= 0 && index < Tabs.Count ? Tabs[index] : null;
        set => SelectedIndex = value == null ? -1 : Tabs.IndexOf(value);
    }

    /// <summary>Where the tabs go: on top (the default), at the bottom, on the left or on the right.</summary>
    public Dock TabStripPlacement
    {
        get => GetValue(TabStripPlacementProperty);
        set => SetValue(TabStripPlacementProperty, value);
    }

    /// <summary>Raised after the selected tab changed.</summary>
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged
    {
        add => ModHandlers<EventHandler<SelectionChangedEventArgs>>.Of(ref _selectionChanged, $"{this}.SelectionChanged").Add(value);
        remove => _selectionChanged?.Remove(value);
    }

    internal List<UiTabItem> Tabs => Items.OfType<UiTabItem>().ToList();

    /// <summary>Whether the tabs are icon tabs (on top or at the bottom) rather than a list.</summary>
    internal bool IconTabs => TabStripPlacement is Dock.Top or Dock.Bottom;

    internal IReadOnlyList<TabStripButton> StripButtons => _strip.Children.OfType<TabStripButton>().ToList();

    /// <summary>
    /// Whether a window shows the tabs in its header (a main window whose content this is, its tabs on top, as the game's
    /// settings menu): the strip leaves the tab control's own layout and the window mounts <see cref="Strip"/> (main thread).
    /// </summary>
    internal bool StripInHeader
    {
        get => _stripInHeader;
        set
        {
            if (_stripInHeader == value) return;
            _stripInHeader = value;
            Rearrange();
        }
    }

    /// <summary>The row of tab buttons; a new one whenever the tabs are laid out again (<see cref="StripReplaced"/>).</summary>
    internal UiStackPanel Strip => _strip;

    /// <summary>Raised while the tabs are in a window's header and the strip was made again: the window mounts the new one.</summary>
    internal event EventHandler? StripReplaced;

    /// <summary>Whether the tab control sits in another one's content: Q and E are the outer one's, as in the journal.</summary>
    internal bool IsNested
    {
        get
        {
            for (var at = Parent; at != null; at = at.Parent)
            {
                if (at is UiTabControl) return true;
            }
            return false;
        }
    }

    internal override IReadOnlyList<Control> LogicalChildren => [_layout];

    /// <summary>The previous or next tab (Q and E; called by its view), round the ends.</summary>
    internal void Step(int by)
    {
        int count = Tabs.Count;
        if (count == 0) return;
        SelectedIndex = ((SelectedIndex + by) % count + count) % count;
    }

    /// <summary>A tab's header, icon or content changed (main thread).</summary>
    internal void ItemChanged(UiTabItem item)
    {
        foreach (var button in StripButtons.Where(b => ReferenceEquals(b.Tab, item))) button.Show(item, IconTabs);
        if (ReferenceEquals(item, SelectedItem)) ShowContent();
    }

    // Items changed: the strip is made again (the selected tab kept if it is still there).
    internal override void Dirty(DirtyFlags dirty)
    {
        if ((dirty & DirtyFlags.Children) != 0)
        {
            CoerceValue(SelectedIndexProperty);
            Dispatcher.Run(BuildStrip);
            return;
        }
        base.Dirty(dirty);
    }

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        base.Changed(e, dirty);
        if (ReferenceEquals(e.Property, SelectedIndexProperty)) Dispatcher.Run(Selected);
        else if (ReferenceEquals(e.Property, TabStripPlacementProperty)) Dispatcher.Run(Rearrange);
    }

    private void Selected()
    {
        foreach (var button in StripButtons) button.IsSelected = ReferenceEquals(button.Tab, SelectedItem);
        ShowContent();
        var now = SelectedItem;
        if (ReferenceEquals(now, _reported)) return;
        var args = new SelectionChangedEventArgs(_reported == null ? [] : new object[] { _reported }, now == null ? [] : new object[] { now });
        _reported = now;
        _selectionChanged?.Raise(h => h(this, args));
    }

    private void ShowContent()
    {
        var wanted = SelectedItem?.Content;
        if (wanted != null && wanted.Parent != null && wanted.Parent != _content) wanted = null; // shown elsewhere: an empty page, not the last tab's
        if (!ReferenceEquals(_content.Child, wanted)) _content.Child = wanted;
    }

    private void Rearrange()
    {
        Release(_layout);
        _layout = Arrange();
        Adopt(_layout);
        BuildStrip();
        base.Dirty(DirtyFlags.Children);
        if (_stripInHeader) StripReplaced?.Invoke(this, EventArgs.Empty);
    }

    // Tabs on top: a column of the strip (centred) and the content; on the left: the list beside the content.
    private Control Arrange()
    {
        if (_strip.Parent != null) ((Panel)_strip.Parent).Children.Remove(_strip);
        if (_content.Parent != null) ((Panel)_content.Parent).Children.Remove(_content);
        bool icons = IconTabs;
        _strip = new UiStackPanel
        {
            Orientation = icons ? Orientation.Horizontal : Orientation.Vertical,
            Spacing = icons ? 0 : 2,
            HorizontalAlignment = icons ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
        };
        return TabStripPlacement switch
        {
            Dock.Top when _stripInHeader => new UiStackPanel { Children = { _content } },
            Dock.Bottom => new UiStackPanel { Children = { _content, _strip } },
            Dock.Left => new UiGrid { ColumnDefinitions = "Auto,*", ColumnSpacing = 16, Children = { _strip, _content } },
            Dock.Right => new UiGrid { ColumnDefinitions = "*,Auto", ColumnSpacing = 16, Children = { _content, _strip } },
            _ => new UiStackPanel { Children = { _strip, _content } },
        };
    }

    private void BuildStrip()
    {
        bool icons = IconTabs;
        var selected = SelectedItem;
        _strip.Children.Clear();
        foreach (var tab in Tabs)
        {
            var button = new TabStripButton(this, tab) { IsSelected = ReferenceEquals(tab, selected) };
            button.Show(tab, icons);
            _strip.Children.Add(button);
        }
        Selected();
    }

    private protected override void Attached() => BuildStrip();
}

/// <summary>
/// A tab of a <see cref="UiTabControl"/> on screen (internal): the journal's icon tab with the header in its hover card,
/// or a list button with icon and header; amber while selected.
/// </summary>
internal sealed class TabStripButton : Control
{
    public static readonly MjolnirProperty<string> HeaderProperty =
        MjolnirProperty.Register<TabStripButton, string>(nameof(Header), "", DirtyFlags.Text);

    public static readonly MjolnirProperty<Bitmap?> IconProperty =
        MjolnirProperty.Register<TabStripButton, Bitmap?>(nameof(Icon), null, DirtyFlags.Image);

    public static readonly MjolnirProperty<bool> IsSelectedProperty =
        MjolnirProperty.Register<TabStripButton, bool>(nameof(IsSelected), false, DirtyFlags.Value);

    // An icon tab and a list button are different game objects: a change of look builds the view again.
    public static readonly MjolnirProperty<bool> IsIconTabProperty =
        MjolnirProperty.Register<TabStripButton, bool>(nameof(IsIconTab), true, DirtyFlags.Rebuild);

    private readonly UiTabControl _tabs;

    public TabStripButton(UiTabControl tabs, UiTabItem tab)
    {
        _tabs = tabs;
        Tab = tab;
    }

    public UiTabItem Tab { get; }

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public Bitmap? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool IsIconTab
    {
        get => GetValue(IsIconTabProperty);
        set => SetValue(IsIconTabProperty, value);
    }

    /// <summary>Shows the tab's header and icon; an icon tab names itself in its hover card.</summary>
    public void Show(UiTabItem tab, bool iconTab)
    {
        IsIconTab = iconTab;
        Header = tab.Header;
        Icon = tab.Icon;
        IsVisible = tab.IsVisible;
        IsEnabled = tab.IsEnabled;
        ToolTip = iconTab && tab.ToolTip == null ? tab.Header : tab.ToolTip;
    }

    /// <summary>The player clicked the tab (called by its view).</summary>
    public void OnClick() => _tabs.SelectedItem = Tab;
}
