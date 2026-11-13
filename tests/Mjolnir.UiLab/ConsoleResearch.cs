using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Player;
using SandSailorStudio;
using SandSailorStudio.DevCon;
using SandSailorStudio.Streaming;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.Controllers;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "consoleresearch" scenario (autopilot.once containing "consoleresearch"): research for a dev console's give and teleport. In the
/// main menu the game's own console (DevConsole and its [ConsoleCmd] commands) and debug menu are looked for among the
/// loaded objects. In the world the local player's PlayerDrive moves the player a few metres onto the ground a ray finds,
/// then far away above the ground while the world streams in (the height, the streaming's state and progress and the
/// player's health logged as it goes), onto the ground there, and back where the player stood.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> ConsoleResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 3f;
        LogAssets<DevConsole>("objects of the game's console", c => $"'{c.name}' scene '{c.gameObject.scene.name}' active {c.gameObject.activeInHierarchy}, canvas {c.consoleCanvas?.name}, input {c.consoleInput?.name}");
        Log($"Console: DevConsole.Instance {(DevConsole.Instance == null ? "none" : DevConsole.Instance.name)}");
        LogAssets<DebugMenu>("objects of the game's debug menu", m => $"'{m.name}' scene '{m.gameObject.scene.name}' active {m.gameObject.activeInHierarchy}");
        LogConsoleCommands();

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 5f;
        Log($"Console: in the world DevConsole.Instance {(DevConsole.Instance == null ? "none" : DevConsole.Instance.name)}");
        LogAssets<DevConsole>("objects of the game's console in the world", c => $"'{c.name}' scene '{c.gameObject.scene.name}' active {c.gameObject.activeInHierarchy}");

        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null)
        {
            Log("Console: no local player");
            yield break;
        }
        var own = character.GetComponent<PlayerDrive>();
        var agent = character.GetComponent<PlayerEntity>()?.Agent;
        var byAgent = agent == null ? null : agent.GetComponent<PlayerDrive>();
        var drive = own ?? byAgent ?? character.GetComponentInParent<PlayerDrive>();
        Log($"Console: the character '{character.name}', PlayerDrive on it {own != null}, the entity's agent '{agent?.name}' with PlayerDrive {byAgent != null}, " +
            $"the same object {own != null && byAgent != null && own.Pointer == byAgent.Pointer}");
        if (drive == null) yield break;
        var streaming = GameManagers.Get<WorldStreamingManager>();
        var body = character.transform;
        string State() => $"at {P(body.position)}, drive teleporting {drive.IsTeleporting()}, streaming teleporting {streaming?.IsTeleporting} " +
                          $"progress {streaming?.GetStreamingProgress():0.00} tile {streaming?.CurrentTile} resident {streaming?.ResidentBounds}, health {LocalPlayer.Health.Value:0.#}";

        var start = body.position;
        Log($"Console: start {State()}; ground under it {Ground(start)}; sampler {Sampler(start)}");
        Shot("console-start");
        yield return AfterShot;

        // A few metres: onto the ground a ray finds there.
        var near = start + new Vector3(6, 0, 0);
        var nearGround = GroundPoint(near);
        Log($"Console: near {P(near)}, ground {Ground(near)}");
        drive.Teleport((nearGround ?? near) + Vector3.up * 0.1f);
        for (int i = 0; i < 8; i++)
        {
            yield return 0.2f;
            Log($"Console: near +{(i + 1) * 0.2f:0.0} s {State()}");
        }
        Shot("console-near");
        yield return AfterShot;

        // Far away, 40 m above the start's height, while the world there streams in.
        var far = new Vector3(start.x + 300, start.y + 40, start.z);
        Log($"Console: far {P(far)} inside the resident area {streaming != null && streaming.ResidentBounds.Contains(new Vector2(far.x, far.z))}; ground there now {Ground(far)}");
        drive.Teleport(far);
        // What stands under the far place, frame by frame while the world there streams in.
        for (int i = 0; i < 20; i++)
        {
            yield return 0.05f;
            Log($"Console: far +{(i + 1) * 0.05f:0.00} s {State()}; sampler {Sampler(far)}; solid {AllHits(far)}");
        }
        for (int i = 0; i < 30; i++)
        {
            yield return 0.25f;
            if (i % 4 == 3) Log($"Console: far later {State()}; sampler {Sampler(far)}");
        }
        Shot("console-far");
        yield return AfterShot;
        var farGround = GroundPoint(body.position);
        if (farGround is { } g)
        {
            drive.Teleport(g + Vector3.up * 0.1f);
            for (int i = 0; i < 6; i++)
            {
                yield return 0.25f;
                Log($"Console: far on the ground +{(i + 1) * 0.25f:0.00} s {State()}");
            }
            Shot("console-far-ground");
            yield return AfterShot;
        }

        // Back where the player stood.
        drive.Teleport(start);
        for (int i = 0; i < 24; i++)
        {
            yield return 0.25f;
            if (i % 4 == 3) Log($"Console: back +{(i + 1) * 0.25f:0.00} s {State()}");
        }
        Log($"Console: back {State()}, {Vector3.Distance(body.position, start):0.00} m from the start");
        Shot("console-back");
        yield return AfterShot;

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // The game's own console finds its commands by the [ConsoleCmd] attribute through reflection over every assembly.
    private static void LogConsoleCommands()
    {
        try
        {
            var names = new List<string>();
            var methods = DevConsole._GetMethodsWith<ConsoleCmd>(false);
            var each = methods.GetEnumerator();
            var plain = each.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (plain.MoveNext())
            {
                var m = each.Current;
                var parameters = m.GetParameters();
                names.Add($"{m.DeclaringType?.FullName}.{m.Name}({string.Join(", ", Enumerable.Range(0, parameters.Length).Select(i => $"{parameters[i].ParameterType.Name} {parameters[i].Name}"))}) static {m.IsStatic}");
            }
            Log($"Console: {names.Count} [ConsoleCmd] methods: {string.Join("; ", names)}");
        }
        catch (Exception e)
        {
            Log($"Console: the [ConsoleCmd] methods could not be listed: {e.GetType().Name}: {e.Message}");
        }
    }

    private static Vector3? GroundPoint(Vector3 at)
    {
        var hits = Physics.RaycastAll(at + Vector3.up * 200f, Vector3.down, 600f);
        Vector3? best = null;
        float nearest = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.isTrigger || hit.collider.GetComponentInParent<PlayerCharacter>() != null) continue;
            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                best = hit.point;
            }
        }
        return best;
    }

    // Every hit under a point, nearest first: what a ray finds there (terrain, trees, water, the player).
    private static string Ground(Vector3 at)
    {
        var hits = Physics.RaycastAll(at + Vector3.up * 200f, Vector3.down, 600f);
        var described = Enumerable.Range(0, hits.Length).Select(i => hits[i]).OrderBy(h => h.distance).Take(4)
                                  .Select(h => $"{h.collider?.name}[{LayerMask.LayerToName(h.collider?.gameObject.layer ?? 0)}{(h.collider?.isTrigger == true ? ", trigger" : "")}] y {h.point.y:0.0}");
        return hits.Length == 0 ? "nothing" : $"{hits.Length} hit(s): {string.Join(", ", described)}";
    }

    // Every solid hit under a point (no triggers), nearest first, with its layer, object path and collider type.
    private static string AllHits(Vector3 at)
    {
        var hits = Physics.RaycastAll(new Vector3(at.x, 2000, at.z), Vector3.down, 4000);
        var solid = Enumerable.Range(0, hits.Length).Select(i => hits[i]).Where(h => h.collider != null && !h.collider.isTrigger).OrderBy(h => h.distance)
                              .Select(h => $"{PathOf(h.collider.transform.root, h.collider.transform)} [{LayerMask.LayerToName(h.collider.gameObject.layer)}, {h.collider.GetIl2CppType().Name}] y {h.point.y:0.0}");
        return $"{hits.Length} hit(s); solid: {string.Join("; ", solid)}";
    }

    // The game's own terrain height (TerrainSampler: the streamed tiles' heightmaps), the tile terrain it finds and the
    // active terrain.
    private static string Sampler(Vector3 at)
    {
        var p = new Vector3(at.x, 0, at.z);
        bool found = SandSailorStudio.TerrainTools.TerrainSampler.TrySampleHeight(ref p, out float height);
        var terrain = SandSailorStudio.TerrainTools.TerrainSampler.GetTerrain(new Vector3(at.x, 0, at.z));
        return $"{(found ? $"y {height:0.0}" : "none")} (position after {P(p)}), tile terrain {terrain?.name ?? "none"}, active {Terrain.activeTerrain?.name}";
    }

    private static string P(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";
}
