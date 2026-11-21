using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mjolnir.SDK.Threading;
using UnityEngine;

namespace Mjolnir.SDK.Camera;

/// <summary>
/// The player's camera: where it is and what it sees, the screen and the world, changes to the game's own cameras
/// (<see cref="AddModifier"/>) and cameras of a mod's own over them (<see cref="Take"/>). Mods share it: modifiers run in
/// the order of their priority, the mods' camera of the highest priority is shown, and everything a mod added goes away when
/// it disposes it or the mod is disabled after an error, the game's own camera coming back. The camera is the local
/// player's only: nothing of it goes over the network. Read it on the main thread; another thread gets what it showed there
/// last.
/// </summary>
/// <example><code>
/// // A wider view while the player walks, and a view from above on a key.
/// GameCamera.AddModifier(view => { if (view.Mode == CameraMode.Normal) view.FieldOfView += 15; });
/// var above = GameCamera.Take(priority: 10, blend: TimeSpan.FromSeconds(1));
/// above.Follow = player; above.FollowOffset = new Vector3(0, 20, -8); above.LookAt = player;
/// // …
/// above.Dispose();
/// </code></example>
public static class GameCamera
{
    private static ModHandlers<EventHandler<ValueChangedEventArgs<CameraMode>>>? s_modeChanged;
    private static Vector3 s_position;
    private static Quaternion s_rotation;
    private static Vector3 s_forward;
    private static float s_fieldOfView;
    private static CameraMode s_mode;

    /// <summary>Whether there is a player's camera: a world with the local player. False in the main menu.</summary>
    public static bool IsAvailable => Live() != null;

    /// <summary>Where the camera the screen shows stands in the world.</summary>
    public static Vector3 Position
    {
        get
        {
            if (Live() is { } source) s_position = source.Position;
            return s_position;
        }
    }

    /// <summary>Where the camera the screen shows looks.</summary>
    public static Quaternion Rotation
    {
        get
        {
            if (Live() is { } source) s_rotation = source.Rotation;
            return s_rotation;
        }
    }

    /// <summary>The direction it looks in, of length 1.</summary>
    public static Vector3 Forward
    {
        get
        {
            if (Live() is { } source) s_forward = source.Forward;
            return s_forward;
        }
    }

    /// <summary>Its vertical field of view in degrees, as drawn now (the game's settings, the modifiers and blends in it).</summary>
    public static float FieldOfView
    {
        get
        {
            if (Live() is { } source) s_fieldOfView = source.FieldOfView;
            return s_fieldOfView;
        }
    }

    /// <summary>The game's camera chosen now (walking, aiming, building…), whatever a mod's camera shows over it.</summary>
    public static CameraMode Mode
    {
        get
        {
            if (Live() is { } source) s_mode = source.Mode;
            return s_mode;
        }
    }

    /// <summary>
    /// The mods' camera the screen shows now (<see cref="Take"/>), or null when it shows the game's.
    /// </summary>
    public static ModCamera? ShownCamera => CameraDirector.Shown;

    /// <summary>
    /// Raised when the game goes over to another of its cameras (the player aims, builds, goes into a cave), on the frame it
    /// does. Not raised as a world loads or is left.
    /// </summary>
    public static event EventHandler<ValueChangedEventArgs<CameraMode>>? ModeChanged
    {
        add => ModHandlers<EventHandler<ValueChangedEventArgs<CameraMode>>>.Of(ref s_modeChanged, "GameCamera.ModeChanged").Add(value);
        remove => s_modeChanged?.Remove(value);
    }

    /// <summary>
    /// A point of the world on the screen, in pixels from the bottom left corner; z is how far in front of the camera it is
    /// (negative behind it). Without a camera, (0, 0, -1).
    /// </summary>
    public static Vector3 WorldToScreen(Vector3 world)
    {
        if (Live() is { } source) return source.WorldToScreen(world);
        Vector3 none = default;
        none.z = -1;
        return none;
    }

    /// <summary>The ray from the camera through a point of the screen, in pixels from the bottom left corner.</summary>
    public static Ray ScreenPointToRay(Vector2 screen) => Live() is { } source ? source.ScreenPointToRay(screen) : default;

    /// <summary>
    /// What the middle of the screen looks at (where the game's crosshair is): the first thing a ray from the camera hits
    /// within <paramref name="maxDistance"/> metres, the local player left out.
    /// </summary>
    /// <param name="maxDistance">How far to look, in metres.</param>
    /// <param name="point">Where the ray hits.</param>
    /// <param name="target">The object it hits.</param>
    /// <returns>Whether it hits anything.</returns>
    public static bool TryRaycast(float maxDistance, out Vector3 point, out GameObject? target)
    {
        if (Live() is { } source && maxDistance > 0) return source.Raycast(source.MiddleRay, maxDistance, out point, out target);
        point = default;
        target = null;
        return false;
    }

    /// <summary>
    /// The first thing a ray hits within <paramref name="maxDistance"/> metres, the local player and the game's trigger
    /// volumes left out: what a cursor points at (<see cref="ScreenPointToRay"/>) — a rock, a trunk, a wall — where a ray
    /// over the ground alone would pass through it to the ground behind.
    /// </summary>
    /// <param name="ray">The ray, in the world.</param>
    /// <param name="maxDistance">How far to look, in metres.</param>
    /// <param name="point">Where the ray hits.</param>
    /// <param name="target">The object it hits.</param>
    /// <returns>Whether it hits anything.</returns>
    public static bool TryRaycast(Ray ray, float maxDistance, out Vector3 point, out GameObject? target)
    {
        if (Live() is { } source && maxDistance > 0) return source.Raycast(ray, maxDistance, out point, out target);
        point = default;
        target = null;
        return false;
    }

    /// <summary>
    /// Changes the game's cameras every frame: <paramref name="modify"/> gets each of them as the game set it (and the
    /// modifiers of a lower priority changed it) and changes it in place. Modifiers add up, so two mods widening the view
    /// both count. The game's values come back once the modifier is disposed or its mod disabled. A modifier that throws
    /// disables its mod.
    /// </summary>
    /// <param name="modify">The change: it runs on the main thread once a frame for each of the game's cameras.</param>
    /// <param name="priority">Its place among the modifiers: a higher one runs later, so it has the last word.</param>
    /// <returns>The modifier: dispose it to take the change away.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modify"/> is null.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CameraModifier AddModifier(Action<CameraView> modify, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(modify);
        return CameraDirector.Add(CallingMod.Of(Assembly.GetCallingAssembly()), modify, priority);
    }

    /// <summary>
    /// Shows a camera of the mod's own over the game's, from where the player's camera is, until it is disposed. Of the mods'
    /// cameras the screen shows the one of the highest priority (of two alike, the newer); the game's comes back when none is
    /// left. The view goes over to it and back over <paramref name="blend"/>. Taken without a world (in the main menu), it
    /// ends on the next frame.
    /// </summary>
    /// <param name="priority">Its place among the mods' cameras.</param>
    /// <param name="blend">How long the view takes to go over to it and back; the default is at once.</param>
    /// <returns>The camera: move it, then dispose it to give the view back.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blend"/> is negative.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ModCamera Take(int priority = 0, TimeSpan blend = default)
    {
        if (blend < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(blend), blend, "A blend takes no time or some.");
        return CameraDirector.Take(CallingMod.Of(Assembly.GetCallingAssembly()), priority, blend);
    }

    internal static void RaiseModeChanged(CameraMode oldMode, CameraMode newMode)
    {
        s_mode = newMode;
        var args = new ValueChangedEventArgs<CameraMode>(oldMode, newMode);
        s_modeChanged?.Raise(h => h(null, args));
    }

    private static ICameraSource? Live() => Dispatcher.IsMainThread && CameraDirector.Camera is { IsAvailable: true } source ? source : null;

    /// <summary>Forgets what the camera last showed, between unit tests.</summary>
    internal static void ResetForTests()
    {
        CameraDirector.ResetForTests();
        s_modeChanged = null;
        s_position = default;
        s_rotation = default;
        s_forward = default;
        s_fieldOfView = 0;
        s_mode = CameraMode.Normal;
    }
}
