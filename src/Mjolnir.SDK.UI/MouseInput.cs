using System;
using Mjolnir.SDK.Threading;
using UnityEngine;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The mouse, read wherever the player is: where the cursor stands, the buttons and the wheel. Its values are this frame's;
/// read them on the main thread (in <c>OnUpdate</c>), elsewhere they are empty. It reads the device itself, so it answers
/// while the game's controls are captured (<see cref="PlayerControls"/>) and while the game ignores the mouse.
/// </summary>
/// <example><code>
/// if (MouseInput.WasPressed(MouseButton.Left) &amp;&amp; !MouseInput.IsOverInterface)
///     Place(GameCamera.ScreenPointToRay(MouseInput.Position));
/// radius += MouseInput.Wheel * 0.5f;
/// </code></example>
public static class MouseInput
{
    /// <summary>Whether the game has a mouse.</summary>
    public static bool IsAvailable => Live() != null;

    /// <summary>Where the cursor stands on the screen, in pixels from its bottom left corner.</summary>
    public static Vector2 Position => Live() is { } source ? source.Position : default;

    /// <summary>
    /// How far the wheel turned this frame, in notches: positive away from the player, negative towards them; 0 when it
    /// did not turn.
    /// </summary>
    public static float Wheel => Live() is { } source ? source.Wheel : 0;

    /// <summary>Whether the cursor is over an interface element that takes the pointer: a window, a menu, a mod's own.</summary>
    public static bool IsOverInterface => Live() is { } source && source.IsOverInterface;

    /// <summary>Whether the button is held down now.</summary>
    public static bool IsPressed(MouseButton button) => Live() is { } source && source.IsPressed(button);

    /// <summary>Whether the button went down this frame.</summary>
    public static bool WasPressed(MouseButton button) => Live() is { } source && source.WasPressed(button);

    /// <summary>Whether the button came up this frame.</summary>
    public static bool WasReleased(MouseButton button) => Live() is { } source && source.WasReleased(button);

    /// <summary>The game's mouse (Unity/GameMouse.cs; a fake in unit tests).</summary>
    internal static IMouseSource? Source { get; set; }

    private static bool s_failed;

    private static IMouseSource? Live()
    {
        if (!Dispatcher.IsMainThread) return null;
        if (Source != null) return Source.IsAvailable ? Source : null;
        if (s_failed || !GameLoop.IsGameReady) return null;
        try
        {
            Source = GameMouse.Create();
        }
        catch (Exception e)
        {
            s_failed = true;
            SdkLog.Error(GameUi.Source, $"MouseInput: reading the mouse failed: {e}");
        }
        return Source is { IsAvailable: true } source ? source : null;
    }

    /// <summary>Unit tests: no mouse.</summary>
    internal static void ResetForTests()
    {
        Source = null;
        s_failed = false;
    }
}

/// <summary>A button of the mouse.</summary>
public enum MouseButton
{
    /// <summary>The left button.</summary>
    Left,

    /// <summary>The right button.</summary>
    Right,

    /// <summary>The middle button, the wheel pressed.</summary>
    Middle,
}

/// <summary>The mouse as <see cref="MouseInput"/> reads it (Unity/GameMouse.cs in the game, a fake in tests).</summary>
internal interface IMouseSource
{
    bool IsAvailable { get; }

    Vector2 Position { get; }

    float Wheel { get; }

    bool IsOverInterface { get; }

    bool IsPressed(MouseButton button);

    bool WasPressed(MouseButton button);

    bool WasReleased(MouseButton button);
}
