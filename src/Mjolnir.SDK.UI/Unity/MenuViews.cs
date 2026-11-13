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

// The Unity side of a game menu's mod buttons: the host of one menu instance, its backend and the views.
public sealed partial class MenuButtons
{
    /// <summary>The mod buttons of one menu instance: alive while the menu object exists, polled while it is shown.</summary>
    private sealed class MenuHost(Transform root, IViewBackend backend, MenuLevels levels) : ViewHost(backend)
    {
        private readonly Dictionary<int, ContainerView> _containers = [];

        public MenuLevels Levels { get; } = levels;

        public override bool IsAlive => root != null;

        public override bool IsActive => root != null && root.gameObject.activeInHierarchy;

        public ContainerView ContainerFor(Transform parent)
        {
            int id = parent.GetInstanceID();
            if (!_containers.TryGetValue(id, out var view)) _containers[id] = view = new ContainerView(parent.Cast<RectTransform>());
            return view;
        }
    }

    /// <summary>
    /// Menu buttons are clones of the menu's own button (not the dialog's), named for the idempotent lookup; a menu item's
    /// items are built in the page of its level.
    /// </summary>
    private sealed class MenuButtonBackend(Button template, Func<Control, string> nameOf, MenuLevels levels) : IViewBackend
    {
        public IElementView Create(Control element, IElementView? parent, ViewHost host)
        {
            var container = (parent as UnityView)?.Container ?? throw new InvalidOperationException($"{element}: no parent to build in");
            return element switch
            {
                UiButton button => new MenuButtonView(button, container, template, nameOf(button)),
                // An item in a level is named after the level's opener: unique, and the same in every instance of the menu.
                UiMenuItem item when parent is MenuItemView owner => new MenuItemView(item, container, owner.Level ?? levels.Root, template,
                                                                                      $"{owner.Name}_{owner.ChildIndex(item)}", levels),
                UiMenuItem item => new MenuItemView(item, container, levels.Root, template, nameOf(item), levels),
                _ => throw new NotSupportedException($"{element}: a game menu takes UiButton and UiMenuItem only"),
            };
        }
    }

    /// <summary>
    /// A <see cref="UiMenuItem"/> in a game menu: a clone of the menu's button, and for an item with items, the page of its
    /// level (where its items are built) and the game's «Back» at the page's end.
    /// </summary>
    private sealed class MenuItemView : UnityView
    {
        private readonly UiMenuItem _item;
        private readonly MenuLevels _levels;
        private readonly string _objectName;
        private string? _shown;

        public MenuItemView(UiMenuItem item, RectTransform parent, MenuLevel parentLevel, Button template, string objectName, MenuLevels levels)
            : base(item)
        {
            _item = item;
            _levels = levels;
            _objectName = objectName;
            var level = item.OpensLevel ? levels.NewLevel($"{objectName}_Level") : null;
            Level = level;
            Action click = level == null ? item.OnClick : () =>
            {
                item.OnClick();
                levels.Opened(level);
            };
            var go = ViewFactory.CloneButton(template, parent, objectName, item.Header, click);
            Root = RectOf(go);
            Listen(go.GetComponent<Button>().onClick);
            if (level == null) return;
            levels.ConnectOpener(go, parentLevel, level);
            Listen(levels.NewBack(level, parentLevel, template).GetComponent<Button>().onClick);
        }

        /// <summary>The level this item opens; null for an item without items.</summary>
        public MenuLevel? Level { get; }

        public string Name => _objectName;

        public override RectTransform Container => Level?.Page ?? Root;

        protected override string DefaultName => _objectName;

        protected override string ObjectName => _objectName; // the idempotent lookup goes by this name

        public int ChildIndex(UiMenuItem child)
        {
            int index = _item.Items.IndexOf(child);
            return index >= 0 ? index : _item.Items.Count;
        }

        protected override void ApplyLayout() { } // the menu's own layout

        protected override void ApplyOwn(DirtyFlags dirty)
        {
            if ((dirty & DirtyFlags.Text) != 0 && _shown != _item.Header) ViewFactory.SetLabel(Root.gameObject, _shown = _item.Header);
        }

        // «Back» stays the level's last button.
        public override void Place(IElementView child, int index)
        {
            base.Place(child, index);
            Level?.Back?.transform.SetAsLastSibling();
        }

        public override void Dispose()
        {
            if (Level != null) _levels.Forget(Level);
            base.Dispose();
        }
    }

    private sealed class MenuButtonView : UnityView
    {
        private readonly UiButton _button;
        private readonly string _objectName;
        private string? _shown;

        public MenuButtonView(UiButton button, RectTransform parent, Button template, string objectName) : base(button)
        {
            _button = button;
            _objectName = objectName;
            var go = ViewFactory.CloneButton(template, parent, objectName, button.Content, button.OnClick);
            Root = RectOf(go);
            Listen(go.GetComponent<Button>().onClick);
        }

        protected override string DefaultName => _objectName;

        protected override string ObjectName => _objectName; // the idempotent lookup goes by this name

        protected override void ApplyLayout() { } // the menu's own layout

        protected override void ApplyOwn(DirtyFlags dirty)
        {
            if ((dirty & DirtyFlags.Text) != 0 && _shown != _button.Content) ViewFactory.SetLabel(Root.gameObject, _shown = _button.Content);
        }
    }
}
