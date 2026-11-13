using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.Player;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "combat" scenario (autopilot.once containing "combat"): Mjolnir.SDK.Combat in the hosted world. The combatants listed by
/// kind and near the player; a villager struck by the player (its armour counted), with a handler halving villagers' damage;
/// the player's own damage cancelled by a handler; a pet struck and killed by the player (the killer reported); a villager
/// spawned for it killed by no one; healing; the console's combatants, hurt, heal and kill. Every Damaging, Damaged and Died is
/// logged. It changes the user's save: back it up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> CombatScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Combat: in the main menu {Combatants.All.Count} combatant(s), the player available {LocalPlayer.Combatant.IsAvailable} (0 and false expected)");
        int damaging = 0, damaged = 0, died = 0;
        Combatants.Damaging += (_, e) =>
        {
            if (++damaging <= 30) Log($"Combat: Damaging {e.Target} by {e.Attacker?.ToString() ?? "no one"}: {e.Amount:0.##} {e.Kinds}");
        };
        Combatants.Damaged += (_, e) =>
        {
            if (++damaged <= 30) Log($"Combat: Damaged {e.Target} by {e.Attacker?.ToString() ?? "no one"}: {e.Amount:0.##} taken, {e.Health:0.##} left, fatal {e.IsFatal}, {e.Kinds}");
        };
        Combatants.Died += (_, e) =>
        {
            if (++died <= 30) Log($"Combat: Died {e.Target}, killer {e.Killer?.ToString() ?? "no one"}");
        };

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime();
        yield return 3f;
        var player = LocalPlayer.Combatant;
        if (!player.IsAvailable)
        {
            Log("Combat: no player");
            yield break;
        }
        Log($"Combat: reported while the world loaded and settled: {damaging} Damaging, {damaged} Damaged, {died} Died (no Died for the save's bodies expected)");
        var all = Combatants.All;
        Log($"Combat: {all.Count} combatant(s): {string.Join(", ", all.GroupBy(c => c.Kind).Select(g => $"{g.Key} {g.Count()} ({g.Count(c => c.IsAlive)} alive)"))}; " +
            $"the player {player} health {player.Health:0.#}/{player.MaxHealth:0.#} team {player.Team} {player.Faction}, the same object as LocalPlayer's {all.Contains(player)}");
        foreach (var c in Combatants.Near(player.Position, 40).Take(12))
            Log($"Combat:   near {c.Kind} '{c.DisplayName}' ({c.Name}) {c.Health:0.#}/{c.MaxHealth:0.#} {c.Faction} team {c.Team} friendly {c.IsFriendly} {Vector3.Distance(c.Position, player.Position):0} m");

        // A villager struck by the player, then with villagers' damage halved.
        var resident = Residents.All.Where(r => r.IsAlive).OrderBy(r => Vector3.Distance(r.Position, player.Position)).FirstOrDefault();
        if (resident != null)
        {
            var villager = resident.Combatant;
            Log($"Combat: the villager {villager} is Resident.Combatant {ReferenceEquals(villager, Combatant(resident))}, health {villager.Health:0.##}");
            Log($"Combat: struck by the player for 10: took {villager.Damage(10, player):0.##}, {villager.Health:0.##} left");
            EventHandler<DamagingEventArgs> halve = (_, e) =>
            {
                if (e.Target.Kind == CombatantKind.Resident) e.Amount *= 0.5f;
            };
            Combatants.Damaging += halve;
            Log($"Combat: halved, struck for 10: took {villager.Damage(10, player):0.##}; by no one for 10: took {villager.Damage(10):0.##}");
            Combatants.Damaging -= halve;
            Log($"Combat: healed 100: gave {villager.Heal(100):0.##}, {villager.Health:0.##}/{villager.MaxHealth:0.##}");
        }
        yield return 1f;

        // The player's own damage cancelled, then taken as it is, then healed back.
        EventHandler<DamagingEventArgs> godMode = (_, e) =>
        {
            if (e.Target == LocalPlayer.Combatant) e.Cancel = true;
        };
        Combatants.Damaging += godMode;
        float before = player.Health;
        Log($"Combat: the player with a handler cancelling: took {player.Damage(10):0.##}, {player.Health:0.##} (was {before:0.##})");
        Combatants.Damaging -= godMode;
        Log($"Combat: the player by no one for 10: took {player.Damage(10):0.##}; healed back {player.Heal(10):0.##}, {player.Health:0.##}");
        yield return 1f;

        // A pet struck and killed by the player.
        var pet = Combatants.All.Where(c => c.Kind == CombatantKind.Pet && c.IsAlive).OrderBy(c => Vector3.Distance(c.Position, player.Position)).FirstOrDefault();
        if (pet != null)
        {
            Log($"Combat: the pet {pet} {Vector3.Distance(pet.Position, player.Position):0} m away, struck for 10: took {pet.Damage(10, player):0.##}, {pet.Health:0.##}/{pet.MaxHealth:0.##}");
            pet.Kill(player);
            yield return 1f;
            Log($"Combat: … killed: alive {pet.IsAlive}, health {pet.Health:0.##}");
        }

        // A villager spawned for it, killed by no one.
        int count = Residents.All.Count;
        var front = player.Position + Camera.main!.transform.forward * 3f;
        Log($"Combat: spawn asked {Residents.Spawn(front)}");
        foreach (float wait in WaitFor("a villager to kill", () => Residents.All.Count > count, 15)) yield return wait;
        yield return 2f;
        var fresh = Residents.All.OrderBy(r => Vector3.Distance(r.Position, front)).FirstOrDefault()?.Combatant;
        if (fresh != null)
        {
            fresh.Kill();
            yield return 2f;
            Shot("combat-killed");
            Log($"Combat: the spawned villager {fresh} killed: alive {fresh.IsAlive}");
        }

        // The console.
        foreach (string line in new[] { "combatants 15", "hurt 5", "heal", "hurt 5 near", "heal near" })
        {
            DevConsole.Execute(line);
            yield return 0.5f;
        }
        Log($"Combat: events: {damaging} Damaging, {damaged} Damaged, {died} Died");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static Combatant Combatant(Resident resident) => resident.Combatant;
}
