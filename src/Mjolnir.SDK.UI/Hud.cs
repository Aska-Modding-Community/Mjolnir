using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SSSGame.UI;
using UnityEngine;
using UnityEngine.UI;
using Mjolnir.SDK.Threading;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>Where a HUD element sits; elements at the same anchor stack.</summary>
public enum HudAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>A part of the game's own HUD that a mod's element can stand in for (<see cref="Hud.AddInPlaceOf{T}"/>).</summary>
public enum HudPart
{
    /// <summary>The quick bar, the row of slots 1–0 at the bottom middle.</summary>
    QuickBar,
}

/// <summary>
/// The in-world HUD. The game creates a new HUD for every world it loads; elements added here appear on each one, and hide
/// and scale with the game's HUD: in a corner (<see cref="Add{T}"/>) or over a point in the world
/// (<see cref="AddAt{T}(T, Func{Vector3}, Vector2)"/>). The HUD has no cursor, so put buttons and other controls in a
/// <see cref="UiWindow"/>.
/// </summary>
/// <example><code>
/// var day = new UiTextBlock();
/// day.Bind(UiTextBlock.TextProperty, () => $"Day {World.Day}");
/// Hud.Add(day, HudAnchor.TopRight);
/// </code></example>
public static class Hud
{
    private static readonly ModHandlers<EventHandler> s_ready = new("Hud.Ready");
    private static readonly object s_lock = new();
    private static readonly List<HudItem> s_items = [];
    private static Transform? s_canvas;
    private static HudHost? s_host;

    /// <summary>
    /// Raised when the HUD of a loaded world is up (it is created as the player spawns). A static event: the sender is
    /// null; <see cref="Root"/> is the new HUD.
    /// </summary>
    public static event EventHandler? Ready
    {
        add
        {
            GameUi.EnsureInstalled();
            s_ready.Add(value);
        }
        remove => s_ready.Remove(value);
    }

    /// <summary>The current HUD's canvas, for Unity objects of your own; null outside a world.</summary>
    public static RectTransform? Root => s_canvas != null ? s_canvas.GetComponent<RectTransform>() : null;

    /// <summary>
    /// Adds an element to the HUD of every world from now on (texts, images, panels; rich text works). Safe from
    /// <see cref="Mod.OnLoad"/>. Returns the element, to change it later or to take it off with <see cref="Remove"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T Add<T>(T element, HudAnchor anchor = HudAnchor.TopLeft) where T : Control
    {
        ArgumentNullException.ThrowIfNull(element);
        AddItem(new HudItem(element, anchor, null, default, CallingMod.Of(Assembly.GetCallingAssembly())));
        return element;
    }

    /// <summary>
    /// Puts an element in the place of a part of the game's HUD, on the HUD of every world from now on: the game's part is
    /// hidden, its scripts left running, and comes back when the element is taken off with <see cref="Remove"/> (or its mod
    /// is disabled, from the next world on). For a tool of a mod's own with modes chosen by number: a row of
    /// <see cref="UiQuickSlot"/>s in the quick bar's place while the tool is in hand (a row of ten, 20 apart, stands where
    /// the game's slots stand). The element is aligned in the part's rectangle as the part aligns its own. Returns the
    /// element.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T AddInPlaceOf<T>(T element, HudPart part) where T : Control
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!Enum.IsDefined(part)) throw new ArgumentOutOfRangeException(nameof(part), part, "No such part of the HUD");
        AddItem(new HudItem(element, HudAnchor.TopLeft, null, default, CallingMod.Of(Assembly.GetCallingAssembly()), part));
        return element;
    }

    /// <summary>
    /// Adds an element over a point in the world, on the HUD of every world from now on: a marker over a place, a
    /// building, a creature. The element's bottom middle sits on the point, moved by <paramref name="offset"/> (reference
    /// pixels, y up); it follows the point every frame and hides while the point is behind the camera or off the screen.
    /// <paramref name="worldPosition"/> is read on the main thread once a frame while the element is on the HUD; if it
    /// throws, your mod is disabled, as with any handler. Take the element off with <see cref="Remove"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T AddAt<T>(T element, Func<Vector3> worldPosition, Vector2 offset = default) where T : Control
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(worldPosition);
        var owner = CallingMod.Of(Assembly.GetCallingAssembly());
        string what = $"{element} position";
        AddItem(new HudItem(element, HudAnchor.TopLeft, () => ModCall.TryRun(owner, what, worldPosition, out var at) ? at : null, offset, owner));
        return element;
    }

    /// <summary>
    /// Adds an element over a Unity object, <paramref name="worldOffset"/> from its position (above a head), on the HUD of
    /// every world from now on; it hides while the object is gone or off the screen (see
    /// <see cref="AddAt{T}(T, Func{Vector3}, Vector2)"/>).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T AddAt<T>(T element, Transform target, Vector3 worldOffset = default, Vector2 offset = default) where T : Control
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(target);
        AddItem(new HudItem(element, HudAnchor.TopLeft, () => target != null ? target.position + worldOffset : null, offset,
                            CallingMod.Of(Assembly.GetCallingAssembly())));
        return element;
    }

    private static void AddItem(HudItem item)
    {
        GameUi.EnsureInstalled();
        lock (s_lock) s_items.Add(item);
        Dispatcher.Run(() =>
        {
            // Still added (no Remove in between) and not mounted yet by a HUD that came up meanwhile.
            if (s_host is { IsAlive: true } host && IsAdded(item.Element) && !host.HasRoot(item.Element))
                GameUi.Guard("Hud.Add", () => host.Mount(item));
        });
    }

    /// <summary>
    /// Takes an element added with <see cref="Add{T}"/>, AddAt or <see cref="AddInPlaceOf{T}"/> off the HUD (the game's part it
    /// stood in for shows again): off the current world's at once (on the next frame
    /// from another thread) and off every world after. The element can be added again. False if it was not on the HUD.
    /// </summary>
    public static bool Remove(Control element)
    {
        ArgumentNullException.ThrowIfNull(element);
        bool removed;
        lock (s_lock) removed = s_items.RemoveAll(item => ReferenceEquals(item.Element, element)) > 0;
        if (!removed) return false;
        Dispatcher.Run(() =>
        {
            if (s_host is { IsAlive: true } host && !IsAdded(element)) GameUi.Guard("Hud.Remove", () => host.Remove(element));
        });
        return true;
    }

    private static bool IsAdded(Control element)
    {
        lock (s_lock) return s_items.Exists(item => ReferenceEquals(item.Element, element));
    }

    internal static void Activated(Transform canvas)
    {
        bool fresh = s_canvas != canvas;
        s_canvas = canvas;
        if (!fresh) return; // the HUD re-activates whenever a menu over it closes; Ready means "a new HUD", once per world
        Theme.Load();
        s_host = new HudHost(canvas);
        HudItem[] items;
        lock (s_lock) items = s_items.ToArray();
        foreach (var item in items)
        {
            // A mod disabled after an error loses its HUD elements from the next world on.
            if (item.Owner != null && ModCall.IsDisabled(item.Owner)) continue;
            GameUi.Guard("HUD element", () => s_host.Mount(item));
        }
        s_ready.Raise(static h => h(null, EventArgs.Empty));
    }

    /// <summary>An element added to the HUD: in a corner, or over a point in the world (<see cref="Point"/>; null hides it).</summary>
    private sealed record HudItem(Control Element, HudAnchor Anchor, Func<Vector3?>? Point, Vector2 Offset, Assembly? Owner,
                                  HudPart? InPlaceOf = null);

    /// <summary>
    /// One world's HUD: alive while its canvas exists. One stacking container per anchor; an element over a point in the
    /// world has a container of its own, placed every frame.
    /// </summary>
    private sealed class HudHost(Transform canvas) : ViewHost(UnityBackend.Instance)
    {
        private readonly Dictionary<HudAnchor, ContainerView> _groups = [];
        private readonly List<(HudItem Item, RectTransform Rect, CanvasGroup Group)> _markers = [];
        private readonly List<(Control Element, HudPart Part, RectTransform Rect)> _inPlace = [];
        private readonly Dictionary<HudPart, CanvasGroup> _hidden = [];
        private Action? _place;
        private Camera? _camera;

        public override bool IsAlive => canvas != null;

        public override bool IsActive => IsAlive;

        public void Mount(HudItem item)
        {
            if (item.InPlaceOf is { } part)
            {
                if (InPlaceOf(item.Element, part) is { } place) Mount(item.Element, new ContainerView(place));
                return;
            }
            if (item.Point == null)
            {
                if (!_groups.TryGetValue(item.Anchor, out var group)) _groups[item.Anchor] = group = new ContainerView(Group(item.Anchor));
                Mount(item.Element, group);
                return;
            }
            var rect = Marker(item.Element);
            var fade = rect.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0; // until placed
            fade.blocksRaycasts = false;
            _markers.Add((item, rect, fade));
            Mount(item.Element, new ContainerView(rect));
            _place ??= Place;
            LiveTree.EveryFrame(_place, true);
        }

        public void Remove(Control element)
        {
            Unmount(element);
            for (int i = _inPlace.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_inPlace[i].Element, element)) continue;
                var part = _inPlace[i].Part;
                if (_inPlace[i].Rect != null) Object.Destroy(_inPlace[i].Rect.gameObject);
                _inPlace.RemoveAt(i);
                // The game's part comes back once no element stands in its place.
                if (!_inPlace.Exists(p => p.Part == part) && _hidden.Remove(part, out var group) && group != null)
                {
                    group.alpha = 1;
                    group.blocksRaycasts = true;
                }
            }
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_markers[i].Item.Element, element)) continue;
                if (_markers[i].Rect != null) Object.Destroy(_markers[i].Rect.gameObject);
                _markers.RemoveAt(i);
            }
            if (_markers.Count == 0 && _place != null) LiveTree.EveryFrame(_place, false);
        }

        // Every frame: each marker over its point, or hidden (behind the camera, off the screen, its object gone).
        private void Place()
        {
            if (!IsAlive)
            {
                LiveTree.EveryFrame(_place!, false);
                return;
            }
            if (_camera == null) _camera = Camera.main;
            float scale = canvas.GetComponent<Canvas>()?.scaleFactor ?? 1;
            foreach (var (item, rect, fade) in _markers)
            {
                if (rect == null) continue;
                bool visible = false;
                if (_camera != null && item.Point!() is { } world)
                {
                    var screen = _camera.WorldToScreenPoint(world);
                    var (shown, x, y) = HudMarkers.Place((screen.x, screen.y, screen.z), (Screen.width, Screen.height), scale,
                                                         (item.Offset.x, item.Offset.y));
                    visible = shown;
                    if (shown) rect.anchoredPosition = new Vector2(x, y);
                }
                fade.alpha = visible ? 1 : 0;
            }
        }

        // The place of a part of the game's HUD: a rectangle beside the part's own, with its anchors and size, the element
        // aligned in it as the part aligns its own; the part itself hidden by a CanvasGroup of its own (its scripts keep running, and it shows again as
        // it was). Null when this HUD has no such part.
        private RectTransform? InPlaceOf(Control element, HudPart part)
        {
            var target = part switch
            {
                HudPart.QuickBar => canvas.GetComponentInChildren<QuickBarWidget>(true)?.layoutPanel,
                _ => null,
            };
            if (target == null || target.parent == null)
            {
                SdkLog.Warn(GameUi.Source, $"Hud: no {part} on this HUD; {element} is not shown");
                return null;
            }
            var rect = ViewFactory.NewRect(target.parent, $"Mjolnir_Hud_InPlaceOf_{part} {element.Name ?? element.GetType().Name}");
            rect.SetSiblingIndex(target.GetSiblingIndex() + 1);
            rect.anchorMin = target.anchorMin;
            rect.anchorMax = target.anchorMax;
            rect.pivot = target.pivot;
            rect.offsetMin = target.offsetMin;
            rect.offsetMax = target.offsetMax;
            // Aligned as the part lays out its own (the quick bar's slots sit at the bottom middle of its rectangle).
            var own = target.GetComponent<HorizontalOrVerticalLayoutGroup>();
            ViewFactory.Column(rect, align: own != null ? own.childAlignment : TextAnchor.MiddleCenter);
            _inPlace.Add((element, part, rect));
            if (!_hidden.ContainsKey(part))
            {
                var group = target.GetComponent<CanvasGroup>() ?? target.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0;
                group.blocksRaycasts = false;
                _hidden[part] = group;
            }
            return rect;
        }

        // A marker's own container: on the canvas's middle (its position is from there), its pivot at its bottom middle,
        // as big as what it holds.
        private RectTransform Marker(Control element)
        {
            var rect = ViewFactory.NewRect(canvas, $"Mjolnir_Hud_Marker {element.Name ?? element.GetType().Name}");
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0);
            ViewFactory.Column(rect, align: TextAnchor.LowerCenter);
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        public override TextAlignment TextAlign(IElementView? parent)
        {
            foreach (var (anchor, group) in _groups)
            {
                if (group != parent) continue;
                return anchor switch
                {
                    HudAnchor.TopLeft or HudAnchor.BottomLeft => TextAlignment.Left,
                    HudAnchor.TopRight or HudAnchor.BottomRight => TextAlignment.Right,
                    _ => TextAlignment.Center,
                };
            }
            return TextAlignment.Left;
        }

        // A column per anchor, created on demand under the HUD canvas.
        private RectTransform Group(HudAnchor anchor)
        {
            string name = $"Mjolnir_Hud_{anchor}";
            var existing = canvas.Find(name)?.TryCast<RectTransform>();
            if (existing != null) return existing;

            var rect = ViewFactory.NewRect(canvas, name);
            var (pivot, align) = anchor switch
            {
                HudAnchor.TopLeft => (new Vector2(0, 1), TextAnchor.UpperLeft),
                HudAnchor.TopCenter => (new Vector2(0.5f, 1), TextAnchor.UpperCenter),
                HudAnchor.TopRight => (new Vector2(1, 1), TextAnchor.UpperRight),
                HudAnchor.BottomLeft => (new Vector2(0, 0), TextAnchor.LowerLeft),
                _ => (new Vector2(1, 0), TextAnchor.LowerRight),
            };
            rect.anchorMin = rect.anchorMax = rect.pivot = pivot;
            rect.sizeDelta = new Vector2(520, 0);
            rect.anchoredPosition = new Vector2(pivot.x == 0 ? 24 : pivot.x == 1 ? -24 : 0, pivot.y == 1 ? -24 : 24);
            ViewFactory.Column(rect, 4, align);
            rect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }
    }
}

/// <summary>Hidden patch behind <see cref="Hud"/>.</summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class HudPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(WidgetMenu), nameof(WidgetMenu.OnActivate))]
    private static void WidgetMenuActivated(WidgetMenu __instance)
    {
        var hud = __instance.TryCast<PlayerHUDMenu>();
        if (hud == null) return;
        GameUi.Guard("HUD", () => Hud.Activated(hud.Canvas.transform));
    }
}
