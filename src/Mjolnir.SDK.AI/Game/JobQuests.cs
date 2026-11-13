using System;
using System.Collections.Generic;
using SSSGame;
using SSSGame.AI;
using SSSGame.AI.FSM;
using UnityEngine;

namespace Mjolnir.SDK.AI;

/// <summary>
/// Jobs in the game. A job's quest is one of the game's own quest classes,
/// <c>CaveCollapsingQuest</c>, one per villager and job: it returns the state machine it was made with, and a hidden
/// prefix answers its priority for the SDK's own (an injected Quest lost its runner's handler to the game's GC after a
/// world load: the GC did not see the injected object's field, seen 2026-10-28). Its state machine is made as the game
/// makes a quest's (Entry → Start with FSM_QuestStart; the Any State's
/// FSM_HasPendingQuest leaves for Stop, so a quest that asks more interrupts; Stop with FSM_QuestSetStatus and
/// FSM_QuestStop, after which the runner picks again), with a state per step: a walk (the game's FSM_MoveToPosition to a
/// place in the villager's data), a wait, or the mod's code. Main thread.
/// </summary>
internal static partial class JobQuests
{
    private const int Start = -1;

    private static readonly Dictionary<nint, Dictionary<ResidentJob, CaveCollapsingQuest>> s_questsOf = [];
    private static readonly Dictionary<nint, ResidentJob> s_jobOfQuest = [];
    private static readonly Dictionary<nint, float> s_lastPriority = [];
    private static readonly Dictionary<ResidentJob, Machine> s_machines = [];
    private static readonly Dictionary<nint, (ResidentJob Job, Machine Machine, int Index)> s_actions = [];
    private static readonly Dictionary<nint, nint> s_villagerOfController = [];

    /// <summary>
    /// Whether a villager is whole: its quest runner can take quests (AddQuest uses the runner's network session, which a
    /// villager of a save has only after it spawned).
    /// </summary>
    public static bool Ready(Villager villager)
    {
        var runner = villager == null ? null : villager._questRunner;
        return runner != null && runner._session != null && runner.isActiveAndEnabled;
    }

    /// <summary>The job of one of the SDK's quests, null for a game quest.</summary>
    public static ResidentJob? JobOf(nint quest) => s_jobOfQuest.GetValueOrDefault(quest);

    /// <summary>Gives a ready villager every job it lacks.</summary>
    public static void Give(Villager villager)
    {
        if (!GameVillagers.Injected || !Ready(villager)) return;
        var runner = villager._questRunner;
        var jobs = JobRegistry.Shared.Jobs;
        if (!s_questsOf.TryGetValue(villager.Pointer, out var quests))
        {
            if (jobs.Count == 0) return;
            s_questsOf[villager.Pointer] = quests = [];
        }
        foreach (var job in jobs)
        {
            if (quests.ContainsKey(job)) continue;
            var quest = new CaveCollapsingQuest(MachineOf(job).Graph);
            quests[job] = quest;
            s_jobOfQuest[quest.Pointer] = job;
            runner.AddQuest(quest);
        }
    }

    /// <summary>
    /// Tells a villager's quest runner of the jobs whose priority changed. The runner keeps the best quest it found and
    /// hears of a new priority only when told, as the game's quests tell it (ReevaluateQuest): without this a job that
    /// came to ask more waited for the running quest to end (seen 2026-10-31). Being told of the running quest makes the
    /// runner give it up for anything else (its priority drops to -2), so a running job is told of only when it stopped
    /// asking, and never while its villager is at an interaction: it would lose its turn half way. A priority that dropped
    /// meanwhile is seen once the use is over.
    /// </summary>
    public static void Reevaluate(Villager villager)
    {
        if (!s_questsOf.TryGetValue(villager.Pointer, out var quests) || ResidentRoster.Find(villager.Pointer) is not { } resident) return;
        var runner = villager._questRunner;
        foreach (var (job, quest) in quests)
        {
            if (resident.Run is { IsUsing: true } current && current.Job == job && runner.ActiveQuest?.Pointer == quest.Pointer) continue;
            float priority = job.Steps.Count == 0 ? -1f : job.PriorityFor(resident);
            bool known = s_lastPriority.TryGetValue(quest.Pointer, out float last);
            if (known && last == priority) continue;
            s_lastPriority[quest.Pointer] = priority;
            if (runner.ActiveQuest?.Pointer != quest.Pointer || (known && last > -1f && priority <= -1f)) runner.ReevaluateQuest(quest);
        }
    }

    /// <summary>A villager left: its quests and controller are forgotten.</summary>
    public static void Forget(nint villagerKey)
    {
        if (s_questsOf.Remove(villagerKey, out var quests))
        {
            foreach (var quest in quests.Values)
            {
                s_jobOfQuest.Remove(quest.Pointer);
                s_lastPriority.Remove(quest.Pointer);
            }
        }
        var gone = new List<nint>();
        foreach (var (controller, villager) in s_villagerOfController)
        {
            if (villager == villagerKey) gone.Add(controller);
        }
        foreach (var controller in gone) s_villagerOfController.Remove(controller);
    }

    /// <summary>
    /// The hidden prefix on the quest class's priority: for a quest of the SDK's own, the job's priority for the villager
    /// whose runner asks (and the quest is given the job's state machine made again if the job got steps since); false
    /// for the game's own quests of the class.
    /// </summary>
    public static bool Priority(CaveCollapsingQuest quest, QuestData data, out float priority)
    {
        priority = -1f;
        if (!s_jobOfQuest.TryGetValue(quest.Pointer, out var job)) return false;
        try
        {
            var graph = MachineOf(job).Graph;
            if (quest._fsmBehaviour == null || quest._fsmBehaviour.Pointer != graph.Pointer) quest._fsmBehaviour = graph;
            var villager = data?.QuestRunner?.GetComponent<Villager>();
            if (villager == null) return true;
            var resident = ResidentRoster.Find(villager.Pointer) ?? ResidentRoster.Track(new GameVillager(villager));
            priority = job.Steps.Count == 0 ? -1f : job.PriorityFor(resident);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameVillagers.Source, $"Villagers: a job's priority failed: {e}");
        }
        return true;
    }

    /// <summary>A state of a job is entered: the run starts, a step begins, or the run ends.</summary>
    public static void Enter(JobAction action, IFSMBehaviourController fsm)
    {
        try
        {
            if (!s_actions.TryGetValue(action.Pointer, out var at) || ResidentOf(fsm) is not { } resident) return;
            var (job, machine, index) = at;
            if (index == Start)
            {
                resident.Run?.End(); // a run its Stop never reached (a load in the middle)
                resident.Run = new JobRun(job, resident, machine.Steps);
                return;
            }
            if (index >= machine.Steps.Count)
            {
                resident.Run?.End();
                resident.Run = null; // Stop: ended, or another quest took the villager
                return;
            }
            if (resident.Run?.Job != job)
            {
                resident.Run?.End();
                resident.Run = new JobRun(job, resident, machine.Steps);
            }
            var run = resident.Run;
            run.Begin(index, Time.timeAsDouble);
            switch (machine.Steps[index])
            {
                case MoveStep move:
                    if (ModCall.TryRun(job.Owner, $"{job} step {index + 1} place", move.Place, resident, out Vector3 place))
                    {
                        run.Target = (place.x, place.y, place.z);
                        string key = PlaceKey(job, index);
                        fsm.SetData(new FSMData<Vector3>(place), ref key);
                    }
                    break;
                case ApproachStep approach:
                    if (ModCall.TryRun(job.Owner, $"{job} step {index + 1} interaction", approach.Find, resident, out ModInteraction? interaction)
                        && interaction is { IsAvailable: true })
                    {
                        run.Interaction = interaction;
                        var spot = interaction.Position;
                        run.Target = (spot.x, spot.y, spot.z);
                        string key = PlaceKey(job, index);
                        fsm.SetData(new FSMData<Vector3>(spot), ref key);
                        // The walk stops short of the thing (the graph is the job's, so this is the interaction now at hand).
                        if (machine.Walks[index] is { } walk) walk.minimumDistance = Math.Max(0.3f, interaction.Distance * 0.8f);
                    }
                    break;
                case DoStep work:
                    ModCall.Run(job.Owner, $"{job} step {index + 1}", work.Action, resident);
                    break;
                case PlayStep play:
                    var options = play.Duration is { } duration ? JobRun.ForDuration(play.Options, duration) : play.Options;
                    if (play.Find() is { } animation) run.Playback = resident.Animator.Play(animation, options);
                    else SdkLog.Warn(GameVillagers.Source, $"Villagers: {job} step {index + 1}: no animation '{play.Name}'");
                    break;
            }
        }
        catch (Exception e)
        {
            SdkLog.Error(GameVillagers.Source, $"Villagers: a job's step failed to start: {e}");
        }
    }

    /// <summary>A job's state updates: the run moves on to the next step's state, or to Stop after the last.</summary>
    public static void Update(JobAction action, IFSMBehaviourController fsm)
    {
        try
        {
            if (!s_actions.TryGetValue(action.Pointer, out var at) || ResidentOf(fsm) is not { } resident) return;
            var (_, machine, index) = at;
            if (index >= machine.Steps.Count) return;
            if (index == Start)
            {
                fsm.ChangeState(machine.Steps.Count > 0 ? machine.States[0] : machine.Stop);
                return;
            }
            var run = resident.Run;
            if (run == null || run.Index != index)
            {
                fsm.ChangeState(machine.Stop); // a run the SDK lost (a load in the middle): end it cleanly
                return;
            }
            if (run.IsOver(Time.timeAsDouble)) fsm.ChangeState(index + 1 < machine.Steps.Count ? machine.States[index + 1] : machine.Stop);
        }
        catch (Exception e)
        {
            SdkLog.Error(GameVillagers.Source, $"Villagers: a job's step failed: {e}");
        }
    }

    // The resident whose state machine this is: the villager the controller sits on (cached by controller).
    private static Resident? ResidentOf(IFSMBehaviourController fsm)
    {
        nint controller = fsm.Pointer;
        if (!s_villagerOfController.TryGetValue(controller, out nint key))
        {
            var villager = fsm.TryCast<FSMBehaviourController>()?.GetComponentInParent<Villager>();
            if (villager == null) return null;
            key = villager.Pointer;
            s_villagerOfController[controller] = key;
            if (ResidentRoster.Find(key) == null) ResidentRoster.Track(new GameVillager(villager));
        }
        return ResidentRoster.Find(key);
    }
}
