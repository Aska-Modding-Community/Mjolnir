using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mjolnir.SDK.Camera;

/// <summary>
/// What the model sees of the game's camera (Game/GameCameraSource.cs; a fake in the unit tests). Used on the main thread
/// only.
/// </summary>
internal interface ICameraSource
{
    /// <summary>Whether the player's camera is there: a world with the local player and the game's cameras.</summary>
    bool IsAvailable { get; }

    Vector3 Position { get; }

    Quaternion Rotation { get; }

    Vector3 Forward { get; }

    float FieldOfView { get; }

    /// <summary>The game's camera it chose (the one of the highest priority among its own), whatever a mod's camera shows.</summary>
    CameraMode Mode { get; }

    /// <summary>The game's cameras, one per mode it has.</summary>
    IReadOnlyList<ICameraRig> Rigs { get; }

    /// <summary>A point of the world on the screen, as the output camera draws it (z: the distance in front of it).</summary>
    Vector3 WorldToScreen(Vector3 world);

    /// <summary>The ray from the output camera through a point of the screen.</summary>
    Ray ScreenPointToRay(Vector2 screen);

    /// <summary>The ray from the output camera through the middle of the screen.</summary>
    Ray MiddleRay { get; }

    /// <summary>What a ray hits first, leaving out the local player.</summary>
    bool Raycast(Ray ray, float maxDistance, out Vector3 point, out GameObject? target);

    /// <summary>
    /// The mods' cameras there are now, lowest first: the last one is shown over the game's. Each frame; <paramref name="blend"/>
    /// is the blend of a change of the camera shown this frame (null: no change).
    /// </summary>
    void ShowCameras(IReadOnlyList<ModCamera> ranked, TimeSpan? blend);
}

/// <summary>One of the game's cameras: its lens and its follow, read and written as the game left them this frame.</summary>
internal interface ICameraRig
{
    /// <summary>Its identity across frames (the game's object).</summary>
    object Key { get; }

    CameraMode Mode { get; }

    float FieldOfView { get; set; }

    float Distance { get; set; }

    Vector3 ShoulderOffset { get; set; }
}
