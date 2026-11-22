using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.Camera;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.Controllers;
using SSSGame.UI;
using SSSGame.Water;
using SandSailorStudio;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using UnityEngine;
using GamePauseMenu = SSSGame.UI.PauseMenu;

namespace Mjolnir.UiLab;

/// <summary>
/// The "water" scenario: ponds through Mjolnir.SDK.Terrain (<c>WaterBodies</c>). In the main menu there is no water and
/// nothing can be measured. On a free spot it digs a basin, measures fills that fit, spill and lie above the ground,
/// fills a pond, looks at it from above and from its shore, puts the character in it (swimming, the game's answer for the
/// water there) and out, digs its shore wider (the water follows), saves, goes back to the main menu and loads again (the
/// pond still there and drawn), runs the console's water, takes the pond away and puts the ground back. It changes the
/// save's ground and saves: back the save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainWaterScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Water: in the main menu {WaterBodies.All.Count} bodies, a measure {WaterBodies.Measure(Vector3.zero, 40).Result}");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log($"Water: no world: character {character != null}, available {Landscape.IsAvailable}");
            yield break;
        }
        var home = character.transform.position;
        Log($"Water: {string.Join("; ", WaterBodies.All.Select(b => $"{b} size {b.Size:0}"))}");

        // A free spot of 30 m, no trees or boulders within 14 m.
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 40f, 50f, 60f, 70f, 80f, 100f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 15)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y) || Landscape.IsUnderwater(at.x, at.z)) continue;
                if (at.y < Landscape.WaterLevel + 2) continue;
                if (NaturalObjects.Around(at, 13).Any(o => o.Kind is NaturalObjectKind.Tree or NaturalObjectKind.LargeObject)) continue;
                if (Landscape.Check(LandscapeArea.Circle(at, 12, 3), LandscapeOperation.Lower) != LandscapeResult.Done) continue;
                spot = at;
                found = true;
                Log(FormattableString.Invariant($"Water: free spot ({spot.x:0.#}, {spot.y:0.##}, {spot.z:0.#}) at {distance} m, {turn}°"));
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Water: no free spot found within 100 m");
            yield break;
        }
        var kept = Landscape.Capture(LandscapeArea.Rectangle(spot, 48, 48, blend: 0));
        var away = spot - home;
        away.y = 0;
        away.Normalize();
        var camera = GameCamera.Take(100);
        camera.Position = spot + new Vector3(0, 24f, -24f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        camera.FieldOfView = 55;
        // The rim: the lowest ground on a ring just outside the basin; the water stands half a metre under it.
        float rim = float.MaxValue;
        for (int k = 0; k < 48; k++)
        {
            var r = spot + Quaternion.Euler(0, k * 7.5f, 0) * Vector3.forward * 13f;
            rim = MathF.Min(rim, Landscape.HeightAt(r.x, r.z));
        }

        // A. A basin dug 3 m, then fills measured.
        var dug = Landscape.Lower(LandscapeArea.Circle(spot, 8, 4), 4.5f);
        yield return 2f;
        float bottom = Landscape.HeightAt(spot.x, spot.z);
        float level = rim - 0.5f;
        var fits = WaterBodies.Measure(spot, level, 40);
        var spills = WaterBodies.Measure(spot, rim + 3f, 40);
        var above = WaterBodies.Measure(spot, bottom - 0.5f, 40);
        Log(FormattableString.Invariant($"Water: dug {dug}, the bottom {bottom:0.##} under a rim of {rim:0.##}; to {level:0.##}: {fits.Result} {fits.Area:0} m² {fits.Volume:0} m³ in {fits.Bounds}; to {rim + 3:0.##}: {spills.Result}; to {bottom - 0.5f:0.##}: {above.Result}"));

        // B. The pond, seen from above and from its shore.
        var pond = WaterBodies.Place(spot, level, 40);
        yield return 3f;
        Log(FormattableString.Invariant($"Water: placed {pond?.ToString() ?? "none"}: {pond?.Area:0} m², {pond?.Volume:0} m³, depth in the middle {pond?.DepthAt(spot.x, spot.z):0.##}; at the spot {WaterBodies.At(spot.x, spot.z)?.ToString() ?? "none"}, nearest {WaterBodies.Nearest(spot, 10)?.ToString() ?? "none"}; under water {Landscape.IsUnderwater(spot.x, spot.z)}; bodies {WaterBodies.All.Count}"));
        Log($"Water: the pond's objects: {string.Join("; ", Resources.FindObjectsOfTypeAll<BiomeLakeStampMask>().Where(m => m != null && m.gameObject.scene.IsValid()).Select(m => $"{m.name} at {P(m.transform.position)} level {m.GetWaterLevel():0.##}"))}");
        Shot("water-above");
        yield return AfterShot;
        camera.Position = spot - away * 16f + Vector3.up * 2.5f;
        camera.Rotation = Quaternion.LookRotation(spot + Vector3.down * 1f - camera.Position, Vector3.up);
        yield return 2f;
        Shot("water-shore");
        yield return AfterShot;

        // C. The character in it, then out.
        camera.Dispose();
        camera = null;
        var movement = character.GetComponentInChildren<CharacterMovement>();
        var into = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(spot.x, spot.z);
        foreach (float wait in WaitFor("the teleport", () => into.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var wsm = GameManagers.Get<WaterSurfaceManager>();
        Log(FormattableString.Invariant($"Water: in the pond {P(character.transform.position)}: swimming {movement?._isSwimming}, water {movement?._waterHeight:0.##}, environment {Type(movement?._activeWaterEnviroment)}; the game's answer there {Type(wsm?.GetEnvironment(spot.x, spot.z, true))} level {wsm?.GetWaterLevel(spot.x, spot.z):0.##}"));
        Shot("water-swimming");
        yield return AfterShot;
        var stand = spot - away * 20f;
        var outOf = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => outOf.IsCompleted, 30)) yield return wait;
        yield return 3f;
        character.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        Log(FormattableString.Invariant($"Water: out of it: swimming {movement?._isSwimming}, water {movement?._waterHeight:0.##}, environment {Type(movement?._activeWaterEnviroment)}"));

        // D. Its shore dug wider: the water follows.
        float before = pond?.Area ?? 0;
        var east = spot + Vector3.right * 9f;
        Landscape.Lower(LandscapeArea.Circle(east, 5, 2), 3f);
        yield return 3f;
        Log(FormattableString.Invariant($"Water: the shore dug at {P(east)}: {before:0} → {pond?.Area:0} m², covers it {pond?.Covers(east.x, east.z)}"));
        camera = GameCamera.Take(100);
        camera.Position = spot + new Vector3(4, 24f, -24f);
        camera.Rotation = Quaternion.LookRotation(spot + Vector3.right * 4 - camera.Position, Vector3.up);
        yield return 2f;
        Shot("water-wider");
        yield return AfterShot;

        // E. Saved, back to the menu, loaded again.
        var storage = GameManagers.Get<StorageManager>();
        if (storage != null)
        {
            var task = storage.SaveCurrentSession();
            foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
            Log($"Water: saved, {storage.LastSaveOperationCode}");
            camera.Dispose();
            camera = null;
            MainMenuReady = false;
            HudReady = false;
            Menu.Show<GamePauseMenu>(null);
            yield return 1.5f;
            Menu.GetInstance<GamePauseMenu>().BtnQuitToMainMenu();
            yield return 1.5f;
            if (Menu.GetInstance<ConfirmActionMenu>() is { } confirm) Click(confirm.ConfirmButton.gameObject);
            foreach (float wait in WaitFor("main menu again", () => MainMenuReady, 180)) yield return wait;
            yield return 3f;
            Log($"Water: back in the main menu: {WaterBodies.All.Count} bodies");
            foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
            yield return 10f;
            character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
            var again = WaterBodies.Nearest(spot, 5);
            Log(FormattableString.Invariant($"Water: loaded again: {again?.ToString() ?? "no pond"}, {again?.Area:0} m², size {again?.Size:0}; bodies {WaterBodies.All.Count}"));
            camera = GameCamera.Take(100);
            camera.Position = spot + new Vector3(4, 24f, -24f);
            camera.Rotation = Quaternion.LookRotation(spot + Vector3.right * 4 - camera.Position, Vector3.up);
            yield return 3f;
            Shot("water-loaded");
            yield return AfterShot;
            pond = again;
        }

        // F. The console, then the pond taken away and the ground put back.
        DevConsole.Execute("water");
        yield return 1f;
        bool removed = pond?.Remove() ?? false;
        yield return 3f;
        Log($"Water: taken away {removed}, gone {pond?.IsGone}; nearest {WaterBodies.Nearest(spot, 5)?.ToString() ?? "none"}, under water {Landscape.IsUnderwater(spot.x, spot.z)}");
        Shot("water-removed");
        yield return AfterShot;
        if (kept != null) Log($"Water: the ground put back: {kept.Restore(new LandscapeOptions { KeepOffBuildings = false })}");
        yield return 2f;
        camera?.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();

        static string Type(object? o)
        {
            if (o == null) return "none";
            try { return new Il2CppSystem.Object(((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)o).Pointer).GetIl2CppType().Name; }
            catch (Exception e) { return "? " + e.Message; }
        }
    }
}
