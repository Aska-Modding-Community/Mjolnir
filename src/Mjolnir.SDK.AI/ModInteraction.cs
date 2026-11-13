using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Mjolnir.SDK.AI;

/// <summary>
/// One interaction of a mod (<see cref="Interactions.Add"/>): what the player sees, who may use it and how many at once, and
/// the events of its use. Settings may change at any time; the game shows them on the next frame.
/// </summary>
public sealed class ModInteraction
{
    private readonly object _lock = new();
    private readonly List<object> _users = [];
    private readonly ModHandlers<EventHandler<InteractionUsingEventArgs>> _using;
    private readonly ModHandlers<EventHandler<InteractionUsedEventArgs>> _used;
    private IInteractionTarget? _target;
    private bool _removed;
    private string? _text;
    private Sprite? _icon;
    private TimeSpan _holdTime = TimeSpan.FromSeconds(1);
    private float _distance = 1.5f;
    private int _maxUsers = 1;
    private bool _isEnabled = true, _playerCanUse = true;

    internal ModInteraction(string modId, string name, Assembly owner)
    {
        ModId = modId;
        Name = name;
        Owner = owner;
        _using = new($"{this}.Using");
        _used = new($"{this}.Used");
    }

    /// <summary>The mod that added it.</summary>
    public string ModId { get; }

    /// <summary>Its name in the mod (shared by the interactions of one kind).</summary>
    public string Name { get; }

    internal Assembly Owner { get; }

    /// <summary>The object it sits on; null once that object is gone.</summary>
    public GameObject? Target => _target?.Object;

    /// <summary>Whether it is in the world: not removed, its object still there.</summary>
    public bool IsAvailable => !_removed && _target is { Exists: true };

    /// <summary>Where it is: its object's place (the last one known once it is gone).</summary>
    public Vector3 Position
    {
        get
        {
            var (x, y, z) = PositionXyz;
            var v = default(Vector3);
            v.x = x;
            v.y = y;
            v.z = z;
            return v;
        }
    }

    internal (float X, float Y, float Z) PositionXyz => _target?.Position ?? default;

    /// <summary>
    /// The words after the key in the player's prompt (the building's card above it is the building's own: its name and
    /// description); its name without one.
    /// </summary>
    public string? Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    /// <summary>The prompt as shown: <see cref="Text"/>, else <see cref="Name"/>.</summary>
    internal string ShownText => _text ?? Name;

    /// <summary>A picture in the prompt; the game's own without one.</summary>
    public Sprite? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    /// <summary>How long the player holds the interact key to use it (1 s by default; 0 uses it at the press).</summary>
    public TimeSpan HoldTime
    {
        get => _holdTime;
        set
        {
            if (value < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value), value, "a time of 0 or more");
            Set(ref _holdTime, value);
        }
    }

    /// <summary>How near a villager stands to use it, in metres across the ground from its object's place (1.5 by default).</summary>
    public float Distance
    {
        get => _distance;
        set
        {
            if (!(value > 0)) throw new ArgumentOutOfRangeException(nameof(value), value, "a distance above 0");
            Set(ref _distance, value);
        }
    }

    /// <summary>How many villagers use it at once (1 by default); while it is full the player cannot use it either.</summary>
    public int MaxUsers
    {
        get => _maxUsers;
        set
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), value, "at least 1");
            Set(ref _maxUsers, value);
        }
    }

    /// <summary>Whether it can be used now; a disabled one shows no prompt and villagers pass it by.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => Set(ref _isEnabled, value);
    }

    /// <summary>
    /// Whether the player sees and uses it (true by default); false keeps it for villagers. The player uses interactions on
    /// buildings only: on another object this stays false.
    /// </summary>
    public bool PlayerCanUse
    {
        get => _playerCanUse && _target is not { PlayerCanUse: false };
        set => Set(ref _playerCanUse, value);
    }

    /// <summary>How many villagers are using it now.</summary>
    public int UserCount
    {
        get
        {
            lock (_lock) return _users.Count;
        }
    }

    /// <summary>Whether it can take one more user now: available, enabled and not full.</summary>
    public bool IsFree => IsAvailable && _isEnabled && UserCount < _maxUsers;

    /// <summary>Whether <paramref name="user"/> may use it now: it is free, or that user has its turn at it already.</summary>
    internal bool IsFreeFor(object? user)
    {
        if (IsFree) return true;
        if (user == null || !IsAvailable || !_isEnabled) return false;
        lock (_lock) return _users.Contains(user);
    }

    /// <summary>Changes with every setting: the game side shows them again.</summary>
    internal int Version { get; private set; }

    /// <summary>
    /// Before someone uses it (the player once the key was held, a villager when its turn comes): a handler may cancel it.
    /// Main thread.
    /// </summary>
    public event EventHandler<InteractionUsingEventArgs>? Using
    {
        add => _using.Add(value);
        remove => _using.Remove(value);
    }

    /// <summary>Once someone used it: the player at once, a villager after its time at it. Main thread.</summary>
    public event EventHandler<InteractionUsedEventArgs>? Used
    {
        add => _used.Add(value);
        remove => _used.Remove(value);
    }

    /// <summary>Sets <see cref="Text"/>; returns the interaction.</summary>
    public ModInteraction WithText(string? text)
    {
        Text = text;
        return this;
    }

    /// <summary>Sets <see cref="Icon"/>; returns the interaction.</summary>
    public ModInteraction WithIcon(Sprite? icon)
    {
        Icon = icon;
        return this;
    }

    /// <summary>Sets <see cref="HoldTime"/>; returns the interaction.</summary>
    public ModInteraction WithHoldTime(TimeSpan holdTime)
    {
        HoldTime = holdTime;
        return this;
    }

    /// <summary>Sets <see cref="Distance"/>; returns the interaction.</summary>
    public ModInteraction WithDistance(float distance)
    {
        Distance = distance;
        return this;
    }

    /// <summary>Sets <see cref="MaxUsers"/>; returns the interaction.</summary>
    public ModInteraction WithMaxUsers(int maxUsers)
    {
        MaxUsers = maxUsers;
        return this;
    }

    internal void Attach(IInteractionTarget target) => _target = target;

    /// <summary>Taken away: its users let go, the game's side undone.</summary>
    internal bool Remove()
    {
        lock (_lock)
        {
            if (_removed) return false;
            _removed = true;
            _users.Clear();
        }
        _target?.Remove();
        return true;
    }

    /// <summary>A villager takes its turn: false while it is not free (or that villager is at it already).</summary>
    internal bool TryReserve(object user)
    {
        lock (_lock)
        {
            if (_removed || _target is not { Exists: true } || !_isEnabled || _users.Count >= _maxUsers || _users.Contains(user)) return false;
            _users.Add(user);
            Version++;
            return true;
        }
    }

    internal void Release(object user)
    {
        lock (_lock)
        {
            if (_users.Remove(user)) Version++;
        }
    }

    /// <summary>Asks the handlers: false when one cancelled the use.</summary>
    internal bool RaiseUsing(Resident? resident)
    {
        if (_using.Count == 0) return true;
        var args = new InteractionUsingEventArgs(this, resident);
        _using.Raise(h => h(this, args));
        return !args.Cancel;
    }

    internal void RaiseUsed(Resident? resident)
    {
        if (_used.Count == 0) return;
        var args = new InteractionUsedEventArgs(this, resident);
        _used.Raise(h => h(this, args));
    }

    /// <summary>The player used it (the game's session ended): asked, then reported, while it is enabled and free.</summary>
    internal bool PlayerUsed()
    {
        if (!PlayerCanUse || !IsFree || !RaiseUsing(null)) return false;
        RaiseUsed(null);
        return true;
    }

    private void Set<T>(ref T field, T value)
    {
        lock (_lock)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            Version++;
        }
    }

    public override string ToString() => $"{ModId}.{Name}";
}
