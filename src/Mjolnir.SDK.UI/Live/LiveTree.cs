using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>What changed on an element, so its live copies refresh only that part.</summary>
[Flags]
internal enum DirtyFlags
{
    None = 0,
    Visible = 1 << 0,
    Enabled = 1 << 1,
    Layout = 1 << 2,   // Width, Height, panel spacing/padding/alignment
    Name = 1 << 3,
    Text = 1 << 4,
    Label = 1 << 5,
    Value = 1 << 6,
    Options = 1 << 7,
    Range = 1 << 8,
    Style = 1 << 9,    // font size, colour, alignment, wrapping
    Image = 1 << 10,
    Children = 1 << 11, // the logical children changed: reconcile
    Rebuild = 1 << 12,  // the element's shape changed (a label appeared or went): recreate its copies
    ToolTip = 1 << 13,  // the hover card's content
    All = Visible | Enabled | Layout | Name | Text | Label | Value | Options | Range | Style | Image | ToolTip,
}

/// <summary>Creates the Unity side of an element (or a fake one in tests).</summary>
internal interface IViewBackend
{
    IElementView Create(Control element, IElementView? parent, ViewHost host);
}

/// <summary>One live copy of an element. It pulls the element's current state when told what changed.</summary>
internal interface IElementView
{
    /// <summary>Shows the element's current state for the given parts.</summary>
    void Apply(DirtyFlags dirty);

    /// <summary>Puts a child's view at this position among this view's children.</summary>
    void Place(IElementView child, int index);

    /// <summary>Called once the copy and its children exist (runs the element's OnCreated).</summary>
    void Created();

    /// <summary>Removes the copy (listeners first, then the Unity object).</summary>
    void Dispose();
}

/// <summary>An element's live copy in one host: its view and the copies of its children.</summary>
internal sealed class LiveNode(Control element, ViewHost host, IElementView? parentView)
{
    public Control Element { get; } = element;

    /// <summary>Created once the element is live, so it is built from values its bindings have already pushed.</summary>
    public IElementView View { get; set; } = null!;

    public ViewHost Host { get; } = host;
    public IElementView? ParentView { get; } = parentView;
    public List<LiveNode> Children { get; set; } = [];

    /// <summary>The view exists and shows the element: changes from now on are applied to it.</summary>
    public bool Ready { get; set; }
}

/// <summary>
/// A place that shows element trees: a mod window, a HUD anchor, a game menu. It knows whether its Unity root still
/// exists (the HUD and the pause menu are rebuilt by the game) and whether it is on screen (only then are computed
/// values polled).
/// </summary>
internal abstract class ViewHost(IViewBackend backend)
{
    private readonly List<LiveNode> _roots = [];

    public IViewBackend Backend { get; } = backend;
    public abstract bool IsAlive { get; }
    public abstract bool IsActive { get; }

    /// <summary>Whether this panel, mounted right under <paramref name="parent"/>, takes over that object instead of nesting.</summary>
    public virtual bool AdoptsRoot(Panel panel, IElementView? parent) => false;

    /// <summary>Extra padding of an adopted root panel (a window's margin).</summary>
    public virtual float RootPadding => 0;

    /// <summary>Default alignment of paragraphs under <paramref name="parent"/>.</summary>
    public virtual TextAlignment TextAlign(IElementView? parent) => TextAlignment.Center;
    public IReadOnlyList<LiveNode> Roots => _roots;

    /// <summary>Builds a live copy of <paramref name="element"/> under <paramref name="container"/> (the host's own view).</summary>
    public LiveNode Mount(Control element, IElementView container, int index = -1)
    {
        LiveTree.Register(this);
        var created = new List<LiveNode>();
        var node = LiveTree.Build(element, container, this, created);
        container.Place(node.View, index < 0 ? _roots.Count(r => r.ParentView == container) : index);
        _roots.Add(node);
        LiveTree.RunCreated(created);
        return node;
    }

    public void Unmount(LiveNode node)
    {
        if (_roots.Remove(node)) LiveTree.Unmount(node);
    }

    /// <summary>Removes every copy of <paramref name="element"/> mounted here as a root.</summary>
    public void Unmount(Control element)
    {
        foreach (var node in _roots.Where(r => ReferenceEquals(r.Element, element)).ToArray()) Unmount(node);
    }

    /// <summary>Whether <paramref name="element"/> is mounted here as a root.</summary>
    public bool HasRoot(Control element) => _roots.Any(r => ReferenceEquals(r.Element, element));

    public void UnmountAll()
    {
        foreach (var node in _roots.ToArray()) Unmount(node);
    }

    /// <summary>Puts <paramref name="replacement"/> in the place of a root copy built again; null (it could not be built) takes the root out.</summary>
    internal void ReplaceRoot(LiveNode old, LiveNode? replacement)
    {
        int i = _roots.IndexOf(old);
        if (i < 0) return;
        if (replacement != null) _roots[i] = replacement;
        else _roots.RemoveAt(i);
    }
}

/// <summary>
/// The live element trees: mounting and unmounting copies, applying changes, reconciling children by element identity,
/// polling function bindings, and the UI tick. Main thread only; changes from other threads are queued (coalesced) and
/// applied on the next tick.
/// </summary>
internal static class LiveTree
{
    private static readonly ConcurrentDictionary<Control, DirtyFlags> s_pending = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<PropertyBinding> s_polled = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<MjolnirObject, bool> s_onScreen = new(ReferenceEqualityComparer.Instance);
    private static readonly List<ViewHost> s_hosts = [];
    private static readonly List<Action> s_everyFrame = [];
    private static int s_ticking;

    /// <summary>Main thread: a view that follows its own size every frame (a scaled child's height) starts or stops doing so.</summary>
    public static void EveryFrame(Action check, bool on)
    {
        if (on && !s_everyFrame.Contains(check)) s_everyFrame.Add(check);
        else if (!on) s_everyFrame.Remove(check);
    }

    public static void Register(ViewHost host)
    {
        EnsureTicking();
        if (!s_hosts.Contains(host)) s_hosts.Add(host);
    }

    public static void EnsureTicking()
    {
        if (System.Threading.Interlocked.Exchange(ref s_ticking, 1) == 1) return;
        Dispatcher.Hook(); // first: posted actions run before the UI's changes in every frame
        GameLoop.Update += Tick;
    }

    /// <summary>
    /// Builds a live copy of <paramref name="element"/> and its children. Their views' <see cref="IElementView.Created"/>
    /// (the element's Loaded, mod code) is not run here: the caller links the copy into its parent or host first and then
    /// runs <paramref name="created"/>, so a handler that changes the tree finds the copy in its place. On a fault nothing
    /// of the copy is left.
    /// </summary>
    public static LiveNode Build(Control element, IElementView? parentView, ViewHost host, List<LiveNode> created)
    {
        var node = new LiveNode(element, host, parentView);
        element.AddNode(node); // the first copy starts the bindings and builds generated children
        IElementView view;
        try
        {
            view = host.Backend.Create(element, parentView, host);
        }
        catch
        {
            element.RemoveNode(node);
            throw;
        }
        node.View = view;
        int first = created.Count;
        try
        {
            view.Apply(DirtyFlags.All);
            node.Ready = true;
            var children = element.LogicalChildren;
            for (int i = 0; i < children.Count; i++)
            {
                var child = Build(children[i], view, host, created);
                node.Children.Add(child);
                view.Place(child.View, i);
            }
        }
        catch
        {
            created.RemoveRange(first, created.Count - first);
            Unmount(node);
            throw;
        }
        created.Add(node); // after its children, in the order they were built
        return node;
    }

    /// <summary>Runs the views' Created of copies just built and linked, skipping a copy that is already gone again.</summary>
    public static void RunCreated(List<LiveNode> created)
    {
        foreach (var node in created)
        {
            if (node.Ready) GameUi.Guard("UI view creation", node.View.Created);
        }
    }

    public static void Unmount(LiveNode node)
    {
        for (int i = node.Children.Count - 1; i >= 0; i--) Unmount(node.Children[i]);
        node.Children.Clear();
        node.Ready = false;
        GameUi.Guard("UI view removal", node.View.Dispose);
        node.Element.RemoveNode(node);
    }

    /// <summary>Called by an element when its state changed.</summary>
    public static void MarkDirty(Control element, DirtyFlags dirty)
    {
        if (dirty == DirtyFlags.None) return;
        if (!Dispatcher.IsMainThread)
        {
            s_pending.AddOrUpdate(element, dirty, (_, before) => before | dirty);
            return;
        }
        Apply(element, dirty);
    }

    public static void Apply(Control element, DirtyFlags dirty)
    {
        if (element.Nodes.Count == 0) return;
        foreach (var node in element.Nodes.ToArray())
        {
            if (!node.Ready) continue; // still being built: it reads the element's state once its view exists
            if ((dirty & DirtyFlags.Rebuild) != 0)
            {
                GameUi.Guard("UI rebuild", () => Remount(node));
                continue;
            }
            if ((dirty & DirtyFlags.Children) != 0) GameUi.Guard("UI children update", () => Reconcile(node));
            var rest = dirty & DirtyFlags.All;
            if (rest != DirtyFlags.None) GameUi.Guard("UI update", () => node.View.Apply(rest));
        }
    }

    // A copy whose shape changed is built again at the same place. If it cannot be built, its place goes.
    private static void Remount(LiveNode node)
    {
        var parent = FindParent(node);
        var siblings = parent?.Children;
        int index = siblings?.IndexOf(node) ?? -1;
        Unmount(node);
        var created = new List<LiveNode>();
        LiveNode? fresh = null;
        try
        {
            fresh = Build(node.Element, node.ParentView, node.Host, created);
        }
        finally
        {
            if (parent != null && index >= 0)
            {
                if (fresh != null)
                {
                    siblings![index] = fresh;
                    parent.View.Place(fresh.View, index);
                }
                else siblings!.RemoveAt(index);
            }
            else
            {
                node.Host.ReplaceRoot(node, fresh);
                if (fresh != null)
                    node.ParentView?.Place(fresh.View, node.Host.Roots.Where(r => r.ParentView == node.ParentView).ToList().IndexOf(fresh));
            }
        }
        RunCreated(created);
    }

    private static LiveNode? FindParent(LiveNode node) =>
        node.Element.Parent?.Nodes.FirstOrDefault(p => p.Host == node.Host && p.Children.Contains(node));

    /// <summary>Brings a copy's children in line with the element's logical children: kept, created, removed, reordered.</summary>
    public static void Reconcile(LiveNode node)
    {
        var desired = node.Element.LogicalChildren;
        var existing = new Dictionary<Control, LiveNode>(ReferenceEqualityComparer.Instance);
        foreach (var child in node.Children) existing.TryAdd(child.Element, child);
        var next = new List<LiveNode>(desired.Count);
        var created = new List<LiveNode>();
        foreach (var element in desired)
        {
            if (existing.Remove(element, out var kept)) next.Add(kept);
            else if (TryBuild(element, node.View, node.Host, created) is { } fresh) next.Add(fresh);
        }
        foreach (var removed in existing.Values) Unmount(removed);
        for (int i = 0; i < next.Count; i++) node.View.Place(next[i].View, i);
        node.Children = next;
        RunCreated(created);
    }

    // A child that cannot be built is left out (logged); the others are shown.
    private static LiveNode? TryBuild(Control element, IElementView parentView, ViewHost host, List<LiveNode> created)
    {
        try
        {
            return Build(element, parentView, host, created);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"{element} not shown, building it failed: {e}");
            return null;
        }
    }

    /// <summary>
    /// The UI tick (every frame, main thread, after the Dispatcher's): changes made off the main thread, hosts the game
    /// destroyed, and function bindings of what is on screen.
    /// </summary>
    public static void Tick()
    {
        if (!s_pending.IsEmpty)
        {
            foreach (var element in s_pending.Keys.ToArray())
            {
                if (s_pending.TryRemove(element, out var dirty)) Apply(element, dirty);
            }
        }

        for (int i = s_hosts.Count - 1; i >= 0; i--)
        {
            var host = s_hosts[i];
            bool alive;
            try
            {
                alive = host.IsAlive;
            }
            catch (Exception e)
            {
                SdkLog.Error(GameUi.Source, $"UI host check failed: {e}");
                alive = false;
            }
            if (alive) continue;
            s_hosts.RemoveAt(i);
            GameUi.Guard("UI host removal", host.UnmountAll);
        }

        foreach (var check in s_everyFrame.ToArray()) GameUi.Guard("UI size check", check);

        if (s_polled.Count == 0) return;
        // One on-screen check per element, however many of its properties are polled (a host check may ask Unity).
        s_onScreen.Clear();
        foreach (var binding in s_polled.ToArray())
        {
            if (!binding.IsActive) continue;
            // One binding's fault never stops the others' polls this frame (mod code is isolated inside; this is the rest).
            try
            {
                if (!s_onScreen.TryGetValue(binding.Owner, out bool shown)) s_onScreen[binding.Owner] = shown = binding.Owner.IsOnScreen;
                if (shown) binding.Poll();
            }
            catch (Exception e)
            {
                SdkLog.Error(GameUi.Source, $"{binding} poll failed: {e}");
            }
        }
    }

    /// <summary>Main thread: a function binding is polled while it is active.</summary>
    public static void SetPolled(PropertyBinding binding, bool polled)
    {
        if (polled)
        {
            EnsureTicking();
            s_polled.Add(binding);
        }
        else
        {
            s_polled.Remove(binding);
        }
    }
}
