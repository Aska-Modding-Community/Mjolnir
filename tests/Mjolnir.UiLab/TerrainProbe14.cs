using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Camera;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe14" scenario: wide roads. On a free square it lays two straight road plans side by side and paves
/// them at once, the first as the game makes it, the second with the road template's width raised for the time it is
/// paved; then it reads the ground's kind and height across both, and the plans' own widths. The square's ground is put
/// back after. It changes the save's ground: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe14()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Probe14: no world");
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
                var whole = LandscapeArea.Rectangle(at, 40, 36, blend: 2);
                if (Landscape.Check(whole, LandscapeOperation.Level) != LandscapeResult.Done) continue;
                var m = Landscape.Measure(whole);
                if (!m.IsKnown || m.Minimum < Landscape.WaterLevel + 1 || m.Maximum - m.Minimum > 8f) continue;
                spot = at;
                found = true;
                Log(Inv($"Probe14: free square {P(at)} at {distance} m, {turn}°: {m}"));
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Probe14: no free square");
            yield break;
        }
        var stand = spot - (spot - home).normalized * 26f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = GameCamera.Take(100);
        camera.Position = spot + new Vector3(0, 34f, -26f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        var kept = Landscape.Capture(LandscapeArea.Rectangle(spot, 44, 40, blend: 2));
        Vector3 At(float x, float z) => spot + new Vector3(x, 0, z);
        string Across(float z) => string.Join(" ", new[] { -6f, -5f, -4f, -3f, -2.5f, -2f, -1.5f, -1f, -0.5f, 0f, 0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 4f, 5f, 6f }
            .Select(dz => $"{dz:0.#}:{Landscape.KindAt(spot.x, spot.z + z + dz).ToString()[0]}"));

        SplineStructureTemplate? template = null;
        foreach (var t in Resources.FindObjectsOfTypeAll<NodeTemplate>())
            if (t != null && t.name == "Item_Structures_TerrainSplineRoad" && t.TryCast<SplineStructureTemplate>() is { } spline) template = spline;
        if (template == null)
        {
            Log("Probe14: no road template");
            yield break;
        }
        Log($"Probe14: the road template: width {template.width}, footprint {template.footprintWidth}, section {template.sectionLength}, segment {template.maxSegmentLength}");

        // A. The game's own width.
        var narrow = RoadPlans.Place([At(-16, -8), At(0, -8), At(16, -8)]);
        yield return 2f;
        int pavedA = narrow?.LevelNow() ?? -1;
        yield return 3f;
        Log($"Probe14: A as the game makes it: width {narrow?.Width}, paved {pavedA}; across [{Across(-8)}]");

        // B. The template's width raised for the time the plan is paved.
        var wide = RoadPlans.Place([At(-16, 8), At(0, 8), At(16, 8)]);
        yield return 2f;
        float width = template.width, footprint = template.footprintWidth;
        float widthB = wide?.Width ?? 0;
        int pavedB = -1;
        string error = Try(() =>
        {
            template.width = 6f;
            try { pavedB = wide?.LevelNow() ?? -1; }
            finally { template.width = width; }
            return "";
        });
        yield return 3f;
        Log($"Probe14: B with the template's width 6 while paved: {error} plan width {widthB}, paved {pavedB}; across [{Across(8)}]; the template back to {template.width}");
        Shot("probe14-roads");
        yield return AfterShot;

        // C. A plan laid while the template is wide: does its layout take the width?
        template.width = 6f;
        template.footprintWidth = footprint + 4f;
        RoadPlan? laidWide = null;
        string errorC = Try(() => { laidWide = RoadPlans.Place([At(-16, 0), At(16, 0)]); return ""; });
        template.width = width;
        template.footprintWidth = footprint;
        yield return 2f;
        Log($"Probe14: C laid while the template is wide: {errorC} {laidWide?.ToString() ?? "none"}, width {laidWide?.Width}");
        Shot("probe14-laid-wide");
        yield return AfterShot;
        int pavedC = laidWide?.LevelNow() ?? -1;
        yield return 3f;
        Log($"Probe14: C paved with the template back to {template.width}: {pavedC}; across [{Across(0)}]");
        laidWide?.Remove();

        var back = kept?.Restore(new LandscapeOptions { KeepOffBuildings = false });
        yield return 2f;
        Log($"Probe14: the ground put back: {back}");
        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
