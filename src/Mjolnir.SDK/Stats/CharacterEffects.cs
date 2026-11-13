using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// A status effect of the game (<see cref="Effects"/>): a well-fed bonus, a perk, a cold. Its texts are in the player's
/// language. There is one object per effect, a mod's own effect being its <c>ModEffect</c>, so effects compare with ==.
/// </summary>
public class EffectInfo
{
    private readonly Func<string> _name;
    private readonly Func<string> _description;
    private string _kind;

    internal EffectInfo(int id, string assetName, string kind, Func<string> name, Func<string> description)
    {
        Id = id;
        AssetName = assetName;
        _kind = kind;
        _name = name;
        _description = description;
    }

    /// <summary>The game's id: saves and the network know an effect by it.</summary>
    public int Id { get; }

    /// <summary>The game's name of the effect (<c>Effect_Energized</c>), the same in every language.</summary>
    public string AssetName { get; }

    /// <summary>
    /// The game's kind: <c>Buff</c>, <c>Debuff</c>, <c>Perk</c>, <c>Cure</c>, <c>PetPerk</c>, <c>GolemPerk</c>, or "". It says which
    /// list the game shows it in, not whether it is good: a well-fed bonus is a <c>Debuff</c>.
    /// </summary>
    public string Kind => _kind;

    /// <summary>Its name. Main thread.</summary>
    public string DisplayName => _name();

    /// <summary>What it does; "" without a text. Main thread.</summary>
    public string Description => _description();

    /// <summary>The asset name.</summary>
    public override string ToString() => AssetName;

    /// <summary>A mod's effect learns its kind from the game effect it is made from.</summary>
    internal void SetKind(string kind) => _kind = kind;
}

/// <summary>
/// Every status effect of the game, mods' included: the same list in a world and out of it, once the game has loaded it.
/// The content module adds a mod's own (<c>Effects.Add</c>).
/// </summary>
public static class Effects
{
    private static readonly Dictionary<int, EffectInfo> s_registered = [];

    /// <summary>Every effect, empty before the game has its effect database. Main thread.</summary>
    public static IReadOnlyList<EffectInfo> All => CharacterSheet.Game?.Effects ?? [];

    /// <summary>An effect by its asset name (<c>Effect_Energized</c>), in any case; null when there is none. Main thread.</summary>
    public static EffectInfo? Find(string assetName)
    {
        ArgumentNullException.ThrowIfNull(assetName);
        return All.FirstOrDefault(e => string.Equals(e.AssetName, assetName, StringComparison.OrdinalIgnoreCase));
    }

    internal static EffectInfo? ById(int id) => CharacterSheet.Game?.Effect(id);

    /// <summary>A mod's effect, as the content module puts it into the game: the game's list then gives this object for its id.</summary>
    internal static void Register(EffectInfo effect)
    {
        lock (s_registered) s_registered[effect.Id] = effect;
    }

    /// <summary>The mod's effect registered for that id, null for a game effect.</summary>
    internal static EffectInfo? Registered(int id)
    {
        lock (s_registered) return s_registered.GetValueOrDefault(id);
    }

    /// <summary>Whether the effect is the game's effect of its id: a mod's effect refused for its id is not.</summary>
    internal static bool IsKnown(EffectInfo effect) => ReferenceEquals(ById(effect.Id), effect);
}

/// <summary>An effect on a character (<see cref="CharacterEffects.Active"/>), as it was when read.</summary>
public sealed class ActiveEffect
{
    internal ActiveEffect(EffectInfo effect, ActiveEffectState state)
    {
        Effect = effect;
        Stacks = state.Stacks;
        // The game takes an effect that ran out off at its next check, and until then its time left is below 0.
        if (state.SecondsLeft < 0 && state.Seconds <= 0) return;
        TimeLeft = TimeSpan.FromSeconds(Math.Max(0f, state.SecondsLeft));
        Duration = TimeSpan.FromSeconds(Math.Max(state.Seconds, Math.Max(0f, state.SecondsLeft)));
    }

    /// <summary>Which effect.</summary>
    public EffectInfo Effect { get; }

    /// <summary>How many times it counts.</summary>
    public int Stacks { get; }

    /// <summary>What is left of it, null for one that does not run out (a perk, a bonus of a place); zero once it ran out.</summary>
    public TimeSpan? TimeLeft { get; }

    /// <summary>
    /// How long it lasts from its start, null for one that does not run out. After a load the game starts it again with the
    /// time it had left.
    /// </summary>
    public TimeSpan? Duration { get; }
}

/// <summary>An effect came onto a character or left it (<see cref="CharacterEffects.Added"/>, <see cref="CharacterEffects.Removed"/>).</summary>
public sealed class EffectChangedEventArgs : EventArgs
{
    internal EffectChangedEventArgs(EffectInfo effect, int stacks)
    {
        Effect = effect;
        Stacks = stacks;
    }

    /// <summary>Which effect.</summary>
    public EffectInfo Effect { get; }

    /// <summary>The stacks it has now, 0 when it is gone.</summary>
    public int Stacks { get; }
}

/// <summary>
/// The status effects on one character (<see cref="CharacterSheet.Effects"/>). The game counts their changes to attributes
/// itself, shows them in the character's window and saves them with the character, each with the time it has left.
/// </summary>
public sealed class CharacterEffects
{
    private readonly ICharacterSource? _source;
    private readonly ModHandlers<EventHandler<EffectChangedEventArgs>> _added = new("CharacterEffects.Added");
    private readonly ModHandlers<EventHandler<EffectChangedEventArgs>> _removed = new("CharacterEffects.Removed");
    private IDisposable? _watch;
    private IReadOnlyList<ActiveEffect> _active = [];

    internal CharacterEffects(ICharacterSource? source) => _source = source;

    /// <summary>The effects on the character now; on another thread those last seen on the main thread.</summary>
    public IReadOnlyList<ActiveEffect> Active
    {
        get
        {
            if (Dispatcher.IsMainThread) _active = Read();
            return _active;
        }
    }

    /// <summary>Raised on the main thread when an effect comes onto the character, or one more stack of it.</summary>
    public event EventHandler<EffectChangedEventArgs>? Added
    {
        add
        {
            _added.Add(value);
            Dispatcher.Run(UpdateWatch);
        }
        remove
        {
            _added.Remove(value);
            Dispatcher.Run(UpdateWatch);
        }
    }

    /// <summary>Raised on the main thread when an effect leaves the character, or a stack of it: it ran out or was removed.</summary>
    public event EventHandler<EffectChangedEventArgs>? Removed
    {
        add
        {
            _removed.Add(value);
            Dispatcher.Run(UpdateWatch);
        }
        remove
        {
            _removed.Remove(value);
            Dispatcher.Run(UpdateWatch);
        }
    }

    /// <summary>Whether the effect is on the character; on another thread whether it was when last seen on the main thread.</summary>
    public bool Has(EffectInfo effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (!Dispatcher.IsMainThread) return _active.Any(a => a.Effect == effect);
        return _source is { IsAlive: true } source && Effects.IsKnown(effect) && source.Effects.Any(s => s.EffectId == effect.Id);
    }

    /// <summary>
    /// Applies an effect for a time, or without one until it is removed. An effect that is already on takes a new stack or
    /// starts over, as the effect's own rules say. A save keeps it, with the time it has left: a mod that applies an effect
    /// whenever the character appears asks <see cref="Has"/> first. False when the character is gone, the game refuses
    /// (immunity) or does not have the effect (a mod's effect refused for its id). Off the main thread it runs on the next frame
    /// and answers true.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The duration is not above zero.</exception>
    public bool Apply(EffectInfo effect, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration), duration, "A duration is above zero.");
        float? seconds = duration is { } time ? (float)time.TotalSeconds : null;
        if (Dispatcher.IsMainThread)
            return _source is { IsAlive: true } source && Effects.IsKnown(effect) && source.ApplyEffect(effect, seconds);
        Dispatcher.Run(() => Apply(effect, duration));
        return true;
    }

    /// <summary>
    /// Takes every stack of an effect off: it is gone from <see cref="Active"/> at once, and its changes to attributes end with
    /// the game's next tick. False when it was not on. Off the main thread it runs on the next frame and answers true.
    /// </summary>
    public bool Remove(EffectInfo effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (Dispatcher.IsMainThread) return _source is { IsAlive: true } source && Effects.IsKnown(effect) && source.RemoveEffect(effect);
        Dispatcher.Run(() => Remove(effect));
        return true;
    }

    /// <summary>Stops watching the game: the character is gone (main thread).</summary>
    internal void Release()
    {
        _watch?.Dispose();
        _watch = null;
    }

    private List<ActiveEffect> Read()
    {
        var active = new List<ActiveEffect>();
        if (_source is not { IsAlive: true } source) return active;
        foreach (var state in source.Effects)
        {
            if (Effects.ById(state.EffectId) is { } effect) active.Add(new ActiveEffect(effect, state));
        }
        return active;
    }

    // The game is watched only while a mod listens.
    private void UpdateWatch()
    {
        bool want = _added.Count + _removed.Count > 0 && _source is { IsAlive: true };
        if (want == (_watch != null)) return;
        if (want)
        {
            _watch = _source!.WatchEffects(OnChanged);
        }
        else
        {
            _watch!.Dispose();
            _watch = null;
        }
    }

    private void OnChanged(int id, int stacks, int change)
    {
        if (change == 0 || Effects.ById(id) is not { } effect) return;
        var args = new EffectChangedEventArgs(effect, stacks);
        (change > 0 ? _added : _removed).Raise(h => h(this, args));
    }
}
