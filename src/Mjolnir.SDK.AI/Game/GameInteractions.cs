using System;
using System.Collections.Generic;
using SandSailorStudio;
using SSSGame;
using UnityEngine;

namespace Mjolnir.SDK.AI;

/// <summary>
/// The game's side of <see cref="Interactions"/>. The player uses an interaction of
/// a mod as the game's own on a building: a <c>SimpleInteraction</c> put into the building's own interaction area, beside
/// what the game has there (a well's «Drink» and «Collect», a house's «Show radius»). The game then outlines the building,
/// shows its card and the prompt, and takes them away when the player looks elsewhere, as for any building; an area made in
/// code was never touched by the game's picking and left its prompt on everything after (seen by the user, 2026-11-11). On an
/// object that is no building, villagers use the interaction and the player does not. When the player's session ends as
/// used, the interaction reports it. Villagers do not go through the game's sessions: a job's Use step walks, waits and
/// acts (<see cref="JobRun"/>).
/// </summary>
internal static class GameInteractions
{
    private static readonly List<IGameInteraction> s_targets = [];
    private static readonly Dictionary<nint, ModInteraction> s_bySimple = [];
    private const float PromptLookedForAgain = 5f; // s
    private static InteractSlot? s_slot;
    private static Sprite? s_icon;
    private static InteractionMode s_mode = InteractionMode.CONTINUOUS;
    private static float s_nextPromptLook;
    private static bool s_promptWarned;
    private const float AgentLookedForAgain = 1f; // s
    private static PlayerInteractionAgent? s_agent;
    private static float s_nextAgentLook;

    /// <summary>
    /// The prompt's slot, picture and mode, taken from one of the game's own SimpleInteractions (a marker's). What it gives is
    /// kept, not the interaction itself, which may be a scene's and go with it; while the slot is not there (none loaded yet,
    /// or it went with its scene) it is looked for again, every 5 s at most.
    /// </summary>
    public static (InteractSlot? Slot, Sprite? Icon, InteractionMode Mode) Prompt
    {
        get
        {
            // Unity's == null: a slot destroyed with its scene is looked for again.
            if (s_slot == null && Time.realtimeSinceStartup >= s_nextPromptLook) LookForPrompt();
            return (s_slot == null ? null : s_slot, s_icon == null ? null : s_icon, s_mode);
        }
    }

    private static void LookForPrompt()
    {
        s_nextPromptLook = Time.realtimeSinceStartup + PromptLookedForAgain;
        foreach (var simple in Resources.FindObjectsOfTypeAll<SimpleInteraction>())
        {
            if (simple == null || simple.interactSlot == null) continue;
            s_slot = simple.interactSlot;
            s_icon = simple.widgetIcon;
            s_mode = simple.mode;
            return;
        }
        if (s_promptWarned) return;
        s_promptWarned = true;
        SdkLog.Warn(GameVillagers.Source, "Interactions: the game has no SimpleInteraction loaded to take its prompt from");
    }

    /// <summary>Puts an interaction on its object (main thread): into a building's own area, else for villagers only.</summary>
    public static IInteractionTarget Attach(GameObject target, ModInteraction interaction)
    {
        var area = target.GetComponentInChildren<StructureInteractionArea>(true) ?? target.GetComponentInParent<StructureInteractionArea>();
        IGameInteraction made = area != null ? new BuildingInteraction(target, area, interaction) : new VillagerOnlyInteraction(target);
        s_targets.Add(made);
        if (made is BuildingInteraction building) s_bySimple[building.SimplePointer] = interaction;
        else SdkLog.Info(GameVillagers.Source, $"Interactions: '{target.name}' is no building, so {interaction} is for villagers only (the player uses interactions on buildings)");
        made.Apply();
        return made;
    }

    internal static void Forget(IGameInteraction target)
    {
        s_targets.Remove(target);
        if (target is BuildingInteraction building) s_bySimple.Remove(building.SimplePointer);
    }

    /// <summary>Every frame: the settings shown, gone objects let go, a player's session left running ended.</summary>
    public static void Tick()
    {
        if (s_targets.Count == 0) return;
        for (int i = s_targets.Count - 1; i >= 0; i--)
        {
            var target = s_targets[i];
            if (!target.Exists) target.Remove();
            else target.Apply();
        }
        var agent = LocalAgent();
        if (agent == null) return;
        var looked = agent.GetActiveInteractionArea()?.GetGameObject();
        var player = agent.Cast<IInteractionAgent>();
        foreach (var target in s_targets)
        {
            if (target is BuildingInteraction building) building.WatchPlayer(player, looked != null && looked.Pointer == building.AreaObjectPointer);
        }
    }

    /// <summary>
    /// The player of this machine's interaction agent, null outside a world. Kept while it lives (one per world); without one
    /// it is asked for again once a second at most, since the game's LocalPlayer throws until the session has a player.
    /// </summary>
    private static PlayerInteractionAgent? LocalAgent()
    {
        // Unity's == null: the agent goes with its world.
        if (s_agent != null) return s_agent;
        float now = Time.realtimeSinceStartup;
        if (now < s_nextAgentLook) return null;
        s_nextAgentLook = now + AgentLookedForAgain;
        try
        {
            s_agent = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerInteractionAgent?.TryCast<PlayerInteractionAgent>();
        }
        catch (Exception)
        {
            s_agent = null; // the game's LocalPlayer throws outside a session
        }
        return s_agent == null ? null : s_agent;
    }

    /// <summary>The player's session at an interaction of a mod ended: as used, the interaction reports it.</summary>
    public static void PlayerSessionEnded(InteractionSession session, InteractionSessionState result)
    {
        if (result != InteractionSessionState.ENDED || session.Interaction is not { } simple || !s_bySimple.TryGetValue(simple.Pointer, out var interaction)) return;
        interaction.PlayerUsed();
    }

    /// <summary>A building's view was set up in the world: the mods' interactions for its kind go on it.</summary>
    public static void BuildingSetUp(Structure structure)
    {
        string? kind = structure.Template?.name;
        if (kind == null || !InteractionRegistry.Shared.HasBuildings) return;
        InteractionRegistry.Shared.BuildingAppeared(structure.Pointer, kind, interaction => Attach(structure.gameObject, interaction));
    }
}

/// <summary>An interaction's objects in the game.</summary>
internal interface IGameInteraction : IInteractionTarget
{
    void Apply();
}
