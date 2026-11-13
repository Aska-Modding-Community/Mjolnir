using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mjolnir.SDK.AI;

/// <summary>
/// A villager of the loaded world (<see cref="Residents.All"/>). Its properties read the game: use them on the main
/// thread (in a mod's callbacks, a job's steps, <see cref="Residents.Added"/>). A villager that left keeps its last name
/// and place, and <see cref="IsAlive"/> turns false.
/// <para>
/// It stands for the game's villager (<c>SSSGame.Villager</c>). The SDK names it Resident so that a mod using the
/// game's types as well never meets two types named Villager.
/// </para>
/// </summary>
public sealed class Resident
{
    private IVillagerSource _source;
    private string _lastName;
    private (float X, float Y, float Z) _lastPosition;

    internal Resident(IVillagerSource source)
    {
        _source = source;
        Key = source.Key;
        _lastName = source.Name;
        _lastPosition = source.Position;
    }

    internal nint Key { get; }

    /// <summary>The game's villager behind it (the game side reads its own source).</summary>
    internal IVillagerSource Source => _source;

    /// <summary>The job of a mod it runs now (set and cleared by the game side).</summary>
    internal JobRun? Run { get; set; }

    /// <summary>Whether it is still in the world.</summary>
    public bool IsAlive => _source.IsAlive;

    /// <summary>What it is made of: characteristics, skills, status effects.</summary>
    public CharacterSheet Sheet => CharacterSheet.Of(Key);

    /// <summary>
    /// The villager as a combatant: health, damage, healing, death (<see cref="Combatants"/>). The same object as in
    /// <see cref="Combatants.All"/>.
    /// </summary>
    public Combatant Combatant => SDK.Combatant.Of(Key);

    /// <summary>Its animations: the game's and mods' animations played over its own, the game's emotes, its bones.</summary>
    public CharacterAnimator Animator => CharacterAnimator.Of(Key);

    /// <summary>Its name, as the game shows it.</summary>
    public string Name
    {
        get
        {
            if (_source.IsAlive) _lastName = _source.Name;
            return _lastName;
        }
    }

    /// <summary>Where it stands.</summary>
    public Vector3 Position
    {
        get
        {
            var (x, y, z) = PositionXyz;
            return new Vector3(x, y, z);
        }
    }

    /// <summary><see cref="Position"/> as numbers (unit tests cannot make a Vector3).</summary>
    internal (float X, float Y, float Z) PositionXyz
    {
        get
        {
            if (_source.IsAlive) _lastPosition = _source.Position;
            return _lastPosition;
        }
    }

    /// <summary>The job of a mod it does now, null while it does the game's own work or needs.</summary>
    public ResidentJob? ModJob => Run?.Job;

    /// <summary>
    /// What it does now: a mod job's full name (<c>&lt;mod id&gt;.&lt;job&gt;</c>), else the game's own quest
    /// (<c>ComplainQuest</c>, <c>WorkstationIdleQuest</c>, <c>VillagerSocialQuest</c>…), "" with none.
    /// </summary>
    public string Activity => Run?.Job.ToString() ?? (_source.IsAlive ? _source.Activity : "");

    internal void Refresh(IVillagerSource source) => _source = source;

    public override string ToString() => _lastName;
}

/// <summary>A villager that came or went (<see cref="Residents.Added"/>, <see cref="Residents.Removed"/>).</summary>
public sealed class ResidentEventArgs(Resident resident) : EventArgs
{
    /// <summary>The villager.</summary>
    public Resident Resident { get; } = resident;
}

/// <summary>
/// The world's villagers by identity (no Unity: unit-tested): the same <see cref="Resident"/> for a villager while it
/// lives, <see cref="Residents.Added"/> once when it comes and <see cref="Residents.Removed"/> once when it goes. Main
/// thread.
/// </summary>
internal static class ResidentRoster
{
    private static readonly Dictionary<nint, Resident> s_byKey = [];
    private static readonly List<Resident> s_all = [];
    private static Resident[] s_snapshot = [];

    public static IReadOnlyList<Resident> All => s_snapshot;

    public static Resident? Find(nint key) => s_byKey.GetValueOrDefault(key);

    /// <summary>The villager's resident, made (and announced) the first time it is seen.</summary>
    public static Resident Track(IVillagerSource source)
    {
        if (s_byKey.TryGetValue(source.Key, out var known))
        {
            known.Refresh(source);
            return known;
        }
        var resident = new Resident(source);
        s_byKey[source.Key] = resident;
        s_all.Add(resident);
        s_snapshot = [.. s_all];
        Residents.RaiseAdded(resident);
        return resident;
    }

    /// <summary>The villager left: its run ended (its turn and animation let go), forgotten and announced.</summary>
    public static void Forget(nint key)
    {
        if (!s_byKey.Remove(key, out var resident)) return;
        resident.Run?.End();
        resident.Run = null;
        s_all.Remove(resident);
        s_snapshot = [.. s_all];
        Residents.RaiseRemoved(resident);
    }

    /// <summary>
    /// Forgets the villagers whose game objects are gone (a world unloads without telling each); returns their keys.
    /// </summary>
    public static List<nint> Sweep()
    {
        var gone = new List<nint>();
        foreach (var resident in s_snapshot)
        {
            if (resident.IsAlive) continue;
            gone.Add(resident.Key);
            Forget(resident.Key);
        }
        return gone;
    }
}
