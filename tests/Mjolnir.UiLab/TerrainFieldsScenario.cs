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
/// The "terrain fields" scenario: the game's levelling fields through Mjolnir.SDK.Terrain. In the main menu there are none.
/// In the world it finds an uneven free spot, lists the fields there, places one of 8 × 8 m, reads its tiles and the
/// unevenness, levels every tile at once and measures the ground across it, places a second one and takes it away, and
/// finishes the first. It changes the ground and the buildings of the save (the game saves the world by itself): back the
/// save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainFieldsScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Fields: in the main menu {LevellingFields.All.Count} fields, nearest {LevellingFields.Nearest(Vector3.zero, 100)?.ToString() ?? "none"}, place {LevellingFields.Place(LandscapeArea.Rectangle(Vector3.zero, 8, 8))?.ToString() ?? "none"}");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log($"Fields: no world: character {character != null}, available {Landscape.IsAvailable}");
            yield break;
        }
        var home = character.transform.position;
        Log($"Fields: {LevellingFields.All.Count} levelling field(s) in the loaded world: {string.Join("; ", LevellingFields.All.Take(5).Select(f => f.ToString()))}");

        // An uneven free spot: no building, above the water, a drop of at least half a metre.
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 60f, 80f, 100f, 130f, 160f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var whole = LandscapeArea.Rectangle(at, 12, 12, blend: 2);
                if (Landscape.Check(whole, LandscapeOperation.Level) != LandscapeResult.Done) continue;
                var measured = Landscape.Measure(whole);
                if (!measured.IsKnown || measured.Minimum < Landscape.WaterLevel + 1) continue;
                if (measured.Maximum - measured.Minimum is < 0.5f or > 6f) continue;
                spot = at;
                found = true;
                Log(Inv($"Fields: free spot {P(at)} at {distance} m, {turn}° from the player: {measured}"));
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Fields: no uneven free spot found within 160 m");
            yield break;
        }
        var away = spot - home;
        away.y = 0;
        away.Normalize();
        var stand = spot - away * 14f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 3f;
        spot.y = Landscape.HeightAt(spot.x, spot.z);
        var camera = GameCamera.Take(100);
        camera.Position = spot + new Vector3(0, 16f, -14f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        camera.FieldOfView = 55;
        var kept = Landscape.Capture(LandscapeArea.Circle(spot, 16, 4));
        Shot("fields-before");
        yield return AfterShot;

        // A. A field of 8 × 8 m.
        var area = LandscapeArea.Rectangle(spot, 8, 8, blend: 0);
        string Across() => string.Join(" ", Enumerable.Range(-4, 9).Select(k => Landscape.HeightAt(spot.x + k, spot.z).ToString("0.00", CultureInfo.InvariantCulture)));
        string before = Across();
        var field = LevellingFields.Place(area);
        yield return 2f;
        if (field == null)
        {
            Log("Fields: no field placed (see the log)");
            camera.Dispose();
            yield break;
        }
        Log(Inv($"Fields: placed {field}: height {field.Height:0.00}, unevenness {field.HeightDifference:0.00}, gone {field.IsGone}; the world now has {LevellingFields.All.Count}"));
        Shot("fields-placed");
        yield return AfterShot;

        // B. A second field beside it, listed, then taken away.
        var second = LevellingFields.Place(LandscapeArea.Rectangle(spot + new Vector3(12, 0, 0), 4, 4, blend: 0));
        yield return 1.5f;
        Log($"Fields: a second one placed: {second?.ToString() ?? "none"}; the world now has {LevellingFields.All.Count}; nearest the spot {LevellingFields.Nearest(spot, 6)?.ToString() ?? "none"}");
        Shot("fields-two");
        yield return AfterShot;
        if (second != null)
        {
            bool removed = second.Remove();
            yield return 2f;
            Log($"Fields: the second one taken away: {removed}, gone {second.IsGone}; the world now has {LevellingFields.All.Count}");
        }

        // C. The first levelled at once (the game finishes a plan whose last tile is levelled).
        int levelled = field.LevelNow();
        yield return 0.1f;
        string atOnce = Inv($"levelled {levelled} of tiles, gone {field.IsGone}, unevenness {field.HeightDifference:0.00}");
        yield return 2f;
        Log(Inv($"Fields: levelled at once ({atOnce}); a frame later gone {field.IsGone}, the world has {LevellingFields.All.Count}; the ground across it before [{before}], after [{Across()}]; kind at the middle {Landscape.KindAt(spot.x, spot.z)}"));
        Shot("fields-levelled");
        yield return AfterShot;

        // D. A field finished without levelling it.
        var third = LevellingFields.Place(LandscapeArea.Rectangle(spot + new Vector3(-12, 0, 0), 4, 4, blend: 0));
        yield return 1.5f;
        bool finished = third?.Finish() ?? false;
        yield return 2f;
        Log($"Fields: a third one finished without levelling: {finished}, gone {third?.IsGone}; the world now has {LevellingFields.All.Count}");
        Shot("fields-finished");
        yield return AfterShot;

        // E. The console.
        DevConsole.Execute("field");
        DevConsole.Execute("field place 6");
        yield return 2f;
        var placed = LevellingFields.Nearest(character.transform.position, 20);
        Log($"Fields: the console placed {placed?.ToString() ?? "none"}");
        if (placed != null)
        {
            DevConsole.Execute("field level");
            yield return 1.5f;
            Log($"Fields: after the console's level: {placed}");
            DevConsole.Execute("field remove");
            yield return 1.5f;
            Log($"Fields: after the console's remove: gone {placed.IsGone}, the world has {LevellingFields.All.Count}");
        }
        Shot("fields-console");
        yield return AfterShot;

        // F. The ground put back.
        if (kept != null)
        {
            var result = kept.Restore(new LandscapeOptions { KeepOffBuildings = false });
            yield return 2f;
            Log($"Fields: the ground put back: {result}");
            Shot("fields-restored");
            yield return AfterShot;
        }
        camera.Dispose();

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
