using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mjolnir.SDK.Config;
using Mjolnir.SDK.Threading;
using Mjolnir.SDK.UI;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>The UI's main thread and its tick are process-wide: UI test classes run one after another.</summary>
[CollectionDefinition("MainThread", DisableParallelization = true)]
public sealed class MainThreadCollection;

/// <summary>A stand-in for the Unity side: records what the live tree asks of each copy.</summary>
internal sealed class FakeBackend : IViewBackend
{
    public List<FakeView> Views { get; } = [];

    /// <summary>Elements whose copy cannot be built (the Unity side throws for them).</summary>
    public Func<Control, bool>? Refuses { get; set; }

    public IElementView Create(Control element, IElementView? parent, ViewHost host)
    {
        if (Refuses?.Invoke(element) == true) throw new InvalidOperationException($"{element}: no view");
        var view = new FakeView(element);
        Views.Add(view);
        return view;
    }
}

internal sealed class FakeView(Control element) : IElementView
{
    public Control Element { get; } = element;
    public List<FakeView> Children { get; } = [];
    public List<DirtyFlags> Applied { get; } = [];
    public bool Disposed { get; private set; }
    public int CreatedCalls { get; private set; }
    public FakeView? Parent { get; private set; }

    public void Apply(DirtyFlags dirty) => Applied.Add(dirty);

    public void Place(IElementView child, int index)
    {
        var view = (FakeView)child;
        view.Parent?.Children.Remove(view);
        view.Parent = this;
        Children.Insert(Math.Min(index, Children.Count), view);
    }

    // As a Unity view does: the element's Loaded (no Unity object here).
    public void Created()
    {
        CreatedCalls++;
        Element.RaiseLoaded(null!);
    }

    public void Dispose()
    {
        Disposed = true;
        Parent?.Children.Remove(this);
    }

    public int AppliedCount(DirtyFlags part) => Applied.Count(d => (d & part) != 0);
}

internal sealed class FakeHost(FakeBackend backend) : ViewHost(backend)
{
    public bool Alive { get; set; } = true;
    public bool Active { get; set; } = true;
    public override bool IsAlive => Alive;
    public override bool IsActive => Active;
}

/// <summary>A fake host and a root to mount elements under; every test starts with no main thread recorded.</summary>
public abstract class ViewTestBase : IDisposable
{
    private protected readonly FakeBackend Backend = new();
    private protected readonly FakeHost Host;
    private protected readonly FakeView Root;

    protected ViewTestBase()
    {
        Dispatcher.ResetForTests();
        Host = new FakeHost(Backend);
        Root = new FakeView(new UiStackPanel());
    }

    public void Dispose()
    {
        Host.Alive = false;
        Tick(); // drops this test's host
        Dispatcher.ResetForTests();
    }

    /// <summary>One frame, as in game: the Dispatcher's tick (posted actions, the main thread), then the UI's.</summary>
    private protected static void Tick()
    {
        Dispatcher.Tick();
        LiveTree.Tick();
    }

    private protected FakeView Mount(Control element)
    {
        Host.Mount(element, Root);
        return ViewOf(element);
    }

    private protected FakeView ViewOf(Control element) => Backend.Views.Single(v => ReferenceEquals(v.Element, element) && !v.Disposed);

    private protected static ModConfig NewConfig() =>
        ModConfig.Load(Path.Combine(Path.GetTempPath(), "mjolnir-ui-tests", Guid.NewGuid().ToString("N") + ".toml"), "Test", _ => { });
}
