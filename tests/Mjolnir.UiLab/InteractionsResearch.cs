using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Mjolnir.SDK.AI;
using SSSGame;
using SSSGame.AI.FSM;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "interactions research" scenario (autopilot.once containing "interactions research"): how villagers and the player use
/// the game's interactions. In the hosted world: every Interaction loaded by its type (and the components beside it), each
/// SimpleInteraction's fields, a villager's and the player's interaction configurations; probes on the use and the sessions
/// (<see cref="InteractionProbes"/>) while the settlement works; then a SimpleInteraction made in code in front of the player,
/// used by the nearest villager and by the player. Into shots/interactions.txt and the log. It loads the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> InteractionsResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        KeepDaytime();
        yield return 3f;
        var player = GameManagers.Get<PlayerManager>()?.LocalPlayer;
        var character = player?.playerCharacter;
        if (character == null)
        {
            Log("Interactions: no player");
            yield break;
        }
        var text = new StringBuilder();
        var all = Object.FindObjectsByType<Interaction>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).ToList();
        text.AppendLine($"interactions {all.Count}:");
        foreach (var group in all.GroupBy(i => i.GetIl2CppType().FullName).OrderByDescending(g => g.Count()))
        {
            var i = group.First();
            text.AppendLine($"  {group.Key} x{group.Count()}: e.g. '{PathOf(i.transform)}' mode {i.mode} order {i.order} access {i.accessDistance} max {i.MaxNumberOfInteractors} " +
                            $"host '{(i.host != null ? i.host.name : "-")}' interactable {i.interactable != null} | {Components(i.gameObject)}");
        }
        foreach (var simple in all.Select(i => i.TryCast<SimpleInteraction>()).Where(s => s != null).Take(40))
            text.AppendLine($"  simple '{PathOf(simple!.transform)}': action '{simple.actionName}' id {simple.actionId} time {simple.actionTime} hold {simple.holdTargetTime} " +
                            $"match {simple.hasMatchTarget} ({(simple.matchTarget != null ? simple.matchTarget.name : "-")}, {simple.matchTargetTime}, strict {simple.strictMatchTarget}) " +
                            $"message '{simple.widgetMessage}' icon {(simple.widgetIcon != null ? simple.widgetIcon.name : "-")} slot {(simple.interactSlot != null ? simple.interactSlot.name : "-")} | parents {Components(simple.transform.parent?.gameObject)}");

        var villagers = Object.FindObjectsOfType<Villager>().Where(v => !v.IsDead).ToList();
        var villager = villagers.OrderBy(v => Vector3.Distance(v.transform.position, character.transform.position)).FirstOrDefault();
        if (villager != null)
        {
            text.AppendLine($"villager {villager.name} configs:");
            foreach (var config in Configs(villager.interactions))
                text.AppendLine($"  {config.GetIl2CppType().FullName} '{config.name}' restriction {config.maxRestrictionLevel} hold {config.UsesHoldInput}");
            text.AppendLine($"villager own interactions: {string.Join(", ", villager.villagerInteractions?.ToArray().Select(i => $"{i.GetIl2CppType().Name} '{i.name}'") ?? [])}");
        }
        var agent = player!.playerInteractionAgent?.TryCast<PlayerInteractionAgent>();
        if (agent != null)
        {
            text.AppendLine("player configs:");
            foreach (var config in Configs(agent.interactions))
                text.AppendLine($"  {config.GetIl2CppType().FullName} '{config.name}' restriction {config.maxRestrictionLevel} hold {config.UsesHoldInput}");
        }
        File.WriteAllText(System.IO.Path.Combine(ShotsDir, "interactions.txt"), text.ToString());
        Log("Interactions: " + text.ToString().Replace("\n", " | "));

        Log($"Interactions: probes {UiLab.Instance.Harmony.CreateClassProcessor(typeof(InteractionProbes)).Patch()?.Count ?? 0} method(s) patched");
        InteractionProbes.Watching = true;
        yield return 40f;
        Log($"Interactions: in 40 s {InteractionProbes.Count} probe line(s)");

        // A SimpleInteraction of our own in front of the player, with an action copied from one of the game's.
        var model = all.Select(i => i.TryCast<SimpleInteraction>()).FirstOrDefault(s => s != null && !string.IsNullOrEmpty(s.actionName));
        var place = character.transform.position + character.transform.forward * 3f;
        var holder = new GameObject("UiLab_Interaction");
        holder.transform.position = place;
        var box = holder.AddComponent<BoxCollider>();
        box.size = Vector3.one;
        var ours = holder.AddComponent<SimpleInteraction>();
        ours.actionName = model?.actionName ?? "";
        ours.actionId = model?.actionId ?? 0;
        ours.actionTime = 4f;
        ours.holdTargetTime = model?.holdTargetTime ?? 1f;
        ours.accessDistance = 2f;
        ours.widgetMessage = "UiLab";
        ours.host = holder;
        Log($"Interactions: ours made with action '{ours.actionName}' id {ours.actionId}, mode {ours.mode}, max {ours.MaxNumberOfInteractors}, pickable {ours.GetPickable() != null}");

        if (villager != null)
        {
            var va = villager.Cast<IInteractionAgent>();
            var config = villager.GetInteractionConfiguration(ours);
            Log($"Interactions: the villager {villager.name} {Vector3.Distance(villager.transform.position, place):0.#} m away, config {(config != null ? config.GetIl2CppType().Name : "none")}, check {Try(() => ours.CheckAgent(va))}");
            bool used = false;
            try
            {
                used = ours.Use(va);
            }
            catch (Exception e)
            {
                Log($"Interactions: villager Use threw {e.GetType().Name} {e.Message}");
            }
            Log($"Interactions: villager Use {used}, sessions {ours.ActiveSessionsCount}");
            for (int n = 0; n < 12; n++)
            {
                yield return 0.5f;
                var session = ours.FindActiveSession(va);
                Log($"Interactions:   {n * 0.5f + 0.5f:0.0} s: session {(session != null ? $"{session.GetIl2CppType().Name} {session.SessionState}" : "none")}, active {ours.ActiveSessionsCount}, at {Vector3.Distance(villager.transform.position, place):0.#} m");
            }
        }

        if (agent != null)
        {
            character.transform.position = place - character.transform.forward * 1.5f;
            yield return 1f;
            var config = agent.GetInteractionConfiguration(ours);
            Log($"Interactions: the player's config {(config != null ? config.GetIl2CppType().Name : "none")}, check {Try(() => ours.CheckAgent(agent.Cast<IInteractionAgent>()))}");
            bool remote = false;
            try
            {
                remote = agent.TryRemoteInteract(ours);
            }
            catch (Exception e)
            {
                Log($"Interactions: player TryRemoteInteract threw {e.GetType().Name} {e.Message}");
            }
            Log($"Interactions: player TryRemoteInteract {remote}, sessions {ours.ActiveSessionsCount}");
            for (int n = 0; n < 12; n++)
            {
                yield return 0.5f;
                var session = ours.FindActiveSession(agent.Cast<IInteractionAgent>());
                Log($"Interactions:   {n * 0.5f + 0.5f:0.0} s: player's session {(session != null ? $"{session.GetIl2CppType().Name} {session.SessionState}" : "none")}");
            }
        }
        InteractionProbes.Watching = false;
        Object.Destroy(holder);
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static IEnumerable<InteractionConfiguration> Configs(InteractionConfigurationList? list)
    {
        if (list?.configs == null) yield break;
        foreach (var c in list.configs)
            if (c != null) yield return c;
    }

    private static string Try(Func<bool> check)
    {
        try
        {
            return check().ToString();
        }
        catch (Exception e)
        {
            return $"threw {e.GetType().Name}";
        }
    }

    private static string PathOf(Transform? t)
    {
        var names = new List<string>();
        for (int n = 0; t != null && n < 4; n++, t = t.parent) names.Insert(0, t.name);
        return string.Join("/", names);
    }

    private static string Components(GameObject? go)
    {
        if (go == null) return "-";
        return string.Join(", ", go.GetComponents<Component>().Select(c => c.GetIl2CppType().Name));
    }
}

/// <summary>Logs the game's use of interactions and their sessions (the first lines of each kind).</summary>
[HarmonyPatch]
internal static class InteractionProbes
{
    public static bool Watching;
    public static int Count;
    public static string? Only; // log only lines naming this
    private static readonly Dictionary<string, int> s_seen = [];

    private static void Line(string kind, string text)
    {
        if (!Watching || (Only != null && !text.Contains(Only))) return;
        s_seen.TryGetValue(kind, out int n);
        s_seen[kind] = n + 1;
        if (n >= (Only != null ? 40 : 6)) return;
        Count++;
        UiLab.Instance.Log.Info($"[UiLab] Interactions probe {kind}: {text}");
    }

    private static string Who(IInteractionAgent? agent)
    {
        if (agent == null) return "nobody";
        try
        {
            var t = agent.GetTransform();
            return t != null ? t.name : "?";
        }
        catch (Exception)
        {
            return "?";
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Villager), nameof(Villager.GetInteractionSession))]
    private static void VillagerSession(Villager __instance, Interaction __0, InteractionSession __result) =>
        Line("Villager.GetInteractionSession", $"{__instance.name} for {__0?.GetIl2CppType().Name} '{__0?.name}' → {__result?.GetIl2CppType().Name ?? "null"}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VillagerSimpleInteractionConfig.VillagerSimpleInteractionSession), nameof(InteractionSession.Start))]
    private static void VillagerSimpleStart(InteractionSession __instance) =>
        Line("VillagerSimple.Start", $"{Who(__instance.Agent)} at '{__instance.Interaction?.name}' state {__instance.SessionState}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VillagerSimpleInteractionConfig.VillagerSimpleInteractionSession), nameof(InteractionSession.End))]
    private static void VillagerSimpleEnd(InteractionSession __instance, InteractionSessionState __0) =>
        Line("VillagerSimple.End", $"{Who(__instance.Agent)} at '{__instance.Interaction?.name}' with {__0}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerSimpleInteractionConfig.PlayerSimpleInteractionSession), nameof(InteractionSession.Start))]
    private static void PlayerSimpleStart(InteractionSession __instance) =>
        Line("PlayerSimple.Start", $"{Who(__instance.Agent)} at '{__instance.Interaction?.name}' state {__instance.SessionState}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerSimpleInteractionConfig.PlayerSimpleInteractionSession), nameof(InteractionSession.End))]
    private static void PlayerSimpleEnd(InteractionSession __instance, InteractionSessionState __0) =>
        Line("PlayerSimple.End", $"{Who(__instance.Agent)} at '{__instance.Interaction?.name}' with {__0}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerSimpleInteractionConfig.PlayerSimpleInteractionSession), nameof(InteractionSession.Touch))]
    private static void PlayerSimpleTouch(InteractionSession __instance) =>
        Line("PlayerSimple.Touch", $"{Who(__instance.Agent)} at '{__instance.Interaction?.name}' state {__instance.SessionState}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerSimpleInteractionConfig.PlayerSimpleInteractionSession), nameof(InteractionSession.ResetSession))]
    private static void PlayerSimpleReset(InteractionSession __instance) =>
        Line("PlayerSimple.ResetSession", $"{Who(__instance.Agent)} at '{__instance.Interaction?.name}' state {__instance.SessionState}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerSimpleInteractionConfig.PlayerSimpleInteractionSession), "_SimpleInput")]
    private static void PlayerSimpleInput(InteractionSession __instance) =>
        Line("PlayerSimple._SimpleInput", $"{Who(__instance.Agent)} at '{__instance.Interaction?.name}' state {__instance.SessionState}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Interaction), "_TryGetSession")]
    private static void TryGetSession(Interaction __instance, IInteractionAgent __0, InteractionSession __1, bool __result) =>
        Line("Interaction._TryGetSession", $"{Who(__0)} at {__instance.GetIl2CppType().Name} '{__instance.name}' → {__result} {__1?.GetIl2CppType().Name} {__1?.SessionState}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Interaction), "_TryStartSession")]
    private static void TryStartSession(Interaction __instance, InteractionSession __0, bool __result) =>
        Line("Interaction._TryStartSession", $"{Who(__0?.Agent)} at '{__instance.name}' → {__result}, mode {__instance.GetStartMode(__0)}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Interaction), nameof(Interaction.Use))]
    private static void Use(Interaction __instance, IInteractionAgent __0, bool __result) =>
        Line("Interaction.Use", $"{Who(__0)} at {__instance.GetIl2CppType().Name} '{__instance.name}' → {__result}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SimpleInteraction), nameof(SimpleInteraction.Use))]
    private static void SimpleUse(SimpleInteraction __instance, IInteractionAgent __0, bool __result) =>
        Line("SimpleInteraction.Use", $"{Who(__0)} at '{__instance.name}' → {__result}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Interaction), "_StartSession")]
    private static void StartSession(Interaction __instance, InteractionSession __0) =>
        Line("Interaction._StartSession", $"{Who(__0?.Agent)} at {__instance.GetIl2CppType().Name} '{__instance.name}' session {__0?.GetIl2CppType().Name}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Interaction), nameof(Interaction.Finish))]
    private static void Finish(Interaction __instance, IInteractionAgent __0, InteractionSessionState __1) =>
        Line("Interaction.Finish", $"{Who(__0)} at {__instance.GetIl2CppType().Name} '{__instance.name}' with {__1}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FSM_InteractWithQuestInteraction), nameof(FSM_InteractWithQuestInteraction.OnStateEnter))]
    private static void QuestInteraction(FSM_InteractWithQuestInteraction __instance, IFSMBehaviourController __0) =>
        Line("FSM_InteractWithQuestInteraction", $"'{__instance.name}' result '{__instance.resultName}' to do {__instance.interactionToDo?.GetIl2CppType().Name}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharacterGeometry), nameof(CharacterGeometry.RunTimedAction))]
    private static void TimedAction(CharacterGeometry __instance, ref string __0, float __1, int __2) =>
        Line("RunTimedAction", $"{__instance.name}: '{__0}' {__1} s id {__2}");
}
