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

// The Unity side of mod tabs in the game's windows: the tabs of one window instance and each tab with its page.
public sealed partial class MenuTabs
{
    /// <summary>
    /// The mod tabs of one instance of the window: alive while the window exists (the game destroys it when it closes),
    /// polled while it is shown. A tab is a clone of the window's last tab with a page of our own beside the game's.
    /// </summary>
    private sealed class TabsHost : ViewHost
    {
        private static readonly Color Muted = new(0.506f, 0.596f, 0.565f); // #819890: the game's titles and subtitles
        private static readonly Color Pale = new(0.776f, 0.902f, 0.855f); // #C6E6DA: the game's tab icons

        private readonly string _name;
        private readonly PageKind _kind;
        private readonly Transform _root;
        private readonly TabGroup _group;
        private readonly TabButton _template;
        private readonly TabPage _gamePage;
        private readonly Transform _lastGamePage;
        private readonly Button[] _shared;
        private readonly List<Button> _hidden = [];
        private readonly Dictionary<UiTabItem, ModTab> _tabs = new(ReferenceEqualityComparer.Instance);
        private readonly Action _everyFrame;
        private int _made;

        public TabsHost(string name, PageKind kind, Transform root, TabGroup group, TabButton template, List<TabPage> pages, Button[] shared)
            : base(UnityBackend.Instance)
        {
            _name = name;
            _kind = kind;
            _root = root;
            _group = group;
            _template = template;
            _gamePage = pages[0];
            var parent = _gamePage.transform.parent;
            _lastGamePage = pages.Select(p => p.transform).Where(t => t.parent == parent).OrderBy(t => t.GetSiblingIndex()).Last();
            _shared = shared;
            _everyFrame = () => GameUi.Guard($"{_name} tabs", HideShared);
            if (_shared.Length > 0) LiveTree.EveryFrame(_everyFrame, true);
        }

        public override bool IsAlive => _root != null;

        public override bool IsActive => _root != null && _root.gameObject.activeInHierarchy;

        public bool Holds(Transform root) => IsAlive && _root.Pointer == root.Pointer;

        // New tabs in registration order, after the window's last tab and the mod tabs made before; a disabled mod's tab hides.
        public void Place(IReadOnlyList<(ButtonEntry Entry, bool Active)> entries)
        {
            if (!IsAlive) return;
            foreach (var (entry, active) in entries)
            {
                var item = (UiTabItem)entry.Element;
                if (!_tabs.TryGetValue(item, out var tab))
                {
                    if (!active) continue;
                    _tabs[item] = tab = Make(item, entry);
                    SdkLog.Info(GameUi.Source, $"{_name}: tab '{item.Header}' added ({tab.Button.name})");
                }
                tab.Active = active;
                Show(tab);
            }
        }

        public void Refresh(UiTabItem item)
        {
            if (IsAlive && _tabs.TryGetValue(item, out var tab)) Show(tab);
        }

        public void Remove(UiTabItem item)
        {
            if (!_tabs.Remove(item, out var tab)) return;
            if (IsAlive)
            {
                LeaveIfSelected(tab);
                _group.RemoveButton(tab.PageTab);
                if (tab.HoverTab is { } hover && hover.group != null) hover.group.RemoveButton(hover);
            }
            if (tab.Content != null) Unmount(tab.Content);
            foreach (var go in new[] { tab.Button, tab.Page.gameObject })
            {
                if (go == null) continue;
                go.SetActive(false);
                go.transform.SetParent(Theme.Holder, false);
                Object.Destroy(go);
            }
            RestoreShared();
        }

        private ModTab Make(UiTabItem item, ButtonEntry entry)
        {
            var after = _tabs.Values.Select(t => t.Button.transform).LastOrDefault() ?? _template.transform;
            var go = Object.Instantiate(_template.gameObject, _template.transform.parent, false);
            go.name = entry.ObjectName;
            go.transform.SetSiblingIndex(after.GetSiblingIndex() + 1);
            ViewFactory.StripLocalization(go);
            // Two tabs on the object: the one in the window's group shows our page; the other one (TabButtonTooltip) shows
            // the hover card. Their fields are set before their Start, where each joins its group, as the game's do.
            TabButton? pageTab = null;
            TabButtonTooltip? hover = null;
            foreach (var button in go.GetComponents<TabButton>())
            {
                if (button.TryCast<TabButtonTooltip>() is { } tooltip) hover = tooltip;
                else if (button.group != null && button.group.Pointer == _group.Pointer) pageTab = button;
            }
            if (pageTab == null) throw new InvalidOperationException($"{_template.name}: no tab of the window's group on the copy");
            pageTab.order = MenuTabRules.FirstOrder + _made++;
            pageTab.selected = false;
            if (hover != null) hover.selected = false;
            foreach (string part in new[] { "Frame_Selected", "HighlightTween" })
                go.transform.Find(part)?.gameObject.SetActive(false); // the template may be the selected tab

            var (page, content, title) = NewPage($"{entry.ObjectName}_Page");
            pageTab.page = page.GetComponent<TabPage>();
            var owner = entry.Owner;
            string details = MenuTabRules.Details(ModRegistry.Mods.FirstOrDefault(m => m.GetType().Assembly == owner)?.Manifest.Name,
                                                  owner?.GetName().Name);
            return new ModTab(item, go, pageTab, hover, page, new ContainerView(content), title, details);
        }

        // A page beside the game's pages (after them: the tab bar stays drawn over the pages), with the game page's rectangle.
        private (RectTransform Page, RectTransform Content, TMP_Text? Title) NewPage(string name)
        {
            var source = _gamePage.GetComponent<RectTransform>();
            var page = ViewFactory.NewRect(source.parent, name);
            page.gameObject.SetActive(false); // like the game's pages: the tab shows it
            page.anchorMin = source.anchorMin;
            page.anchorMax = source.anchorMax;
            page.pivot = source.pivot;
            page.sizeDelta = source.sizeDelta;
            page.anchoredPosition = source.anchoredPosition;
            page.SetSiblingIndex(Math.Max(_lastGamePage.GetSiblingIndex(), _tabs.Values.Select(t => t.Page.GetSiblingIndex()).DefaultIfEmpty(0).Max()) + 1);
            page.gameObject.AddComponent<TabPage>();
            return _kind == PageKind.Settings ? SettingsPage(page) : JournalPage(page);
        }

        // The settings menu: the header where the game's pages have their subtitle, the content in the game page's scroll area.
        private (RectTransform, RectTransform, TMP_Text?) SettingsPage(RectTransform page)
        {
            TMP_Text? title = null;
            var subtitle = Enumerable.Range(0, _gamePage.transform.childCount).Select(i => _gamePage.transform.GetChild(i))
                                     .Select(t => t.GetComponent<TMP_Text>()).FirstOrDefault(t => t != null);
            if (subtitle != null)
            {
                var copy = Object.Instantiate(subtitle.gameObject, page, false);
                copy.name = "Title";
                ViewFactory.StripLocalization(copy, children: false);
                title = copy.GetComponent<TMP_Text>();
                var rect = copy.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(Math.Max(rect.sizeDelta.x, 800), rect.sizeDelta.y);
            }
            var (area, _, content) = ViewFactory.NewScrollView(page, "Scroll", padding: 0, spacing: 15);
            var scroll = _gamePage.GetComponentsInChildren<ScrollRect>(true).FirstOrDefault(s => s.transform.parent == _gamePage.transform);
            if (scroll != null)
            {
                var from = scroll.GetComponent<RectTransform>();
                area.anchorMin = from.anchorMin;
                area.anchorMax = from.anchorMax;
                area.pivot = from.pivot;
                area.sizeDelta = from.sizeDelta;
                area.anchoredPosition = from.anchoredPosition;
            }
            else ViewFactory.Stretch(area, 60);
            return (page, content, title);
        }

        // The journal: each of its pages draws the window (the frame, its corners and the close diamond), its title on the
        // top left, and holds its content on a lighter panel.
        private (RectTransform, RectTransform, TMP_Text?) JournalPage(RectTransform page)
        {
            if (Theme.MenuChrome != null)
            {
                var chrome = Object.Instantiate(Theme.MenuChrome, page, false);
                chrome.name = "Chrome";
                chrome.SetActive(true);
                foreach (var graphic in chrome.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            }
            var title = ViewFactory.NewText(page, "Title", "", Theme.TitleFont, 20);
            title.color = Muted;
            title.alignment = TextAlignmentOptions.Left;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1);
            titleRect.pivot = new Vector2(0, 0.5f);
            titleRect.sizeDelta = new Vector2(900, 48.6f);
            titleRect.anchoredPosition = new Vector2(JournalTitleLeft, -41); // where the game's page titles start

            RectTransform holder;
            if (Theme.JournalPanel != null)
            {
                var panel = Object.Instantiate(Theme.JournalPanel, page, false);
                panel.name = "Panel";
                panel.SetActive(true);
                holder = panel.GetComponent<RectTransform>();
            }
            else holder = ViewFactory.NewRect(page, "Panel");
            holder.anchorMin = Vector2.zero;
            holder.anchorMax = Vector2.one;
            holder.offsetMin = new Vector2(JournalMargin, JournalBottom);
            holder.offsetMax = new Vector2(-JournalMargin, -JournalTop);
            var (area, scroll, content) = ViewFactory.NewScrollView(holder, "Scroll", padding: 16, spacing: 10);
            ViewFactory.Stretch(area, 8);
            if (scroll.viewport != null && scroll.viewport.GetComponent<Image>() is { } back) back.enabled = false; // the panel shows through
            return (page, content, title);
        }

        // Measured on the game's journal pages: the title's left edge from the page's middle, the
        // content panel's margins.
        private const float JournalTitleLeft = -587.5f;
        private const float JournalMargin = 24;
        private const float JournalTop = 67;
        private const float JournalBottom = 36;

        // The item's header, icon, visibility and content on the tab and its page.
        private void Show(ModTab tab)
        {
            var item = tab.Item;
            string header = item.Header;
            if (tab.HoverTab != null)
            {
                tab.HoverTab.OverrideTitle = header;
                tab.HoverTab.OverrideDetails = tab.Details;
            }
            if (tab.Title != null) tab.Title.text = header;

            var sprite = Pictures.SpriteOf(item.Icon);
            var icon = tab.Button.transform.Find("Icon");
            var shadow = tab.Button.transform.Find("Icon_Shadow");
            var letter = tab.Button.transform.Find("Title")?.GetComponent<TMP_Text>();
            if (sprite != null)
            {
                foreach (var part in new[] { icon, shadow, icon?.Find("Icon_Selected") })
                {
                    if (part?.GetComponent<Image>() is { } image) image.sprite = sprite;
                }
            }
            icon?.gameObject.SetActive(sprite != null);
            shadow?.gameObject.SetActive(sprite != null);
            if (letter != null)
            {
                letter.gameObject.SetActive(sprite == null);
                letter.text = MenuTabRules.Initial(header);
                letter.fontSize = 40;
                letter.color = Pale;
            }

            bool shown = tab.Active && item.IsVisible;
            if (!shown) LeaveIfSelected(tab);
            tab.Button.SetActive(shown);

            if (!ReferenceEquals(tab.Content, item.Content))
            {
                if (tab.Content != null) Unmount(tab.Content);
                tab.Content = item.Content;
                if (tab.Content != null) Mount(tab.Content, tab.ContentView);
            }
        }

        // A tab that goes away while its page shows: the window's first tab shows instead.
        private void LeaveIfSelected(ModTab tab)
        {
            if (_group.selectedButton == null || _group.selectedButton.Pointer != tab.PageTab.Pointer) return;
            if (_group.TryCast<TabGroupControls>() is { } controls) controls.SelectPageAtIndex(0);
            else _group.TabSelect(_template);
        }

        // The settings menu's Apply, Discard and Reset stand under every page and act on the game's settings: hidden while
        // a mod page shows, given back as they were when a game page shows again.
        private void HideShared()
        {
            if (!IsAlive)
            {
                LiveTree.EveryFrame(_everyFrame, false);
                return;
            }
            bool ours = _tabs.Values.Any(t => t.Page != null && t.Page.gameObject.activeSelf);
            if (ours && _hidden.Count == 0)
            {
                foreach (var button in _shared.Where(b => b != null && b.gameObject.activeSelf))
                {
                    button.gameObject.SetActive(false);
                    _hidden.Add(button);
                }
            }
            else if (!ours) RestoreShared();
        }

        private void RestoreShared()
        {
            foreach (var button in _hidden.Where(b => b != null)) button.gameObject.SetActive(true);
            _hidden.Clear();
        }
    }

    /// <summary>One mod tab in one instance of the window: the tab (with its hover-card tab) and its page.</summary>
    private sealed class ModTab(UiTabItem item, GameObject button, TabButton pageTab, TabButtonTooltip? hoverTab, RectTransform page,
                                ContainerView contentView, TMP_Text? title, string details)
    {
        public UiTabItem Item { get; } = item;
        public GameObject Button { get; } = button;
        public TabButton PageTab { get; } = pageTab;
        public TabButtonTooltip? HoverTab { get; } = hoverTab;
        public RectTransform Page { get; } = page;
        public ContainerView ContentView { get; } = contentView;
        public TMP_Text? Title { get; } = title;
        public string Details { get; } = details;
        public Control? Content { get; set; }

        /// <summary>Its mod is not disabled.</summary>
        public bool Active { get; set; } = true;
    }
}
