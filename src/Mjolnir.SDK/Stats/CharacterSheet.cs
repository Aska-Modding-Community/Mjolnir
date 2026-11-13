using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// What a character is made of, the player's or a villager's alike (<c>LocalPlayer.Sheet</c>, <c>Resident.Sheet</c>): its
/// base characteristics and every other attribute, its <see cref="Skills"/> and its status <see cref="Effects"/>. An object
/// belongs to one character: after a load take it again. Without the character it is empty, and everything reads 0.
/// </summary>
/// <remarks>
/// Use it on the main thread (mod callbacks, events, a job's steps). On another thread reading gives what was last seen on
/// the main thread, and changes are made on the next frame.
/// </remarks>
/// <example><code>
/// var sheet = LocalPlayer.Sheet;
/// var buff = sheet.Strength.AddModifier(5);                  // +5 strength until disposed
/// float farming = sheet.Skill(SkillKind.Farming).Level.Value;
/// sheet.Effects.Apply(Effects.Find("Effect_Energized")!, TimeSpan.FromMinutes(2));
/// </code></example>
public sealed class CharacterSheet
{
    private static readonly GameObjectWrappers<CharacterSheet> s_known = new(sheet => sheet._source is { IsAlive: false }, sheet => sheet.Release());
    private static ModHandlers<EventHandler<ExperienceGainingEventArgs>> s_gaining = new("Skill.ExperienceGaining");
    private static readonly CharacterSheet s_none = new(null);
    private static IStatsSource? s_game;

    private readonly ICharacterSource? _source;
    private readonly Dictionary<string, CharacterStat> _stats = [];
    private readonly Skill[] _skills;
    private bool _available;

    private CharacterSheet(ICharacterSource? source)
    {
        _source = source;
        Vitality = Stat("Attribute_Vitality");
        Strength = Stat("Attribute_Strength");
        Dexterity = Stat("Attribute_Dexterity");
        Intelligence = Stat("Attribute_Intelligence");
        MaxStamina = Stat("Attribute_Stamina");
        StaminaRegeneration = Stat("Attribute_StaminaRegen");
        _skills = Enum.GetValues<SkillKind>().Select(kind => new Skill(kind, source, Stat)).ToArray();
        Effects = new CharacterEffects(source);
    }

    /// <summary>The game's side, set by the stats module's start.</summary>
    internal static IStatsSource? Game
    {
        get => Volatile.Read(ref s_game);
        set
        {
            Volatile.Write(ref s_game, value);
            if (s_gaining.Count > 0) value?.WatchExperience(true); // a mod subscribed before the module started
        }
    }

    /// <summary>
    /// The attributes of a character, one object per character; an empty one without it. The game's side is made on the main
    /// thread only: asked for on another thread, it is made at its first use there.
    /// </summary>
    internal static CharacterSheet Of(nint key)
    {
        if (key == 0 || Game is not { } game) return s_none;
        return s_known.Get(key, () =>
        {
            if (!Dispatcher.IsMainThread) return new CharacterSheet(new DeferredCharacter(() => game.Character(key)));
            return game.Character(key) is { } source ? new CharacterSheet(source) : null;
        }) ?? s_none;
    }

    /// <summary>Whether its character is in the world.</summary>
    public bool IsAvailable
    {
        get
        {
            if (Dispatcher.IsMainThread) _available = _source is { IsAlive: true };
            return _available;
        }
    }

    /// <summary>Maximum health.</summary>
    public CharacterStat Vitality { get; }

    /// <summary>Melee damage and gathering speed.</summary>
    public CharacterStat Strength { get; }

    /// <summary>Ranged combat and crafting.</summary>
    public CharacterStat Dexterity { get; }

    /// <summary>Some skills and magic.</summary>
    public CharacterStat Intelligence { get; }

    /// <summary>Maximum stamina.</summary>
    public CharacterStat MaxStamina { get; }

    /// <summary>How fast stamina comes back.</summary>
    public CharacterStat StaminaRegeneration { get; }

    /// <summary>Every skill, in the order of <see cref="SkillKind"/>; those the character never used read 0.</summary>
    public IReadOnlyList<Skill> Skills => _skills;

    /// <summary>The status effects on the character.</summary>
    public CharacterEffects Effects { get; }

    /// <summary>
    /// Every attribute the character has, also those the game does not show (<c>Attribute_Armor</c>,
    /// <c>VariableAttribute_Food</c>…; Mjolnir's console lists them with <c>stats</c>). Main thread: empty on another.
    /// </summary>
    public IReadOnlyList<CharacterStat> Stats =>
        Dispatcher.IsMainThread && _source is { IsAlive: true } source ? source.Stats.Select(s => Stat(s.AssetName)).ToList() : [];

    /// <summary>A skill.</summary>
    public Skill Skill(SkillKind kind) => _skills[(int)kind];

    /// <summary>An attribute by its asset name (<c>Attribute_Armor</c>), null when the character has none. Main thread: null on another.</summary>
    public CharacterStat? Find(string assetName)
    {
        ArgumentNullException.ThrowIfNull(assetName);
        return Dispatcher.IsMainThread && _source is { IsAlive: true } source && source.Stat(assetName) != null ? Stat(assetName) : null;
    }

    /// <summary>A handler of <see cref="Skill.ExperienceGaining"/> comes.</summary>
    internal static void AddGaining(EventHandler<ExperienceGainingEventArgs>? handler)
    {
        s_gaining.Add(handler);
        Dispatcher.Run(UpdateWatch);
    }

    /// <summary>A handler of <see cref="Skill.ExperienceGaining"/> goes.</summary>
    internal static void RemoveGaining(EventHandler<ExperienceGainingEventArgs>? handler)
    {
        s_gaining.Remove(handler);
        Dispatcher.Run(UpdateWatch);
    }

    // Whether the game reports experience is decided when this runs, from the handlers there are then.
    private static void UpdateWatch() => Game?.WatchExperience(s_gaining.Count > 0);

    /// <summary>The game is about to award experience: what to award, null for nothing.</summary>
    internal static float? Gaining(nint character, SkillKind skill, float amount)
    {
        if (s_gaining.Count == 0) return amount;
        var sheet = Of(character);
        var args = new ExperienceGainingEventArgs(sheet, sheet.Skill(skill), amount);
        s_gaining.Raise(h => h(null, args));
        return args.Cancel ? null : args.Amount;
    }

    /// <summary>
    /// An attribute by its id (the game's HUD finds the player's health, food… by theirs; <c>LocalPlayer</c>'s stats), null
    /// while the game has no attribute of that id or there is no stats module. Main thread.
    /// </summary>
    internal CharacterStat? StatById(int id) => Game?.AttributeName(id) is { } name ? Stat(name) : null;

    /// <summary>Forgets the game and every character, between unit tests.</summary>
    internal static void ResetForTests()
    {
        Volatile.Write(ref s_game, null);
        s_gaining = new("Skill.ExperienceGaining");
        s_known.Clear();
    }

    private CharacterStat Stat(string assetName)
    {
        lock (_stats)
        {
            if (_stats.TryGetValue(assetName, out var known)) return known;
            return _stats[assetName] = new CharacterStat(assetName, () => _source?.Stat(assetName), () => _source is { IsAlive: true });
        }
    }

    // The character is gone: nothing of it is watched any more, so the game's objects no longer hold the mods' handlers.
    private void Release()
    {
        lock (_stats)
        {
            foreach (var stat in _stats.Values) stat.Release();
        }
        Effects.Release();
    }
}

/// <summary>
/// Experience about to be given (<see cref="Skill.ExperienceGaining"/>). The amount is what the action earns before
/// the character's <see cref="Skill.ExperienceMultiplier"/>.
/// </summary>
public sealed class ExperienceGainingEventArgs : CancelEventArgs
{
    private float _amount;

    internal ExperienceGainingEventArgs(CharacterSheet character, Skill skill, float amount)
    {
        Sheet = character;
        Skill = skill;
        _amount = amount;
    }

    /// <summary>Who earns it.</summary>
    public CharacterSheet Sheet { get; }

    /// <summary>The skill it counts for.</summary>
    public Skill Skill { get; }

    /// <summary>The amount; set it to give more or less, never below 0.</summary>
    public float Amount
    {
        get => _amount;
        set => _amount = Math.Max(0f, value);
    }
}
