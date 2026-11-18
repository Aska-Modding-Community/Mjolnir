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
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain zones" scenario: protected zones of Mjolnir.SDK.Terrain. On an uneven free spot, with the player standing
/// before it and facing it, UiLab protects the spot itself: its own raise and lower pass, and a levelling field set down
/// there levels at once through its own zone. Then the console protects the same square (a zone that is not UiLab's):
/// UiLab's raise is blocked, a new field's LevelNow leaves every tile untried, and a tile struck by the field's own tool as
/// a villager strikes it is refused (the game still marks it worked). The console's zone ended, the field levels. The
/// ground is put back at the end. It changes the ground and the buildings of the save: back the save up and put it back.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainZonesScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log($"Zones: no world: character {character != null}, available {Landscape.IsAvailable}");
            yield break;
        }
        var home = character.transform.position;

        // An uneven free spot: no building, above the water, a drop of at least half a metre.
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 60f, 80f, 100f, 130f, 160f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var whole = LandscapeArea.Rectangle(at, 14, 14, blend: 2);
                if (Landscape.Check(whole, LandscapeOperation.Level) != LandscapeResult.Done) continue;
                var measured = Landscape.Measure(whole);
                if (!measured.IsKnown || measured.Minimum < Landscape.WaterLevel + 1) continue;
                if (measured.Maximum - measured.Minimum is < 0.5f or > 6f) continue;
                spot = at;
                found = true;
                Log(Inv($"Zones: free spot {P(at)} at {distance} m, {turn}° from the player: {measured}"));
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Zones: no uneven free spot found within 160 m");
            yield break;
        }

        // The player 8.5 m before the spot, facing it: the console's squares lie there.
        var away = spot - home;
        away.y = 0;
        away.Normalize();
        var stand = spot - away * 8.5f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 2f;
        character.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        yield return 1f;
        var forward = character.transform.forward;
        forward.y = 0;
        forward.Normalize();
        var square = character.transform.position + forward * 8.5f;
        square.y = Landscape.HeightAt(square.x, square.z);
        Log($"Zones: the player at {P(character.transform.position)} facing {P(forward)}; the console's square at {P(square)} ({Vector3.Distance(square, spot):0.0} m from the spot)");
        spot = square;
        var camera = GameCamera.Take(100);
        camera.Position = spot + new Vector3(0, 16f, -14f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        camera.FieldOfView = 55;
        var kept = Landscape.Capture(LandscapeArea.Circle(spot, 12, 4));
        string Across() => string.Join(" ", Enumerable.Range(-3, 7).Select(k => Landscape.HeightAt(spot.x + k, spot.z).ToString("0.00", CultureInfo.InvariantCulture)));

        // A. UiLab's own zone: its own changes and its own field pass.
        var zone = Landscape.Protect(LandscapeArea.Rectangle(spot, 10, 10, blend: 0));
        var refused = new List<string>();
        zone.Refused += (_, e) => refused.Add($"{e.Operation} by {e.Source}{(e.ModId != null ? " (" + e.ModId + ")" : "")}");
        var inner = LandscapeArea.Rectangle(spot + new Vector3(0, 0, 3), 3, 3, blend: 0.5f);
        float before = Landscape.HeightAt(spot.x, spot.z + 3);
        var raised = Landscape.Raise(inner, 0.5f);
        yield return 1f;
        float up = Landscape.HeightAt(spot.x, spot.z + 3) - before;
        var lowered = Landscape.Lower(inner, 0.5f);
        yield return 1f;
        string across = Across();
        var own = LevellingFields.Place(LandscapeArea.Rectangle(spot, 6, 6, blend: 0));
        yield return 2f;
        int ownLevelled = own?.LevelNow() ?? -1;
        yield return 2f;
        Log(Inv($"Zones: UiLab's {zone}: raise {raised} (+{up:0.00}), lower {lowered}; its own field of {own?.Tiles} tiles: LevelNow {ownLevelled}, gone {own?.IsGone}; the ground across before [{across}], after [{Across()}]; refused [{string.Join("; ", refused)}]"));
        Shot("zones-own");
        yield return AfterShot;
        zone.Dispose();
        if (kept != null) kept.Restore(new LandscapeOptions { KeepOffBuildings = false });
        yield return 2f;

        // B. The console's zone over the same square: not UiLab's.
        DevConsole.Execute("protect 10");
        yield return 0.5f;
        var check = Landscape.Check(inner, LandscapeOperation.Raise);
        var blocked = Landscape.Raise(inner, 0.5f);
        across = Across();
        var field = LevellingFields.Place(LandscapeArea.Rectangle(spot, 6, 6, blend: 0));
        yield return 2f;
        if (field == null)
        {
            Log("Zones: no field placed (see the log)");
            DevConsole.Execute("protect clear");
            camera.Dispose();
            yield break;
        }
        int marked = field.LevelledTiles;
        int levelled = field.LevelNow();
        yield return 2f;
        Log(Inv($"Zones: the console's zone: UiLab's check {check}, raise {blocked}; a field of {field.Tiles} tiles ({marked} level already): LevelNow {levelled}, levelled now {field.LevelledTiles}, gone {field.IsGone}; the ground across before [{across}], after [{Across()}]"));

        // A villager's blow: the field's own tool, struck as the game strikes it, with no mod's call behind it.
        var grid = Object.FindObjectsOfType<TerraformingGrid>().FirstOrDefault(g => g != null && g.dynamicBuilding != null && Vector3.Distance(g.transform.position, spot) < 8f);
        if (grid != null)
        {
            int tile = Enumerable.Range(0, grid.GetCellsCount()).FirstOrDefault(i => !grid.IsCellLeveled(i));
            var middle = grid.GetCellPosition(tile);
            float ground = Landscape.HeightAt(middle.x, middle.z);
            bool wasLevelled = grid.IsCellLeveled(tile);
            string struck = Try(() => { grid.terraformingInteraction?.TryLevelTile(tile); return "struck"; });
            yield return 1.5f;
            Log(Inv($"Zones: tile {tile} struck by the field's own tool: {struck}; levelled before {wasLevelled}, now {grid.IsCellLeveled(tile)}; its ground {ground:0.00} → {Landscape.HeightAt(middle.x, middle.z):0.00}"));
        }
        Shot("zones-console");
        yield return AfterShot;

        // C. The console's zone ended: the field levels.
        DevConsole.Execute("protect clear");
        yield return 0.5f;
        levelled = field.LevelNow();
        yield return 2f;
        Log(Inv($"Zones: the console's zone ended: LevelNow {levelled}, gone {field.IsGone}; the ground across now [{Across()}]"));
        Shot("zones-ended");
        yield return AfterShot;

        // D. The ground put back, and a field still standing taken away.
        if (!field.IsGone) field.Remove();
        if (kept != null)
        {
            yield return 1f;
            var result = kept.Restore(new LandscapeOptions { KeepOffBuildings = false });
            yield return 2f;
            Log($"Zones: the ground put back: {result}");
        }
        camera.Dispose();

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
