using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The game's mouse for <see cref="MouseInput"/>: Unity's Input System device, read directly, so it answers whatever input
/// maps the game has on. The wheel is in notches: the game keeps the Input System's scroll uniform across platforms
/// (about 1 a notch); with the platform's own range a Windows notch is 120.
/// </summary>
internal sealed class GameMouse : IMouseSource
{
    private const float PlatformNotch = 120f;

    private int _overFrame = -1;
    private bool _over;

    private GameMouse() { }

    /// <summary>The mouse reader, or null in a game without a screen.</summary>
    public static GameMouse? Create() => Application.isBatchMode ? null : new GameMouse();

    private static Mouse? Current => Mouse.current;

    public bool IsAvailable => Current != null;

    public Vector2 Position => Current is { } mouse ? mouse.position.ReadValue() : default;

    public float Wheel
    {
        get
        {
            if (Current is not { } mouse) return 0;
            float y = mouse.scroll.ReadValue().y;
            return InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange
                ? y / PlatformNotch
                : y;
        }
    }

    public bool IsOverInterface
    {
        get
        {
            // Once a frame: a ray from the cursor through every raycaster of the interface (the HUD has none).
            int frame = Time.frameCount;
            if (_overFrame == frame) return _over;
            _overFrame = frame;
            _over = false;
            var events = EventSystem.current;
            if (events == null || Current is not { } mouse) return false;
            var data = new PointerEventData(events) { position = mouse.position.ReadValue() };
            var results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            events.RaycastAll(data, results);
            _over = results.Count > 0;
            return _over;
        }
    }

    public bool IsPressed(MouseButton button) => Button(button) is { } control && control.isPressed;

    public bool WasPressed(MouseButton button) => Button(button) is { } control && control.wasPressedThisFrame;

    public bool WasReleased(MouseButton button) => Button(button) is { } control && control.wasReleasedThisFrame;

    private static ButtonControl? Button(MouseButton button) => Current is not { } mouse
        ? null
        : button switch
        {
            MouseButton.Right => mouse.rightButton,
            MouseButton.Middle => mouse.middleButton,
            _ => mouse.leftButton,
        };
}
