using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Mjolnir.SDK;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.World;
using SandSailorStudio;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.AI;
using SSSGame.AI.FSM;
using SSSGame.UI;
using UnityEngine;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "villagers" scenario (autopilot.once containing "villagers", with -Examples): Mjolnir.SDK.AI with
/// Mjolnir.VillagersExample's job (a villager with nothing to do near the player walks up and greets them) and this mod's
/// jobs lab_pause and lab_worker (they ask 30 and 20 for the villager named by <see cref="LabPauseFor"/> and
/// <see cref="LabWorker"/>). In the settlement, at a working hour, a villager is spawned through Villagers.Spawn for the jobs
/// among its quests and its job's state machine (shots/graphs.txt), then the scenario follows who greets: the greeting
/// (screenshot), another greeting interrupted by lab_pause and taken up again, and a worker leaving its work for lab_worker
/// and going back to it. The save is written, the game goes back to the main menu and loads it again: the villagers are
/// there. The console's villagers lists them. It writes the user's save: back it up first, put it back after.
/// </summary>
internal static partial class Autopilot
{
    private const string GreetJob = "mjolnir.villagersexample.greet";
    private const string PauseJob = "mjolnir.test.uilab.lab_pause";

    /// <summary>The villager the lab_pause job asks for; its last step clears it.</summary>
    public static Resident? LabPauseFor;

    /// <summary>The villager the lab_worker job asks for; its last step clears it.</summary>
    public static Resident? LabWorker;

    // The game's quests that are a villager's work, asking 15 in a working settlement.
    private static readonly string[] WorkQuests =
        ["GatherAndHarvestQuest", "FarmingQuestTill", "FarmingQuestPlant", "FarmingQuestGather", "CookingQuest", "LightkeepingQuest", "RepairQuest", "ForesterPlantQuest", "CraftingQuest"];

    private static IEnumerator<float> VillagersScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 4f;
        var events = new List<string>();
        Residents.Added += (_, e) => events.Add($"{Time.realtimeSinceStartup:0.0} added {e.Resident.Name}");
        Residents.Removed += (_, e) => events.Add($"{Time.realtimeSinceStartup:0.0} removed {e.Resident.Name}");
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null)
        {
            Log("Villagers: no player");
            yield break;
        }
        Log($"Villagers: {Residents.All.Count} in the world before");
        KeepDaytime(); // the settlement at work, not asleep
        yield return 5f;

        // A spawned villager: its quests and the SDK's among them.
        var first = default(Resident);
        foreach (float wait in SpawnOne(character, 8f, r => first = r)) yield return wait;
        if (first == null) yield break;
        Log($"Villagers: spawned '{first.Name}', alive {first.IsAlive}, activity '{first.Activity}'; its jobs {JobsOf(first)}");
        DumpJobGraph(first);

        // Somebody with nothing to do near the player walks up and greets them; work asks more than the greeting.
        Resident? greeter = null;
        foreach (float wait in WaitFor("a greeting", () => (greeter = Greeter(null)) != null, 120)) yield return wait;
        Log($"Villagers: the greeter {Watch(greeter!, character)}; asking {Asking(greeter!)}");
        foreach (float wait in WaitFor("its end", () => greeter!.Activity != GreetJob, 40)) yield return wait;
        Shot("villagers-greeting"); // the job's last step shows the notification
        yield return AfterShot;
        yield return 3f;
        Log($"Villagers: after the greeting {Watch(greeter!, character)} (a game quest expected: the job waits its minutes)");

        // Another greeting is interrupted by a job that asks more, then taken up again.
        Resident? second = null;
        foreach (float wait in WaitFor("another greeting", () => (second = Greeter(greeter)) != null, 120)) yield return wait;
        LabPauseFor = second;
        Log($"Villagers: lab_pause asked; {Watch(second!, character)}");
        for (int i = 0; i < 10; i++)
        {
            yield return 1f;
            Log($"Villagers: pause +{i + 1} s {Watch(second!, character)}, lab_pause wanted {LabPauseFor != null}; asking {Asking(second!)}");
        }
        for (int i = 0; i < 30 && second!.Activity != GreetJob; i++) yield return 1f;
        Log($"Villagers: after the pause {Watch(second!, character)}; asking {Asking(second!)}");

        // A worker at its work leaves it for a job that asks more, and goes back to it.
        var worker = Residents.All.FirstOrDefault(r => WorkQuests.Contains(r.Activity));
        if (worker == null) Log($"Villagers: nobody at work ({string.Join(", ", Residents.All.Select(r => r.Activity).Distinct())})");
        else
        {
            string work = worker.Activity;
            Log($"Villagers: the worker {Watch(worker, character)}; asking {Asking(worker)}");
            LabWorker = worker;
            for (int i = 0; i < 40 && (LabWorker != null || i < 20) && worker.Activity != work; i++)
            {
                yield return 1f;
                Log($"Villagers: worker +{i + 1} s {Watch(worker, character)}, job wanted {LabWorker != null}");
            }
            for (int i = 0; i < 30 && LabWorker != null; i++)
            {
                yield return 1f;
                Log($"Villagers: worker +{i + 1} s {Watch(worker, character)}, job wanted {LabWorker != null}");
            }
            for (int i = 0; i < 30 && worker.Activity != work; i++)
            {
                yield return 1f;
                if (i % 3 == 0) Log($"Villagers: after the job +{i + 1} s {Watch(worker, character)}");
            }
            Log($"Villagers: the worker afterwards {Watch(worker, character)}; asking {Asking(worker)}");
        }
        DevConsole.Execute("villagers");
        yield return 0.5f;

        // Saved, back in the main menu, loaded again: the villagers are there and get the jobs again.
        var storage = GameManagers.Get<StorageManager>();
        if (storage != null)
        {
            var task = storage.SaveCurrentSession();
            foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
            Log($"Villagers: saved, {storage.LastSaveOperationCode}");
            MainMenuReady = false;
            HudReady = false;
            Menu.Show<GamePauseMenu>(null);
            yield return 1.5f;
            Menu.GetInstance<GamePauseMenu>().BtnQuitToMainMenu();
            yield return 1.5f;
            if (Menu.GetInstance<ConfirmActionMenu>() is { } confirm) Click(confirm.ConfirmButton.gameObject);
            foreach (float wait in WaitFor("main menu again", () => MainMenuReady, 180)) yield return wait;
            yield return 3f;
            Log($"Villagers: in the main menu {Residents.All.Count} villager(s) (0 expected)");
            foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
            yield return 4f;
            character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
            Log($"Villagers: loaded again, {Residents.All.Count} villager(s): {string.Join("; ", Residents.All.Select(r => $"{r.Name} '{r.Activity}', jobs {JobsOf(r)}"))}");
            for (int i = 0; i < 20 && character != null; i++)
            {
                yield return 1f;
                if (i % 2 == 1) Log($"Villagers: reloaded +{i + 1} s {string.Join("; ", Residents.All.Select(r => Watch(r, character)))}");
            }
        }
        Log($"Villagers: events [{string.Join("; ", events)}]");
        Log($"Villagers: state changes ({VillagerProbes.States.Count}) {string.Join(" > ", VillagerProbes.States.Take(100))}");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // A villager on the example's greeting, not the one given.
    private static Resident? Greeter(Resident? other) => Residents.All.FirstOrDefault(r => r != other && r.Activity == GreetJob);

    // A villager spawned in front of the player, handed back once Villagers.Added announced it.
    private static IEnumerable<float> SpawnOne(PlayerCharacter character, float distance, Action<Resident> found)
    {
        var body = character.transform;
        var forward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
        var at = GroundPoint(body.position + forward * distance) ?? body.position + forward * distance;
        int before = Residents.All.Count;
        Log($"Villagers: Spawn at {P(at)} asked {Residents.Spawn(at)}");
        foreach (float wait in WaitFor("the new villager", () => Residents.All.Count > before, 15)) yield return wait;
        var resident = Residents.All[^1];
        VillagerProbes.Tracked = Game(resident)?._questRunner?.fsmController?.Pointer ?? IntPtr.Zero;
        found(resident);
    }

    private static Villager? Game(Resident resident) =>
        Object.FindObjectsOfType<Villager>().FirstOrDefault(v => v.GetName() == resident.Name && Vector3.Distance(v.transform.position, resident.Position) < 0.5f);

    private static float Distance(Resident resident, PlayerCharacter character)
    {
        var a = resident.Position;
        var b = character.transform.position;
        return new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }

    private static string Watch(Resident resident, PlayerCharacter character) =>
        $"{resident.Name}: '{resident.Activity}' job {resident.ModJob?.ToString() ?? "none"}, {Distance(resident, character):0.0} m from the player, state '{Game(resident)?._questRunner?.fsmController?._currentState?.name}'";

    // Every quest of the villager that wants it now, with its priority: what a job competes with.
    private static string Asking(Resident resident)
    {
        var runner = Game(resident)?._questRunner;
        if (runner == null) return "-";
        var asking = new List<(float Priority, string Name)>();
        var each = runner._questDataTable.GetEnumerator();
        while (each.MoveNext())
        {
            var pair = each._current; // Current reads as ambiguous in an IDE: the interop has two of that name
            float priority = float.NaN;
            try { priority = pair.Key.GetPriority(pair.Value); } catch (Exception) { }
            if (!(priority <= -1f)) asking.Add((priority, pair.Key.GetIl2CppType().Name));
        }
        return string.Join(", ", asking.OrderByDescending(a => a.Priority).Select(a => $"{a.Name} {a.Priority:0.##}"));
    }

    // The SDK's job quests among the villager's quests (CaveCollapsingQuests with a "Mjolnir job" state machine), with
    // their priorities now.
    private static string JobsOf(Resident resident)
    {
        var runner = Game(resident)?._questRunner;
        if (runner == null) return "-";
        var names = new List<string>();
        var each = runner._questDataTable.GetEnumerator();
        while (each.MoveNext())
        {
            var pair = each._current; // Current reads as ambiguous in an IDE: the interop has two of that name
            var quest = pair.Key;
            if (!IsJobQuest(quest, pair.Value, out var graph)) continue;
            float priority = float.NaN;
            try { priority = quest.GetPriority(pair.Value); } catch (Exception) { }
            names.Add($"{quest.GetIl2CppType().Name} '{graph}' {priority:0.##}");
        }
        return $"{names.Count}: {string.Join(", ", names)}";
    }

    private static bool IsJobQuest(Quest quest, QuestData data, out string graph)
    {
        graph = "";
        try { graph = quest.GetFSMBehavior(data)?.name ?? ""; } catch (Exception) { }
        return graph.StartsWith("Mjolnir job", StringComparison.Ordinal);
    }

    // The state machine of each SDK job quest of the villager (shots/graphs.txt).
    private static void DumpJobGraph(Resident resident)
    {
        var runner = Game(resident)?._questRunner;
        if (runner == null) return;
        var text = new StringBuilder();
        var each = runner._questDataTable.GetEnumerator();
        while (each.MoveNext())
        {
            var pair = each._current; // Current reads as ambiguous in an IDE: the interop has two of that name
            if (!IsJobQuest(pair.Key, pair.Value, out _)) continue;
            var graph = pair.Key.GetFSMBehavior(pair.Value);
            text.Append($"=== graph '{graph?.name}' ({graph?.states?.Count} states)\n");
            for (int i = 0; graph?.states != null && i < graph.states.Count; i++)
            {
                var state = graph.states[i];
                text.Append($"  [{i}] '{state.Name}' noAny {state.noAnyStateTransition} default -> '{state.defaultTransition?.Name}'; actions {string.Join(", ", Enumerable.Range(0, state.actions.Count).Select(k => state.actions[k]?.GetIl2CppType().Name))}\n");
                for (int k = 0; k < state.transitions.Count; k++)
                {
                    var t = state.transitions[k];
                    text.Append($"      transition if {string.Join(" & ", Enumerable.Range(0, t.decisions?.Count ?? 0).Select(d => $"{(t.decisions![d].trueValue ? "" : "!")}{t.decisions[d].decision?.GetIl2CppType().Name}"))} -> '{t.trueState?.Name}' ({t.transitionType}/{t.evaluationType})\n");
                }
            }
        }
        File.WriteAllText(Path.Combine(ShotsDir, "graphs.txt"), text.ToString());
        Log($"Villagers: job graphs dumped to graphs.txt");
    }
}

/// <summary>Probes for the "villagers" scenario: the tracked villager's state changes.</summary>
[HarmonyPatch]
internal static class VillagerProbes
{
    public static IntPtr Tracked;
    public static readonly List<string> States = [];

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FSMBehaviourController), nameof(FSMBehaviourController.ChangeState))]
    private static void ChangeState(FSMBehaviourController __instance, vFSMState __0)
    {
        if (__0 != null && Tracked != IntPtr.Zero && __instance.Pointer == Tracked && States.Count < 300)
            States.Add($"{Time.realtimeSinceStartup:0.0} {__0.name}");
    }
}
