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
using SandSailorStudio.TerrainTools;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain" scenario: Mjolnir.SDK.Terrain against the game's own ground. In the main menu there is no ground to read. In
/// the world it reads the ground under the player beside the game's sampler and the world's water; checks a change among
/// the settlement's houses (refused for the buildings) and finds a free spot up to 160 m away through <c>Landscape.Check</c>;
/// puts the player 12 m from it with a camera of the SDK's above it; then raises it by 2 m, lowers it by 1 m, levels it to
/// a height, paints a road across it, clears its vegetation and raises the largest brush at its corner, each with the
/// heights read back and the events seen; cancels a raise from a handler; digs with the tool on the player's own character
/// (the event tells the player made it); and runs the console's ground, raise and paint. It changes the ground of the save
/// (the game saves the world by itself): back the save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Terrain: in the main menu available {Landscape.IsAvailable}, height at 0,0 {Landscape.HeightAt(0, 0)}, loaded {Landscape.IsLoaded(0, 0)}");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log($"Terrain: no world: character {character != null}, available {Landscape.IsAvailable}");
            yield break;
        }

        var home = character.transform.position;
        Log($"Terrain: under the player {GroundAt(home)}; water {Landscape.WaterLevel:0.00}; 1000 m away loaded {Landscape.IsLoaded(home.x + 1000, home.z)} height {Landscape.HeightAt(home.x + 1000, home.z)}");
        var amongHouses = LandscapeArea.Rectangle(home + character.transform.forward * 12f, 8, 8);
        Log($"Terrain: a levelling among the houses: Check {Landscape.Check(amongHouses, LandscapeOperation.Level)}, Level {Landscape.Level(amongHouses, Landscape.HeightAt(home.x, home.z))} (refused: nothing changed)");

        // A free spot through the API itself.
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 40f, 60f, 80f, 100f, 130f, 160f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y) || Landscape.IsUnderwater(at.x, at.z)) continue;
                if (Landscape.Check(LandscapeArea.Rectangle(at, 10, 10, 0, 2), LandscapeOperation.Level) != LandscapeResult.Done) continue;
                spot = at;
                found = true;
                Log($"Terrain: free spot {P(at)} at {distance} m, {turn}° from the player");
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Terrain: no free spot found within 160 m");
            yield break;
        }
        var away = spot - home;
        away.y = 0;
        away.Normalize();
        var stand = spot - away * 12f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 3f;
        spot.y = Landscape.HeightAt(spot.x, spot.z);
        var camera = GameCamera.Take(100);
        camera.Position = spot + new Vector3(0, 16f, -8f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        camera.FieldOfView = 50;

        var changing = new List<string>();
        var changed = new List<string>();
        EventHandler<LandscapeChangingEventArgs> onChanging = (_, e) => changing.Add($"{e.Operation} by {e.Source}{(e.ModId != null ? " (" + e.ModId + ")" : "")} over {Rc(e.Area)}");
        EventHandler<LandscapeChangedEventArgs> onChanged = (_, e) => changed.Add($"{e.Operation?.ToString() ?? "?"} by {e.Source}{(e.ModId != null ? " (" + e.ModId + ")" : "")} over {Rc(e.Area)}");
        Landscape.GroundChanging += onChanging;
        Landscape.GroundChanged += onChanged;
        yield return 1f;
        Log($"Terrain: the spot {GroundAt(spot)}");
        Shot("terrain-api-before");
        yield return AfterShot;

        var square = LandscapeArea.Rectangle(spot, 4, 4, 0, 1);
        float h0 = Landscape.HeightAt(spot.x, spot.z);
        foreach (var (what, run) in new (string, Func<LandscapeResult>)[]
                 {
                     ("raise 2 m", () => Landscape.Raise(square, 2f)),
                     ("lower 1 m", () => Landscape.Lower(square, 1f)),
                     ("level to start + 0.5 m", () => Landscape.Level(square, h0 + 0.5f)),
                     ("paint a road 2 × 6", () => Landscape.Paint(LandscapeArea.Rectangle(spot, 2, 6, 30, 0.5f), GroundKind.Road)),
                     ("clear the vegetation 8 × 8", () => Landscape.ClearVegetation(LandscapeArea.Rectangle(spot + new Vector3(6, 0, 0), 8, 8))),
                 })
        {
            float before = Landscape.HeightAt(spot.x, spot.z);
            int changingBefore = changing.Count, changedBefore = changed.Count;
            var result = run();
            yield return 1f;
            Log(string.Create(CultureInfo.InvariantCulture, $"Terrain: {what}: {result}; centre {before:0.000} → {Landscape.HeightAt(spot.x, spot.z):0.000} (Δ {Landscape.HeightAt(spot.x, spot.z) - before:+0.000;-0.000}), kind {Landscape.KindAt(spot.x, spot.z)}, cleared {Landscape.IsCleared(spot.x, spot.z)} / beside {Landscape.IsCleared(spot.x + 6, spot.z)}; events [{string.Join("; ", changing.Skip(changingBefore))}] then [{string.Join("; ", changed.Skip(changedBefore))}]"));
            Shot("terrain-api-" + what.Split(' ')[0]);
            yield return AfterShot;
        }

        // The largest brush at a corner: how far it reaches.
        var corner = spot + new Vector3(4, 0, 4);
        var aroundBefore = Ring(corner);
        var large = Landscape.Raise(LandscapeArea.Point(corner, LandscapeBrush.Large), 0.5f);
        yield return 1f;
        Log($"Terrain: the large brush raised 0.5 m: {large}; Δ by distance from its middle {Ring(corner, aroundBefore)}");

        // A handler cancels a raise.
        EventHandler<LandscapeChangingEventArgs> veto = (_, e) => e.Cancel = e.Operation == LandscapeOperation.Raise;
        Landscape.GroundChanging += veto;
        float vetoBefore = Landscape.HeightAt(spot.x, spot.z);
        var vetoed = Landscape.Raise(square, 1f);
        yield return 1f;
        Landscape.GroundChanging -= veto;
        Log(string.Create(CultureInfo.InvariantCulture, $"Terrain: a raise cancelled by a handler: {vetoed}; centre Δ {Landscape.HeightAt(spot.x, spot.z) - vetoBefore:+0.000;-0.000}"));

        // The game's own tool on the player's character: the source says the player.
        var own = character.GetComponent<HeightmapTool>();
        if (own != null)
        {
            int changingBefore = changing.Count;
            var dug = own.Run(TerraformingToolOperation.DIG, spot + new Vector3(-4, 0, 0));
            yield return 1f;
            Log($"Terrain: the character's own tool dug: {dug}; events [{string.Join("; ", changing.Skip(changingBefore))}]");
        }
        else Log("Terrain: the character has no tool of its own");

        // Under the player's own feet.
        var feet = character.transform.position;
        Log($"Terrain: a raise under the player's feet: {Landscape.Raise(LandscapeArea.Rectangle(feet, 3, 3), 0.5f)}");
        yield return 1f;

        // The console, the player facing the spot: the console changes a square in front of them.
        character.transform.LookAt(new Vector3(spot.x, character.transform.position.y, spot.z));
        yield return 0.5f;
        DevConsole.Execute("ground");
        DevConsole.Execute($"ground {spot.x.ToString(CultureInfo.InvariantCulture)} {spot.z.ToString(CultureInfo.InvariantCulture)}");
        DevConsole.Execute("raise 0.5 3");
        DevConsole.Execute("paint path 3");
        yield return 1f;
        Log("Terrain: the console's answers are the [Console] lines above");
        Shot("terrain-api-console");
        yield return AfterShot;

        Landscape.GroundChanging -= onChanging;
        Landscape.GroundChanged -= onChanged;
        camera.Dispose();
        yield return 1f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static string GroundAt(Vector3 at)
    {
        var p = new Vector3(at.x, 0, at.z);
        bool sampled = TerrainSampler.TrySampleHeight(ref p, out float sampler);
        var n = Landscape.NormalAt(at.x, at.z);
        return string.Create(CultureInfo.InvariantCulture, $"({at.x:0.0}, {at.z:0.0}): loaded {Landscape.IsLoaded(at.x, at.z)}, height {Landscape.HeightAt(at.x, at.z):0.000} (sampler {(sampled ? sampler.ToString("0.000", CultureInfo.InvariantCulture) : "none")}), kind {Landscape.KindAt(at.x, at.z)}, cleared {Landscape.IsCleared(at.x, at.z)}, normal ({n.x:0.00}, {n.y:0.00}, {n.z:0.00}), underwater {Landscape.IsUnderwater(at.x, at.z)}");
    }

    private static string Rc(Rect r) => string.Create(CultureInfo.InvariantCulture, $"({r.m_XMin:0.0}, {r.m_YMin:0.0}) {r.m_Width:0.0}×{r.m_Height:0.0}");

    // Heights at 0, 1, 2… 6 m east of a point; with a before, their changes.
    private static float[] Ring(Vector3 at) => Enumerable.Range(0, 7).Select(d => Landscape.HeightAt(at.x + d, at.z)).ToArray();

    private static string Ring(Vector3 at, float[] before)
    {
        var now = Ring(at);
        return string.Join(", ", now.Select((h, d) => string.Create(CultureInfo.InvariantCulture, $"{d} m {h - before[d]:+0.00;-0.00}")));
    }
}
