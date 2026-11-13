using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Threading;

namespace Mjolnir.SDK.Threading;

/// <summary>
/// The game's main thread, where all Unity work happens (<c>Dispatcher.MainThread</c>). The SDK's elements can
/// be changed from any thread: their live copies follow on the next frame. Use <c>Dispatcher.MainThread.Post</c> for
/// anything else that must run on the main thread, such as showing a dialog when a download finishes. Code that
/// <c>await</c>s on the main thread (<c>await Dialog.ConfirmAsync(…)</c>) goes on there too, at the start of a later
/// frame: the main thread's SynchronizationContext posts to the dispatcher.
/// </summary>
public sealed class Dispatcher
{
    private static readonly ConcurrentQueue<(Action Action, string What)> s_queue = new();
    private static int s_main = -1;
    private static int s_hooked;

    private Dispatcher() { }

    /// <summary>The game's main thread.</summary>
    public static Dispatcher MainThread { get; } = new();

    /// <summary>The game's main thread: <see cref="MainThread"/>, its former name.</summary>
    [Obsolete("Use Dispatcher.MainThread: the game has one main thread, for its interface and everything else.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Dispatcher UIThread => MainThread;

    /// <summary>
    /// Whether the caller is on the main thread. Before the game is ready (phase A, <see cref="Mod.OnLoad"/>) nothing is on
    /// screen yet and every thread counts as main.
    /// </summary>
    public bool CheckAccess() => IsMainThread;

    /// <summary>Throws unless the caller is on the main thread.</summary>
    public void VerifyAccess()
    {
        if (!IsMainThread) throw new InvalidOperationException("call this from the game's main thread (from elsewhere, through Dispatcher.MainThread.Post)");
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the main thread at the start of the next frame (also when called from it). If it
    /// throws, the mod that wrote it is disabled.
    /// </summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Hook();
        s_queue.Enqueue((action, "Dispatcher.MainThread.Post"));
    }

    /// <summary><see cref="CheckAccess"/> for SDK code.</summary>
    internal static bool IsMainThread
    {
        get
        {
            int main = Volatile.Read(ref s_main);
            return main < 0 || Environment.CurrentManagedThreadId == main;
        }
    }

    /// <summary>Runs SDK code now on the main thread, or posts it from another one.</summary>
    internal static void Run(Action action)
    {
        if (IsMainThread)
        {
            action();
            return;
        }
        Hook();
        s_queue.Enqueue((action, "SDK"));
    }

    /// <summary>Queues <paramref name="action"/> for the next tick, from any thread (the main thread's SynchronizationContext).</summary>
    internal static void Enqueue(Action action, string what)
    {
        Hook();
        s_queue.Enqueue((action, what));
    }

    /// <summary>
    /// Starts the dispatcher's tick (once). Modules with their own per-frame work (the UI's live tree) hook it before
    /// subscribing to the frame, so posted actions run first in every frame.
    /// </summary>
    internal static void Hook()
    {
        if (Interlocked.Exchange(ref s_hooked, 1) == 1) return;
        if (!GameLoop.IsGameReady) GameLoop.GameReady += MarkMainThread;
        GameLoop.Update += Tick;
    }

    /// <summary>
    /// The dispatcher's tick (every frame, main thread): records the main thread (in case it was hooked late from another
    /// thread) and runs what was posted.
    /// </summary>
    internal static void Tick()
    {
        MarkMainThread();
        if (GameLoop.IsGameReady) DispatcherSynchronizationContext.InstallOnThisThread();
        // Only what was queued before this frame: an action that posts again runs next frame, not in a loop.
        int count = s_queue.Count;
        for (int i = 0; i < count && s_queue.TryDequeue(out var item); i++) ModCall.InvokeEach(item.Action, item.What);
    }

    internal static void MarkMainThread() => Volatile.Write(ref s_main, Environment.CurrentManagedThreadId);

    /// <summary>Unit tests: forget the main thread (every thread counts as main again) and drop queued actions.</summary>
    internal static void ResetForTests()
    {
        Volatile.Write(ref s_main, -1);
        s_queue.Clear();
    }
}

/// <summary>
/// The main thread's SynchronizationContext: an <c>await</c> started on the
/// main thread goes on through <see cref="Dispatcher"/>, at the start of a later frame, and stays on the main thread.
/// Installed by the dispatcher's tick in the running game only (unit tests keep their own context).
/// </summary>
internal sealed class DispatcherSynchronizationContext : SynchronizationContext
{
    internal static void InstallOnThisThread()
    {
        if (Current is not DispatcherSynchronizationContext) SetSynchronizationContext(new DispatcherSynchronizationContext());
    }

    public override void Post(SendOrPostCallback d, object? state) => Dispatcher.Enqueue(() => d(state), "await continuation");

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Dispatcher.IsMainThread)
        {
            d(state);
            return;
        }
        using var done = new ManualResetEventSlim();
        Dispatcher.Enqueue(() =>
        {
            try
            {
                d(state);
            }
            finally
            {
                done.Set();
            }
        }, "SynchronizationContext.Send");
        done.Wait();
    }

    public override SynchronizationContext CreateCopy() => this;
}
