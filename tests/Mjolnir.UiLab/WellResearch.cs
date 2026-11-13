using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using SSSGame;
using SSSGame.AI;
using SSSGame.AI.FSM;
using SSSGame.Render;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "well research" scenario: how the game's water well works, the model for a mod's altar (the user's idea, 2026-11-11):
/// a structure the player and villagers both use. In the hosted world: the wells (GatherInteraction with a WaterLevelHelper),
/// one well's hierarchy (components, layers, colliders), its interaction areas and every interaction's fields, its template,
/// the player's and a villager's configurations for them, a villager's survival quests (attribute, items, structure, graph and
/// its states); then a minute of probes: who uses a well, with which session, in which quest and state. Into shots/well.txt
/// and the log; it quits when done. It loads the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> WellResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime();
        yield return 3f;
        var text = new StringBuilder();
        var player = GameManagers.Get<PlayerManager>()?.LocalPlayer;
        var agent = player?.playerInteractionAgent?.TryCast<PlayerInteractionAgent>();

        var gathers = Object.FindObjectsByType<GatherInteraction>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).ToList();
        text.AppendLine($"gather interactions {gathers.Count}:");
        foreach (var group in gathers.GroupBy(g => g.GetComponentInParent<Structure>()?.name ?? $"(no structure) {g.transform.root.name}").OrderBy(g => g.Key))
            text.AppendLine($"  {group.Key} x{group.Count()}: '{group.First().name}' item {group.First().GetGatherableItemInfo()?.name} water level {group.First().GetComponent<SandSailorStudio.Utils.WaterLevelHelper>() != null}");
        var well = gathers.Select(g => g.GetComponentInParent<Structure>()).Where(s => s != null && s.name.IndexOf("Well", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(s => agent != null ? Vector3.Distance(s!.transform.position, agent.transform.position) : 0).FirstOrDefault();
        if (well == null)
        {
            text.AppendLine("no well");
            File.WriteAllText(Path.Combine(ShotsDir, "well.txt"), text.ToString());
            Log("Well: no well");
            yield break;
        }
        var template = well.Template;
        text.AppendLine();
        text.AppendLine($"well '{well.name}' ({well.GetIl2CppType().FullName}) template '{template?.name}' ({template?.GetIl2CppType().Name}) id {well.TemplateID}, name '{well.StructureName}', default '{well.DefaultName}'");
        var node = template?.TryCast<NodeTemplate>();
        text.AppendLine($"  node structure '{node?.nodeStructure?.name}', preview '{node?.nodePreview?.name}', placement tool '{node?.placementTool?.name}', category '{template?.category?.name}'");
        text.AppendLine("hierarchy:");
        Dump(text, well.transform, 0, 7);

        text.AppendLine("areas:");
        foreach (var area in well.GetComponentsInChildren<StandaloneInteractionArea>(true))
        {
            text.AppendLine($"  {area.GetIl2CppType().Name} on '{PathOf(area.transform)}' layer {LayerMask.LayerToName(area.gameObject.layer)}: name '{Safe(area.GetName)}' description '{Safe(area.GetDescription)}' description asset {(area.areaDescription != null ? area.areaDescription.GetIl2CppType().Name : "-")}, interactions {area.interactions?.Count}, pickables {area.pickables?.Count}");
            foreach (var pick in area.pickables?.ToArray() ?? Array.Empty<Pickable>())
                text.AppendLine($"    pickable '{PathOf(pick?.transform)}' objects {pick?.pickableObjects?.Count} volumes [{string.Join(", ", pick?.pickableVolumes?.ToArray().Select(v => $"{v.GetIl2CppType().Name} on '{v.name}' {LayerMask.LayerToName(v.gameObject.layer)}") ?? [])}] config {(pick?.config != null ? pick.config.name : "-")}");
            foreach (var interaction in area.interactions?.ToArray() ?? Array.Empty<Interaction>())
                text.AppendLine("    " + Describe(interaction, agent, null));
        }
        var villager = Object.FindObjectsOfType<Villager>().Where(v => !v.IsDead).OrderBy(v => Vector3.Distance(v.transform.position, well.transform.position)).FirstOrDefault();
        if (villager != null)
        {
            text.AppendLine($"configs of the player and of {villager.name} for the well's interactions:");
            foreach (var interaction in well.GetComponentsInChildren<Interaction>(true))
                text.AppendLine($"  {interaction.GetIl2CppType().Name} '{interaction.name}': player {Config(agent?.GetInteractionConfiguration(interaction))}, villager {Config(villager.GetInteractionConfiguration(interaction))}");
            text.AppendLine($"survival quests of {villager.name}:");
            var runner = villager._questRunner;
            foreach (var quest in Object.FindObjectsOfType<VillagerSurvival>().Where(s => s.GetComponentInParent<Villager>() == villager).SelectMany(_ => Array.Empty<Quest>()))
                text.AppendLine($"  {quest}");
            var table = runner?._questDataTable;
            if (table != null)
            {
                foreach (var entry in table)
                {
                    var survival = entry.Key?.TryCast<SurvivalObjectiveQuest>();
                    if (survival == null) continue;
                    var graph = survival.fsmBehaviour;
                    text.AppendLine($"  SurvivalObjectiveQuest: attribute {(survival.relevantVAttribute != null ? survival.relevantVAttribute.NameToString() : "-")}, items {survival.consumableItems?.name}, emergency {survival.emergencyConsumableItems?.name}, structure {survival.structureTemplate?.name}, " +
                                    $"priorities {survival.priorityHigh}/{survival.priorityLow}/{survival.priorityLowPending}, only consume {survival.onlyTryToConsume}, graph '{graph?.name}' states [{string.Join(", ", graph?.states?.ToArray().Select(s => s.Name) ?? [])}]");
                }
            }
        }
        File.WriteAllText(Path.Combine(ShotsDir, "well.txt"), text.ToString());
        Log("Well: " + text.ToString().Replace("\n", " | "));

        WellProbes.Well = well.Pointer;
        Log($"Well: probes {UiLab.Instance.Harmony.CreateClassProcessor(typeof(WellProbes)).Patch()?.Count ?? 0} method(s) patched");
        WellProbes.Watching = true;
        yield return 60f;
        WellProbes.Watching = false;
        Log($"Well: {WellProbes.Count} use(s) of wells seen in a minute");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static string Config(InteractionConfiguration? config) => config == null ? "none" : $"{config.GetIl2CppType().Name} '{config.name}'";

    internal static string Describe(Interaction i, PlayerInteractionAgent? agent, string? _)
    {
        var s = new StringBuilder($"{i.GetIl2CppType().Name} '{i.name}' order {i.order} mode {i.mode} access {i.accessDistance} max {i.MaxNumberOfInteractors} host '{(i.host != null ? i.host.name : "-")}' interactable {(i.interactable != null ? i.interactable.TryCast<Il2CppSystem.Object>()?.GetIl2CppType().Name : "-")}");
        if (i.TryCast<GatherInteraction>() is { } g)
            s.Append($"; gather {g.GetGatherableItemInfo()?.name} x{g.itemCountPerCharge}/charge, available {g.CheckAvailableItemCount()}/{g.CheckMaximumItemCount()}, use mode {g.AllowsUseMode} '{g.useModeInteractName}' x{g.itemUseCountPerCharge} order {g.UseOrder}, villager orders {g.villagerGatherOrder}/{g.villagerAbortOrder}, moveset {g.Moveset?.name}, direct feed {g.HasDirectFeed()}");
        if (i.TryCast<SimpleInteraction>() is { } simple)
            s.Append($"; simple '{simple.widgetMessage}' hold {simple.holdTargetTime} action '{simple.actionName}'");
        return s.ToString();
    }

    private static void Dump(StringBuilder text, Transform t, int depth, int max)
    {
        if (depth > max) return;
        var colliders = t.GetComponents<Collider>().Select(c => c.TryCast<BoxCollider>() is { } b ? $"Box{(b.isTrigger ? "T" : "")} {b.size.x:0.0}x{b.size.y:0.0}x{b.size.z:0.0}" : c.GetIl2CppType().Name + (c.isTrigger ? "T" : ""));
        text.AppendLine($"{new string(' ', depth * 2 + 2)}{t.name} [{LayerMask.LayerToName(t.gameObject.layer)}{(t.gameObject.activeSelf ? "" : ", inactive")}] {string.Join(", ", t.GetComponents<Component>().Select(c => c.GetIl2CppType().Name).Where(n => n != "Transform"))}{(colliders.Any() ? " | " + string.Join(", ", colliders) : "")}");
        for (int i = 0; i < t.childCount; i++) Dump(text, t.GetChild(i), depth + 1, max);
    }
}

/// <summary>Who uses a well: the agent, the session, its quest and its state.</summary>
[HarmonyPatch]
internal static class WellProbes
{
    public static bool Watching;
    public static int Count;
    public static nint Well;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GatherInteraction), nameof(GatherInteraction.Use))]
    private static void Use(GatherInteraction __instance, IInteractionAgent __0, bool __result)
    {
        if (!Watching || Count >= 25) return;
        var structure = __instance.GetComponentInParent<Structure>();
        if (structure == null || structure.name.IndexOf("Well", StringComparison.OrdinalIgnoreCase) < 0) return;
        Count++;
        string who = "?", quest = "-", state = "-";
        try
        {
            who = __0?.GetTransform()?.name ?? "?";
            var villager = __0?.GetTransform()?.GetComponentInParent<Villager>();
            var runner = villager?._questRunner;
            quest = runner?.ActiveQuest?.GetIl2CppType().Name ?? "-";
            var fsm = runner?.fsmController;
            state = fsm?.CurrentState?.Name ?? "-";
            var actions = fsm?.CurrentState?.actions?.ToArray().Select(a => a.GetIl2CppType().Name) ?? [];
            state += " [" + string.Join(", ", actions) + "]";
        }
        catch (Exception e)
        {
            state = e.GetType().Name;
        }
        var session = __instance.FindActiveSession(__0!);
        UiLab.Instance.Log.Info($"[UiLab] Well probe: {who} at '{__instance.name}' of '{structure.name}' ({(structure.Pointer == Well ? "the one dumped" : "another")}) → {__result}, session {session?.GetIl2CppType().Name ?? "none"}, quest {quest}, state {state}");
    }
}
