using System;
using System.Collections.Generic;
using Mjolnir.SDK.Camera;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "camera" scenario: Mjolnir.SDK.Camera in the world, checked against the game's own camera objects. It reads the camera
/// (where it is, the screen point of the player, what the middle of the screen looks at); puts a modifier on the field of
/// view and another, of a higher priority, on the distance, and watches that they hold steady and add up over seconds and
/// across the game's zoom; makes a view from the head with a modifier; raises the build camera's priority for ModeChanged;
/// takes a mod's camera above the player (following and looking at them, blended in over a second), a second one of a lower
/// priority under it, and gives both back; and checks after each that the game's own values came back. Screenshots on the
/// way. It loads the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> CameraScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        Log($"Camera: in the main menu available {GameCamera.IsAvailable}");
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var im = GameManagers.Get<InputManager>();
        var normal = im?.GetCameraObject(InputManager.CameraStates.NORMAL);
        var follow = normal?.baseComp;
        var player = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (im == null || normal?.cam == null || follow == null || player == null)
        {
            Log($"Camera: missing: input manager {im != null}, normal camera {normal?.cam != null}, follow {follow != null}, player {player != null}");
            yield break;
        }

        string Game() => $"game's normal: fov {normal.cam.m_Lens.FieldOfView:0.00} distance {follow.CameraDistance:0.00} shoulder {follow.ShoulderOffset}";
        var main = Camera.main;
        Log($"Camera: available {GameCamera.IsAvailable}, at {GameCamera.Position} (main camera at {main?.transform.position}), forward {GameCamera.Forward}, fov {GameCamera.FieldOfView:0.0}, mode {GameCamera.Mode}; {Game()}");
        Log($"Camera: the player on the screen at {GameCamera.WorldToScreen(player.transform.position + Vector3.up)} of {Screen.width}×{Screen.height}");
        bool hit = GameCamera.TryRaycast(200, out var point, out var target);
        Log($"Camera: the middle of the screen looks at {(hit ? $"'{target?.name}' at {point}, {Vector3.Distance(point, GameCamera.Position):0.0} m" : "nothing")}");
        float baseFov = normal.cam.m_Lens.FieldOfView, baseDistance = follow.CameraDistance;
        var baseShoulder = follow.ShoulderOffset;
        string Back() => $"back: fov {normal.cam.m_Lens.FieldOfView:0.00} (was {baseFov:0.00}), distance {follow.CameraDistance:0.00} (was {baseDistance:0.00}), shoulder {follow.ShoulderOffset} (was {baseShoulder})";

        // A wider view, and the distance doubled by a modifier of a higher priority that runs after it.
        var order = new List<string>();
        var wider = GameCamera.AddModifier(view =>
        {
            if (view.IsCurrent && order.Count < 2) order.Add("wider");
            view.FieldOfView += 30;
        });
        var farther = GameCamera.AddModifier(view =>
        {
            if (view.IsCurrent && order.Count < 2) order.Add("farther");
            view.Distance *= 2;
        }, priority: 10);
        yield return 1f;
        Log($"Camera: modifiers in the order {string.Join(", ", order)}; camera fov {GameCamera.FieldOfView:0.0}; {Game()}");
        Shot("camera-modified");
        yield return 2f;
        Log($"Camera: 2 s later (no compounding): {Game()}");
        float zoom = im.TargetZoomLevel;
        im.TargetZoomLevel = 1f;
        yield return 2.5f;
        Log($"Camera: zoomed out by the game with the modifiers on: {Game()}");
        im.TargetZoomLevel = zoom;
        yield return 2.5f;
        Log($"Camera: zoomed back: {Game()}");
        wider.Dispose();
        farther.Dispose();
        yield return 1f;
        Log($"Camera: modifiers disposed: {Back()}; active {wider.IsActive}");

        // A view from the head: no distance, no shoulder, on the walking camera only.
        var head = GameCamera.AddModifier(view =>
        {
            if (view.Mode != CameraMode.Normal) return;
            view.Distance = 0;
            view.ShoulderOffset = Vector3.zero;
        });
        yield return 1.5f;
        Log($"Camera: from the head: camera at {GameCamera.Position}, the player's head at {player.transform.position + Vector3.up * 1.7f}; {Game()}");
        Shot("camera-head");
        yield return 0.5f; // the screenshot is written at the end of a frame
        head.Dispose();
        yield return 1.5f;
        Log($"Camera: from the head disposed: {Back()}");

        // The game's build camera above the others: ModeChanged.
        var modes = new List<string>();
        EventHandler<SDK.ValueChangedEventArgs<CameraMode>> changed = (_, e) => modes.Add($"{e.OldValue}→{e.NewValue}");
        GameCamera.ModeChanged += changed;
        // The game's override adds only to a camera that is on (AddCameraPriorityOverride): the build camera's own priority.
        var build = im.GetCameraObject(InputManager.CameraStates.BUILD)?.cam;
        int buildPriority = build != null ? build.Priority : 0;
        if (build != null) build.Priority = 100;
        yield return 2f;
        Log($"Camera: build camera raised: mode {GameCamera.Mode}, its priority {build?.Priority}, active '{im.brain?.ActiveVirtualCamera?.Name}'");
        Shot("camera-build");
        yield return 0.5f;
        if (build != null) build.Priority = buildPriority;
        yield return 2f;
        GameCamera.ModeChanged -= changed;
        Log($"Camera: build camera back: mode {GameCamera.Mode}; changes [{string.Join(", ", modes)}]");

        // A mod's camera above the player, blended in over a second; a second one of a lower priority under it.
        var brain = im.brain;
        string Active() => $"active '{brain?.ActiveVirtualCamera?.Name}' blending {brain?.IsBlending}, camera at {GameCamera.Position}";
        int ended = 0;
        var above = GameCamera.Take(priority: 5, blend: TimeSpan.FromSeconds(1));
        above.Ended += (_, _) => ended++;
        above.Follow = player.transform;
        above.FollowOffset = new Vector3(0, 18f, -6f);
        above.LookAt = player.transform;
        yield return 0.4f;
        Log($"Camera: a mod's camera taken, 0.4 s: shown {above.IsShown}, {Active()}");
        yield return 2f;
        Log($"Camera: a mod's camera, 2.4 s: {Active()}, its place {above.Position}, the player at {player.transform.position}");
        Shot("camera-above");
        yield return 0.5f;
        var under = GameCamera.Take(priority: 1);
        under.Ended += (_, _) => ended++;
        under.Position = player.transform.position + new Vector3(6f, 2f, 0);
        under.Rotation = Quaternion.LookRotation(player.transform.position - under.Position);
        under.FieldOfView = 30;
        yield return 1f;
        Log($"Camera: a second one of a lower priority: shown above {above.IsShown}, under {under.IsShown}, {Active()}");
        above.Dispose();
        yield return 1.5f;
        Log($"Camera: the higher one disposed: shown under {under.IsShown}, {GameCamera.ShownCamera == under}, fov {GameCamera.FieldOfView:0.0}, {Active()}");
        Shot("camera-under");
        yield return 0.5f;
        under.Dispose();
        yield return 0.3f;
        Log($"Camera: both disposed, 0.3 s: {Active()}, shown {GameCamera.ShownCamera?.ToString() ?? "none"}");
        yield return 2f;
        Log($"Camera: both disposed, 2.3 s: {Active()}; ended {ended} of 2; {Back()}");
        Shot("camera-back");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
