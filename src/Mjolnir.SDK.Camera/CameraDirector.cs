using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mjolnir.SDK.Camera;

/// <summary>
/// The camera model's work each frame, on the main thread. Around the game's own camera update
/// (<see cref="BeforeGame"/>, <see cref="AfterGame"/>): before it, every value a modifier wrote that nothing else changed
/// since goes back to the game's, so the game always works on its own values; after it, the game's values are taken as the
/// base, the modifiers run on each camera in their order, and what they made is written. A value someone else changed in
/// between is the game's new value. Then the mods' cameras are ranked and shown. <see cref="Frame"/> runs every frame, with
/// or without the game's update, and ends the mods' cameras once the world is left.
/// </summary>
internal static class CameraDirector
{
    private const string Source = "SDK.Camera";
    private const float MinimumFieldOfView = 1f;
    private const float MaximumFieldOfView = 179f;

    private static readonly object s_lock = new();
    private static readonly List<CameraModifier> s_modifiers = [];
    private static readonly List<ModCamera> s_cameras = [];
    private static readonly Dictionary<object, RigState> s_rigs = [];
    private static readonly List<CameraModifier> s_running = [];
    private static readonly List<ModCamera> s_ranked = [];
    private static readonly List<ModCamera> s_ending = [];
    private static long s_sequence;
    private static ModCamera? s_shown;
    private static TimeSpan s_lastBlend;

    /// <summary>The game's side; a fake in the unit tests.</summary>
    internal static ICameraSource? Camera { get; set; }

    /// <summary>The game's camera chosen in the last frame with a world, for <see cref="GameCamera.ModeChanged"/>.</summary>
    internal static CameraMode? LastMode { get; private set; }

    internal static ModCamera? Shown => s_shown;

    internal static CameraModifier Add(System.Reflection.Assembly owner, Action<CameraView> modify, int priority)
    {
        lock (s_lock)
        {
            var modifier = new CameraModifier(owner, modify, priority, ++s_sequence);
            s_modifiers.Add(modifier);
            return modifier;
        }
    }

    internal static ModCamera Take(System.Reflection.Assembly owner, int priority, TimeSpan blend)
    {
        lock (s_lock)
        {
            // It starts where the player's camera is: now on the main thread with a world, else on its first frame (Show), in
            // what the mod has not set by then.
            var camera = new ModCamera(owner, priority, blend, ++s_sequence) { FieldOfView = 45f, Unset = ModCamera.Start.All };
            if (Threading.Dispatcher.IsMainThread && Camera is { IsAvailable: true } source)
                camera.StartAt(source.Position, source.Rotation, source.FieldOfView);
            s_cameras.Add(camera);
            return camera;
        }
    }

    internal static void Removed(CameraModifier modifier)
    {
        lock (s_lock) s_modifiers.Remove(modifier);
    }

    /// <summary>Before the game updates its camera: the values the modifiers wrote, and nothing changed since, back to the game's.</summary>
    internal static void BeforeGame()
    {
        if (Camera is not { IsAvailable: true } source) return;
        foreach (var rig in source.Rigs)
        {
            if (!s_rigs.TryGetValue(rig.Key, out var state) || !state.Written) continue;
            state.Written = false;
            if (rig.FieldOfView == state.WrittenFieldOfView) rig.FieldOfView = state.BaseFieldOfView;
            if (rig.Distance == state.WrittenDistance) rig.Distance = state.BaseDistance;
            if (Same(rig.ShoulderOffset, state.WrittenShoulder)) rig.ShoulderOffset = state.BaseShoulder;
        }
    }

    /// <summary>After the game updated its camera: the modifiers on its values, and the mods' cameras ranked and shown.</summary>
    internal static void AfterGame()
    {
        if (Camera is not { IsAvailable: true } source) return;
        var mode = source.Mode;
        var old = LastMode;
        LastMode = mode;
        if (old is { } before && before != mode) GameCamera.RaiseModeChanged(before, mode);
        Modify(source, mode);
        Show(source);
    }

    /// <summary>Every frame: once the world is left (or there is no game side), the mods' cameras end and the rigs are forgotten.</summary>
    internal static void Frame()
    {
        if (Camera is { IsAvailable: true }) return;
        LastMode = null;
        s_rigs.Clear();
        lock (s_lock)
        {
            if (s_cameras.Count == 0) return;
            s_ending.AddRange(s_cameras);
            s_cameras.Clear();
        }
        s_shown = null;
        EndAll();
    }

    private static void Modify(ICameraSource source, CameraMode current)
    {
        lock (s_lock)
        {
            s_running.Clear();
            foreach (var modifier in s_modifiers)
                if (!ModCall.IsDisabled(modifier.Owner)) s_running.Add(modifier);
        }
        if (s_running.Count > 1) s_running.Sort(static (a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.Sequence.CompareTo(b.Sequence));
        foreach (var rig in source.Rigs)
        {
            if (!s_rigs.TryGetValue(rig.Key, out var state)) s_rigs[rig.Key] = state = new RigState(rig.Mode);
            if (s_running.Count == 0) continue;
            state.BaseFieldOfView = rig.FieldOfView;
            state.BaseDistance = rig.Distance;
            state.BaseShoulder = rig.ShoulderOffset;
            var view = state.View;
            view.IsCurrent = rig.Mode == current;
            view.FieldOfView = state.BaseFieldOfView;
            view.Distance = state.BaseDistance;
            view.ShoulderOffset = state.BaseShoulder;
            foreach (var modifier in s_running)
                ModCall.Run(modifier.Owner, "A camera modifier", modifier.Modify, view);
            float fieldOfView = float.IsFinite(view.FieldOfView) ? Math.Clamp(view.FieldOfView, MinimumFieldOfView, MaximumFieldOfView) : state.BaseFieldOfView;
            float distance = float.IsFinite(view.Distance) ? Math.Max(0f, view.Distance) : state.BaseDistance;
            var shoulder = Finite(view.ShoulderOffset) ? view.ShoulderOffset : state.BaseShoulder;
            if (fieldOfView != state.BaseFieldOfView) rig.FieldOfView = fieldOfView;
            if (distance != state.BaseDistance) rig.Distance = distance;
            if (!Same(shoulder, state.BaseShoulder)) rig.ShoulderOffset = shoulder;
            // What the rig holds now, read back: the game's side may round a value it stores.
            state.WrittenFieldOfView = rig.FieldOfView;
            state.WrittenDistance = rig.Distance;
            state.WrittenShoulder = rig.ShoulderOffset;
            state.Written = true;
        }
    }

    private static void Show(ICameraSource source)
    {
        lock (s_lock)
        {
            for (int i = s_cameras.Count - 1; i >= 0; i--)
            {
                if (s_cameras[i].IsActive) continue;
                s_ending.Add(s_cameras[i]);
                s_cameras.RemoveAt(i);
            }
            s_ranked.Clear();
            s_ranked.AddRange(s_cameras);
        }
        if (s_ranked.Count > 1) s_ranked.Sort(static (a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.Sequence.CompareTo(b.Sequence));
        foreach (var camera in s_ranked)
            if (camera.Unset != ModCamera.Start.None) camera.StartAt(source.Position, source.Rotation, source.FieldOfView);
        var shown = s_ranked.Count > 0 ? s_ranked[^1] : null;
        TimeSpan? blend = null;
        if (!ReferenceEquals(shown, s_shown))
        {
            // Into a camera with its own blend; back to the game's with the blend of the camera that was shown last.
            blend = shown?.Blend ?? s_lastBlend;
            if (s_shown != null) s_shown.IsShown = false;
            s_shown = shown;
            if (shown != null)
            {
                shown.IsShown = true;
                s_lastBlend = shown.Blend;
            }
        }
        try
        {
            source.ShowCameras(s_ranked, blend);
        }
        catch (Exception e)
        {
            SdkLog.Error(Source, $"Showing the mods' cameras failed: {e}");
        }
        EndAll();
    }

    private static void EndAll()
    {
        foreach (var camera in s_ending) camera.End();
        s_ending.Clear();
    }

    private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    /// <summary>Forgets everything, between unit tests.</summary>
    internal static void ResetForTests()
    {
        lock (s_lock)
        {
            s_modifiers.Clear();
            s_cameras.Clear();
        }
        s_rigs.Clear();
        s_running.Clear();
        s_ranked.Clear();
        s_ending.Clear();
        s_sequence = 0;
        s_shown = null;
        s_lastBlend = TimeSpan.Zero;
        LastMode = null;
        Camera = null;
    }

    private sealed class RigState(CameraMode mode)
    {
        public readonly CameraView View = new(mode);
        public bool Written;
        public float BaseFieldOfView;
        public float BaseDistance;
        public Vector3 BaseShoulder;
        public float WrittenFieldOfView;
        public float WrittenDistance;
        public Vector3 WrittenShoulder;
    }
}
