namespace Mjolnir.SDK.Camera;

/// <summary>
/// The game's own cameras, one for each thing the player does: the game switches between them, and
/// each keeps its own field of view, distance and offset.
/// </summary>
public enum CameraMode
{
    /// <summary>Walking about: the camera behind the shoulder, zoomed with the wheel.</summary>
    Normal,

    /// <summary>Aiming a bow: closer, narrower.</summary>
    Aim,

    /// <summary>Climbing.</summary>
    Climbing,

    /// <summary>Locked on to a target in combat.</summary>
    LockOn,

    /// <summary>Building: farther back and higher.</summary>
    Build,

    /// <summary>Inside a building.</summary>
    Inside,

    /// <summary>In a cave.</summary>
    Cave,

    /// <summary>Flying as the raven.</summary>
    Fly,

    /// <summary>On a lookout tower.</summary>
    Lookout,

    /// <summary>On a boat.</summary>
    Boat,
}
