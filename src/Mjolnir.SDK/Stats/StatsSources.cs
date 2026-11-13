using System;
using System.Collections.Generic;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// What the model sees of the game (the stats module's Game/ folder; fakes in the unit tests), as <see cref="IStringsSource"/>
/// is for the localization. Without that module nothing is connected: characters show nothing and effects are unknown.
/// Called on the main thread.
/// </summary>
internal interface IStatsSource
{
    /// <summary>The character behind a key (its IL2CPP object), null when there is none.</summary>
    ICharacterSource? Character(nint key);

    /// <summary>Every status effect of the game, empty until the game has its effect database; one object per effect.</summary>
    IReadOnlyList<EffectInfo> Effects { get; }

    /// <summary>The effect of that id in <see cref="Effects"/>, null for none; any thread.</summary>
    EffectInfo? Effect(int id);

    /// <summary>The asset name of the attribute of that id (the ids the game's own HUD finds the player's pools by), null for none.</summary>
    string? AttributeName(int id);

    /// <summary>Whether <see cref="CharacterSheet.Gaining"/> is called for experience awarded: only while a mod listens.</summary>
    void WatchExperience(bool watch);
}

/// <summary>One character of the game.</summary>
internal interface ICharacterSource
{
    /// <summary>False once the game destroyed the character.</summary>
    bool IsAlive { get; }

    /// <summary>What it has of an attribute, by the asset name, null when it does not have it (yet).</summary>
    ICharacterStatSource? Stat(string assetName);

    /// <summary>Everything it has.</summary>
    IReadOnlyList<ICharacterStatSource> Stats { get; }

    /// <summary>Awards experience the game's way, levels included; false when no action of the game trains the skill.</summary>
    bool AddExperience(SkillKind skill, float amount);

    /// <summary>The effects on it now.</summary>
    IReadOnlyList<ActiveEffectState> Effects { get; }

    /// <summary>Applies an effect for that many seconds, null until removed; false when the game refuses.</summary>
    bool ApplyEffect(EffectInfo effect, float? seconds);

    /// <summary>Takes every stack of an effect off; false when it has none.</summary>
    bool RemoveEffect(EffectInfo effect);

    /// <summary>Calls back with the id of an effect, its stacks now and the change in stacks.</summary>
    IDisposable WatchEffects(Action<int, int, int> changed);
}

/// <summary>One attribute of a character.</summary>
internal interface ICharacterStatSource
{
    string AssetName { get; }

    /// <summary>Its name in the player's language, "" when the game has none.</summary>
    string Name { get; }

    string Description { get; }

    /// <summary>A pool: a value kept between limits (health, food, experience), not a sum of modifiers.</summary>
    bool IsPool { get; }

    /// <summary>The value with its modifiers.</summary>
    float Value { get; }

    /// <summary>The value before modifiers; a pool's current value.</summary>
    float BaseValue { get; set; }

    float Minimum { get; }

    float Maximum { get; }

    IStatModifierSource AddModifier(float amount, ModifierKind kind);

    IDisposable Watch(Action changed);
}

/// <summary>A modifier on an attribute.</summary>
internal interface IStatModifierSource
{
    float Amount { get; set; }

    void Remove();
}

/// <summary>
/// An effect on a character: its stacks, and the seconds left and given. One that does not run out has a negative time left
/// and no time given; one that ran out but is not taken off yet has a negative time left and its time given.
/// </summary>
internal readonly record struct ActiveEffectState(int EffectId, int Stacks, float SecondsLeft, float Seconds);

/// <summary>
/// A character asked for off the main thread: the game's side is made on the main thread, at its first use there (every member
/// is called there). Until then, and when the game has no such character, it is gone.
/// </summary>
internal sealed class DeferredCharacter(Func<ICharacterSource?> make) : ICharacterSource
{
    private ICharacterSource? _real;

    public bool IsAlive => Real() is { IsAlive: true };

    public IReadOnlyList<ICharacterStatSource> Stats => Real()?.Stats ?? [];

    public IReadOnlyList<ActiveEffectState> Effects => Real()?.Effects ?? [];

    public ICharacterStatSource? Stat(string assetName) => Real()?.Stat(assetName);

    public bool AddExperience(SkillKind skill, float amount) => Real()?.AddExperience(skill, amount) ?? false;

    public bool ApplyEffect(EffectInfo effect, float? seconds) => Real()?.ApplyEffect(effect, seconds) ?? false;

    public bool RemoveEffect(EffectInfo effect) => Real()?.RemoveEffect(effect) ?? false;

    public IDisposable WatchEffects(Action<int, int, int> changed) => Real()?.WatchEffects(changed) ?? Nothing.Instance;

    private ICharacterSource? Real() => Dispatcher.IsMainThread ? _real ??= make() : _real;

    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
