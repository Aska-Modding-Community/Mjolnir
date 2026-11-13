using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Fusion;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using SSSGame;
using SSSGame.Network;
using UnityEngine.SceneManagement;

namespace Mjolnir.SmokeMod;

/// <summary>
/// Logging-only postfixes on methods named by string, to find out whether, when and for whom the game calls them
/// (research into the game's API). A probe never changes behaviour; a missing type or method only logs.
/// All overloads of a named method are probed. The first three hits also log the context: the instance's real
/// IL2CPP class (and Unity object name), the frame, the active scene, the thread, simple arguments and the result.
/// A Stack probe also logs the IL2CPP call stack on its first hit. A Distinct probe also logs the first hit of each
/// distinct instance/argument object (each menu, game event, FSM graph, state or quest), up to 60 per method.
/// </summary>
internal static class Probes
{
    // Step 7 (extension-point map): timing and dispatch questions that static analysis cannot answer.
    private static readonly (string Type, string Method, ProbeKind Kind)[] Targets =
    [
        // Stamina: who ticks the attributes (VariableAttribute.Tick and AttributeManager.Tick are reached only virtually).
        ("SandSailorStudio.Attributes.AttributeManager", "Tick", ProbeKind.Stack),

        // Content: when the item database and the language are loaded, and the localization key format.
        ("SandSailorStudio.Inventory.ItemDatabaseManager", "Awake", ProbeKind.Count),
        ("SandSailorStudio.Inventory.ItemInfoDatabase", "Initialize", ProbeKind.Count),
        ("SandSailorStudio.Localization.LocalizationManager", "_LoadLanguage", ProbeKind.Count),
        ("SandSailorStudio.Localization.LocalizationManager", "SetLanguage", ProbeKind.Stack),
        ("SandSailorStudio.Inventory.ItemInfo", "GetKey", ProbeKind.Count),
        ("SSSGame.PlayerBuilder", "Start", ProbeKind.Count),
        ("SSSGame.CraftInteraction", "Awake", ProbeKind.Distinct),

        // Villager AI: host-only tick, graph and state changes, quest choice.
        ("SSSGame.AI.AIManager", "FixedUpdate", ProbeKind.Count),
        ("SSSGame.AI.AIManager", "Register", ProbeKind.Distinct),
        ("SSSGame.AI.FSM.FSMBehaviourController", "UpdateBehaviour", ProbeKind.Count),
        ("SSSGame.AI.FSM.FSMBehaviourController", "ChangeBehaviour", ProbeKind.Distinct),
        ("SSSGame.AI.FSM.FSMBehaviourController", "ChangeState", ProbeKind.Distinct),
        ("SSSGame.AI.QuestRunner", "Update", ProbeKind.Count),
        ("SSSGame.AI.QuestRunner", "AddQuest", ProbeKind.Distinct),
        ("SSSGame.AI.QuestRunner", "FSMQuestStart", ProbeKind.Distinct),

        // UI: when the HUD is built and activated, which menus open, which game events fire.
        ("SSSGame.GameState_Play", "OnActivate", ProbeKind.Count),
        ("SSSGame.PlayerCharacterDefinition", "Spawn", ProbeKind.Stack),
        ("SSSGame.UI.PlayerHUDMenu", "Awake", ProbeKind.Count),
        ("SSSGame.UI.WidgetMenu", "OnActivate", ProbeKind.Distinct),
        ("SandSailorStudio.UI.MenuManager", "OpenMenu", ProbeKind.Distinct),
        ("SandSailorStudio.GameEvent", "Raise", ProbeKind.Distinct),
        ("SSSGame.MainMenu", "OnActivate", ProbeKind.Count),
        ("SSSGame.UI.PauseMenu", "OnActivate", ProbeKind.Count),

        // Network: runner creation, joins, reliable data (see also NetProbes below).
        ("SSSGame.Network.NetworkSession", "_CreateRunner", ProbeKind.Count),
        ("Fusion.NetworkRunner", "Fusion_Simulation_ICallbacks_PlayerJoined", ProbeKind.Count),
        ("Fusion.NetworkRunner", "Fusion_Simulation_ICallbacks_OnConnectionRequest", ProbeKind.Count),
        ("Fusion.NetworkRunner", "Fusion_Simulation_ICallbacks_OnReliableData", ProbeKind.Count),
        ("Fusion.NetworkRunner", "SendReliableDataToPlayer", ProbeKind.Count),
        ("SSSGame.Network.NetworkJoinHandler", "OnPlayerJoined", ProbeKind.Count),
        ("SSSGame.PlayerManager", "SetPlayerUser", ProbeKind.Count),
    ];

    private static readonly ConcurrentDictionary<MethodBase, int[]> s_counts = new();
    private static readonly ConcurrentDictionary<MethodBase, ProbeKind> s_kinds = new();
    private static readonly ConcurrentDictionary<(MethodBase, string), bool> s_seen = new();
    private static readonly ConcurrentDictionary<MethodBase, int[]> s_distinct = new();
    private static int s_mainThread;

    public static void Apply(Harmony harmony)
    {
        s_mainThread = Environment.CurrentManagedThreadId;
        var postfix = new HarmonyMethod(typeof(Probes), nameof(Postfix));
        var postfixResult = new HarmonyMethod(typeof(Probes), nameof(PostfixResult));
        foreach (var (typeName, methodName, kind) in Targets)
        {
            string name = $"{typeName}.{methodName}";
            try
            {
                var type = AccessTools.TypeByName(typeName) ?? throw new InvalidOperationException("type not found");
                var methods = AccessTools.GetDeclaredMethods(type).Where(m => m.Name == methodName && !m.IsAbstract).ToArray();
                if (methods.Length == 0) throw new InvalidOperationException("method not found");
                foreach (var method in methods)
                {
                    s_kinds[method] = kind;
                    harmony.Patch(method, postfix: method.ReturnType == typeof(void) ? postfix : postfixResult);
                }
                Smoke.Logger.Info($"Probe on {name} applied ({methods.Length} overload(s))");
            }
            catch (Exception e)
            {
                Smoke.Logger.Error($"Probe on {name} failed: {e.Message}");
            }
        }
    }

    private static void Postfix(MethodBase __originalMethod, object? __instance, object?[] __args) =>
        Hit(__originalMethod, __instance, __args, hasResult: false, result: null);

    private static void PostfixResult(MethodBase __originalMethod, object? __instance, object?[] __args, object? __result) =>
        Hit(__originalMethod, __instance, __args, hasResult: true, __result);

    private static void Hit(MethodBase method, object? instance, object?[] args, bool hasResult, object? result)
    {
        var counter = s_counts.GetOrAdd(method, static _ => new int[1]);
        var kind = s_kinds.TryGetValue(method, out var k) ? k : ProbeKind.Count;
        string what = $"Probe {method.DeclaringType?.Name}.{method.Name}";
        int n = Interlocked.Increment(ref counter[0]);
        string returned = hasResult ? $" → {Simple(result)}" : "";
        if (n <= 3)
        {
            if (kind == ProbeKind.Distinct) Novel(method, instance, args);
            Smoke.Logger.Info($"{what} hit #{n} [{Context(instance, args)}]{returned}");
            if (n == 1 && kind == ProbeKind.Stack) Smoke.Logger.Info($"{what} stack: {Stack()}");
        }
        else if (kind == ProbeKind.Distinct && Novel(method, instance, args))
            Smoke.Logger.Info($"{what} new #{n} [{Context(instance, args)}]{returned}");
        if (n is 100 or 1000 or 10_000 or 100_000) Smoke.Logger.Info($"{what} hit #{n}");
    }

    /// <summary>Whether this instance/argument combination (Unity objects by class and name) is the first of its kind.</summary>
    private static bool Novel(MethodBase method, object? instance, object?[] args)
    {
        var distinct = s_distinct.GetOrAdd(method, static _ => new int[1]);
        if (Volatile.Read(ref distinct[0]) >= 60) return false;
        string key = string.Join("|", new[] { instance }.Concat(args).Where(a => a is Il2CppObjectBase).Select(Simple));
        if (!s_seen.TryAdd((method, key), true)) return false;
        Interlocked.Increment(ref distinct[0]);
        return true;
    }

    private static string Context(object? instance, object?[] args)
    {
        try
        {
            string thread = Environment.CurrentManagedThreadId == s_mainThread ? "main" : $"thread {Environment.CurrentManagedThreadId}";
            string where = thread == "main"
                ? $"frame {UnityEngine.Time.frameCount}, scene '{SceneManager.GetActiveScene().name}'"
                : thread;
            string self = instance is Il2CppObjectBase ? $"this {Simple(instance)}, " : "";
            string values = string.Join(", ", args.Select(Simple));
            return $"{self}{where}" + (values.Length > 0 ? $", args ({values})" : "");
        }
        catch (Exception e)
        {
            return $"context failed: {e.GetType().Name}";
        }
    }

    private static string Stack()
    {
        try
        {
            // Came back empty in game on 2026-10-20 (no IL2CPP frames through the Harmony trampoline); kept to see if that changes.
            string stack = UnityEngine.StackTraceUtility.ExtractStackTrace();
            return string.IsNullOrWhiteSpace(stack) ? "(empty)" : "\n" + stack;
        }
        catch (Exception e)
        {
            return $"stack failed: {e.GetType().Name}: {e.Message}";
        }
    }

    /// <summary>The runtime class of an IL2CPP object, which for virtual methods may be a subclass of the patched type.</summary>
    private static string ClassName(IntPtr obj)
    {
        if (obj == IntPtr.Zero) return "null";
        IntPtr klass = IL2CPP.il2cpp_object_get_class(obj);
        string? ns = Marshal.PtrToStringUTF8(IL2CPP.il2cpp_class_get_namespace(klass));
        string? name = Marshal.PtrToStringUTF8(IL2CPP.il2cpp_class_get_name(klass));
        return string.IsNullOrEmpty(ns) ? name ?? "?" : $"{ns}.{name}";
    }

    private static string Simple(object? arg)
    {
        try
        {
            return arg switch
            {
                null => "null",
                string s => $"\"{(s.Length > 120 ? s[..120] + "…" : s)}\"",
                Enum or bool or char or sbyte or byte or short or ushort or int or uint or long or ulong or float or double => arg.ToString()!,
                PlayerRef player => player.ToString(),
                Il2CppObjectBase obj when obj.Pointer == IntPtr.Zero => "null",
                // Unity objects: class plus object name (asset name for ScriptableObjects such as graphs, states, events).
                Il2CppObjectBase obj when obj.TryCast<UnityEngine.Object>() is { } unity => $"{ClassName(obj.Pointer)} '{unity.name}'",
                Il2CppObjectBase obj => ClassName(obj.Pointer),
                _ => arg.GetType().Name,
            };
        }
        catch (Exception e)
        {
            return $"? ({e.GetType().Name})";
        }
    }
}

internal enum ProbeKind
{
    Count,
    Stack,
    Distinct,
}

/// <summary>Network probes that read game values (logging only): the session's Fusion GameMode and the master flag.</summary>
[HarmonyPatch(typeof(NetworkSession.__c__DisplayClass114_0), nameof(NetworkSession.__c__DisplayClass114_0._StartSession_b__0))]
internal static class StartSessionProbe
{
    private static void Prefix(NetworkSession.__c__DisplayClass114_0 __instance)
    {
        try
        {
            Smoke.Logger.Info($"Probe StartSession → StartGame: mode {__instance.mode}, startOpen {__instance.startOpen}, " +
                              $"session properties {__instance.sessionPropperties?.Count.ToString() ?? "null"}");
        }
        catch (Exception e)
        {
            Smoke.Logger.Error($"Probe StartSession failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.OnPlayerJoined))]
internal static class PlayerJoinedProbe
{
    private static void Postfix(NetworkRunner runner, PlayerRef player)
    {
        try
        {
            // Read through Simulation.ICallbacks, not runner.IsSharedModeMasterClient: Il2CppInterop emits Fusion's explicit
            // ICallbacks implementation as a second public property named "Fusion.Simulation.ICallbacks.IsSharedModeMasterClient",
            // which Rider resolves as a second IsSharedModeMasterClient (csc ignores it). Both getters are one body in
            // GameAssembly.dll (CodeMap: 0x0163F4D0, folded), so the value is the same.
            bool sharedModeMaster = runner.Cast<Simulation.ICallbacks>().IsSharedModeMasterClient;
            Smoke.Logger.Info($"Probe PlayerManager.OnPlayerJoined: player {player}, local {runner.LocalPlayer}, " +
                              $"GameMode {runner.GameMode}, shared-mode master {sharedModeMaster}, " +
                              $"server {runner.IsServer}, client {runner.IsClient}");
        }
        catch (Exception e)
        {
            Smoke.Logger.Error($"Probe PlayerManager.OnPlayerJoined failed: {e.Message}");
        }
    }
}
