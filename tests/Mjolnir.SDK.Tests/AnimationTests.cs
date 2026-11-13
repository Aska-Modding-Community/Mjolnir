using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mjolnir.Core.Mods;
using Mjolnir.SDK.Threading;
using UnityEngine;
using Xunit;

namespace Mjolnir.SDK.Tests;

internal sealed class FakeAnimationGame : IAnimationSource
{
    public readonly Dictionary<nint, FakeAnimatedCharacter> Characters = [];
    public List<GameClip> Clips = [new("aska_wave", 3.33f), new("aska_idle", 3.67f), new("aska_wave", 1f)];
    public readonly List<(nint Key, string Animation)> Announced = [];
    public readonly List<nint> StopsAnnounced = [];

    IReadOnlyList<GameClip> IAnimationSource.Clips => Clips;

    public IAnimatedCharacter? Character(nint key) => Characters.GetValueOrDefault(key);

    public bool Announce(nint key, AnimationInfo animation, AnimationOptions options)
    {
        Announced.Add((key, animation.FullName));
        return true;
    }

    public void AnnounceStop(nint key) => StopsAnnounced.Add(key);
}

internal sealed class FakeAnimatedCharacter : IAnimatedCharacter
{
    public bool IsAlive { get; set; } = true;
    public string Rig { get; set; } = "aska";
    public bool Refuse;
    public readonly List<FakeClipPlayer> Players = [];
    public readonly List<Emote> Emotes = [];
    public readonly List<bool> RootMotion = [];
    public float MoveSpeed = 1;

    public IClipPlayer? Begin(AnimationInfo animation, AnimationMask? mask, bool loop, double speed)
    {
        if (Refuse) return null;
        var player = new FakeClipPlayer(animation, mask, loop) { Speed = speed };
        Players.Add(player);
        return player;
    }

    public bool PlayEmote(Emote emote)
    {
        Emotes.Add(emote);
        return emote != Emote.Skal;
    }

    public void SetRootMotion(bool hold) => RootMotion.Add(hold);

    public void SetMoveSpeed(float factor) => MoveSpeed = factor;

    public Transform? Bone(string name) => null;
}

internal sealed class FakeClipPlayer(AnimationInfo animation, AnimationMask? mask, bool loop) : IClipPlayer
{
    public AnimationInfo Animation { get; } = animation;
    public AnimationMask? Mask { get; } = mask;
    public bool Loop { get; } = loop;
    public double Time { get; set; }
    public double Speed { get; set; }
    public float Weight { get; set; }
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

/// <summary>
/// Characters' animations on a fake game: one animator per character, an animation coming in and going out, its end, a stop,
/// another animation taking over, moments of a clip, a loop, root motion and walking held while it plays, a character that
/// is not there or is gone, another player's animation, the catalog, and a call from another thread.
/// </summary>
[Collection("Stats")]
public sealed class AnimationTests : IDisposable
{
    private readonly FakeAnimationGame _game = new();
    private readonly FakeAnimatedCharacter _hero = new();
    private double _now = 100;

    public AnimationTests()
    {
        Dispatcher.ResetForTests();
        Animations.ResetForTests();
        Animations.Game = _game;
        Animations.Clock = () => _now;
        _game.Characters[7] = _hero;
    }

    public void Dispose()
    {
        Animations.ResetForTests();
        Animations.Game = null;
        Animations.Clock = () => 0;
        Dispatcher.ResetForTests();
    }

    private AnimationInfo Wave => Animations.Find("aska_wave")!;

    // One frame: the clip advances by its speed, the clock by the same seconds.
    private void Frame(double seconds = 0.1)
    {
        _now += seconds;
        foreach (var player in _hero.Players.Where(p => !p.Disposed)) player.Time += seconds * player.Speed;
        Animations.Tick();
    }

    [Fact]
    public void OneAnimatorPerCharacterAndAnEmptyOneWithoutIt()
    {
        Assert.Same(CharacterAnimator.Of(7), CharacterAnimator.Of(7));
        Assert.True(CharacterAnimator.Of(7).IsAvailable);
        Assert.Equal("aska", CharacterAnimator.Of(7).Rig);
        Assert.False(CharacterAnimator.Of(0).IsAvailable);
        Assert.Equal("", CharacterAnimator.Of(0).Rig);
        Assert.False(CharacterAnimator.Of(9).IsAvailable);
    }

    [Fact]
    public void AnAnimationComesInPlaysAndGivesTheCharacterBack()
    {
        var animator = CharacterAnimator.Of(7);
        var playback = animator.Play(Wave);
        bool? stopped = null;
        playback.Ended += (_, e) => stopped = e.WasStopped;
        Assert.Same(playback, animator.Current);
        Assert.Equal([(7, "aska_wave")], _game.Announced);

        Animations.Tick();
        var player = Assert.Single(_hero.Players);
        Assert.Null(player.Mask);
        Assert.Equal(0, player.Weight);
        Frame();
        Assert.Equal(0.5f, player.Weight, 3); // 0.1 s of a 0.2 s fade in
        Frame();
        Assert.Equal(1f, player.Weight, 3);
        Assert.Equal(0f, _hero.MoveSpeed); // the whole body: the player stands

        for (int i = 0; i < 30; i++) Frame(); // 3.2 s: the fade out starts 0.2 s before the clip's end
        Frame();
        Assert.True(player.Weight < 1);
        Assert.True(playback.IsPlaying);
        for (int i = 0; i < 3; i++) Frame();
        Assert.False(playback.IsPlaying);
        Assert.True(player.Disposed);
        Assert.False(stopped);
        Assert.Equal(1f, _hero.MoveSpeed);
        Assert.Null(animator.Current);
    }

    [Fact]
    public void StoppingFadesOutAndTellsTheOthers()
    {
        var playback = CharacterAnimator.Of(7).Play(Wave, new AnimationOptions { FadeIn = TimeSpan.Zero, FadeOut = TimeSpan.FromSeconds(0.4) });
        bool? stopped = null;
        playback.Ended += (_, e) => stopped = e.WasStopped;
        Frame();
        var player = _hero.Players[0];
        Assert.Equal(1f, player.Weight);
        playback.Stop();
        Assert.Equal([7], _game.StopsAnnounced);
        Frame(); // the fade out starts
        Frame();
        Frame();
        Assert.Equal(0.5f, player.Weight, 3);
        Frame();
        Frame();
        Assert.False(playback.IsPlaying);
        Assert.True(stopped);
        playback.Stop(); // nothing once ended
        Assert.Single(_game.StopsAnnounced);
    }

    [Fact]
    public void ANewAnimationTakesOverFromTheOnePlaying()
    {
        var animator = CharacterAnimator.Of(7);
        var first = animator.Play(Wave);
        Frame();
        var second = animator.Play(Animations.Find("aska_idle")!);
        Assert.Same(second, animator.Current);
        bool? firstStopped = null;
        first.Ended += (_, e) => firstStopped = e.WasStopped;
        for (int i = 0; i < 4; i++) Frame();
        Assert.True(firstStopped);
        Assert.True(second.IsPlaying);
        Assert.Equal(2, _hero.Players.Count);
    }

    [Fact]
    public void AMomentOfTheClipRunsOnceOrOnEachRound()
    {
        int once = 0, each = 0;
        var playback = CharacterAnimator.Of(7).Play(Wave).At(TimeSpan.FromSeconds(1), () => once++);
        for (int i = 0; i < 15; i++) Frame();
        Assert.Equal(1, once);
        playback.Stop();
        for (int i = 0; i < 3; i++) Frame();

        CharacterAnimator.Of(7).Play(Wave, new AnimationOptions { Loop = true }).At(TimeSpan.FromSeconds(1), () => each++);
        for (int i = 0; i < 80; i++) Frame(); // 8 s: rounds of 3.33 s pass 1 s at 1, 4.33 and 7.66
        Assert.Equal(3, each);
        Assert.Equal(1, once);
    }

    [Fact]
    public void AMomentAtTheStartRunsOnTheFirstRoundOfALoopToo()
    {
        int starts = 0;
        CharacterAnimator.Of(7).Play(Wave, new AnimationOptions { Loop = true }).At(TimeSpan.Zero, () => starts++);
        for (int i = 0; i < 80; i++) Frame(); // 8 s: rounds of 3.33 s begin at 0, 3.33 and 6.66
        Assert.Equal(3, starts);
    }

    [Fact]
    public void AThrowingMomentBlamesTheModThatSetIt()
    {
        // The platform plays it (a module's job step), a mod's code sets the moment: the mod is the one disabled.
        var mod = new FakeMod("Animation.MomentOwner");
        var playback = CharacterAnimator.Of(7).Play(Wave);
        using (ModFaults.Enter(mod.Assembly)) playback.At(TimeSpan.FromSeconds(1), () => throw new InvalidOperationException("moment fault"));
        for (int i = 0; i < 15; i++) Frame();
        Assert.True(ModCall.IsDisabled(mod.Assembly));
        Assert.False(ModCall.IsDisabled(typeof(Mod).Assembly));
    }

    [Fact]
    public void ALoopPlaysForItsDurationAndAMaskLeavesTheLegsWalking()
    {
        var playback = CharacterAnimator.Of(7).Play(Wave, new AnimationOptions { Loop = true, Duration = TimeSpan.FromSeconds(5), Mask = AnimationMask.UpperBody });
        Frame();
        var player = _hero.Players[0];
        Assert.True(player.Loop);
        Assert.Same(AnimationMask.UpperBody, player.Mask);
        for (int i = 0; i < 10; i++) Frame();
        Assert.Equal(1f, _hero.MoveSpeed);
        for (int i = 0; i < 38; i++) Frame(); // 4.9 s
        Assert.True(playback.IsPlaying);
        for (int i = 0; i < 4; i++) Frame();
        Assert.False(playback.IsPlaying);
    }

    [Fact]
    public void RootMotionIsHeldWhilePlayingAndGivenBack()
    {
        CharacterAnimator.Of(7).Play(Wave, new AnimationOptions { RootMotion = true, MoveSpeed = 0.3f });
        for (int i = 0; i < 5; i++) Frame();
        Assert.All(_hero.RootMotion, Assert.True);
        Assert.Equal(0.3f, _hero.MoveSpeed, 3);
        for (int i = 0; i < 40; i++) Frame();
        Assert.False(_hero.RootMotion[^1]);
        Assert.Equal(1f, _hero.MoveSpeed);
    }

    [Fact]
    public void AnotherPlayersAnimationIsShownButNeverSentBackOrMoved()
    {
        var animator = CharacterAnimator.Of(7);
        var playback = animator.PlayRemote(Wave, new AnimationOptions { RootMotion = true });
        for (int i = 0; i < 5; i++) Frame();
        Assert.Empty(_game.Announced);
        Assert.Empty(_hero.RootMotion);
        Assert.Equal(1f, _hero.MoveSpeed);
        animator.StopRemote();
        for (int i = 0; i < 3; i++) Frame();
        Assert.False(playback.IsPlaying);
        Assert.Empty(_game.StopsAnnounced);
    }

    [Fact]
    public void WithoutTheCharacterOrItsClipAnAnimationEndsAtOnce()
    {
        var nobody = CharacterAnimator.Of(9).Play(Wave);
        bool? stopped = null;
        nobody.Ended += (_, e) => stopped = e.WasStopped;
        Animations.Tick();
        Assert.False(nobody.IsPlaying);
        Assert.True(stopped);

        _hero.Refuse = true;
        var refused = CharacterAnimator.Of(7).Play(Wave);
        Animations.Tick();
        Assert.False(refused.IsPlaying);
    }

    [Fact]
    public void ACharacterTheGameDestroyedEndsItsAnimation()
    {
        var playback = CharacterAnimator.Of(7).Play(Wave);
        Frame();
        _hero.IsAlive = false;
        Frame();
        Assert.False(playback.IsPlaying);
        Assert.True(_hero.Players[0].Disposed);
        Assert.False(CharacterAnimator.Of(7).IsAvailable);
    }

    [Fact]
    public void EmotesGoTheGamesWay()
    {
        Assert.True(CharacterAnimator.Of(7).PlayEmote(Emote.Wave));
        Assert.False(CharacterAnimator.Of(7).PlayEmote(Emote.Skal));
        Assert.False(CharacterAnimator.Of(9).PlayEmote(Emote.Wave));
        Assert.Equal([Emote.Wave, Emote.Skal], _hero.Emotes);
    }

    [Fact]
    public void TheCatalogHasOneObjectPerClipAndTheModsAnimations()
    {
        var all = Animations.All;
        Assert.Equal(["aska_wave", "aska_idle"], all.Select(a => a.Name));
        Assert.Same(Wave, Animations.Find("aska_wave"));
        Assert.Equal(TimeSpan.FromSeconds(3.33f), Wave.Length);
        Assert.Null(Animations.Find("nothing"));

        var bow = new AnimationInfo("bow", "my.mod", 0);
        Animations.Register(bow);
        Animations.Register(bow);
        Assert.Same(bow, Animations.Find("my.mod.bow"));
        Assert.Equal("my.mod.bow", bow.FullName);
        Assert.Equal(3, Animations.All.Count);

        _game.Clips = [.. _game.Clips, new GameClip("aska_sit", 2)];
        Assert.Same(Wave, Animations.Find("aska_wave")); // the list changed: the same objects for the same clips
        Assert.NotNull(Animations.Find("aska_sit"));
    }

    [Fact]
    public void AnAnimationPlayedOnAnotherThreadStartsOnTheMainThread()
    {
        var wave = Wave; // read on the main thread, where the clips are
        Dispatcher.MarkMainThread();
        AnimationPlayback? playback = null;
        var worker = new Thread(() => playback = CharacterAnimator.Of(7).Play(wave));
        worker.Start();
        worker.Join();
        Assert.Empty(_game.Announced); // announced from the main thread
        Dispatcher.Tick();
        Animations.Tick();
        Assert.Single(_game.Announced);
        Assert.Single(_hero.Players);
        Assert.True(playback!.IsPlaying);
    }

    [Fact]
    public void OptionsRefuseWhatCannotPlay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationOptions { Speed = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationOptions { MoveSpeed = 2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterAnimator.Of(7).Play(Wave).At(TimeSpan.FromSeconds(-1), () => { }));
        Assert.Equal(0f, AnimationOptions.Default.EffectiveMoveSpeed);
        Assert.Equal(1f, new AnimationOptions { Mask = AnimationMask.UpperBody }.EffectiveMoveSpeed);
    }
}
