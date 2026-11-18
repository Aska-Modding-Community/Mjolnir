using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The player's controls in the world. A mod with a tool of its own — a view from above, a brush, a placement mode —
/// captures them: the character stands still, attacks nothing, the game's camera, its quick slots and its menu keys (Esc,
/// inventory, map, build) do nothing, and the mouse cursor shows. The mod then reads the keys (<see cref="HotKeys"/>) and
/// the mouse (<see cref="MouseInput"/>) itself, and gives the controls back by disposing the capture.
/// </summary>
/// <remarks>
/// <para>Esc does not open the pause menu while the controls are captured: give the player a key that ends the tool, Esc
/// itself if you like (<c>HotKeys.Add(KeyGesture.Parse("Escape"), …)</c>).</para>
/// <para>A mod window opened meanwhile works as ever and takes the keys while it is open; when it closes, the capture
/// holds again.</para>
/// </remarks>
/// <example><code>
/// var capture = PlayerControls.Capture();
/// capture.Ended += (_, _) => StopTool(); // the world left, the mod disabled, or disposed
/// // … each frame: MouseInput.Position, MouseInput.WasPressed(MouseButton.Left), MouseInput.Wheel …
/// capture.Dispose(); // the player walks again
/// </code></example>
public static class PlayerControls
{
    private static readonly object s_lock = new();
    private static List<ControlsCapture> s_captures = [];
    private static int s_ticking;
    private static bool s_blocked;
    private static bool s_cursor;

    /// <summary>
    /// Takes the player's controls in a loaded world until the result is disposed. Without a world it ends at once
    /// (<see cref="ControlsCapture.Ended"/> on the next frame). May be called from any thread: it holds from the next frame.
    /// </summary>
    /// <param name="showCursor">Whether the mouse cursor shows meanwhile (<see cref="ControlsCapture.ShowsCursor"/>).</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ControlsCapture Capture(bool showCursor = true) =>
        CaptureFor(CallingMod.Of(Assembly.GetCallingAssembly()), showCursor);

    /// <summary>Whether a mod holds the player's controls now.</summary>
    public static bool IsCaptured
    {
        get
        {
            lock (s_lock) return s_blocked;
        }
    }

    /// <summary>The game's controls (Unity/GameControls.cs; a fake in unit tests).</summary>
    internal static IControlsSource? Source { get; set; }

    internal static ControlsCapture CaptureFor(Assembly owner, bool showCursor)
    {
        var capture = new ControlsCapture(owner, showCursor);
        lock (s_lock) s_captures = [.. s_captures, capture];
        EnsureTicking();
        return capture;
    }

    /// <summary>
    /// One frame (main thread): captures disposed, of a disabled mod, or without a world end; the controls are held while
    /// one is left, and given back after the last.
    /// </summary>
    internal static void Tick(IControlsSource source)
    {
        List<ControlsCapture> captures;
        lock (s_lock) captures = s_captures;
        if (captures.Count == 0 && !s_blocked && !s_cursor) return;
        bool inWorld = source.InWorld;
        List<ControlsCapture>? ended = null;
        bool cursor = false;
        foreach (var capture in captures)
        {
            if (capture.IsDisposed || !inWorld || ModCall.IsDisabled(capture.Owner)) (ended ??= []).Add(capture);
            else cursor |= capture.ShowsCursor;
        }
        if (ended != null)
            lock (s_lock) s_captures = s_captures.FindAll(c => !ended.Contains(c));
        bool hold = captures.Count > (ended?.Count ?? 0);
        if (hold && !s_blocked)
        {
            source.Block();
            lock (s_lock) s_blocked = true;
        }
        else if (!hold && s_blocked)
        {
            source.Unblock();
            lock (s_lock) s_blocked = false;
        }
        if (cursor) source.HoldCursor();
        else if (s_cursor) source.ReleaseCursor();
        s_cursor = cursor;
        if (ended == null) return;
        foreach (var capture in ended) capture.End();
    }

    /// <summary>After the game's own LateUpdate work (main thread): the cursor shown again where a menu closing hid it.</summary>
    internal static void LateTick(IControlsSource source)
    {
        if (s_cursor) source.HoldCursor();
    }

    /// <summary>Unit tests: no captures, nothing held, no source.</summary>
    internal static void ResetForTests()
    {
        lock (s_lock)
        {
            s_captures = [];
            s_blocked = false;
        }
        s_cursor = false;
        Source = null;
    }

    private static void EnsureTicking()
    {
        if (Interlocked.Exchange(ref s_ticking, 1) == 1) return;
        Dispatcher.Hook();
        GameLoop.Update += OnUpdate;
        GameLoop.LateUpdate += OnLateUpdate;
    }

    private static void OnUpdate()
    {
        if (!GameLoop.IsGameReady) return;
        try
        {
            Source ??= GameControls.Create();
            if (Source != null) Tick(Source);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"PlayerControls: {e}");
        }
    }

    private static void OnLateUpdate()
    {
        if (!GameLoop.IsGameReady || Source == null) return;
        try
        {
            LateTick(Source);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"PlayerControls: {e}");
        }
    }
}

/// <summary>
/// A mod's hold on the player's controls (<see cref="PlayerControls.Capture"/>). Dispose it to give them back; it also ends
/// when the world is left or its mod is disabled after an error (<see cref="Ended"/>).
/// </summary>
public sealed class ControlsCapture : IDisposable
{
    private ModHandlers<EventHandler>? _ended;
    private volatile bool _disposed;
    private volatile bool _showsCursor;

    internal ControlsCapture(Assembly owner, bool showCursor)
    {
        Owner = owner;
        _showsCursor = showCursor;
    }

    /// <summary>Whether the mouse cursor shows while it holds; the cursor shows while any capture asks for it.</summary>
    public bool ShowsCursor
    {
        get => _showsCursor;
        set => _showsCursor = value;
    }

    /// <summary>Whether it still holds: not disposed, not ended, its mod not disabled.</summary>
    public bool IsActive => !_disposed && !HasEnded && !ModCall.IsDisabled(Owner);

    /// <summary>Raised once on the main thread when it ends, whatever the reason: disposed, the world left, its mod disabled.</summary>
    public event EventHandler? Ended
    {
        add => ModHandlers<EventHandler>.Of(ref _ended, "ControlsCapture.Ended").Add(value);
        remove => _ended?.Remove(value);
    }

    internal Assembly Owner { get; }

    internal bool IsDisposed => _disposed;

    internal bool HasEnded { get; private set; }

    /// <summary>Gives the controls back (on the next frame). Calling it again does nothing.</summary>
    public void Dispose() => _disposed = true;

    internal void End()
    {
        if (HasEnded) return;
        HasEnded = true;
        _ended?.Raise(h => h(this, EventArgs.Empty));
    }

    /// <inheritdoc/>
    public override string ToString() => $"controls captured by {Owner.GetName().Name}";
}

/// <summary>The game's controls as <see cref="PlayerControls"/> holds them (Unity/GameControls.cs in the game, a fake in tests).</summary>
internal interface IControlsSource
{
    /// <summary>Whether a world is loaded, with the player's HUD.</summary>
    bool InWorld { get; }

    /// <summary>Switches the player's controls off: a context of the SDK's with no input maps above the game's.</summary>
    void Block();

    /// <summary>Gives them back.</summary>
    void Unblock();

    /// <summary>Shows the cursor and lets it move (each frame while wanted: the game hides it when a menu closes).</summary>
    void HoldCursor();

    /// <summary>Lets the game decide the cursor again, as its menus do.</summary>
    void ReleaseCursor();
}
