using System.Collections.Generic;
using Il2CppInterop.Runtime;
using SSSGame.AI;
using SSSGame.AI.FSM;
using UnityEngine;

namespace Mjolnir.SDK.AI;

/// <summary>The state machines of jobs, made in code as the game makes a quest's (see the other part).</summary>
internal static partial class JobQuests
{
    private sealed record Machine(int Version, vFSMBehaviour Graph, IReadOnlyList<JobStep> Steps, vFSMState[] States, vFSMState Stop, FSM_MoveToPosition?[] Walks)
    {
        /// <summary>Its steps' injected actions, by pointer (the keys of the actions' table).</summary>
        public List<nint> Actions { get; } = [];
    }

    // The machine a job had before its last one: a villager may still be in its states until it picks again.
    private static readonly Dictionary<ResidentJob, Machine> s_previous = [];

    private static Machine MachineOf(ResidentJob job)
    {
        int version = job.Version;
        if (s_machines.TryGetValue(job, out var made))
        {
            if (made.Version == version) return made;
            // Made again (the job got steps): the machine before the last one is let go of.
            if (s_previous.Remove(job, out var older))
            {
                foreach (nint action in older.Actions) s_actions.Remove(action);
            }
            s_previous[job] = made;
        }
        var steps = job.Steps;
        var graph = Make<vFSMBehaviour>($"Mjolnir job {job}");
        var entry = State(graph, "Entry");
        var any = State(graph, "Any State");
        var start = State(graph, "Start", Make<FSM_QuestStart>("Quest Start"));
        var status = Make<FSM_QuestSetStatus>("Set Quest Status");
        status.questStatus = QuestStatus.Completed;
        var stop = State(graph, "Stop", status, Make<FSM_QuestStop>("Quest Stop"));
        stop.noAnyStateTransition = true;
        entry.defaultTransition = start;
        var pending = Make<FSM_HasPendingQuest>("Has Pending Quest");
        var interrupt = new vStateTransition(pending) { trueState = stop, parentState = any };
        if (interrupt.decisions == null || interrupt.decisions.Count == 0)
        {
            interrupt.decisions ??= new Il2CppSystem.Collections.Generic.List<vStateDecisionObject>();
            interrupt.decisions.Add(new vStateDecisionObject(pending) { trueValue = true });
        }
        any.transitions.Add(interrupt);
        var states = new vFSMState[steps.Count];
        var walks = new FSM_MoveToPosition?[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i] is MoveStep or ApproachStep)
            {
                var walk = Make<FSM_MoveToPosition>("Move To Place");
                walk.positionName = PlaceKey(job, i);
                // The SDK's step ends the walk at its own distance (an interaction's is known only when the step starts).
                walk.minimumDistance = steps[i] is MoveStep move ? move.Distance : 0.5f;
                walks[i] = walk;
                states[i] = State(graph, $"Step {i + 1}: walk", walk);
            }
            else states[i] = State(graph, $"Step {i + 1}: {steps[i] switch { WaitStep => "wait", PlayStep => "play", UseStep => "use", _ => "do" }}");
        }
        var machine = new Machine(version, graph, steps, states, stop, walks);
        // The SDK's step goes first in Stop, so the run ends before FSM_QuestStop lets the runner pick again.
        Step(machine, job, stop, steps.Count, first: true);
        Step(machine, job, start, Start);
        for (int i = 0; i < steps.Count; i++) Step(machine, job, states[i], i);
        var list = new Il2CppSystem.Collections.Generic.List<vFSMState>();
        list.Add(entry);
        list.Add(any);
        list.Add(start);
        foreach (var state in states) list.Add(state);
        list.Add(stop);
        graph.states = list;
        s_machines[job] = machine;
        return machine;
    }

    private static string PlaceKey(ResidentJob job, int step) => $"Mjolnir.{job}.{step}";

    private static void Step(Machine machine, ResidentJob job, vFSMState state, int index, bool first = false)
    {
        var action = Make<JobAction>("Mjolnir Step");
        action.parentFSM = machine.Graph;
        if (first) state.actions.Insert(0, action);
        else state.actions.Add(action);
        s_actions[action.Pointer] = (job, machine, index);
        machine.Actions.Add(action.Pointer);
    }

    private static vFSMState State(vFSMBehaviour graph, string name, params vStateAction[] actions)
    {
        var state = Make<vFSMState>(name);
        state.Name = name;
        state.parentGraph = graph;
        state.useActions = true;
        state.useDecisions = true;
        state.actions = new Il2CppSystem.Collections.Generic.List<vStateAction>();
        state.transitions = new Il2CppSystem.Collections.Generic.List<vStateTransition>();
        foreach (var action in actions)
        {
            action.parentFSM = graph;
            state.actions.Add(action);
        }
        return state;
    }

    // An asset of the game's kind (or the SDK's injected step), kept through scene loads.
    private static T Make<T>(string name) where T : ScriptableObject
    {
        var made = ScriptableObject.CreateInstance(Il2CppType.Of<T>()).Cast<T>();
        made.name = name;
        made.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return made;
    }
}
