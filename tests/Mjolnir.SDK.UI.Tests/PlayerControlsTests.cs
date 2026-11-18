using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;

namespace Mjolnir.SDK.UI.Tests;

/// <summary>The game's controls for the tests: whether a world is up, whether the SDK's context is on, the cursor.</summary>
internal sealed class FakeControls : IControlsSource
{
    public bool InWorld { get; set; } = true;
    public bool Blocked { get; private set; }
    public int Blocks { get; private set; }
    public int CursorHolds { get; private set; }
    public int CursorReleases { get; private set; }

    public void Block()
    {
        Blocked = true;
        Blocks++;
    }

    public void Unblock() => Blocked = false;

    public void HoldCursor() => CursorHolds++;

    public void ReleaseCursor() => CursorReleases++;
}

/// <summary>A mouse for the tests.</summary>
internal sealed class FakeMouse : IMouseSource
{
    public bool IsAvailable { get; set; } = true;
    public Vector2 Position { get; set; }
    public float Wheel { get; set; }
    public bool IsOverInterface { get; set; }
    public HashSet<MouseButton> Held { get; } = [];
    public HashSet<MouseButton> Down { get; } = [];
    public HashSet<MouseButton> Up { get; } = [];

    public bool IsPressed(MouseButton button) => Held.Contains(button);

    public bool WasPressed(MouseButton button) => Down.Contains(button);

    public bool WasReleased(MouseButton button) => Up.Contains(button);
}

/// <summary>
/// PlayerControls on fake controls: a capture holds them from the next frame and while any is left, shows the cursor while
/// one asks, and ends — once, on the main thread — when disposed, when the world is left, when taken without a world or
/// when its mod is disabled. MouseInput and KeyboardInput read their devices on the main thread only.
/// </summary>
[Collection("MainThread")]
public sealed class PlayerControlsTests : IDisposable
{
    private readonly FakeControls _controls = new();

    public PlayerControlsTests()
    {
        Dispatcher.ResetForTests();
        Dispatcher.Tick(); // this thread is the main one
        PlayerControls.ResetForTests();
        MouseInput.ResetForTests();
        HotKeys.ResetForTests();
    }

    public void Dispose()
    {
        HotKeys.ResetForTests();
        PlayerControls.ResetForTests();
        MouseInput.ResetForTests();
        Dispatcher.ResetForTests();
    }

    private static Assembly NewMod(string name) =>
        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);

    private void Frame()
    {
        PlayerControls.Tick(_controls);
        PlayerControls.LateTick(_controls);
    }

    [Fact]
    public void ACaptureHoldsTheControlsUntilTheLastOneGoes()
    {
        var mod = NewMod("Controls.Hold");
        var first = PlayerControls.CaptureFor(mod, showCursor: true);
        Assert.False(_controls.Blocked); // from the next frame
        Frame();
        Assert.True(_controls.Blocked);
        Assert.True(PlayerControls.IsCaptured);
        Assert.True(first.IsActive);
        Assert.Equal(2, _controls.CursorHolds); // in the frame and after the game's own late work

        var second = PlayerControls.CaptureFor(mod, showCursor: false);
        Frame();
        Assert.Equal(1, _controls.Blocks); // one context for every capture
        first.Dispose();
        Assert.False(first.IsActive);
        int holds = _controls.CursorHolds;
        Frame();
        Assert.True(_controls.Blocked); // the second still holds
        Assert.Equal(holds, _controls.CursorHolds); // and wants no cursor
        Assert.Equal(1, _controls.CursorReleases); // given back to the game's menus

        second.ShowsCursor = true;
        Frame();
        Assert.True(_controls.CursorHolds > holds);
        second.Dispose();
        Frame();
        Assert.False(_controls.Blocked);
        Assert.False(PlayerControls.IsCaptured);
        Assert.Equal(2, _controls.CursorReleases);
        Assert.Contains("controls captured by Controls.Hold", second.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ACaptureEndsOnceWhateverTheReason()
    {
        var mod = NewMod("Controls.End");
        var disposed = PlayerControls.CaptureFor(mod, true);
        var left = PlayerControls.CaptureFor(mod, true);
        int disposedEnds = 0, leftEnds = 0;
        disposed.Ended += (sender, _) =>
        {
            Assert.Same(disposed, sender);
            disposedEnds++;
        };
        left.Ended += (_, _) => leftEnds++;
        Frame();
        disposed.Dispose();
        disposed.Dispose();
        Frame();
        Frame();
        Assert.Equal(1, disposedEnds);
        Assert.Equal(0, leftEnds);

        _controls.InWorld = false; // the world left
        Frame();
        Assert.Equal(1, leftEnds);
        Assert.False(left.IsActive);
        Assert.False(_controls.Blocked);
    }

    [Fact]
    public void ACaptureWithoutAWorldEndsAtOnce()
    {
        _controls.InWorld = false;
        var capture = PlayerControls.CaptureFor(NewMod("Controls.Menu"), true);
        int ends = 0;
        capture.Ended += (_, _) => ends++;
        Frame();
        Assert.Equal(1, ends);
        Assert.False(_controls.Blocked);
        Assert.Equal(0, _controls.CursorHolds);
    }

    [Fact]
    public void ADisabledModsCaptureEnds()
    {
        var broken = NewMod("Controls.Broken");
        var healthy = NewMod("Controls.Healthy");
        var lost = PlayerControls.CaptureFor(broken, true);
        var kept = PlayerControls.CaptureFor(healthy, false);
        Frame();
        ModCall.Disable(broken, "test");
        Assert.False(lost.IsActive);
        Frame();
        Assert.True(kept.IsActive);
        Assert.True(_controls.Blocked);
        Assert.Equal(1, _controls.CursorReleases); // only the broken one wanted the cursor
    }

    [Fact]
    public void ACaptureMayBeTakenFromAnotherThread()
    {
        ControlsCapture? capture = null;
        var thread = new System.Threading.Thread(() => capture = PlayerControls.CaptureFor(NewMod("Controls.Thread"), true));
        thread.Start();
        thread.Join();
        Frame();
        Assert.True(_controls.Blocked);
        Assert.True(capture!.IsActive);
    }

    [Fact]
    public void TheMouseIsReadOnTheMainThread()
    {
        Assert.False(MouseInput.IsAvailable); // no mouse yet: the game is not ready in the tests
        var mouse = new FakeMouse { Wheel = -1, IsOverInterface = true };
        mouse.Held.Add(MouseButton.Left);
        mouse.Down.Add(MouseButton.Right);
        mouse.Up.Add(MouseButton.Middle);
        MouseInput.Source = mouse;
        Assert.True(MouseInput.IsAvailable);
        Assert.Equal(-1f, MouseInput.Wheel);
        Assert.True(MouseInput.IsOverInterface);
        Assert.True(MouseInput.IsPressed(MouseButton.Left));
        Assert.False(MouseInput.IsPressed(MouseButton.Right));
        Assert.True(MouseInput.WasPressed(MouseButton.Right));
        Assert.True(MouseInput.WasReleased(MouseButton.Middle));

        bool elsewhere = true;
        float wheel = 1;
        var thread = new System.Threading.Thread(() =>
        {
            elsewhere = MouseInput.IsPressed(MouseButton.Left);
            wheel = MouseInput.Wheel;
        });
        thread.Start();
        thread.Join();
        Assert.False(elsewhere);
        Assert.Equal(0f, wheel);

        mouse.IsAvailable = false;
        Assert.False(MouseInput.IsPressed(MouseButton.Left));
    }

    [Fact]
    public void TheKeyboardIsReadOnTheMainThread()
    {
        Assert.False(KeyboardInput.IsAvailable); // no keyboard yet: the game is not ready in the tests
        var keyboard = new FakeKeyboard { Modifiers = KeyModifiers.Shift };
        keyboard.Held.Add("W");
        keyboard.Pressed.Add("Q");
        keyboard.Released.Add("E");
        HotKeys.Keyboard = keyboard;
        Assert.True(KeyboardInput.IsAvailable);
        Assert.True(KeyboardInput.IsPressed("w"));
        Assert.False(KeyboardInput.IsPressed("S"));
        Assert.True(KeyboardInput.WasPressed("Q"));
        Assert.True(KeyboardInput.WasReleased("E"));
        Assert.Equal(KeyModifiers.Shift, KeyboardInput.Modifiers);
        Assert.False(KeyboardInput.IsPressed("NoSuchKey"));
        Assert.False(KeyboardInput.IsTyping);
        keyboard.IsTyping = true;
        Assert.True(KeyboardInput.IsTyping);

        bool elsewhere = true;
        var thread = new System.Threading.Thread(() => elsewhere = KeyboardInput.IsPressed("W"));
        thread.Start();
        thread.Join();
        Assert.False(elsewhere);
    }
}
