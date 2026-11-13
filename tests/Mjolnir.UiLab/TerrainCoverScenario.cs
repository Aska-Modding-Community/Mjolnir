using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mjolnir.SDK.Camera;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain cover" scenario: what covers the ground, through Mjolnir.SDK.Terrain. In the main menu no biome is known. In
/// the world it goes to the sunny lawn of the terrain probes, keeps it in a snapshot, and there draws an outline (moved,
/// hidden, shown), paints a biome over a square (its cells along a line read back), paints one only on natural ground round a
/// patch of dirt, digs a patch to dirt and gives it its grass back, raises a square only on its natural ground across a road,
/// and puts the whole lawn back from the snapshot, compared point by point. It changes the ground of the save (the game
/// saves the world by itself): back the save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainCoverScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        var menuOutline = Landscape.ShowOutline(LandscapeArea.Circle(Vector3.zero, 4), Color.yellow);
        yield return 0.5f;
        Log($"Cover: in the main menu {Landscape.Biomes.Count} biomes, here {Landscape.BiomeAt(0, 0)?.ToString() ?? "none"}, find BirchForest {Landscape.FindBiome("BirchForest")?.ToString() ?? "none"}; an outline's lines {Lines()}");
        menuOutline.Dispose();
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log($"Cover: no world: character {character != null}, available {Landscape.IsAvailable}; {Lines()}");
            yield break;
        }
        var home = character.transform.position;
        Log($"Cover: this part of the world grows [{string.Join(", ", Landscape.Biomes)}]; under the player {Landscape.BiomeAt(home.x, home.z)}; the menu's outline gone: {Lines()}");

        foreach (float wait in GoAbove(character, home, s_probe6Biome)) yield return wait;
        var spot = s_probe6Biome + new Vector3(0, 0, -6f);
        spot.y = Landscape.HeightAt(spot.x, spot.z);
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 30f, -22f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        yield return 3f;
        var everything = LandscapeArea.Circle(spot, 28, 4);
        var kept = Landscape.Capture(everything);
        var grid = Grid(spot, 28, 2f).Select(p => (P: p, H: Landscape.HeightAt(p.x, p.z), K: Landscape.KindAt(p.x, p.z), C: Landscape.IsCleared(p.x, p.z), B: Landscape.BiomeAt(p.x, p.z)?.Id ?? -1)).ToList();
        Log($"Cover: the lawn at {P(spot)} kept: {(kept != null ? kept.ToString() : "none")}; biomes there [{string.Join(", ", grid.GroupBy(g => g.B).Select(g => $"{g.Key}×{g.Count()}"))}]; kinds [{string.Join(", ", grid.GroupBy(g => g.K).Select(g => $"{g.Key}×{g.Count()}"))}]");
        if (kept == null) yield break;
        var changing = new List<string>();
        var changed = new List<string>();
        EventHandler<LandscapeChangingEventArgs> onChanging = (_, e) => changing.Add($"{e.Operation} by {e.Source}{(e.ModId != null ? " (" + e.ModId + ")" : "")}");
        EventHandler<LandscapeChangedEventArgs> onChanged = (_, e) => changed.Add($"{e.Operation?.ToString() ?? "?"} by {e.Source}{(e.ModId != null ? " (" + e.ModId + ")" : "")}");
        Landscape.GroundChanging += onChanging;
        Landscape.GroundChanged += onChanged;
        Shot("cover-before");
        yield return AfterShot;

        // An outline: a turned rectangle with a blend, then moved onto a circle, hidden and shown.
        var outline = Landscape.ShowOutline(LandscapeArea.Rectangle(spot + new Vector3(-10, 0, 0), 10, 6, rotation: 30, blend: 2), new Color(1f, 0.55f, 0.05f, 1f));
        yield return 1f;
        Log($"Cover: an outline drawn: {Lines()}");
        Shot("cover-outline");
        yield return AfterShot;
        outline.Area = LandscapeArea.Circle(spot + new Vector3(10, 0, 0), 5, 2);
        outline.Color = new Color(0.1f, 0.8f, 1f, 1f);
        yield return 1f;
        Log($"Cover: the outline moved onto a circle: {Lines()}");
        Shot("cover-outline-moved");
        yield return AfterShot;
        outline.IsVisible = false;
        yield return 0.5f;
        string hidden = Lines();
        outline.IsVisible = true;
        yield return 0.5f;
        Log($"Cover: hidden {hidden}; shown again {Lines()}");
        outline.Dispose();
        yield return 0.5f;
        Log($"Cover: the outline disposed: {Lines()}");

        // A biome over a 10 m square left of the middle: its cells read along a line through it every 2 m.
        var a = spot + new Vector3(-10, 0, 0);
        var here = Landscape.BiomeAt(a.x, a.z);
        var biome = Landscape.Biomes.FirstOrDefault(b => b.Name == "ResourceForest" && b != here);
        if (biome.Name.Length == 0) biome = Landscape.Biomes.First(b => b != here);
        string Row(Vector3 at) => string.Join(" ", Enumerable.Range(-5, 11).Select(k => Landscape.BiomeAt(at.x + 2 * k, at.z)?.Id.ToString() ?? "?"));
        string rowBefore = Row(a);
        var timer = Stopwatch.StartNew();
        var result = Landscape.PaintBiome(LandscapeArea.Rectangle(a, 10, 10, blend: 0), biome);
        long ms = timer.ElapsedMilliseconds;
        yield return 1f;
        Log($"Cover: {biome} painted over 10 × 10 m at {P(a)} (was {here}): {result} in {ms} ms; every 2 m from −10 to +10 along x, before [{rowBefore}], after [{Row(a)}]; kind {Landscape.KindAt(a.x, a.z)}, cleared {Landscape.IsCleared(a.x, a.z)}; {Events(changing, changed)}");
        yield return 2f;
        Shot("cover-biome");
        yield return AfterShot;

        // A biome only on natural ground, round a patch of dirt.
        var d = spot + new Vector3(-10, 0, 13);
        var patch = Landscape.Paint(LandscapeArea.Circle(d, 3, 0), GroundKind.Dirt);
        string rowD = Row(d);
        result = Landscape.PaintBiome(LandscapeArea.Rectangle(d, 14, 8, blend: 0), biome, new LandscapeOptions { OnlyOn = GroundKind.Natural });
        yield return 1f;
        Log($"Cover: dirt round {P(d)} ({patch}), then {biome} only on natural ground over 14 × 8 m: {result}; along x before [{rowD}], after [{Row(d)}] (the dirt from −3 to +3)");

        // A patch dug to dirt and its grass given back.
        var b = spot;
        var square = LandscapeArea.Rectangle(b, 5, 5, blend: 0);
        result = Landscape.Paint(square, GroundKind.Dirt, new LandscapeOptions { ClearVegetation = true });
        yield return 2f;
        Log($"Cover: a 5 m square dirt at {P(b)}: {result}, kind {Landscape.KindAt(b.x, b.z)}, cleared {Landscape.IsCleared(b.x, b.z)}");
        Shot("cover-dirt");
        yield return AfterShot;
        float height = Landscape.HeightAt(b.x, b.z);
        result = Landscape.RestoreVegetation(square);
        yield return 2f;
        Log($"Cover: its grass given back: {result}, kind {Landscape.KindAt(b.x, b.z)}, cleared {Landscape.IsCleared(b.x, b.z)}, at 2 m {Landscape.KindAt(b.x + 2, b.z)}; height moved {Landscape.HeightAt(b.x, b.z) - height:0.000}; {Events(changing, changed)}");
        Shot("cover-grass");
        yield return AfterShot;

        // A square raised only on its natural ground, across a road.
        var c = spot + new Vector3(10, 0, 0);
        Landscape.Paint(LandscapeArea.Rectangle(c, 2, 10, blend: 0), GroundKind.Road);
        yield return 0.5f; // the game's tool applies it later in the frame
        float onRoad = Landscape.HeightAt(c.x, c.z), beside = Landscape.HeightAt(c.x + 3, c.z);
        result = Landscape.Raise(LandscapeArea.Rectangle(c, 8, 8, blend: 1), 1, new LandscapeOptions { OnlyOn = GroundKind.Natural, KeepKind = true, ClearVegetation = false });
        yield return 1f;
        Log($"Cover: 8 m raised 1 m only on natural ground across a road at {P(c)}: {result}; the road moved {Landscape.HeightAt(c.x, c.z) - onRoad:0.000} ({Landscape.KindAt(c.x, c.z)}), the meadow 3 m off {Landscape.HeightAt(c.x + 3, c.z) - beside:0.000} ({Landscape.KindAt(c.x + 3, c.z)}); {Events(changing, changed)}");
        Shot("cover-onlyon");
        yield return AfterShot;

        // The whole lawn put back.
        timer.Restart();
        result = kept.Restore(new LandscapeOptions { KeepOffBuildings = false }); // a tower of the fence stands at its edge, unchanged
        ms = timer.ElapsedMilliseconds;
        yield return 2f;
        float worst = 0;
        int kinds = 0, cleared = 0, biomes = 0;
        foreach (var g in grid)
        {
            worst = Math.Max(worst, Math.Abs(Landscape.HeightAt(g.P.x, g.P.z) - g.H));
            if (Landscape.KindAt(g.P.x, g.P.z) != g.K) kinds++;
            if (Landscape.IsCleared(g.P.x, g.P.z) != g.C) cleared++;
            if ((Landscape.BiomeAt(g.P.x, g.P.z)?.Id ?? -1) != g.B) biomes++;
        }
        Log(Inv($"Cover: the lawn put back: {result} in {ms} ms; {grid.Count} points: height off by at most {worst:0.0000} m, {kinds} kinds, {cleared} vegetation marks and {biomes} biomes differ; {Events(changing, changed)}"));
        Shot("cover-restored");
        yield return AfterShot;
        Landscape.GroundChanging -= onChanging;
        Landscape.GroundChanged -= onChanged;
        camera.Dispose();

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // The outlines' lines in the world: each line's points, whether it shows, its shader and colour.
    private static string Lines()
    {
        var found = Resources.FindObjectsOfTypeAll<LineRenderer>()
            .Where(l => l != null && l.transform.parent != null && l.transform.parent.name == "Mjolnir_LandscapeOutline")
            .Select(l => $"{l.name} {l.positionCount} points {(l.gameObject.activeInHierarchy ? "shown" : "hidden")} width {l.widthMultiplier:0.00} shader {l.sharedMaterial?.shader?.name ?? "none"} colour {l.sharedMaterial?.color.ToString() ?? "none"}")
            .ToList();
        return found.Count == 0 ? "none" : string.Join("; ", found);
    }
}
