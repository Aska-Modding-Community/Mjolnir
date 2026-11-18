using System;
using System.Collections.Generic;
using System.Globalization;
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
/// The "terrain roads" scenario: the game's road plans through Mjolnir.SDK.Terrain. In the main menu there are none and none
/// can be laid. On a free square it lays a road plan of three points, builds it and paves it at once (the game finishes
/// it), lays a path plan and paves it, lays a plan of eight points and one of a single 40 m segment (past what the road
/// maker allows a player) and paves them, paves one under the console's protected zone (its sections there left), takes a
/// plan away, runs the console's road, and puts the ground back. It changes the ground and the buildings of the save: back
/// the save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainRoadsScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Roads: in the main menu {RoadPlans.All.Count} plans, laid {RoadPlans.Place([Vector3.zero, new Vector3(8, 0, 0)])?.ToString() ?? "none"}");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log($"Roads: no world: character {character != null}, available {Landscape.IsAvailable}");
            yield break;
        }
        var home = character.transform.position;
        Log($"Roads: {RoadPlans.All.Count} road plan(s) in the loaded world");

        // A free square of 44 × 40 m, gently sloping.
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 60f, 80f, 100f, 130f, 160f, 200f, 240f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 20)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var whole = LandscapeArea.Rectangle(at, 44, 40, blend: 2);
                if (Landscape.Check(whole, LandscapeOperation.Level) != LandscapeResult.Done) continue;
                var m = Landscape.Measure(whole);
                if (!m.IsKnown || m.Minimum < Landscape.WaterLevel + 1 || m.Maximum - m.Minimum > 8f) continue;
                spot = at;
                found = true;
                Log(Inv($"Roads: free square {P(at)} at {distance} m, {turn}°: {m}"));
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Roads: no free square found within 240 m");
            yield break;
        }
        var away = spot - home;
        away.y = 0;
        away.Normalize();
        var stand = spot - away * 28f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 2f;
        character.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        yield return 1f;
        var camera = GameCamera.Take(100);
        camera.Position = spot + new Vector3(0, 40f, -30f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        camera.FieldOfView = 60;
        var kept = Landscape.Capture(LandscapeArea.Rectangle(spot, 50, 46, blend: 2));
        Vector3 At(float x, float z) => spot + new Vector3(x, 0, z);
        string Kinds(Vector3 a, Vector3 b, int n = 8) => string.Join("", Enumerable.Range(0, n).Select(k =>
        {
            var p = Vector3.Lerp(a, b, (k + 0.5f) / n);
            return Landscape.KindAt(p.x, p.z).ToString()[0];
        }));

        // A. A road of three points, built, then paved at once.
        var road = RoadPlans.Place([At(-20, -18), At(0, -14), At(20, -18)]);
        yield return 2f;
        Log($"Roads: laid {road?.ToString() ?? "none"}: built {road?.IsBuilt}, width {road?.Width}, points [{(road == null ? "" : string.Join(" ", road.Points.Select(P)))}]");
        Shot("roads-laid");
        yield return AfterShot;
        bool built = road?.Build() ?? false;
        yield return 1f;
        Log($"Roads: built {built}: {road}");
        int paved = road?.LevelNow() ?? -1;
        yield return 3f;
        Log($"Roads: paved {paved}, gone {road?.IsGone}; the kinds along it [{Kinds(At(-20, -18), At(0, -14))}{Kinds(At(0, -14), At(20, -18))}], the world has {RoadPlans.All.Count}");
        Shot("roads-paved");
        yield return AfterShot;

        // B. A levelling plan, paved at once (built on the way).
        var strip = RoadPlans.Place([At(-20, -8), At(20, -8)], GroundKind.Dirt);
        yield return 2f;
        string stripKind = $"{strip?.Kind} of {strip?.Sections} sections";
        int stripPaved = strip?.LevelNow() ?? -1;
        yield return 3f;
        Log($"Roads: a levelling plan ({stripKind}) paved {stripPaved}, gone {strip?.IsGone}; kinds [{Kinds(At(-20, -8), At(20, -8), 10)}]");

        // C. Eight points, and one segment of 40 m.
        var eight = RoadPlans.Place(Enumerable.Range(0, 8).Select(k => At(-21 + k * 6, 2 + (k % 2 == 0 ? 0 : 3))));
        var single = RoadPlans.Place([At(-20, 14), At(20, 14)]);
        yield return 2f;
        Log($"Roads: eight points {eight?.ToString() ?? "none"}; a 40 m segment {single?.ToString() ?? "none"}");
        int eightPaved = eight?.LevelNow() ?? -1;
        int singlePaved = single?.LevelNow() ?? -1;
        yield return 3f;
        Log($"Roads: paved {eightPaved} (gone {eight?.IsGone}) and {singlePaved} (gone {single?.IsGone}); kinds along the 40 m [{Kinds(At(-20, 14), At(20, 14), 10)}]");
        Shot("roads-more");
        yield return AfterShot;

        // D. Under the console's zone (in front of the player, whose road it is not), then taken away.
        DevConsole.Execute("protect 10");
        yield return 0.5f;
        var forward = character.transform.forward;
        forward.y = 0;
        forward.Normalize();
        var zoneMiddle = character.transform.position + forward * 8.5f;
        var right = Vector3.Cross(Vector3.up, forward);
        var through = RoadPlans.Place([zoneMiddle - right * 12f, zoneMiddle + right * 12f]);
        yield return 2f;
        int throughPaved = through?.LevelNow() ?? -1;
        yield return 2f;
        Log($"Roads: through the console's zone at {P(zoneMiddle)}: paved {throughPaved} of {through?.Sections}, gone {through?.IsGone}; kinds [{Kinds(zoneMiddle - right * 12f, zoneMiddle + right * 12f, 12)}]");
        DevConsole.Execute("protect clear");
        bool removed = through?.Remove() ?? false;
        yield return 2f;
        Log($"Roads: taken away {removed}, gone {through?.IsGone}; the world has {RoadPlans.All.Count}");

        // E. The console, its plan watched for a while.
        int structuresBefore = Resources.FindObjectsOfTypeAll<TerraformingSpline>().Length;
        DevConsole.Execute("road place 12");
        for (int tick = 0; tick < 8; tick++)
        {
            yield return 0.25f;
            var splines = Resources.FindObjectsOfTypeAll<TerraformingSpline>().Where(sp => sp != null && sp.gameObject.scene.IsValid()).ToList();
            string states = string.Join("; ", splines.Select(sp =>
            {
                var owner = sp.GetComponentInParent<Structure>();
                return owner == null ? "no structure" : $"'{owner.name}' active {owner.gameObject.activeInHierarchy}, dismantled {owner.Dismantled}, cells {sp.GetCellsCount()}";
            }));
            Log($"Roads: {tick * 0.25f + 0.25f:0.00} s after the console's place: all {RoadPlans.All.Count}, splines in the scene {splines.Count} (before {structuresBefore}) [{states}]");
        }
        var console = RoadPlans.Nearest(character.transform.position, 20);
        Log($"Roads: the console laid {console?.ToString() ?? "none"}");
        DevConsole.Execute("road level");
        yield return 2f;
        Log($"Roads: after the console's level: gone {console?.IsGone}; the world has {RoadPlans.All.Count}");

        // F. What is left taken away, and the ground put back.
        foreach (var left in RoadPlans.All) left.Remove();
        yield return 2f;
        if (kept != null) Log($"Roads: the ground put back: {kept.Restore(new LandscapeOptions { KeepOffBuildings = false })}");
        yield return 2f;
        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
