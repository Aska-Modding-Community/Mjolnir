using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mjolnir.SDK.Threading;
using SandSailorStudio;
using SSSGame;
using SSSGame.Combat;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Combat;

/// <summary>
/// The game's side of <see cref="Combatants"/>: the players, villagers and creatures loaded, a blow
/// through the game's own <c>IDamageReceiver.TakeDamage</c>, and the patches that report the game's blows and deaths. Nothing
/// here throws into the game or into a mod: faults are logged as [SDK.Combat].
/// </summary>
internal sealed class GameCombat : ICombatSource
{
    public const string Source = "SDK.Combat";
    private static readonly List<nint> s_all = [];
    private static int s_allFrame = -1;

    /// <summary>Connects <see cref="Combatants"/> to the game and patches its damage (the module's start, phase A).</summary>
    public static void Connect()
    {
        Dispatcher.Hook(); // records the main thread at GameReady
        Combatants.Game = new GameCombat();
        Guard("patching the game's damage", () =>
        {
            int count = new Harmony("mjolnir.sdk.combat").CreateClassProcessor(typeof(DamagePatches)).Patch()?.Count ?? 0;
            SdkLog.Info(Source, $"Combat: {count} method(s) patched");
        });
    }

    public ICombatantSource? Combatant(nint key)
    {
        if (key == 0) return null;
        ICombatantSource? found = null;
        Guard("finding a combatant", () =>
        {
            var obj = new Il2CppSystem.Object(key);
            if (obj.TryCast<Character>() is { } character) found = new GameCombatant(character, null);
            else if (obj.TryCast<Creature>() is { } creature) found = new GameCombatant(null, creature);
        });
        return found;
    }

    public IReadOnlyList<nint> All
    {
        get
        {
            if (Time.frameCount == s_allFrame) return s_all;
            s_allFrame = Time.frameCount;
            s_all.Clear();
            Guard("listing the combatants", () =>
            {
                foreach (var character in Object.FindObjectsOfType<Character>())
                    if (character.TryCast<PlayerCharacter>() != null || character.TryCast<Villager>() != null) s_all.Add(character.Pointer);
                foreach (var creature in Object.FindObjectsByType<Creature>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (!creature.isComponent) s_all.Add(creature.Pointer); // a den's parts are the den's
            });
            return s_all;
        }
    }

    public nint LocalPlayer
    {
        get
        {
            nint key = 0;
            Guard("finding the local player", () =>
            {
                try
                {
                    key = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.Pointer ?? 0;
                }
                catch (Exception)
                {
                    key = 0; // no session: LocalPlayer throws
                }
            });
            return key;
        }
    }

    public static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            SdkLog.Warn(Source, $"Combat: {what} failed: {e.GetType().Name} {e.Message}");
        }
    }

    /// <summary>The combatant a game object belongs to (a dealer's, a receiver's): its character or creature, 0 for none.</summary>
    public static nint OwnerOf(GameObject? gameObject)
    {
        if (gameObject == null) return 0;
        var character = gameObject.GetComponentInParent<Character>();
        if (character != null) return character.Pointer;
        var creature = gameObject.GetComponentInParent<Creature>();
        if (creature == null) return 0;
        return creature.isComponent && creature.host != null ? creature.host.Pointer : creature.Pointer;
    }

    /// <summary>The game's marks of a blow in the SDK's terms.</summary>
    public static DamageKinds KindsOf(DamageFlags flags)
    {
        var kinds = DamageKinds.None;
        if ((flags & DamageFlags.HeavyAttack) != 0) kinds |= DamageKinds.Heavy;
        if ((flags & DamageFlags.Melee) != 0) kinds |= DamageKinds.Melee;
        if ((flags & DamageFlags.Critical) != 0) kinds |= DamageKinds.Critical;
        if ((flags & DamageFlags.Unblockable) != 0) kinds |= DamageKinds.Unblockable;
        if ((flags & DamageFlags.Undodgeable) != 0) kinds |= DamageKinds.Undodgeable;
        if ((flags & DamageFlags.Blocked) != 0) kinds |= DamageKinds.Blocked;
        if ((flags & DamageFlags.Dodged) != 0) kinds |= DamageKinds.Dodged;
        if ((flags & DamageFlags.Staggered) != 0) kinds |= DamageKinds.Staggered;
        if ((flags & DamageFlags.KnockedDown) != 0) kinds |= DamageKinds.KnockedDown;
        return kinds;
    }
}

/// <summary>One character (the player, a villager) or creature (a pet, an animal, an enemy, a den).</summary>
internal sealed class GameCombatant(Character? character, Creature? creature) : ICombatantSource
{
    private Component Body => character != null ? character : creature!;

    public bool Exists => character != null ? character != null && !character.WasCollected : creature != null && !creature.WasCollected;

    public CombatantKind Kind
    {
        get
        {
            if (character != null) return character.TryCast<PlayerCharacter>() != null ? CombatantKind.Player : CombatantKind.Resident;
            return creature!.GetIl2CppType().Name switch
            {
                "Pet" => CombatantKind.Pet,
                "Den" => CombatantKind.Den,
                _ => CombatantKind.Creature,
            };
        }
    }

    public string Name => character != null ? Plain(character.gameObject.name) : creature!.dataSheet != null ? creature.dataSheet.name : Plain(creature.gameObject.name);

    public string DisplayName
    {
        get
        {
            if (character != null) return character.GetName() ?? "";
            var sheet = creature!.dataSheet;
            if (sheet == null) return Plain(creature.gameObject.name);
            try
            {
                string name = sheet.Loc("name", Array.Empty<string>());
                if (!string.IsNullOrEmpty(name) && !name.Contains('.')) return name;
            }
            catch (Exception)
            {
                // a sheet without its texts: its English name below
            }
            return sheet.localizedName ?? "";
        }
    }

    public CombatFaction Faction => (CombatFaction)(int)(character != null ? character.Faction : creature!.Faction);

    public int Team => character != null ? character.TeamId : creature!.TeamId;

    public bool IsDead => character != null ? character.IsDead : creature!.IsDead;

    public float Health
    {
        get => Math.Max(0, character != null ? character.CurrentHealth : creature!.CurrentHealth);
        set
        {
            if (character != null)
            {
                character.CurrentHealth = value; // the game's death at 0 (checked: a villager's loot and body)
                return;
            }
            creature!.CurrentHealth = value;
            if (value <= 0 && !creature.IsDead) creature.IsDead = true; // the creature's setter leaves its death to TakeDamage
        }
    }

    public float MaxHealth => character != null ? character.MaxHealth : creature!.MaxHealth;

    public Vector3 Position => Body.transform.position;

    public bool CanStrike => Dealer(Body) != null;

    public float Strike(float amount, nint attacker)
    {
        float before = Health;
        GameCombat.Guard("striking a blow", () =>
        {
            var by = new Il2CppSystem.Object(attacker);
            Component? attackerBody = by.TryCast<Character>() is { } c ? c : by.TryCast<Creature>();
            if (attackerBody == null || Dealer(attackerBody) is not { } dealer) return;
            var receiver = Body.Cast<IDamageReceiver>();
            var direction = Body.transform.position - attackerBody.transform.position;
            receiver.TakeDamage(new DamageData
            {
                damageReceiver = receiver,
                damageDealer = dealer,
                baseDamage = amount,
                damageMultiplier = 1f,
                hitPosition = Body.transform.position + Vector3.up,
                hitDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Body.transform.forward,
            });
        });
        return Math.Max(0, before - Health);
    }

    // The component a combatant strikes with, as the game takes its blows' dealer: its melee, else its bow or its spells.
    private static IDamageDealer? Dealer(Component body)
    {
        Component? found = body.GetComponentInChildren<MeleeManager>(true);
        if (found == null) found = body.GetComponentInChildren<RangedManager>(true);
        if (found == null) found = body.GetComponentInChildren<SpellsManager>(true);
        return found?.TryCast<IDamageDealer>();
    }

    private static string Plain(string name) => name.Replace("(Clone)", "", StringComparison.Ordinal).Trim();
}
