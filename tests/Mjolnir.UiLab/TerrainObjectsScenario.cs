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
/// The "terrain objects" scenario: the world's natural objects through Mjolnir.SDK.Terrain. In the main menu nothing is
/// found. In the world it goes to a wooded spot, lists what grows there (kinds, health, resources, what a change of the
/// ground takes), takes a removable thing away, raises the ground under a tree with the objects kept and then settles them,
/// raises another patch with the objects settled by the change itself, moves one object by hand and puts it back, runs the
/// console's objects, and puts the whole spot back from a snapshot. It changes the ground and the objects of the save (the
/// game saves the world by itself): back the save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainObjectsScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Objects: in the main menu {NaturalObjects.Around(Vector3.zero, 20).Count} found, nearest {NaturalObjects.Nearest(Vector3.zero)?.ToString() ?? "none"}");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log($"Objects: no world: character {character != null}, available {Landscape.IsAvailable}");
            yield break;
        }
        var home = character.transform.position;

        // A wooded spot with trees and smaller things, clear of buildings.
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 60f, 80f, 100f, 130f, 160f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var whole = LandscapeArea.Circle(at, 14, 4);
                if (Landscape.Check(whole, LandscapeOperation.Raise) != LandscapeResult.Done) continue;
                if (Landscape.Measure(whole) is { IsKnown: true } measured && measured.Minimum < Landscape.WaterLevel + 1) continue;
                var there = NaturalObjects.Around(at, 12);
                if (there.Count(o => o.Kind == NaturalObjectKind.Tree) < 2 || !there.Any(o => o.IsRemovable)) continue;
                spot = at;
                found = true;
                Log($"Objects: wooded spot {P(at)} at {distance} m, {turn}° from the player: {there.Count} objects");
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Objects: no wooded spot found within 160 m");
            yield break;
        }
        var away = spot - home;
        away.y = 0;
        away.Normalize();
        var stand = spot - away * 16f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 3f;
        spot.y = Landscape.HeightAt(spot.x, spot.z);
        var camera = GameCamera.Take(100);
        camera.Position = spot + new Vector3(0, 18f, -16f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        camera.FieldOfView = 55;
        var everything = LandscapeArea.Circle(spot, 14, 4);
        var kept = Landscape.Capture(everything);
        Shot("objects-before");
        yield return AfterShot;

        // A. What grows here.
        var all = NaturalObjects.Around(spot, 12);
        Log($"Objects: {all.Count} round {P(spot)}: " + string.Join(", ", all.GroupBy(o => o.Kind).Select(g => $"{g.Count()} {g.Key}")));
        foreach (var o in all.Take(8))
            Log(Inv($"Objects:   {o} health {o.Health:0.#}, quantity {o.Quantity}, gives '{o.ResourceName}', removable {o.IsRemovable}, harvestable {o.IsHarvestable}, {o.HeightOverGround:+0.00;-0.00;0.00} m over the ground"));
        var inArea = NaturalObjects.In(LandscapeArea.Circle(spot, 4, blend: 0));
        Log($"Objects: in a circle of 4 m: {inArea.Count}; nearest to the player {NaturalObjects.Nearest(character.transform.position, 20)}");

        // B. One removable thing taken away.
        var removable = all.FirstOrDefault(o => o.IsRemovable && o.Kind != NaturalObjectKind.Grass);
        if (removable != null)
        {
            int before = NaturalObjects.Around(spot, 12).Count;
            bool gone = removable.Remove();
            bool atOnce = removable.IsGone;
            yield return 1.5f;
            Log($"Objects: '{removable.AssetName}' taken away: {gone}, gone at once {atOnce}, gone now {removable.IsGone}; round the spot {before} → {NaturalObjects.Around(spot, 12).Count}");
            var tree0 = NaturalObjects.Around(spot, 12).FirstOrDefault(o => o.Kind == NaturalObjectKind.Tree);
            if (tree0 != null) Log($"Objects: a tree is not taken by a change of the ground: Remove() {tree0.Remove()}, gone {tree0.IsGone}");
            Shot("objects-removed");
            yield return AfterShot;
        }
        else Log("Objects: nothing removable here");

        // C. The ground raised under the trees with the objects kept, then settled by hand.
        var tree = NaturalObjects.Around(spot, 10).FirstOrDefault(o => o.Kind is NaturalObjectKind.Tree or NaturalObjectKind.LargeObject);
        if (tree != null)
        {
            var under = LandscapeArea.Circle(tree.Position, 4, blend: 1);
            var result = Landscape.Raise(under, 1.5f, new LandscapeOptions { Objects = LandscapeObjects.Keep, KeepKind = true, ClearVegetation = false });
            yield return 1.5f;
            Log(Inv($"Objects: 4 m raised 1.5 m under '{tree.AssetName}' keeping the objects: {result}; it stands {tree.HeightOverGround:+0.00;-0.00;0.00} m over the ground"));
            Shot("objects-kept");
            yield return AfterShot;
            bool settled = tree.Settle();
            yield return 1.5f;
            Log(Inv($"Objects: settled by hand: {settled}; now {tree.HeightOverGround:+0.00;-0.00;0.00} m over the ground, at {P(tree.Position)}"));
            Shot("objects-settled");
            yield return AfterShot;
        }
        else Log("Objects: no tree near the spot");

        // D. A change that settles what stands in it.
        var patch = spot + new Vector3(6, 0, 0);
        patch.y = Landscape.HeightAt(patch.x, patch.z);
        var standing = NaturalObjects.Around(patch, 4);
        var heightsBefore = standing.Select(o => o.HeightOverGround).ToArray();
        var settle = Landscape.Lower(LandscapeArea.Circle(patch, 4, blend: 1), 1f, new LandscapeOptions { Objects = LandscapeObjects.Settle, KeepKind = true, ClearVegetation = false });
        yield return 1.5f;
        Log(Inv($"Objects: 4 m dug 1 m at {P(patch)} settling {standing.Count} objects: {settle}; over the ground before [{string.Join(", ", heightsBefore.Select(h => h.ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture)))}], after [{string.Join(", ", standing.Select(o => o.IsGone ? "gone" : o.HeightOverGround.ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture)))}]"));
        Shot("objects-dug");
        yield return AfterShot;

        // E. One moved by hand and put back.
        var mover = NaturalObjects.Around(spot, 10).FirstOrDefault(o => o.Kind != NaturalObjectKind.Grass && !o.IsGone);
        if (mover != null)
        {
            var was = mover.Position;
            var there = was + new Vector3(3, 0, 0);
            there.y = Landscape.HeightAt(there.x, there.z);
            bool moved = mover.MoveTo(there);
            yield return 1.5f;
            Log($"Objects: '{mover.AssetName}' moved 3 m: {moved}, now at {P(mover.Position)} (was {P(was)})");
            Shot("objects-moved");
            yield return AfterShot;
            mover.MoveTo(was);
            yield return 1f;
            Log($"Objects: put back at {P(mover.Position)}");
        }

        // F. The console (its answers go to the console window; the log only says it ran).
        DevConsole.Execute("objects 12");
        Log("Objects: the console's objects ran");

        // G. The spot put back (the objects taken do not come back).
        if (kept != null)
        {
            var result = kept.Restore(new LandscapeOptions { KeepOffBuildings = false });
            yield return 2f;
            Log($"Objects: the spot put back: {result}");
            Shot("objects-restored");
            yield return AfterShot;
        }
        camera.Dispose();

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
