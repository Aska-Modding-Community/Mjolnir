using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe9" scenario: road plans from code. It reads the game's two road templates, sets a road plan of three
/// points down through the settlement as the game's spline tool does (a poly-line placement), reads what came up (the
/// layout's points and sections, the network state's points, the terraforming spline's cells), levels its sections one by
/// one through the spline's own tool, reads the ground and its kind along it and whether the plan finished, then sets a
/// second plan of six points (more than the template allows a player) down, and takes what is left away. It changes the
/// ground and the buildings of the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe9()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        if (character == null || settlement == null || !Landscape.IsAvailable)
        {
            Log("Probe9: no world");
            yield break;
        }
        var home = character.transform.position;

        // The templates.
        var templates = Resources.FindObjectsOfTypeAll<NodeTemplate>().Select(t => t?.TryCast<SplineStructureTemplate>()).Where(t => t != null).Cast<SplineStructureTemplate>().ToList();
        foreach (var t in templates)
            Log(Inv($"Probe9: template '{t.name}': points {t.maxControlPoints}, segment {t.maxSegmentLength} m, section {t.sectionLength} m, depth {t.maxSectionDepth}, width {t.width}, footprint {t.footprintWidth}, sampling {t.samplingStep}, result '{Try(() => t.GetIl2CppType().Name)}'"));
        var road = templates.FirstOrDefault(t => t.name.Contains("Road", StringComparison.Ordinal));
        if (road == null)
        {
            Log("Probe9: no road template");
            yield break;
        }

        // A free, gently sloping strip 30 m long.
        Vector3 start = default, end = default;
        bool found = false;
        foreach (float distance in new[] { 50f, 70f, 90f, 120f, 150f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                var dir = Quaternion.Euler(0, turn + 90, 0) * Vector3.forward;
                var a = at - dir * 15f;
                var b = at + dir * 15f;
                if (!Landscape.TryGetHeight(a.x, a.z, out a.y) || !Landscape.TryGetHeight(b.x, b.z, out b.y)) continue;
                var strip = LandscapeArea.Rectangle(at, 6, 30, rotation: turn + 90, blend: 2);
                if (Landscape.Check(strip, LandscapeOperation.Level) != LandscapeResult.Done) continue;
                var m = Landscape.Measure(strip);
                if (!m.IsKnown || m.Minimum < Landscape.WaterLevel + 1 || m.Maximum - m.Minimum > 3f) continue;
                start = a;
                end = b;
                found = true;
                Log(Inv($"Probe9: strip from {P(a)} to {P(b)} at {distance} m, {turn}°: {m}"));
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Probe9: no free strip found");
            yield break;
        }
        var middle = (start + end) / 2;
        var away = middle - home;
        away.y = 0;
        away.Normalize();
        var stand = middle - away * 10f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = Overhead(middle);
        camera.Position = middle + new Vector3(0, 24f, -16f);
        camera.Rotation = Quaternion.LookRotation(middle - camera.Position, Vector3.up);
        var kept = Landscape.Capture(LandscapeArea.Rectangle(middle, 12, 40, rotation: Quaternion.LookRotation(end - start).eulerAngles.y, blend: 2));
        string Along(Vector3 a, Vector3 b) => string.Join(" ", Enumerable.Range(0, 7).Select(k =>
        {
            var p = Vector3.Lerp(a, b, k / 6f);
            return $"{Landscape.HeightAt(p.x, p.z).ToString("0.00", CultureInfo.InvariantCulture)}/{Landscape.KindAt(p.x, p.z).ToString()[0]}";
        }));
        string before = Along(start, end);

        // A. A plan of three points, as the spline tool makes one.
        var points = new[] { start, middle + Vector3.Cross(Vector3.up, end - start).normalized * 2f, end };
        for (int i = 0; i < points.Length; i++) points[i].y = Landscape.HeightAt(points[i].x, points[i].z);
        Structure? plan = Place(settlement, road, points);
        yield return 1f;
        Log($"Probe9: placed: {Describe(plan)}");
        Log($"Probe9: points given to the state: {GivePoints(plan, points)}");
        yield return 2f;
        Log($"Probe9: a plan of 3 points: {Describe(plan)}");
        Shot("probe9-placed");
        yield return AfterShot;
        Log($"Probe9: its build part finished: {FinishBuild(plan)}");
        yield return 2f;
        Log($"Probe9: after the build part: {Describe(plan)}");

        // B. Its sections levelled one by one through the spline's own tool.
        var spline = plan != null ? plan.GetComponentInChildren<TerraformingSpline>(true) : null;
        if (spline != null)
        {
            int cells = spline.GetCellsCount();
            int levelledBefore = Enumerable.Range(0, cells).Count(i => spline.IsCellLeveled(i));
            string tried = Try(() =>
            {
                for (int i = 0; i < cells; i++)
                    if (!spline.IsCellLeveled(i)) spline.TryLevelTile(i);
                return "done";
            });
            yield return 3f;
            int levelledAfter = plan != null && spline != null ? Enumerable.Range(0, Try2(() => spline.GetCellsCount())).Count(i => Try2(() => spline.IsCellLeveled(i))) : -1;
            Log($"Probe9: {cells} sections, {levelledBefore} level before; TryLevelTile on the rest: {tried}; level after {levelledAfter}; the plan {Describe(plan)}; along before [{before}], after [{Along(start, end)}]");
            Shot("probe9-levelled");
            yield return AfterShot;

            // The same through the interaction a villager uses, if the plan is still there.
            var interaction = plan != null ? plan.GetComponentInChildren<TerraformingSplineInteraction>(true) : null;
            if (interaction != null && plan != null && plan.gameObject != null)
            {
                string levelled = Try(() => string.Join(" ", Enumerable.Range(0, Math.Max(cells, 1)).Select(i => interaction.LevelSection(i) ? "1" : "0")));
                yield return 2f;
                Log($"Probe9: LevelSection on each: [{levelled}]; the plan {Describe(plan)}");
            }
        }
        else Log("Probe9: the plan has no terraforming spline");

        // C. A plan of six points, more than a player may set down.
        var side = Vector3.Cross(Vector3.up, end - start).normalized;
        var six = Enumerable.Range(0, 6).Select(k =>
        {
            var p = Vector3.Lerp(start, end, k / 5f) + side * (8f + (k % 2 == 0 ? 0f : 2f));
            p.y = Landscape.HeightAt(p.x, p.z);
            return p;
        }).ToArray();
        Structure? longer = Place(settlement, road, six);
        yield return 1f;
        Log($"Probe9: 6 points given to the state: {GivePoints(longer, six)}");
        yield return 2f;
        Log($"Probe9: a plan of 6 points placed: {Describe(longer)}");
        Shot("probe9-six");
        yield return AfterShot;

        // D. What is left taken away, and the ground put back.
        foreach (var s in new[] { plan, longer })
            if (s != null && s.gameObject != null) Try(() => { s.Dismantle(true); return ""; });
        yield return 2f;
        if (kept != null) Log($"Probe9: the ground put back: {kept.Restore(new LandscapeOptions { KeepOffBuildings = false })}");
        yield return 2f;
        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // A road plan through the settlement, as the spline tool's placement makes one; then the layout's state given the
    // points too, where the placement did not carry them.
    private static Structure? Place(Settlement settlement, SplineStructureTemplate template, Vector3[] points)
    {
        Structure? made = null;
        string how = Try(() =>
        {
            var placement = new PolyLinePlacement();
            var list = new Il2CppSystem.Collections.Generic.List<PointPlacement>();
            foreach (var at in points)
            {
                var point = new PointPlacement();
                point.position = at;
                point.rotation = Quaternion.identity;
                list.Add(point);
            }
            placement.points = list;
            made = settlement.CreateStructure(template, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
            return made != null ? "made" : "null";
        });
        Log($"Probe9: CreateStructure with {points.Length} points: {how}");
        return made;
    }

    // The layout's network state takes the points (the placement does not carry them into a structure made in code).
    private static string GivePoints(Structure? plan, Vector3[] points)
    {
        if (plan == null) return "no plan";
        return Try(() =>
        {
            var state = plan.GetComponentInChildren<SSSGame.Network.NetworkStructureSplineLayoutState>(true);
            if (state == null) return "no state";
            foreach (var at in points) state.AddPoint(at);
            state.Rebuild();
            return $"{state.GetPointCount()} points";
        });
    }

    // The plan's build parts finished, as the build site does once supplied and built.
    private static string FinishBuild(Structure? plan)
    {
        if (plan == null) return "no plan";
        return Try(() =>
        {
            var parts = plan.GetComponentsInChildren<BuildPart>(true);
            string before = string.Join(", ", parts.Select(b => $"{b.GetIl2CppType().Name} built {b.IsBuilt()} supplied {b.IsSupplied()}"));
            foreach (var part in parts)
                if (!part.IsBuilt()) part.FinishPart();
            string after = string.Join(", ", parts.Select(b => $"built {b.IsBuilt()}"));
            return $"[{before}] → [{after}]";
        });
    }

    private static string Describe(Structure? plan)
    {
        if (plan == null || plan.gameObject == null) return "gone";
        return Try(() =>
        {
            var layout = plan.GetComponentInChildren<StructureSplineLayout>(true);
            var state = layout?.SplineState;
            var spline = plan.GetComponentInChildren<TerraformingSpline>(true);
            string sections = layout == null ? "?" : string.Join(" ", Enumerable.Range(0, layout.GetSectionCount()).Select(i => state != null ? state.GetSectionState(i).ToString(CultureInfo.InvariantCulture) : "?"));
            return $"'{plan.name}' at {P(plan.transform.position)}, dismantled {plan.Dismantled}; layout points {layout?.GetPointCount()}, sections {layout?.GetSectionCount()} [{sections}], width {layout?.SplineWidth}; state points {state?.GetPointCount()}; spline cells {spline?.GetCellsCount()}, levelled {(spline == null ? "?" : Enumerable.Range(0, spline.GetCellsCount()).Count(i => spline.IsCellLeveled(i)).ToString(CultureInfo.InvariantCulture))}";
        });
    }
}
