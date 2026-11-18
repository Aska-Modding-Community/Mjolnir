using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.Camera;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain wide roads" scenario: road plans of a mod's width (<c>RoadPlan.Width</c>). On a free square it paves a
/// road of the game's width, one 8 m wide, one laid at the game's width and widened to 12 m before it is paved, and a
/// levelling plan 6 m wide, reading the ground's kind across each and the road template's own width after; then the
/// console widens a plan. The square's ground is put back after. It changes the save's ground and buildings: back the
/// save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainWideRoadsScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Wide roads: no world");
            yield break;
        }
        var home = character.transform.position;
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 60f, 80f, 100f, 130f, 160f, 200f, 240f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 20)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var whole = LandscapeArea.Rectangle(at, 40, 60, blend: 2);
                if (Landscape.Check(whole, LandscapeOperation.Level) != LandscapeResult.Done) continue;
                var m = Landscape.Measure(whole);
                if (!m.IsKnown || m.Minimum < Landscape.WaterLevel + 1 || m.Maximum - m.Minimum > 8f) continue;
                spot = at;
                found = true;
                Log(Inv($"Wide roads: free square {P(at)} at {distance} m, {turn}°: {m}"));
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Wide roads: no free square");
            yield break;
        }
        var stand = spot - (spot - home).normalized * 30f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = GameCamera.Take(100);
        camera.Position = spot + new Vector3(0, 46f, -34f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        var kept = Landscape.Capture(LandscapeArea.Rectangle(spot, 44, 64, blend: 2));
        Vector3 At(float x, float z) => spot + new Vector3(x, 0, z);
        // The kinds across a road at z, every half metre from −9 to +9 m, and how wide its road and its dirt are.
        string Across(float z)
        {
            var kinds = Enumerable.Range(0, 37).Select(k => Landscape.KindAt(spot.x, spot.z + z - 9f + k * 0.5f)).ToList();
            int road = kinds.Count(k => k == GroundKind.Road), dirt = kinds.Count(k => k == GroundKind.Dirt);
            return Inv($"{string.Concat(kinds.Select(k => k.ToString()[0]))} (road {road * 0.5f:0.#} m, dirt {dirt * 0.5f:0.#} m)");
        }
        SplineStructureTemplate? template = null;
        foreach (var t in Resources.FindObjectsOfTypeAll<NodeTemplate>())
            if (t != null && t.name == "Item_Structures_TerrainSplineRoad" && t.TryCast<SplineStructureTemplate>() is { } spline) template = spline;
        Log($"Wide roads: the road template's width {template?.width}");

        var roads = new (string Name, float Z, GroundKind Kind, float Width, float Widen)[]
        {
            ("the game's width", -24f, GroundKind.Road, 0f, 0f),
            ("8 m", -10f, GroundKind.Road, 8f, 0f),
            ("widened to 12 m", 6f, GroundKind.Road, 0f, 12f),
            ("a levelling plan 6 m", 22f, GroundKind.Dirt, 6f, 0f),
        };
        foreach (var (name, z, kind, width, widen) in roads)
        {
            var plan = RoadPlans.Place([At(-16, z), At(0, z), At(16, z)], kind, width);
            yield return 2f;
            float laid = plan?.Width ?? 0;
            if (widen > 0 && plan != null) plan.Width = widen;
            float now = plan?.Width ?? 0;
            int paved = plan?.LevelNow() ?? -1;
            yield return 3f;
            Log(Inv($"Wide roads: {name}: laid {laid:0.##} m wide, {now:0.##} m when paved; paved {paved}, gone {plan?.IsGone}; across [{Across(z)}]; the template's width {template?.width}"));
        }
        Shot("wide-roads");
        yield return AfterShot;

        // The console widens the nearest plan.
        var console = RoadPlans.Place([At(-16, -30), At(16, -30)]);
        yield return 1f;
        var by = At(0, -27);
        var go = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(by.x, by.z);
        foreach (float wait in WaitFor("the teleport", () => go.IsCompleted, 30)) yield return wait;
        yield return 2f;
        DevConsole.Execute("road width 10");
        yield return 1f;
        Log(Inv($"Wide roads: the console's road width 10: {console?.Width:0.##} m"));
        console?.Remove();

        var back = kept?.Restore(new LandscapeOptions { KeepOffBuildings = false });
        yield return 2f;
        Log($"Wide roads: the ground put back: {back}");
        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
