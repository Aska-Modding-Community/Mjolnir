using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cinemachine;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "camera research" scenario: how the game's camera is made, for a camera module of the SDK. In the world (cameras are
/// spawned with the player) it writes shots/camera.txt: the Cinemachine brain (update method, blends, the camera live now), the
/// output camera (lens, clip planes, HDRP data), every camera state of <c>InputManager</c> (its virtual camera, priorities, lens,
/// the third-person follow's offsets, distance and collision, its <c>CameraConfig</c>, its extensions and what it follows), the
/// zoom and rotation fields, the camera settings, and the volumes and their overrides. Then it tries what a mod would do, each
/// with a screenshot and the camera read back, and puts each back: the field of view through the game's own method, the zoom,
/// the shoulder offset, the distance at 0 (a first-person view), a virtual camera of its own above the player at a higher
/// priority (a top-down view), and the brain switched off with the camera moved by hand (a free camera). It loads the save: back
/// it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> CameraResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var im = GameManagers.Get<InputManager>();
        var brain = im?.brain;
        var camera = im?._playerCamera ?? Camera.main;
        if (im == null || brain == null || camera == null)
        {
            Log($"Camera: missing: input manager {im != null}, brain {brain != null}, camera {camera != null}");
            yield break;
        }

        var text = new StringBuilder();
        text.AppendLine($"brain on '{brain.name}': update {brain.m_UpdateMethod}, blend update {brain.m_BlendUpdateMethod}, default blend {brain.m_DefaultBlend.m_Style} {brain.m_DefaultBlend.m_Time}s, custom blends '{brain.m_CustomBlends?.name}', active '{brain.ActiveVirtualCamera?.Name}', blending {brain.IsBlending}");
        text.AppendLine($"camera '{camera.name}' (main {camera == Camera.main}): fov {camera.fieldOfView} near {camera.nearClipPlane} far {camera.farClipPlane} depth {camera.depth} culling {camera.cullingMask} components [{string.Join(", ", camera.GetComponents<Component>().Select(c => c.GetIl2CppType().Name))}]");
        text.AppendLine($"all cameras: {string.Join(", ", Camera.allCameras.Select(c => $"{c.name} (enabled {c.enabled}, depth {c.depth}, target {(c.targetTexture != null ? c.targetTexture.name : "screen")})"))}");
        text.AppendLine($"zoom: target {im.TargetZoomLevel} current {im._currentGlobalZoom} target global {im._currentGlobalTargetZoom} speed {im.cameraZoomSpeed}; rotation speed {im.cameraRotationSpeed} target rotation {im.targetRotation} look scale {im.LookScale}; max priority {im._maxCamPrio}; fov delta {im.fovFloat}");
        foreach (var state in Enum.GetValues<InputManager.CameraStates>())
        {
            var o = im.GetCameraObject(state);
            if (o?.cam == null)
            {
                text.AppendLine($"{state}: none");
                continue;
            }
            var v = o.cam;
            var f = o.baseComp;
            var c = o.config;
            text.AppendLine($"{state}: '{v.name}' priority {v.Priority} (base {o.iPriority}, override {o.overridePriorty}) initial fov {o.initialFOV} lens fov {v.m_Lens.FieldOfView} near {v.m_Lens.NearClipPlane} far {v.m_Lens.FarClipPlane} dutch {v.m_Lens.Dutch}; follow '{v.Follow?.name}' look at '{v.LookAt?.name}' active {v.isActiveAndEnabled}");
            if (f != null)
                text.AppendLine($"    follow: shoulder {f.ShoulderOffset} arm {f.VerticalArmLength} side {f.CameraSide} distance {f.CameraDistance} radius {f.CameraRadius} damping {f.Damping} collision {f.CameraCollisionFilter.value} into {f.DampingIntoCollision} from {f.DampingFromCollision}");
            if (c != null)
                text.AppendLine($"    config: offset {c.offset} zoom {c.zoomLimits} damping {c.damping} distance {c.initialCamDistance} accel {c.inputAcceleration}/{c.inputDeceleration} rotation {c.rotationSpeed} pad x{c.controllerRotSpeedMultiplier} zoomed {c.affectedByZoom} inherit {c.inheritZoom} dolly {c.dollyVelocityPullFactor}/{c.maxDollyDistance}");
            text.AppendLine($"    components [{string.Join(", ", v.GetComponentsInChildren<Component>(true).Where(x => x != null).Select(x => x.GetIl2CppType().Name).Distinct())}]");
        }
        var settings = GameManagers.Get<SettingsManager>();
        if (settings != null)
            text.AppendLine($"settings: shake {settings.CameraShakeSettings?.Value} fov normal {settings.CameraNormalFOVSetting?.Value} aim {settings.CameraAimFOVSetting?.Value} inside {settings.CameraInsideFOVSetting?.Value} fly {settings.CameraFlyFOVSetting?.Value} hud filter {settings.HUDFilterSetting?.Value}");
        foreach (var volume in Object.FindObjectsOfType<UnityEngine.Rendering.Volume>())
        {
            var overrides = new List<string>();
            var components = volume.sharedProfile?.components;
            for (int i = 0; components != null && i < components.Count; i++)
                if (components[i] != null) overrides.Add($"{components[i].GetIl2CppType().Name}{(components[i].active ? "" : " (off)")}");
            text.AppendLine($"volume '{volume.name}' global {volume.isGlobal} priority {volume.priority} weight {volume.weight} profile '{volume.sharedProfile?.name}': {string.Join(", ", overrides)}");
        }
        File.WriteAllText(Path.Combine(ShotsDir, "camera.txt"), text.ToString());
        Log($"Camera: written; active '{brain.ActiveVirtualCamera?.Name}', fov {camera.fieldOfView}");
        Shot("camera-base");

        string Read() => $"active '{brain.ActiveVirtualCamera?.Name}' blending {brain.IsBlending}, camera at {camera.transform.position} fov {camera.fieldOfView:0.0}";
        var normal = im.GetCameraObject(InputManager.CameraStates.NORMAL);
        var follow = normal?.baseComp;

        // The field of view through the game's own method (what its settings use).
        float fov = settings?.CameraNormalFOVSetting?.Value ?? 60f;
        im.SetCamerasFieldOfView(90f, new[] { InputManager.CameraStates.NORMAL });
        yield return 1f;
        Log($"Camera: fov 90: {Read()}");
        Shot("camera-fov90");
        yield return 0.5f; // the screenshot is written at the end of a frame
        im.SetCamerasFieldOfView(fov, new[] { InputManager.CameraStates.NORMAL });

        // The zoom: the game's target level at its two ends.
        float zoom = im.TargetZoomLevel;
        foreach (float level in new[] { 0f, 1f })
        {
            im.TargetZoomLevel = level;
            yield return 2f;
            Log($"Camera: zoom {level}: distance {follow?.CameraDistance}, {Read()}");
            Shot($"camera-zoom{level}");
        }
        im.TargetZoomLevel = zoom;
        yield return 1.5f;

        if (follow != null)
        {
            // The shoulder offset, then the distance at 0: what over-the-shoulder and first-person mods change.
            var shoulder = follow.ShoulderOffset;
            follow.ShoulderOffset = shoulder + new Vector3(0.8f, 0.2f, 0f);
            yield return 1.5f;
            Log($"Camera: shoulder {follow.ShoulderOffset}: {Read()}");
            Shot("camera-shoulder");
            yield return 0.5f; // the screenshot is written at the end of a frame
            follow.ShoulderOffset = shoulder;

            float distance = follow.CameraDistance;
            var limits = normal!.config != null ? normal.config.zoomLimits : default;
            if (normal.config != null) normal.config.zoomLimits = Vector2.zero;
            follow.CameraDistance = 0f;
            yield return 1.5f;
            Log($"Camera: distance 0 (zoom limits {limits} → 0): distance now {follow.CameraDistance}, {Read()}");
            Shot("camera-firstperson");
            yield return 0.5f; // the screenshot is written at the end of a frame
            if (normal.config != null) normal.config.zoomLimits = limits;
            follow.CameraDistance = distance;
            yield return 1.5f;
        }

        // A virtual camera of the mod's own above the player, at a higher priority: does the brain take it, and how does the
        // game's own camera code bear it?
        var player = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var holder = new GameObject("Mjolnir UiLab camera");
        var own = holder.AddComponent<CinemachineVirtualCamera>();
        if (player != null)
        {
            holder.transform.position = player.transform.position + new Vector3(0, 18f, -6f);
            holder.transform.LookAt(player.transform.position);
        }
        own.m_Lens = normal?.cam?.m_Lens ?? LensSettings.Default;
        own.Priority = 1000;
        yield return 3f;
        Log($"Camera: a mod's camera at priority 1000: {Read()}");
        Shot("camera-own");
        yield return 0.5f; // the screenshot is written at the end of a frame
        Object.Destroy(holder);
        yield return 3f;
        Log($"Camera: the mod's camera gone: {Read()}");

        // The brain off, the camera moved by hand: a free camera.
        brain.enabled = false;
        var at = camera.transform.position;
        camera.transform.position = at + new Vector3(0, 6f, 0);
        camera.transform.rotation = Quaternion.LookRotation(Vector3.down + camera.transform.forward * 0.5f);
        yield return 1f;
        Log($"Camera: brain off, moved: camera at {camera.transform.position} (moved {Vector3.Distance(at, camera.transform.position):0.0} m)");
        Shot("camera-free");
        yield return 0.5f; // the screenshot is written at the end of a frame
        brain.enabled = true;
        yield return 2f;
        Log($"Camera: brain on again: {Read()}");
        Shot("camera-back");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
