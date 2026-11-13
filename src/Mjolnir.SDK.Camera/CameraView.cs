using UnityEngine;

namespace Mjolnir.SDK.Camera;

/// <summary>
/// One of the game's cameras as a modifier sees it this frame (<see cref="GameCamera.AddModifier"/>): the values the game
/// gave it and the modifiers of lower priority left, which the modifier changes in place. The game's own values come back
/// once no modifier changes them. Valid only while the modifier runs: do not keep it.
/// </summary>
public sealed class CameraView
{
    internal CameraView(CameraMode mode) => Mode = mode;

    /// <summary>The game's camera this is: a modifier may change only some of them (the boat's, the build camera's).</summary>
    public CameraMode Mode { get; }

    /// <summary>Whether this is the game's camera it chose now (the others are for when the player aims, builds…).</summary>
    public bool IsCurrent { get; internal set; }

    /// <summary>The vertical field of view in degrees, from 1 to 179 (the game's settings give the walking camera 45).</summary>
    public float FieldOfView { get; set; }

    /// <summary>
    /// How far behind the player the camera stands, in metres (the game's zoom moves the walking camera from 2.1 to 10.8).
    /// 0 is a view from the head.
    /// </summary>
    public float Distance { get; set; }

    /// <summary>
    /// Where the camera's pivot stands from the player's, in the player's space: x to the right, y up, z forward (the
    /// walking camera's is -0.5, 0, 0: over the left shoulder).
    /// </summary>
    public Vector3 ShoulderOffset { get; set; }
}
