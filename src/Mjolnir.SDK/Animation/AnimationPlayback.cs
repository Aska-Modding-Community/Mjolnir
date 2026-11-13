using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mjolnir.Core.Mods;

namespace Mjolnir.SDK;

/// <summary>
/// An animation playing on a character (<see cref="CharacterAnimator.Play"/>). It runs by itself until its end; stop it
/// sooner with <see cref="Stop"/> or by disposing it. Its events and actions run on the main thread.
/// </summary>
/// <example><code>
/// var cast = LocalPlayer.Animator.Play(spell, new AnimationOptions { Mask = AnimationMask.UpperBody })
///                                .At(TimeSpan.FromSeconds(0.55), () => LaunchFireball());
/// cast.Ended += (_, e) => Log.Info(e.WasStopped ? "interrupted" : "cast");
/// </code></example>
public sealed class AnimationPlayback : IDisposable
{
    private enum State { Pending, Playing, Stopping, Ended }

    private readonly object _lock = new();
    private readonly bool _remote;
    private readonly List<(double Time, Action Action, Assembly? Owner)> _marks = [];
    private readonly ModHandlers<EventHandler<AnimationEndedEventArgs>> _ended = new("AnimationPlayback.Ended");
    private State _state;
    private IClipPlayer? _player;
    private double _speed;
    private double _startedAt, _stopAt;
    private float _weight, _stopWeight;
    private double _lastTime;
    private bool _stopRequested, _wasStopped, _rootMotionHeld;

    internal AnimationPlayback(CharacterAnimator animator, AnimationInfo animation, AnimationOptions options, bool remote)
    {
        Animator = animator;
        Animation = animation;
        Options = options;
        _remote = remote;
        _speed = options.Speed;
    }

    /// <summary>The character's animator it plays on.</summary>
    public CharacterAnimator Animator { get; }

    public AnimationInfo Animation { get; }

    public AnimationOptions Options { get; }

    /// <summary>Whether it still plays: false once it ended (fading out still counts as playing).</summary>
    public bool IsPlaying
    {
        get
        {
            lock (_lock) return _state != State.Ended;
        }
    }

    /// <summary>How much of the clip has played (past its length while it loops).</summary>
    public TimeSpan Time
    {
        get
        {
            lock (_lock) return TimeSpan.FromSeconds(_lastTime);
        }
    }

    /// <summary>How fast it plays now: 1 as made. Above 0.</summary>
    public double Speed
    {
        get
        {
            lock (_lock) return _speed;
        }
        set
        {
            if (!(value > 0) || !double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "a speed above 0");
            lock (_lock)
            {
                _speed = value;
                if (_player != null) Threading.Dispatcher.Run(() => SetSpeed(value));
            }
        }
    }

    /// <summary>Raised once on the main thread when it ended: played to its end or stopped. Added after that, it is not raised.</summary>
    public event EventHandler<AnimationEndedEventArgs>? Ended
    {
        add => _ended.Add(value);
        remove => _ended.Remove(value);
    }

    /// <summary>
    /// Runs <paramref name="action"/> when the clip reaches <paramref name="time"/> (of the clip, at speed 1): the moment a hand
    /// lets go of a spell, a foot lands. A looping clip runs it on each round. Returns the playback.
    /// </summary>
    public AnimationPlayback At(TimeSpan time, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (time < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(time), time, "a time of 0 or more");
        // A throw blames the mod that wrote the action, whoever started the playback (a module's job step plays for itself).
        var owner = action.Method.DeclaringType?.Assembly is { } declaring ? CallingMod.Of(declaring) : ModFaults.CurrentOwner;
        lock (_lock) _marks.Add((time.TotalSeconds, action, owner));
        return this;
    }

    /// <summary>Gives the character back to the game's animation, over <see cref="AnimationOptions.FadeOut"/>. Nothing once it ended.</summary>
    public void Stop()
    {
        bool remoteToo;
        lock (_lock)
        {
            if (_state == State.Ended || _stopRequested) return;
            _stopRequested = true;
            remoteToo = !_remote;
        }
        if (remoteToo) Animator.Announced(this);
    }

    /// <summary>Stops it (<see cref="Stop"/>).</summary>
    public void Dispose() => Stop();

    public override string ToString() => $"{Animation} on {Animator}";

    /// <summary>One frame on the main thread, <paramref name="now"/> the steady clock's seconds.</summary>
    internal void Tick(double now)
    {
        var character = Animator.Source;
        if (character is not { IsAlive: true })
        {
            End(stopped: true);
            return;
        }
        State state;
        lock (_lock) state = _state;
        if (state == State.Pending)
        {
            _player = character.Begin(Animation, Options.Mask, Options.Loop, _speed);
            if (_player == null)
            {
                End(stopped: true);
                return;
            }
            lock (_lock)
            {
                _state = State.Playing;
                _startedAt = now;
            }
        }
        double time = _player!.Time;
        RunMarks(_lastTime, time);
        lock (_lock)
        {
            _lastTime = time;
            if (_state == State.Playing && (_stopRequested || ShouldFadeOut(now, time)))
            {
                _wasStopped = _stopRequested;
                _state = State.Stopping;
                _stopAt = now;
                _stopWeight = _weight;
            }
            state = _state;
        }
        float weight;
        if (state == State.Stopping)
        {
            double fade = Options.FadeOut.TotalSeconds;
            weight = fade <= 0 ? 0 : (float)(_stopWeight * (1 - (now - _stopAt) / fade));
            if (weight <= 0.001f || Finished(time))
            {
                End(_wasStopped);
                return;
            }
        }
        else
        {
            double fade = Options.FadeIn.TotalSeconds;
            weight = fade <= 0 ? 1 : (float)Math.Clamp((now - _startedAt) / fade, 0, 1);
        }
        lock (_lock) _weight = weight;
        _player.Weight = weight;
        character.SetMoveSpeed(1 - weight * (1 - Options.EffectiveMoveSpeed));
        if (Options.RootMotion && !_remote)
        {
            character.SetRootMotion(true);
            _rootMotionHeld = true;
        }
    }

    /// <summary>Ends it at once: the character's animator is given another animation, or the character is gone.</summary>
    internal void End(bool stopped)
    {
        lock (_lock)
        {
            if (_state == State.Ended) return;
            _state = State.Ended;
            _weight = 0;
        }
        try
        {
            _player?.Dispose();
            var character = Animator.Source;
            if (character is { IsAlive: true })
            {
                character.SetMoveSpeed(1);
                if (_rootMotionHeld) character.SetRootMotion(false);
            }
        }
        catch (Exception e)
        {
            SdkLog.Error("SDK", $"Animations: ending {this} failed: {e}");
        }
        _player = null;
        Animator.Ended(this);
        var args = new AnimationEndedEventArgs(stopped);
        _ended.Raise(h => h(this, args));
    }

    // Whether the fade out starts: the clip's own end (or the duration's) comes within the fade.
    private bool ShouldFadeOut(double now, double time)
    {
        double fadeOut = Options.FadeOut.TotalSeconds;
        if (Options.Duration is { } duration) return now - _startedAt >= duration.TotalSeconds - fadeOut;
        if (Options.Loop) return false;
        double length = Animation.Length.TotalSeconds;
        return length > 0 && time >= length - fadeOut * _speed;
    }

    // A clip played once and through, fade or not: nothing more to show.
    private bool Finished(double time) => !Options.Loop && Options.Duration == null && Animation.Length.TotalSeconds is > 0 and var length && time >= length;

    private void RunMarks(double from, double to)
    {
        (double Time, Action Action, Assembly? Owner)[] marks;
        lock (_lock)
        {
            if (_marks.Count == 0 || to <= from) return;
            marks = [.. _marks];
        }
        double length = Animation.Length.TotalSeconds;
        foreach (var (at, action, owner) in marks)
        {
            // A moment at the very start is passed by the first frame, which begins there, on a loop's first round too.
            bool passed = Options.Loop && length > 0
                ? Math.Floor((to - at) / length) > Math.Floor((from - at) / length) && to >= at || from == 0 && at == 0
                : from < at && to >= at || from == 0 && at == 0;
            if (passed) ModCall.Run(owner, $"{this} at {at:0.###} s", action);
        }
    }

    private void SetSpeed(double value)
    {
        if (_player != null) _player.Speed = value;
    }

    internal bool IsRemote => _remote;
}
