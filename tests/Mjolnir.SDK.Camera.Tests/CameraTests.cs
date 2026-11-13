using System;
using System.Collections.Generic;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;

namespace Mjolnir.SDK.Camera.Tests;

/// <summary>
/// The camera model on fake game cameras: reading it with and without a world; modifiers on every camera, in their order,
/// adding up, kept in range, the game's values back once they go and a value the game changed taken as its own; which
/// camera the game chose and its change; the mods' cameras where the player's is, ranked, shown, blended in and out, and
/// ended by a dispose or the world left.
/// </summary>
[Collection("Camera")]
public sealed class CameraTests : IDisposable
{
    private readonly FakeCamera _game = new();

    public CameraTests()
    {
        Dispatcher.ResetForTests();
        GameCamera.ResetForTests();
        CameraDirector.Camera = _game;
    }

    public void Dispose()
    {
        GameCamera.ResetForTests();
        Dispatcher.ResetForTests();
    }

    // One frame as the game runs it: the per-frame check, then the game's camera update wrapped by the patch.
    private void Frame(Action? game = null)
    {
        CameraDirector.Frame();
        CameraDirector.BeforeGame();
        game?.Invoke();
        CameraDirector.AfterGame();
    }

    private static Vector3 V(float x, float y, float z)
    {
        Vector3 v = default;
        v.x = x;
        v.y = y;
        v.z = z;
        return v;
    }

    private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    [Fact]
    public void WithoutAWorldTheCameraReadsNothing()
    {
        _game.IsAvailable = false;
        Assert.False(GameCamera.IsAvailable);
        Assert.Equal(0f, GameCamera.FieldOfView);
        Assert.Equal(-1f, GameCamera.WorldToScreen(default).z);
        Assert.False(GameCamera.TryRaycast(100, out _, out var target));
        Assert.Null(target);
        CameraDirector.Camera = null;
        Assert.False(GameCamera.IsAvailable);
    }

    [Fact]
    public void TheCameraIsReadFromTheGame()
    {
        Assert.True(GameCamera.IsAvailable);
        Assert.True(Same(_game.Position, GameCamera.Position));
        Assert.Equal(45f, GameCamera.FieldOfView);
        Assert.Equal(CameraMode.Normal, GameCamera.Mode);
        Assert.Equal(7f, GameCamera.WorldToScreen(default).z);
    }

    [Fact]
    public void AnotherThreadGetsWhatTheMainThreadSawLast()
    {
        Dispatcher.Tick(); // this thread is the main one now
        Assert.Equal(45f, GameCamera.FieldOfView);
        _game.FieldOfViewNow = 60f;
        float seen = 0;
        var other = new System.Threading.Thread(() => seen = GameCamera.FieldOfView);
        other.Start();
        other.Join();
        Assert.Equal(45f, seen);
    }

    [Fact]
    public void AModifierChangesEveryCameraAndTheGamesValuesComeBack()
    {
        var modifier = GameCamera.AddModifier(view => view.FieldOfView += 10);
        Frame();
        Assert.Equal(55f, _game.Normal.FieldOfView);
        Assert.Equal(45f, _game.Aim.FieldOfView); // its own 35, plus 10
        Frame(() => Assert.Equal(45f, _game.Normal.FieldOfView)); // the game works on its own value
        Assert.Equal(55f, _game.Normal.FieldOfView);
        modifier.Dispose();
        Assert.False(modifier.IsActive);
        Frame();
        Assert.Equal(45f, _game.Normal.FieldOfView);
        Assert.Equal(35f, _game.Aim.FieldOfView);
        modifier.Dispose();
    }

    [Fact]
    public void ModifiersRunByPriorityAndAddUp()
    {
        var order = new List<string>();
        GameCamera.AddModifier(view => { if (view.Mode == CameraMode.Normal) order.Add("late"); view.FieldOfView *= 2; }, priority: 10);
        GameCamera.AddModifier(view => { if (view.Mode == CameraMode.Normal) order.Add("first"); view.FieldOfView += 5; });
        GameCamera.AddModifier(view => { if (view.Mode == CameraMode.Normal) order.Add("second"); view.FieldOfView += 5; });
        Frame();
        Assert.Equal(["first", "second", "late"], order);
        Assert.Equal(110f, _game.Normal.FieldOfView);
    }

    [Fact]
    public void ModifiersNeverCompoundFromFrameToFrame()
    {
        GameCamera.AddModifier(view => view.Distance *= 2);
        for (int i = 0; i < 5; i++) Frame();
        Assert.Equal(4.2f, _game.Normal.Distance);
    }

    [Fact]
    public void AValueTheGameChangedIsItsNewValue()
    {
        GameCamera.AddModifier(view => view.Distance += 1);
        Frame();
        Assert.Equal(3.1f, _game.Normal.Distance, 0.0001f);
        _game.Normal.Distance = 10f; // the zoom, between two updates
        Frame();
        Assert.Equal(11f, _game.Normal.Distance);
        Frame(() => _game.Normal.Distance = 5f); // the zoom, in its update
        Assert.Equal(6f, _game.Normal.Distance);
    }

    [Fact]
    public void ValuesAreKeptInRange()
    {
        GameCamera.AddModifier(view =>
        {
            view.FieldOfView = view.Mode == CameraMode.Normal ? 500 : float.NaN;
            view.Distance = -3;
            view.ShoulderOffset = V(float.PositiveInfinity, 0, 0);
        });
        Frame();
        Assert.Equal(179f, _game.Normal.FieldOfView);
        Assert.Equal(35f, _game.Aim.FieldOfView);
        Assert.Equal(0f, _game.Normal.Distance);
        Assert.True(Same(V(-0.5f, 0, 0), _game.Normal.ShoulderOffset));
    }

    [Fact]
    public void TheShoulderIsModifiedAndGivenBack()
    {
        var modifier = GameCamera.AddModifier(view => view.ShoulderOffset = V(0.5f, 0.2f, 0));
        Frame();
        Assert.True(Same(V(0.5f, 0.2f, 0), _game.Normal.ShoulderOffset));
        modifier.Dispose();
        Frame();
        Assert.True(Same(V(-0.5f, 0, 0), _game.Normal.ShoulderOffset));
    }

    [Fact]
    public void AThrowingModifierDoesNotStopTheOthers()
    {
        GameCamera.AddModifier(_ => throw new InvalidOperationException("a broken mod"));
        GameCamera.AddModifier(view => view.FieldOfView += 1, priority: 1);
        Frame();
        Assert.Equal(46f, _game.Normal.FieldOfView);
    }

    [Fact]
    public void AModifierKnowsWhichCameraTheGameChose()
    {
        var current = new List<CameraMode>();
        GameCamera.AddModifier(view => { if (view.IsCurrent) current.Add(view.Mode); });
        Frame();
        _game.ModeNow = CameraMode.Aim;
        Frame();
        Assert.Equal([CameraMode.Normal, CameraMode.Aim], current);
    }

    [Fact]
    public void AChangeOfTheGamesCameraIsAnnouncedButNotTheWorldsLoad()
    {
        var changes = new List<(CameraMode, CameraMode)>();
        GameCamera.ModeChanged += (sender, e) =>
        {
            Assert.Null(sender);
            changes.Add((e.OldValue, e.NewValue));
        };
        Frame();
        Frame();
        _game.ModeNow = CameraMode.Build;
        Frame();
        Frame();
        _game.IsAvailable = false;
        Frame();
        _game.IsAvailable = true;
        _game.ModeNow = CameraMode.Normal;
        Frame();
        Assert.Equal([(CameraMode.Normal, CameraMode.Build)], changes);
        Assert.Equal(CameraMode.Normal, GameCamera.Mode);
    }

    [Fact]
    public void AModsCameraStartsWhereThePlayersIs()
    {
        var camera = GameCamera.Take();
        Assert.True(camera.IsActive);
        Assert.False(camera.IsShown);
        Frame();
        Assert.True(camera.IsShown);
        Assert.Same(camera, GameCamera.ShownCamera);
        Assert.True(Same(_game.Position, camera.Position));
        Assert.Equal(45f, camera.FieldOfView);
        Assert.Equal([camera], _game.Shown);
        Assert.Equal(TimeSpan.Zero, _game.Blends[^1]);
    }

    [Fact]
    public void WhatTheModSetsAfterTakingItIsKept()
    {
        var camera = GameCamera.Take();
        camera.Position = V(1, 2, 3);
        camera.FieldOfView = 30;
        Frame();
        Assert.True(Same(V(1, 2, 3), camera.Position));
        Assert.Equal(30f, camera.FieldOfView);
    }

    [Fact]
    public void ACameraTakenOffTheMainThreadStartsWhereThePlayersIsInWhatTheModLeft()
    {
        Dispatcher.Tick(); // this thread is the main one now
        ModCamera? camera = null;
        var other = new System.Threading.Thread(() =>
        {
            camera = GameCamera.Take();
            camera.FieldOfView = 70;
        });
        other.Start();
        other.Join();
        Assert.Equal(70f, camera!.FieldOfView);
        Frame();
        Assert.True(Same(_game.Position, camera.Position));
        Assert.Equal(70f, camera.FieldOfView);
    }

    [Fact]
    public void TheHighestPriorityIsShownAndOfTwoAlikeTheNewer()
    {
        var low = GameCamera.Take(priority: 1);
        var high = GameCamera.Take(priority: 5, blend: TimeSpan.FromSeconds(2));
        var alike = GameCamera.Take(priority: 1);
        Frame();
        Assert.Equal([low, alike, high], _game.Shown);
        Assert.True(high.IsShown);
        Assert.False(low.IsShown);
        Assert.Equal(TimeSpan.FromSeconds(2), _game.Blends[^1]);
        high.Dispose();
        Frame();
        Assert.Equal([low, alike], _game.Shown);
        Assert.Same(alike, GameCamera.ShownCamera);
        Assert.Equal(TimeSpan.Zero, _game.Blends[^1]);
    }

    [Fact]
    public void ABlendIsAskedForOnlyWhenTheCameraShownChanges()
    {
        GameCamera.Take(blend: TimeSpan.FromSeconds(1));
        Frame();
        Frame();
        Frame();
        Assert.Equal([TimeSpan.FromSeconds(1)], _game.Blends);
    }

    [Fact]
    public void TheViewGoesBackWithTheBlendOfTheCameraShownLast()
    {
        var camera = GameCamera.Take(blend: TimeSpan.FromSeconds(1.5));
        int ended = 0;
        camera.Ended += (sender, _) =>
        {
            Assert.Same(camera, sender);
            ended++;
        };
        Frame();
        camera.Dispose();
        Assert.False(camera.IsActive);
        Assert.Equal(0, ended); // ended on the next frame, on the main thread
        Frame();
        Frame();
        camera.Dispose();
        Assert.Equal(1, ended);
        Assert.False(camera.IsShown);
        Assert.Null(GameCamera.ShownCamera);
        Assert.Empty(_game.Shown);
        Assert.Equal([TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(1.5)], _game.Blends);
    }

    [Fact]
    public void LeavingTheWorldEndsTheModsCameras()
    {
        var camera = GameCamera.Take();
        int ended = 0;
        camera.Ended += (_, _) => ended++;
        Frame();
        _game.IsAvailable = false;
        Frame();
        Frame();
        Assert.Equal(1, ended);
        Assert.False(camera.IsActive);
        Assert.Null(GameCamera.ShownCamera);
        _game.IsAvailable = true;
        Frame();
        Assert.Empty(_game.Shown);
    }

    [Fact]
    public void ACameraTakenWithoutAWorldEndsOnTheNextFrame()
    {
        _game.IsAvailable = false;
        var camera = GameCamera.Take();
        int ended = 0;
        camera.Ended += (_, _) => ended++;
        Assert.True(camera.IsActive);
        Frame();
        Assert.Equal(1, ended);
        Assert.False(camera.IsActive);
    }

    [Fact]
    public void BadArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => GameCamera.AddModifier(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameCamera.Take(blend: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void TheModifierAndTheCameraNameTheirMod()
    {
        Assert.Contains("Mjolnir.SDK.Camera.Tests", GameCamera.AddModifier(_ => { }, 3).ToString());
        Assert.Contains("priority 4", GameCamera.Take(4).ToString());
    }

    private sealed class FakeRig(CameraMode mode, float fieldOfView, float distance, Vector3 shoulder) : ICameraRig
    {
        public object Key { get; } = new();

        public CameraMode Mode { get; } = mode;

        public float FieldOfView { get; set; } = fieldOfView;

        public float Distance { get; set; } = distance;

        public Vector3 ShoulderOffset { get; set; } = shoulder;
    }

    private sealed class FakeCamera : ICameraSource
    {
        public FakeCamera()
        {
            Normal = new FakeRig(CameraMode.Normal, 45f, 2.1f, V(-0.5f, 0, 0));
            Aim = new FakeRig(CameraMode.Aim, 35f, 1.5f, V(-0.6f, 0.1f, 0));
            Rigs = [Normal, Aim];
            Position = V(10, 20, 30);
        }

        public FakeRig Normal { get; }

        public FakeRig Aim { get; }

        public bool IsAvailable { get; set; } = true;

        public CameraMode ModeNow { get; set; } = CameraMode.Normal;

        public float FieldOfViewNow { get; set; } = 45f;

        public List<ModCamera> Shown { get; } = [];

        public List<TimeSpan> Blends { get; } = [];

        public Vector3 Position { get; }

        public Quaternion Rotation => default;

        public Vector3 Forward => default;

        public float FieldOfView => FieldOfViewNow;

        public CameraMode Mode => ModeNow;

        public IReadOnlyList<ICameraRig> Rigs { get; }

        public Ray MiddleRay => default;

        public Vector3 WorldToScreen(Vector3 world) => V(world.x, world.y, 7f);

        public Ray ScreenPointToRay(Vector2 screen) => default;

        public bool Raycast(Ray ray, float maxDistance, out Vector3 point, out GameObject? target)
        {
            point = default;
            target = null;
            return false;
        }

        public void ShowCameras(IReadOnlyList<ModCamera> ranked, TimeSpan? blend)
        {
            Shown.Clear();
            Shown.AddRange(ranked);
            if (blend is { } time) Blends.Add(time);
        }
    }
}
