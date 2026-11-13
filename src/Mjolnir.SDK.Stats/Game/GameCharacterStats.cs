using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Mjolnir.SDK.Threading;
using SandSailorStudio.Attributes;
using SSSGame;
using UnityEngine;
using GameAttribute = SandSailorStudio.Attributes.Attribute;
using GameStatusEffect = SSSGame.StatusEffect;

namespace Mjolnir.SDK.Stats;

/// <summary>
/// One character of the game, the player or a villager: its <see cref="AttributeManager"/>, its <see cref="StatusEffectManager"/>,
/// and the interaction agent the game awards experience through. What it asks of the game answers false on a fault (logged),
/// never throwing into the mod that asked.
/// </summary>
internal sealed class GameCharacterStats(Character character) : ICharacterSource
{
    // Health, as the game's HUD finds it: its limit is the character's MaxHealth, its vitality, which the attribute's own max
    // follows only on its ticks.
    private const int HealthId = 0x898;
    private readonly Dictionary<string, GameStat> _stats = [];

    // Unity's null check: false once the game destroyed the object, though our wrapper still holds it.
    public bool IsAlive => character != null;

    public IReadOnlyList<ICharacterStatSource> Stats
    {
        get
        {
            var properties = new Il2CppSystem.Collections.Generic.List<Property>();
            character.Attributes.GetProperties(properties);
            return properties.ToArray().OrderBy(p => p.id).Select(p => GameStats.Attribute(p.id)).Where(c => c != null)
                             .Select(c => Stat(c!.name)).OfType<ICharacterStatSource>().ToList();
        }
    }

    public ICharacterStatSource? Stat(string assetName)
    {
        if (_stats.TryGetValue(assetName, out var known)) return known;
        if (GameStats.Attribute(assetName) is not { } config) return null;
        int id = config.attributeId;
        var attributes = character.Attributes;
        if (attributes == null) return null;
        Property? property = attributes.HasVariableAttribute(ref id) ? attributes.GetVariableAttribute(ref id)
                           : attributes.HasAttribute(ref id) ? attributes.GetAttribute(id) : null;
        if (property == null) return null;
        return _stats[assetName] = new GameStat(property, config, id == HealthId ? () => character.MaxHealth : null);
    }

    public bool AddExperience(SkillKind skill, float amount) => GameStats.Try($"giving {skill} experience", () =>
    {
        if (GameStats.Moveset(skill) is not { } moveset || Agent() is not { } agent) return false;
        // The gain of the action is the game's own multiplier, so the amount asked for is what the character is given.
        moveset.AwardProfficiencyPoints(agent, amount / moveset.expertiseGainMultiplier, agent.GetWorkstationRewardCapAttribute());
        return true;
    });

    public IReadOnlyList<ActiveEffectState> Effects =>
        character.StatusEffects?.statusEffects?.ToArray()
                 .Where(e => e?.table != null && e.stacks.Count > 0) // one taken off stays listed, empty, until the game's next tick
                 .Select(e => new ActiveEffectState(e.table.id, e.stacks.Count, e.GetRemainingTime(), e.GetInitialDuration()))
                 .ToList() ?? [];

    public bool ApplyEffect(EffectInfo effect, float? seconds) => GameStats.Try($"applying {effect}", () =>
    {
        var manager = character.StatusEffects;
        if (manager == null || GameStats.Database() is not { } database || !database.TryGetStatusEffect(effect.Id, out var table)) return false;
        // Without a time the game's own lasting effects have a negative duration: 0 would run out on its next tick. A save keeps
        // only persistent stacks (with the time they have left), and an effect runs out by its time either way.
        manager.Apply(new GameStatusEffect { table = table, duration = seconds ?? -1f, persistent = true });
        return manager.HasStatusEffect(table);
    });

    public bool RemoveEffect(EffectInfo effect) => GameStats.Try($"removing {effect}", () =>
    {
        var manager = character.StatusEffects;
        var on = manager?.statusEffects?.ToArray().FirstOrDefault(e => e?.table != null && e.table.id == effect.Id && e.stacks.Count > 0);
        if (on == null) return false;
        foreach (var stack in on.stacks.ToArray()) manager!.Remove(stack.effect);
        return true;
    });

    public IDisposable WatchEffects(Action<int, int, int> changed)
    {
        var watch = new EffectsWatch(character, changed);
        watch.Attach();
        return watch;
    }

    // The player's agent is not the character itself.
    private IInteractionAgent? Agent()
    {
        if (character.GetInteractionAgent() is { } agent) return agent;
        var own = character.GetComponentInChildren<PlayerInteractionAgent>(true);
        return own == null ? null : own.Cast<IInteractionAgent>();
    }
}

/// <summary>One attribute of a character: a sum with modifiers, or a pool between limits (<paramref name="maximum"/> its own limit, else the pool's).</summary>
internal sealed class GameStat(Property property, AttributeConfig config, Func<float>? maximum = null) : ICharacterStatSource
{
    private readonly GameAttribute? _sum = property.TryCast<GameAttribute>();
    private readonly VariableAttribute? _pool = property.TryCast<VariableAttribute>();

    public string AssetName => config.name;

    public string Name => config.GetName() ?? "";

    public string Description => config.GetDescription() ?? "";

    public bool IsPool => _pool != null;

    public float Value => property.GetValue();

    public float BaseValue
    {
        get => _sum?.baseValue ?? property.GetValue();
        set => property.SetValue(value);
    }

    public float Minimum => _pool?.min ?? 0f;

    public float Maximum => maximum?.Invoke() ?? _pool?.max ?? 0f;

    public IStatModifierSource AddModifier(float amount, ModifierKind kind)
    {
        var modifier = new AttributeModifier(amount, kind switch
        {
            ModifierKind.Multiply => ModifierOperation.MULTIPLY,
            ModifierKind.Percent => ModifierOperation.PERCENTADD,
            _ => ModifierOperation.ADD,
        });
        _sum!.AddModifier(modifier);
        return new GameStatModifier(_sum, modifier);
    }

    public IDisposable Watch(Action changed)
    {
        var handler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => GameStats.Guard("attribute OnChanged", changed)))!;
        // A public delegate field, not an event: combined with what the game put there.
        property.OnChanged = property.OnChanged == null
            ? handler
            : Il2CppSystem.Delegate.Combine(property.OnChanged, handler).Cast<Il2CppSystem.Action>();
        return new GameSubscription(GameStats.Source, () => property.OnChanged = Il2CppSystem.Delegate.Remove(property.OnChanged, handler)?.Cast<Il2CppSystem.Action>());
    }
}

/// <summary>A modifier of a mod on an attribute of the game.</summary>
internal sealed class GameStatModifier(GameAttribute attribute, AttributeModifier modifier) : IStatModifierSource
{
    public float Amount
    {
        get => modifier.Value;
        set => GameStats.Guard("changing a modifier", () => modifier.Value = value);
    }

    public void Remove() => GameStats.Guard("taking a modifier off", () => attribute.RemoveModifier(modifier));
}

/// <summary>
/// The game's effect changes on one character, watched while a mod listens (main thread). A character the game has not given
/// its effect manager yet is looked at again every frame until it has one or is gone, so the first changes are not missed.
/// </summary>
internal sealed class EffectsWatch(Character character, Action<int, int, int> changed) : IDisposable
{
    private StatusEffectManager? _manager;
    private Il2CppSystem.Action<StatusEffectManager.StatusEffectEventData>? _handler;
    private bool _disposed;

    /// <summary>Subscribes to the character's effect manager, or tries again on the next frame while it has none.</summary>
    public void Attach()
    {
        if (_disposed || _manager != null || character == null) return; // Unity's == null: the character is gone
        var manager = GameStats.Try("finding the effects", () => character.StatusEffects, null);
        if (manager == null)
        {
            Dispatcher.Enqueue(Attach, "Effects watch");
            return;
        }
        var handler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<StatusEffectManager.StatusEffectEventData>>(
            new Action<StatusEffectManager.StatusEffectEventData>(data =>
                GameStats.Guard("effects changed", () => changed(data.table.id, data.current, data.delta))))!;
        GameStats.Guard("watching the effects", () =>
        {
            manager.add_OnStatusEfectsChanged(handler);
            _manager = manager;
            _handler = handler;
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_manager is not { } manager || _handler is not { } handler) return;
        _manager = null;
        _handler = null;
        GameStats.Guard("Unsubscribing from the game", () => manager.remove_OnStatusEfectsChanged(handler));
    }
}
