using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Il2CppInterop.Runtime;
using Mjolnir.SDK;
using Mjolnir.SDK.Camera;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.Render;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain tools" scenario: what Mjolnir.SDK.Terrain writes itself, against the game's own ground. In the main menu
/// nothing can be measured or kept. In the world it finds a free spot 50 m across through the API, keeps all of it in a
/// snapshot, and there raises a circle (its profile from the middle out), measures it, smooths it, levels a triangle into a
/// path, lays a ramp, digs a crater of its own shape, raises a mask, copies the mound and pastes it turned and relative to
/// the ground elsewhere, clears a circle's vegetation and counts the objects taken, smooths 40 m across against the clock,
/// runs the console's smooth and undo, and puts the whole spot back from the snapshot, compared sample by sample. It
/// changes the ground of the save (the game saves the world by itself): back the save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainToolsScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        var menuArea = LandscapeArea.Circle(Vector3.zero, 4);
        Log($"Tools: in the main menu measure {Landscape.Measure(menuArea)}, capture {(Landscape.Capture(menuArea) == null ? "none" : "a snapshot")}");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log($"Tools: no world: character {character != null}, available {Landscape.IsAvailable}");
            yield break;
        }

        // A free spot: no building within the whole area, above the water.
        var home = character.transform.position;
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 70f, 90f, 110f, 140f, 170f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var whole = LandscapeArea.Circle(at, 24, 8);
                if (Landscape.Check(whole, LandscapeOperation.Raise) != LandscapeResult.Done) continue;
                var measured = Landscape.Measure(whole);
                if (!measured.IsKnown || measured.Minimum < Landscape.WaterLevel + 1 || measured.Maximum - measured.Minimum > 8) continue;
                spot = at;
                found = true;
                Log($"Tools: free spot {P(at)} at {distance} m, {turn}° from the player: {measured}");
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Tools: no free spot found within 170 m");
            yield break;
        }
        var away = spot - home;
        away.y = 0;
        away.Normalize();
        var stand = spot - away * 34f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 3f;
        spot.y = Landscape.HeightAt(spot.x, spot.z);
        var camera = GameCamera.Take(100);
        camera.Position = spot + new Vector3(0, 26f, -22f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        camera.FieldOfView = 55;

        var everything = LandscapeArea.Circle(spot, 24, 8);
        var kept = Landscape.Capture(everything);
        Log($"Tools: the whole spot kept: {(kept != null ? kept.ToString() : "none")}, its middle {kept?.CenterHeight:0.000}");
        if (kept == null) yield break;
        var grid = Grid(spot, 30, 3f).Select(p => (P: p, H: Landscape.HeightAt(p.x, p.z), K: Landscape.KindAt(p.x, p.z), C: Landscape.IsCleared(p.x, p.z))).ToList();

        var changing = new List<string>();
        var changed = new List<string>();
        EventHandler<LandscapeChangingEventArgs> onChanging = (_, e) => changing.Add($"{e.Operation} by {e.Source}{(e.ModId != null ? " (" + e.ModId + ")" : "")}");
        EventHandler<LandscapeChangedEventArgs> onChanged = (_, e) => changed.Add($"{e.Operation?.ToString() ?? "?"} by {e.Source}{(e.ModId != null ? " (" + e.ModId + ")" : "")}");
        Landscape.GroundChanging += onChanging;
        Landscape.GroundChanged += onChanged;
        Shot("tools-before");
        yield return AfterShot;

        // Where the SDK's samples lie: a step of 1 m written at x ≥ 0 (then z ≥ 0), read across it every 10 cm by the sampler and
        // by the data; the half-way point tells where the game draws the samples the SDK computed.
        foreach (bool alongX in new[] { true, false })
        {
            var s0 = spot + new Vector3(alongX ? 0 : 12, 0, alongX ? -12 : 0);
            var stepArea = LandscapeArea.Rectangle(s0, 6, 6, 0, 0);
            var line = Enumerable.Range(-20, 41).Select(k => k * 0.1f).ToArray();
            float[] Read() => line.Select(o => Landscape.HeightAt(s0.x + (alongX ? o : 0), s0.z + (alongX ? 0 : o))).ToArray();
            var plain = Read();
            Landscape.Shape(stepArea, (dx, dz, now) => (alongX ? dx : dz) >= 0 ? now + 1 : float.NaN, new LandscapeOptions { KeepKind = true, ClearVegetation = false });
            yield return 1f;
            var raised = Read();
            var data = Landscape.Capture(stepArea);
            string half = "none";
            for (int k = 1; k < line.Length; k++)
                if (raised[k] - plain[k] >= 0.5f && raised[k - 1] - plain[k - 1] < 0.5f) { half = line[k].ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture); break; }
            Log(Inv($"Tools: a step along {(alongX ? "x" : "z")} at 0: the sampler passes half of it at {half} m; Δ every 10 cm from −0.5 to +0.5 m: {string.Join(" ", Enumerable.Range(15, 11).Select(k => (raised[k] - plain[k]).ToString("0.00", CultureInfo.InvariantCulture)))}; the data at −0.5, −0.25, 0, +0.25, +0.5: {string.Join(" ", new[] { -0.5f, -0.25f, 0, 0.25f, 0.5f }.Select(o => ((data?.HeightAt(s0.x + (alongX ? o : 0), s0.z + (alongX ? 0 : o)) ?? float.NaN) - (data?.HeightAt(s0.x + (alongX ? -1.5f : 0), s0.z + (alongX ? 0 : -1.5f)) ?? float.NaN)).ToString("0.00", CultureInfo.InvariantCulture)))}; {Events(changing, changed)}"));
        }

        // A circle raised: its profile from the middle out.
        var a = spot + new Vector3(-10, 0, 0);
        var before = Spoke(a);
        var circle = LandscapeArea.Circle(a, 3, 2);
        var timer = Stopwatch.StartNew();
        var result = Landscape.Raise(circle, 1.5f);
        long ms = timer.ElapsedMilliseconds;
        yield return 1f;
        Log($"Tools: circle r 3, blend 2 raised 1.5 m: {result} in {ms} ms; Δ at 0..7 m {Spoke(a, before)}; kind {Landscape.KindAt(a.x, a.z)}, cleared {Landscape.IsCleared(a.x, a.z)}; {Events(changing, changed)}");
        Log($"Tools: measured {Landscape.Measure(circle)}; the game's own data at 0..7 m {DataSpoke(a, before)} (the sampler above reads the drawn terrain)");
        Shot("tools-circle");
        yield return AfterShot;

        // The mound smoothed three times.
        for (int i = 0; i < 3; i++) Landscape.Smooth(LandscapeArea.Circle(a, 6, 1), 1f);
        yield return 1f;
        Log($"Tools: smoothed three times: Δ at 0..7 m {Spoke(a, before)}; {Events(changing, changed)}");

        // A triangle levelled half a metre down into a path, its grass kept.
        var b = spot + new Vector3(8, 0, -8);
        float hb = Landscape.HeightAt(b.x, b.z), outsideBefore = Landscape.HeightAt(b.x + 6, b.z);
        var triangle = LandscapeArea.Polygon([b + new Vector3(-4, 0, -3), b + new Vector3(4, 0, -3), b + new Vector3(0, 0, 4)], 1);
        result = Landscape.Level(triangle, hb - 0.5f, new LandscapeOptions { Kind = GroundKind.Path, ClearVegetation = false });
        yield return 1f;
        Log(Inv($"Tools: triangle levelled to {hb - 0.5f:0.00} as a path: {result}; inside {Landscape.HeightAt(b.x, b.z - 1):0.000} {Landscape.KindAt(b.x, b.z - 1)} cleared {Landscape.IsCleared(b.x, b.z - 1)}, outside {Landscape.HeightAt(b.x + 6, b.z) - outsideBefore:+0.000;-0.000}; {Events(changing, changed)}"));

        // A ramp of 3 m over 12 m.
        var c = spot + new Vector3(10, 0, 8);
        float hc = Landscape.HeightAt(c.x, c.z);
        var ramp = LandscapeArea.Rectangle(c, 12, 4, 0, 1);
        result = Landscape.Slope(ramp, new Vector3(c.x - 6, hc, c.z), new Vector3(c.x + 6, hc + 3, c.z));
        yield return 1f;
        var rampData = Landscape.Capture(LandscapeArea.Circle(c, 8, 0));
        Log(Inv($"Tools: ramp from {hc:0.00} to {hc + 3:0.00}: {result}; at −6, −3, 0, 3, 6 m {string.Join(", ", new[] { -6f, -3, 0, 3, 6 }.Select(d => (Landscape.HeightAt(c.x + d, c.z) - hc).ToString("+0.00;-0.00", CultureInfo.InvariantCulture)))}; the data {string.Join(", ", new[] { -6f, -3, 0, 3, 6 }.Select(d => ((rampData?.HeightAt(c.x + d, c.z) ?? float.NaN) - hc).ToString("+0.00;-0.00", CultureInfo.InvariantCulture)))}; {Events(changing, changed)}"));
        Shot("tools-ramp");
        yield return AfterShot;

        // A crater of the mod's own shape: a pit of 1 m inside 2 m, a rim of 0.5 m out to 4 m.
        var d = spot + new Vector3(0, 0, 12);
        var craterBefore = Spoke(d);
        result = Landscape.Shape(LandscapeArea.Circle(d, 4, 1), (dx, dz, now) => MathF.Sqrt(dx * dx + dz * dz) < 2 ? now - 1 : now + 0.5f);
        yield return 1f;
        Log($"Tools: a crater of its own shape: {result}; Δ at 0..7 m {Spoke(d, craterBefore)}; {Events(changing, changed)}");

        // A mask: a bump at its middle.
        var e = spot + new Vector3(-8, 0, 10);
        var maskBefore = Spoke(e);
        var weights = new float[,] { { 0, 0, 0 }, { 0, 1, 0 }, { 0, 0, 0 } };
        result = Landscape.Raise(LandscapeArea.Mask(e, weights, 8, 8, 30), 1f);
        yield return 1f;
        Log($"Tools: a 3 × 3 mask over 8 m raised 1 m: {result}; Δ at 0..7 m {Spoke(e, maskBefore)}; {Events(changing, changed)}");

        // The mound copied and laid down 16 m away, turned 45° and relative to the ground there.
        var copy = Landscape.Capture(LandscapeArea.Circle(a, 5, 1));
        var f = spot + new Vector3(-4, 0, -14);
        var pasteBefore = Spoke(f);
        float sourceRise = Landscape.HeightAt(a.x, a.z) - before[0];
        result = copy?.PasteAt(f, 45, Landscape.HeightAt(f.x, f.z) - copy.CenterHeight) ?? LandscapeResult.Failed;
        yield return 1f;
        Log(Inv($"Tools: the mound (risen {sourceRise:+0.000;-0.000} at its middle) pasted turned 45° and relative: {result}; Δ at 0..7 m {Spoke(f, pasteBefore)}; the copy's own profile {Spoke(a, before)}; {Events(changing, changed)}"));
        Shot("tools-paste");
        yield return AfterShot;

        // A circle's vegetation cleared: the objects it takes.
        var g = spot + new Vector3(14, 0, -2);
        int objectsBefore = Removable(g, 4);
        result = Landscape.ClearVegetation(LandscapeArea.Circle(g, 4, 0));
        yield return 1f;
        Log($"Tools: a circle of 4 m cleared: {result}; removable objects {objectsBefore} → {Removable(g, 4)}, cleared {Landscape.IsCleared(g.x, g.z)}; {Events(changing, changed)}");

        // Large: 40 m across against the clock.
        timer.Restart();
        var natural = Grid(spot, 18, 1f).FirstOrDefault(p => Landscape.KindAt(p.x, p.z) == GroundKind.Natural && !Landscape.IsCleared(p.x, p.z));
        result = Landscape.Smooth(LandscapeArea.Circle(spot, 20, 4), 0.3f, new LandscapeOptions { KeepKind = true, ClearVegetation = false });
        ms = timer.ElapsedMilliseconds;
        yield return 2f;
        Log($"Tools: smoothed 48 m across keeping the kind and the grass: {result} in {ms} ms; a natural point {P(natural)} now {Landscape.KindAt(natural.x, natural.z)}, cleared {Landscape.IsCleared(natural.x, natural.z)}; {Events(changing, changed)}");
        Shot("tools-smoothed");
        yield return AfterShot;

        // The console: smooth in front of the player, then undo.
        float ahead = Landscape.HeightAt(character.transform.position.x, character.transform.position.z);
        DevConsole.Execute("raise 1 6");
        yield return 1f;
        DevConsole.Execute("smooth 0.5 8");
        yield return 1f;
        DevConsole.Execute("undo");
        yield return 1f;
        Log($"Tools: the console's raise, smooth and undo ran (their answers are the [Console] lines); feet {ahead:0.000}");

        Shot("tools-after");
        yield return AfterShot;

        // Everything put back.
        timer.Restart();
        result = kept.Restore(new LandscapeOptions { KeepOffBuildings = false });
        ms = timer.ElapsedMilliseconds;
        yield return 2f;
        float worst = 0;
        int kinds = 0, cleared = 0;
        foreach (var p in grid)
        {
            worst = Math.Max(worst, Math.Abs(Landscape.HeightAt(p.P.x, p.P.z) - p.H));
            if (Landscape.KindAt(p.P.x, p.P.z) != p.K) kinds++;
            if (Landscape.IsCleared(p.P.x, p.P.z) != p.C) cleared++;
        }
        Log($"Tools: while it was put back: {Events(changing, changed)}");
        var again = Landscape.Capture(everything);
        float worstData = 0;
        var worstAt = spot;
        foreach (var p in Grid(spot, 31, 0.5f))
        {
            float off = Math.Abs((again?.HeightAt(p.x, p.z) ?? float.NaN) - kept.HeightAt(p.x, p.z));
            if (off > worstData) { worstData = off; worstAt = p; }
        }
        Log(Inv($"Tools: the whole spot put back: {result} in {ms} ms; {grid.Count} points by the sampler: height off by at most {worst:0.0000} m, {kinds} kinds and {cleared} vegetation marks differ; by the data every half metre: off by at most {worstData:0.0000} m at {P(worstAt)} ({Math.Sqrt((worstAt.x - spot.x) * (worstAt.x - spot.x) + (worstAt.z - spot.z) * (worstAt.z - spot.z)):0.0} m from the middle); {Events(changing, changed)}"));
        Shot("tools-restored");
        yield return AfterShot;
        Landscape.GroundChanging -= onChanging;
        Landscape.GroundChanged -= onChanged;
        camera.Dispose();

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<Vector3> Grid(Vector3 middle, float radius, float step)
    {
        for (float x = -radius; x <= radius; x += step)
            for (float z = -radius; z <= radius; z += step)
                if (x * x + z * z <= radius * radius) yield return middle + new Vector3(x, 0, z);
    }

    private static float[] Spoke(Vector3 at) => Enumerable.Range(0, 8).Select(m => Landscape.HeightAt(at.x + m, at.z)).ToArray();

    private static string Spoke(Vector3 at, float[] before)
    {
        var now = Spoke(at);
        return string.Join(" ", now.Select((h, i) => (h - before[i]).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)));
    }

    // The heights the game keeps, read through a snapshot (not the sampler, which reads the drawn terrain).
    private static string DataSpoke(Vector3 at, float[] before)
    {
        var data = Landscape.Capture(LandscapeArea.Circle(at, 8, 0));
        return string.Join(" ", Enumerable.Range(0, 8).Select(m => ((data?.HeightAt(at.x + m, at.z) ?? float.NaN) - before[m]).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)));
    }

    private static string Events(List<string> changing, List<string> changed)
    {
        string text = $"events [{string.Join("; ", changing)}] then [{string.Join("; ", changed)}]";
        changing.Clear();
        changed.Clear();
        return text;
    }

    private static int Removable(Vector3 at, float radius)
    {
        try
        {
            var list = new Il2CppSystem.Collections.Generic.List<WorldItemInstance>();
            var filter = new HeightmapTool.RemovableInstanceFilter().BoxIl2CppObject().Cast<IWorldItemInstanceFilter>();
            GameManagers.Get<ResourceManager>()?.GetAllInstancesList(list, at, Quaternion.identity, new Vector3(radius * 2, 40, radius * 2), SDFShape.CIRCLE, filter, InstanceActivationContext.RESOURCE_FINDER, false);
            int count = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !list[i].Destroyed) count++;
            return count;
        }
        catch (Exception ex)
        {
            Log($"Tools: objects not counted: {ex.Message}");
            return -1;
        }
    }
}
