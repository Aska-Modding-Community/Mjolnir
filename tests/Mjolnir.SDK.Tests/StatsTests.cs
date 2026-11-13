using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mjolnir.SDK.Threading;
using Xunit;

namespace Mjolnir.SDK.Tests;

/// <summary>
/// A character's stats on a fake game: one object per character, no character, attributes and pools, modifiers, watching the
/// game only while a mod listens, skills and their ranks, experience, status effects, other threads.
/// </summary>
[Collection("Stats")]
public sealed class StatsTests : IDisposable
{
    private readonly FakeGame _game = new();
    private readonly FakeCharacter _hero;

    public StatsTests()
    {
        Dispatcher.ResetForTests();
        CharacterSheet.ResetForTests();
        CharacterSheet.Game = _game;
        _hero = _game.Add(1);
    }

    public void Dispose()
    {
        CharacterSheet.ResetForTests();
        Dispatcher.ResetForTests();
    }

    [Fact]
    public void ACharacterHasOneObjectAndNoCharacterAnEmptyOne()
    {
        Assert.Same(CharacterSheet.Of(1), CharacterSheet.Of(1));
        Assert.True(CharacterSheet.Of(1).IsAvailable);
        var none = CharacterSheet.Of(0);
        Assert.False(none.IsAvailable);
        Assert.Equal(0, none.Strength.Value);
        Assert.Empty(none.Stats);
        Assert.Empty(none.Effects.Active);
    }

    [Fact]
    public void WithoutTheStatsModuleEverythingIsEmpty()
    {
        CharacterSheet.Game = null;
        Assert.False(CharacterSheet.Of(1).IsAvailable);
        Assert.Empty(Effects.All);
    }

    [Fact]
    public void ACharacterTheGameDestroyedIsNotAvailableAndLetGo()
    {
        var stats = CharacterSheet.Of(1);
        _hero.IsAlive = false;
        Assert.False(stats.IsAvailable);
        Assert.Empty(stats.Effects.Active);
        _game.Add(2);
        _ = CharacterSheet.Of(2); // a new character sweeps the gone ones
        _game.Add(1);
        Assert.NotSame(stats, CharacterSheet.Of(1));
    }

    [Fact]
    public void BaseCharacteristicsReadTheGame()
    {
        _hero.Stat("Attribute_Strength", 9, 4);
        _hero.Stat("Attribute_Vitality", 200, 200);
        var stats = CharacterSheet.Of(1);
        Assert.Equal(9, stats.Strength.Value);
        Assert.Equal(4, stats.Strength.BaseValue);
        Assert.Equal(200, stats.Vitality.Value);
        Assert.Equal("Attribute_Strength", stats.Strength.AssetName);
        Assert.Equal("Attribute_Strength name", stats.Strength.DisplayName);
    }

    [Fact]
    public void AnAttributeTheCharacterLacksReadsZeroAndComesLater()
    {
        var stats = CharacterSheet.Of(1);
        Assert.Equal(0, stats.Dexterity.Value);
        Assert.Equal("Attribute_Dexterity", stats.Dexterity.DisplayName);
        _hero.Stat("Attribute_Dexterity", 3, 3);
        Assert.Equal(3, stats.Dexterity.Value);
    }

    [Fact]
    public void FindGivesOnlyWhatTheCharacterHas()
    {
        _hero.Stat("Attribute_Armor", 50, 0);
        var stats = CharacterSheet.Of(1);
        Assert.Equal(50, stats.Find("Attribute_Armor")!.Value);
        Assert.Null(stats.Find("Attribute_Nothing"));
        Assert.Same(stats.Find("Attribute_Armor"), stats.Stats.Single());
    }

    [Fact]
    public void SettingAValueSetsItsBase()
    {
        var strength = _hero.Stat("Attribute_Strength", 5, 5);
        CharacterSheet.Of(1).Strength.Value = 7;
        Assert.Equal(7, strength.BaseValue);
        CharacterSheet.Of(1).Strength.BaseValue = 8;
        Assert.Equal(8, strength.BaseValue);
    }

    [Fact]
    public void APoolShowsItsLimitsAndHasNoModifiers()
    {
        var food = _hero.Stat("VariableAttribute_Food", 40, 40);
        food.IsPool = true;
        food.Maximum = 100;
        var stat = CharacterSheet.Of(1).Find("VariableAttribute_Food")!;
        Assert.True(stat.IsPool);
        Assert.Equal(100, stat.Maximum);
        Assert.Equal(0.4f, stat.Fraction, 5);
        Assert.Equal(0f, CharacterSheet.Of(1).Strength.Fraction); // a sum has no range
        Assert.Throws<InvalidOperationException>(() => stat.AddModifier(5));
    }

    [Fact]
    public void AModifierCountsUntilDisposedAndOnlyOnce()
    {
        var strength = _hero.Stat("Attribute_Strength", 4, 4);
        var stat = CharacterSheet.Of(1).Strength;
        var modifier = stat.AddModifier(3);
        Assert.Equal(7, stat.Value);
        Assert.Equal((3f, ModifierKind.Add), strength.Modifiers.Single());
        modifier.Dispose();
        modifier.Dispose();
        Assert.Equal(4, stat.Value);
        Assert.Empty(strength.Modifiers);
    }

    [Fact]
    public void AModifierCanChangeItsAmount()
    {
        _hero.Stat("Attribute_Strength", 4, 4);
        var stat = CharacterSheet.Of(1).Strength;
        var modifier = stat.AddModifier(3);
        modifier.Amount = 5;
        Assert.Equal(9, stat.Value);
        Assert.Equal(5, modifier.Amount);
    }

    [Fact]
    public void AModifierOnWhatTheCharacterLacksDoesNothing()
    {
        var modifier = CharacterSheet.Of(1).Strength.AddModifier(3);
        modifier.Amount = 4;
        modifier.Dispose();
    }

    [Fact]
    public void AListenerHearsOfAnAttributeTheCharacterGetsLater()
    {
        var stat = CharacterSheet.Of(1).Dexterity;
        var seen = new List<(float, float)>();
        stat.Changed += (_, e) => seen.Add((e.OldValue, e.NewValue));
        Dispatcher.Tick(); // looked for again, still missing
        var dexterity = _hero.Stat("Attribute_Dexterity", 3, 3);
        Dispatcher.Tick();
        Assert.Equal(1, dexterity.Watchers);
        dexterity.Change(5);
        Assert.Equal([(3f, 5f)], seen); // its first value is no change
        Dispatcher.Tick();
        Assert.Equal(1, dexterity.Watchers);
    }

    [Fact]
    public void ACharacterTheGameDestroyedReadsZeroAndIsLeftAlone()
    {
        var strength = _hero.Stat("Attribute_Strength", 9, 4);
        var stat = CharacterSheet.Of(1).Strength;
        Assert.Equal(9, stat.Value);
        _hero.IsAlive = false;
        Assert.Equal(0, stat.Value);
        Assert.Equal(0, stat.BaseValue);
        stat.BaseValue = 3;
        stat.AddModifier(2);
        Assert.Equal(4, strength.BaseValue);
        Assert.Empty(strength.Modifiers);
    }

    [Fact]
    public void WhatWasWatchedIsLetGoWithTheCharacter()
    {
        var strength = _hero.Stat("Attribute_Strength", 4, 4);
        var stats = CharacterSheet.Of(1);
        stats.Strength.Changed += (_, _) => { };
        stats.Effects.Added += (_, _) => { };
        Assert.Equal(1, strength.Watchers);
        Assert.Equal(1, _hero.EffectWatchers);
        _hero.IsAlive = false;
        _game.Add(2);
        _ = CharacterSheet.Of(2);
        Assert.Equal(0, strength.Watchers);
        Assert.Equal(0, _hero.EffectWatchers);
    }

    [Fact]
    public void ACharacterAskedForOnAnotherThreadIsMadeOnTheMainThread()
    {
        Dispatcher.Tick(); // this thread is now the main thread
        _game.Add(3).Stat("Attribute_Strength", 6, 6);
        CharacterSheet? stats = null;
        var worker = new Thread(() => stats = CharacterSheet.Of(3));
        worker.Start();
        worker.Join();
        Assert.Empty(_game.AskedOn);
        Assert.Same(stats, CharacterSheet.Of(3));
        Assert.True(stats!.IsAvailable);
        Assert.Equal(6, stats.Strength.Value);
        Assert.Equal([Environment.CurrentManagedThreadId], _game.AskedOn);
    }

    [Fact]
    public void APoolsModifierAskedForOnAnotherThreadIsRefusedOnTheNextFrame()
    {
        Dispatcher.Tick();
        var food = _hero.Stat("VariableAttribute_Food", 40, 40);
        food.IsPool = true;
        var stat = CharacterSheet.Of(1).Find("VariableAttribute_Food")!;
        var worker = new Thread(() => stat.AddModifier(5));
        worker.Start();
        worker.Join();
        Dispatcher.Tick();
        Assert.Empty(food.Modifiers);
    }

    [Fact]
    public void AModifierDisposedBeforeItReachedTheGameNeverGoesOn()
    {
        Dispatcher.Tick();
        var strength = _hero.Stat("Attribute_Strength", 4, 4);
        var stat = CharacterSheet.Of(1).Strength;
        StatModifier? modifier = null;
        var worker = new Thread(() => modifier = stat.AddModifier(3));
        worker.Start();
        worker.Join();
        modifier!.Dispose();
        Dispatcher.Tick();
        Assert.Empty(strength.Modifiers);
        Assert.Equal(4, stat.Value);
    }

    [Fact]
    public void TheGameIsWatchedOnlyWhileAModListens()
    {
        var strength = _hero.Stat("Attribute_Strength", 4, 4);
        var stat = CharacterSheet.Of(1).Strength;
        var seen = new List<(float, float)>();
        EventHandler<ValueChangedEventArgs<float>> handler = (_, e) => seen.Add((e.OldValue, e.NewValue));
        Assert.Equal(0, strength.Watchers);
        stat.Changed += handler;
        Assert.Equal(1, strength.Watchers);
        strength.Change(6);
        Assert.Equal([(4f, 6f)], seen);
        stat.Changed -= handler;
        Assert.Equal(0, strength.Watchers);
    }

    [Fact]
    public void AChangeAReadSeesFirstIsReportedToo()
    {
        var strength = _hero.Stat("Attribute_Strength", 4, 4);
        var stat = CharacterSheet.Of(1).Strength;
        var seen = new List<(float, float)>();
        stat.Changed += (_, e) => seen.Add((e.OldValue, e.NewValue));
        strength.ChangeQuietly(7); // the game has not told yet
        Assert.Equal(7, stat.Value);
        strength.Change(7);        // and then it does
        Assert.Equal([(4f, 7f)], seen);
    }

    [Fact]
    public void AModifierIsAChangeToo()
    {
        _hero.Stat("Attribute_Strength", 4, 4);
        var stat = CharacterSheet.Of(1).Strength;
        var seen = new List<float>();
        stat.Changed += (_, e) => seen.Add(e.NewValue);
        var modifier = stat.AddModifier(1);
        modifier.Dispose();
        Assert.Equal([5f, 4f], seen);
    }

    [Fact]
    public void AnotherThreadGetsTheLastValueAndChangesOnTheNextFrame()
    {
        Dispatcher.Tick(); // this thread is now the main thread
        var strength = _hero.Stat("Attribute_Strength", 4, 4);
        var stat = CharacterSheet.Of(1).Strength;
        _ = stat.Value;
        strength.Change(6);
        float seen = -1;
        var worker = new Thread(() =>
        {
            seen = stat.Value;
            stat.BaseValue = 9;
        });
        worker.Start();
        worker.Join();
        Assert.Equal(4, seen);
        Assert.Equal(4, strength.BaseValue);
        Dispatcher.Tick();
        Assert.Equal(9, strength.BaseValue);
    }

    [Fact]
    public void EverySkillIsThereWithItsAttributes()
    {
        var stats = CharacterSheet.Of(1);
        Assert.Equal(Enum.GetValues<SkillKind>().Length, stats.Skills.Count);
        foreach (var kind in Enum.GetValues<SkillKind>())
        {
            Assert.Equal(kind, stats.Skill(kind).Kind);
            Assert.StartsWith("Proficiency_", stats.Skill(kind).Level.AssetName, StringComparison.Ordinal);
            Assert.Equal(kind, SkillNames.ByLevel(stats.Skill(kind).Level.AssetName));
        }
        Assert.Null(SkillNames.ByLevel("Attribute_Strength"));
    }

    [Fact]
    public void ASkillReadsItsLevelExperienceAndMultiplier()
    {
        _hero.Stat("Proficiency_Gatherer", 18.7f, 12);
        _hero.Stat("VariableAttribute_GatheringExpertise", 606, 606);
        _hero.Stat("ExpMultiplier_Gatherer", 1.2f, 1);
        var skill = CharacterSheet.Of(1).Skill(SkillKind.Gathering);
        Assert.Equal(18.7f, skill.Level.Value);
        Assert.Equal(12, skill.Level.BaseValue);
        Assert.Equal(606, skill.Experience.Value);
        Assert.Equal(1.2f, skill.ExperienceMultiplier.Value);
        Assert.Equal("Proficiency_Gatherer name", skill.DisplayName);
    }

    [Fact]
    public void AGameWithoutAnAttributeLeavesItsPartEmpty()
    {
        var hunting = CharacterSheet.Of(1).Skill(SkillKind.Hunting);
        Assert.Equal(0, hunting.Experience.Value);
        Assert.Equal("", hunting.Experience.AssetName);
    }

    [Theory]
    [InlineData(0f, SkillRank.Novice)]
    [InlineData(24.9f, SkillRank.Novice)]
    [InlineData(25f, SkillRank.Apprentice)]
    [InlineData(49f, SkillRank.Apprentice)]
    [InlineData(50f, SkillRank.Adept)]
    [InlineData(75f, SkillRank.Master)]
    [InlineData(130f, SkillRank.Master)]
    [InlineData(-3f, SkillRank.Novice)]
    public void ARankFollowsTheLevel(float level, SkillRank rank)
    {
        _hero.Stat("Proficiency_Carpentry", level, level);
        Assert.Equal(rank, CharacterSheet.Of(1).Skill(SkillKind.Carpentry).Rank);
    }

    [Fact]
    public void ExperienceGoesThroughTheGame()
    {
        var skill = CharacterSheet.Of(1).Skill(SkillKind.Cooking);
        Assert.True(skill.AddExperience(30));
        Assert.Equal((SkillKind.Cooking, 30f), _hero.Awards.Single());
        Assert.Throws<ArgumentOutOfRangeException>(() => skill.AddExperience(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => skill.AddExperience(float.NaN));
    }

    [Fact]
    public void ExperienceOfNoCharacterGoesNowhere()
    {
        var none = CharacterSheet.Of(0);
        Assert.False(none.Skill(SkillKind.Cooking).AddExperience(5));
    }

    [Fact]
    public void GainingIsReportedOnlyWhileAModListens()
    {
        EventHandler<ExperienceGainingEventArgs> handler = (_, _) => { };
        Skill.ExperienceGaining += handler;
        Assert.True(_game.WatchingExperience);
        Skill.ExperienceGaining -= handler;
        Assert.False(_game.WatchingExperience);
    }

    [Fact]
    public void AListenerBeforeTheGameIsConnectedIsRemembered()
    {
        CharacterSheet.Game = null;
        Skill.ExperienceGaining += (_, _) => { };
        var game = new FakeGame();
        CharacterSheet.Game = game;
        Assert.True(game.WatchingExperience);
    }

    [Fact]
    public void ExperienceIsWatchedAsTheHandlersAreWhenItRuns()
    {
        Dispatcher.Tick(); // this thread is now the main thread
        EventHandler<ExperienceGainingEventArgs> first = (_, _) => { }, second = (_, _) => { };
        var worker = new Thread(() =>
        {
            Skill.ExperienceGaining += first;
            Skill.ExperienceGaining -= first;
        });
        worker.Start();
        worker.Join();
        Skill.ExperienceGaining += second;
        Dispatcher.Tick(); // the worker's two run now, after the main thread's
        Assert.True(_game.WatchingExperience);
        Skill.ExperienceGaining -= second;
        Assert.False(_game.WatchingExperience);
    }

    [Fact]
    public void AHandlerChangesTheExperience()
    {
        Skill.ExperienceGaining += (_, e) =>
        {
            Assert.Same(CharacterSheet.Of(1), e.Sheet);
            Assert.Equal(SkillKind.Farming, e.Skill.Kind);
            e.Amount *= 2;
        };
        Assert.Equal(30f, CharacterSheet.Gaining(1, SkillKind.Farming, 15));
    }

    [Fact]
    public void ExperienceIsNeverBelowZero()
    {
        Skill.ExperienceGaining += (_, e) => e.Amount = -5;
        Assert.Equal(0f, CharacterSheet.Gaining(1, SkillKind.Farming, 15));
    }

    [Fact]
    public void AHandlerCancelsTheExperience()
    {
        Skill.ExperienceGaining += (_, e) => e.Cancel = true;
        Assert.Null(CharacterSheet.Gaining(1, SkillKind.Farming, 15));
    }

    [Fact]
    public void WithNoHandlerTheAmountStands() => Assert.Equal(15f, CharacterSheet.Gaining(1, SkillKind.Farming, 15));

    [Fact]
    public void EffectsAreFoundInAnyCase()
    {
        Assert.Equal(2, Effects.All.Count);
        Assert.Equal(7, Effects.Find("effect_energized")!.Id);
        Assert.Null(Effects.Find("Effect_Nothing"));
        Assert.Equal("Buff", Effects.Find("Effect_Energized")!.Kind);
        Assert.Equal("Energized", Effects.Find("Effect_Energized")!.DisplayName);
    }

    [Fact]
    public void AnEffectIsAppliedForATimeOrForGood()
    {
        var effects = CharacterSheet.Of(1).Effects;
        var energized = Effects.Find("Effect_Energized")!;
        var cold = Effects.Find("Effect_Cold")!;
        Assert.True(effects.Apply(energized, TimeSpan.FromSeconds(30)));
        Assert.True(effects.Apply(cold));
        Assert.True(effects.Has(energized));
        Assert.Equal(TimeSpan.FromSeconds(30), effects.Active.First(a => a.Effect == energized).TimeLeft);
        Assert.Equal(TimeSpan.FromSeconds(30), effects.Active.First(a => a.Effect == energized).Duration);
        Assert.Null(effects.Active.First(a => a.Effect == cold).TimeLeft);
        Assert.Null(effects.Active.First(a => a.Effect == cold).Duration);
    }

    [Fact]
    public void AnEffectThatRanOutButIsStillOnHasNoTimeLeft()
    {
        _hero.Put(new ActiveEffectState(7, 1, -0.4f, 10));
        var active = CharacterSheet.Of(1).Effects.Active.Single();
        Assert.Equal(TimeSpan.Zero, active.TimeLeft);
        Assert.Equal(TimeSpan.FromSeconds(10), active.Duration);
    }

    [Fact]
    public void AnEffectIsOneObjectEverywhere()
    {
        var effects = CharacterSheet.Of(1).Effects;
        var energized = Effects.Find("Effect_Energized")!;
        EffectInfo? announced = null;
        effects.Added += (_, e) => announced = e.Effect;
        effects.Apply(energized);
        Assert.Same(energized, announced);
        Assert.Same(energized, effects.Active.Single().Effect);
    }

    [Fact]
    public void OnlyTheGamesObjectOfAnIdCounts()
    {
        var effects = CharacterSheet.Of(1).Effects;
        var refused = new EffectInfo(7, "Effect_mod_refused", "", () => "Refused", () => ""); // a mod's effect left out for its id
        Assert.False(effects.Apply(refused));
        effects.Apply(Effects.Find("Effect_Energized")!);
        Assert.False(effects.Has(refused));
        Assert.False(effects.Remove(refused));
        Assert.True(effects.Has(Effects.Find("Effect_Energized")!));
    }

    [Fact]
    public void ADurationIsAboveZero()
    {
        var energized = Effects.Find("Effect_Energized")!;
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterSheet.Of(1).Effects.Apply(energized, TimeSpan.Zero));
    }

    [Fact]
    public void AnEffectIsRemoved()
    {
        var effects = CharacterSheet.Of(1).Effects;
        var energized = Effects.Find("Effect_Energized")!;
        Assert.False(effects.Remove(energized));
        effects.Apply(energized);
        Assert.True(effects.Remove(energized));
        Assert.False(effects.Has(energized));
    }

    [Fact]
    public void EffectsOfNoCharacterDoNothing()
    {
        var effects = CharacterSheet.Of(0).Effects;
        var energized = Effects.Find("Effect_Energized")!;
        Assert.False(effects.Apply(energized));
        Assert.False(effects.Remove(energized));
    }

    [Fact]
    public void AddedAndRemovedSayWhichEffectAndItsStacks()
    {
        var effects = CharacterSheet.Of(1).Effects;
        var energized = Effects.Find("Effect_Energized")!;
        var seen = new List<string>();
        effects.Added += (_, e) => seen.Add($"+{e.Effect.AssetName} {e.Stacks}");
        effects.Removed += (_, e) => seen.Add($"-{e.Effect.AssetName} {e.Stacks}");
        Assert.Equal(1, _hero.EffectWatchers);
        effects.Apply(energized);
        effects.Remove(energized);
        Assert.Equal(["+Effect_Energized 1", "-Effect_Energized 0"], seen);
    }

    [Fact]
    public void EffectsAreWatchedOnlyWhileAModListens()
    {
        var effects = CharacterSheet.Of(1).Effects;
        EventHandler<EffectChangedEventArgs> handler = (_, _) => { };
        effects.Added += handler;
        effects.Removed += handler;
        Assert.Equal(1, _hero.EffectWatchers);
        effects.Added -= handler;
        Assert.Equal(1, _hero.EffectWatchers);
        effects.Removed -= handler;
        Assert.Equal(0, _hero.EffectWatchers);
    }

    private sealed class FakeGame : IStatsSource
    {
        private readonly Dictionary<nint, FakeCharacter> _characters = [];

        public bool WatchingExperience { get; private set; }

        public List<int> AskedOn { get; } = [];

        public IReadOnlyList<EffectInfo> Effects { get; } =
        [
            new(7, "Effect_Energized", "Buff", () => "Energized", () => "Full of it."),
            new(8, "Effect_Cold", "Debuff", () => "Cold", () => ""),
        ];

        public FakeCharacter Add(nint key) => _characters[key] = new FakeCharacter();

        public ICharacterSource? Character(nint key)
        {
            AskedOn.Add(Environment.CurrentManagedThreadId);
            return _characters.GetValueOrDefault(key);
        }

        public EffectInfo? Effect(int id) => Effects.FirstOrDefault(e => e.Id == id);

        public string? AttributeName(int id) => null;

        public void WatchExperience(bool watch) => WatchingExperience = watch;
    }

    private sealed class FakeCharacter : ICharacterSource
    {
        private readonly List<FakeStat> _stats = [];
        private readonly List<ActiveEffectState> _effects = [];
        private Action<int, int, int>? _watcher;

        public bool IsAlive { get; set; } = true;

        public List<(SkillKind, float)> Awards { get; } = [];

        public int EffectWatchers { get; private set; }

        public IReadOnlyList<ICharacterStatSource> Stats => _stats;

        public IReadOnlyList<ActiveEffectState> Effects => _effects;

        public FakeStat Stat(string assetName, float value, float baseValue)
        {
            var stat = new FakeStat(assetName, value, baseValue);
            _stats.Add(stat);
            return stat;
        }

        public ICharacterStatSource? Stat(string assetName) => _stats.FirstOrDefault(s => s.AssetName == assetName);

        public void Put(ActiveEffectState state) => _effects.Add(state);

        public bool AddExperience(SkillKind skill, float amount)
        {
            Awards.Add((skill, amount));
            return true;
        }

        public bool ApplyEffect(EffectInfo effect, float? seconds)
        {
            _effects.Add(new ActiveEffectState(effect.Id, 1, seconds ?? -1, seconds ?? 0));
            _watcher?.Invoke(effect.Id, 1, 1);
            return true;
        }

        public bool RemoveEffect(EffectInfo effect)
        {
            if (_effects.RemoveAll(e => e.EffectId == effect.Id) == 0) return false;
            _watcher?.Invoke(effect.Id, 0, -1);
            return true;
        }

        public IDisposable WatchEffects(Action<int, int, int> changed)
        {
            _watcher = changed;
            EffectWatchers++;
            return new Undo(() =>
            {
                _watcher = null;
                EffectWatchers--;
            });
        }
    }

    private sealed class FakeStat(string assetName, float current, float baseValue) : ICharacterStatSource
    {
        private Action? _changed;
        private float _added;

        public string AssetName { get; } = assetName;
        public string Name => $"{AssetName} name";
        public string Description => "";
        public bool IsPool { get; set; }
        public float Value => current + _added;
        public float BaseValue
        {
            get => baseValue;
            set
            {
                current += value - baseValue;
                baseValue = value;
                _changed?.Invoke();
            }
        }

        public float Minimum => 0;
        public float Maximum { get; set; }
        public List<(float, ModifierKind)> Modifiers { get; } = [];
        public int Watchers { get; private set; }

        public void Change(float now)
        {
            current = now;
            _changed?.Invoke();
        }

        public void ChangeQuietly(float now) => current = now;

        public IStatModifierSource AddModifier(float amount, ModifierKind kind)
        {
            Modifiers.Add((amount, kind));
            _added += amount;
            _changed?.Invoke();
            return new FakeModifier(this, amount, kind);
        }

        public IDisposable Watch(Action changed)
        {
            _changed = changed;
            Watchers++;
            return new Undo(() =>
            {
                _changed = null;
                Watchers--;
            });
        }

        private sealed class FakeModifier(FakeStat stat, float amount, ModifierKind kind) : IStatModifierSource
        {
            public float Amount
            {
                get => amount;
                set
                {
                    stat._added += value - amount;
                    amount = value;
                }
            }

            public void Remove()
            {
                stat.Modifiers.Remove((amount, kind));
                stat._added -= amount;
                stat._changed?.Invoke();
            }
        }
    }

    private sealed class Undo(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
