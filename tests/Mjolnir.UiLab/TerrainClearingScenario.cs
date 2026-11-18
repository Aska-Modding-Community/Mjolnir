using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain clearing" scenario: trees and boulders taken away through Mjolnir.SDK.Terrain. In the main menu nothing is
/// found. On a spot with a tree beside a boulder, <c>Remove</c> keeps both and <c>Clear</c> takes both, with nothing
/// dropped; on a wooded spot a levelling with <c>LandscapeObjects.Clear</c> takes every tree it reaches and the ground is
/// put back; round the player the console's <c>objects clear</c> takes what grows. It changes the save's world: back the
/// save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainClearingScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Clearing: in the main menu {NaturalObjects.Around(Vector3.zero, 16).Count} objects");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Clearing: no world");
            yield break;
        }
        var home = character.transform.position;
        string Standing(Vector3 at, float r) => string.Join(", ", NaturalObjects.Around(at, r).Where(o => o.Kind != NaturalObjectKind.Item).GroupBy(o => o.Kind).Select(g => $"{g.Key} ×{g.Count()}"));
        string Items(Vector3 at, float r) => string.Join(", ", NaturalObjects.Around(at, r).Where(o => o.Kind == NaturalObjectKind.Item).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"));

        // A spot with a tree beside a boulder, and a wooded spot away from it.
        Vector3 spot = default, wood = default;
        NaturalObject? tree = null, boulder = null;
        bool hasWood = false;
        foreach (float distance in new[] { 40f, 60f, 80f, 100f, 130f, 160f, 200f })
        {
            for (int turn = 0; turn < 360; turn += 20)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var near = NaturalObjects.Around(at, 14);
                if (tree == null)
                {
                    var t = near.FirstOrDefault(o => o.Kind == NaturalObjectKind.Tree);
                    var b = near.FirstOrDefault(o => o.Kind == NaturalObjectKind.LargeObject);
                    if (t != null && b != null)
                    {
                        spot = at;
                        tree = t;
                        boulder = b;
                        Log($"Clearing: spot {P(at)} at {distance} m, {turn}°: tree {t}, boulder {b}");
                        continue;
                    }
                }
                if (!hasWood && tree != null && (at - spot).magnitude > 40f)
                {
                    var square = LandscapeArea.Rectangle(at, 8, 8, blend: 1);
                    var m = Landscape.Measure(square);
                    if (NaturalObjects.In(square).Count(o => o.Kind == NaturalObjectKind.Tree) >= 2 && m.IsKnown && m.Minimum > Landscape.WaterLevel + 1 && m.Maximum - m.Minimum < 3f && Landscape.Check(square, LandscapeOperation.Level) == LandscapeResult.Done)
                    {
                        wood = at;
                        hasWood = true;
                        Log($"Clearing: wooded spot {P(at)} at {distance} m, {turn}°: {Standing(at, 5)}");
                    }
                }
            }
            if (tree != null && hasWood) break;
        }
        if (tree == null || boulder == null)
        {
            Log("Clearing: no tree beside a boulder within 200 m");
            yield break;
        }

        // A. One tree and one boulder.
        var stand = spot - (spot - home).normalized * 12f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 18f, -16f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        Shot("clearing-before");
        yield return AfterShot;
        var treeAt = tree.Position;
        var boulderAt = boulder.Position;
        string itemsBefore = Items(spot, 16);
        bool treeRemoved = tree.Remove(), boulderRemoved = boulder.Remove();
        Log($"Clearing: Remove kept them: tree {treeRemoved}, boulder {boulderRemoved}; clearable: tree {tree.IsClearable}, boulder {boulder.IsClearable}");
        bool treeCleared = tree.Clear(), boulderCleared = boulder.Clear();
        yield return 3f;
        Log($"Clearing: Clear: tree {treeCleared} (gone {tree.IsGone}), boulder {boulderCleared} (gone {boulder.IsGone}); 3 s later at their places {NaturalObjects.Nearest(treeAt, 0.5f)?.ToString() ?? "none"} / {NaturalObjects.Nearest(boulderAt, 0.5f)?.ToString() ?? "none"}; items before [{itemsBefore}], after [{Items(spot, 16)}]");
        Shot("clearing-taken");
        yield return AfterShot;
        camera.Dispose();

        // B. A levelling that clears every tree it reaches, then the ground put back.
        if (hasWood)
        {
            var near = wood - (wood - home).normalized * 14f;
            var go = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(near.x, near.z);
            foreach (float wait in WaitFor("the teleport", () => go.IsCompleted, 30)) yield return wait;
            yield return 3f;
            var view = Overhead(wood);
            view.Position = wood + new Vector3(0, 16f, -14f);
            view.Rotation = Quaternion.LookRotation(wood - view.Position, Vector3.up);
            var square = LandscapeArea.Rectangle(wood, 8, 8, blend: 1);
            var kept = Landscape.Capture(square);
            var heights = Landscape.Measure(square);
            string before = Standing(wood, 5);
            int inside = NaturalObjects.In(square).Count(o => o.Kind != NaturalObjectKind.Item);
            Shot("clearing-wood");
            yield return AfterShot;
            var result = Landscape.Level(square, heights.Average, new LandscapeOptions { Objects = LandscapeObjects.Clear });
            yield return 3f;
            Log(Inv($"Clearing: levelled 8 m to {heights.Average:0.00} with Objects = Clear: {result}; standing in the square {inside} → {NaturalObjects.In(square).Count(o => o.Kind != NaturalObjectKind.Item)}; within 5 m before [{before}], after [{Standing(wood, 5)}]; outside, 5–9 m: {NaturalObjects.Around(wood, 9).Count(o => o.Kind == NaturalObjectKind.Tree && (o.Position - wood).magnitude > 6f)} trees"));
            Shot("clearing-levelled");
            yield return AfterShot;
            var back = kept?.Restore(new LandscapeOptions { KeepOffBuildings = false, Objects = LandscapeObjects.Keep });
            yield return 2f;
            Log($"Clearing: the ground put back: {back?.ToString() ?? "no snapshot"}, now {Landscape.Measure(square)}");
            view.Dispose();
        }
        else Log("Clearing: no wooded spot found");

        // C. The console round the player.
        var player = character.transform.position;
        string round = Standing(player, 4);
        DevConsole.Execute("objects clear 4");
        yield return 2f;
        Log($"Clearing: the console's objects clear at {P(player)}: within 4 m before [{round}], after [{Standing(player, 4)}]");

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
