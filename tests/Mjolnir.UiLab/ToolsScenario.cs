using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.Camera;
using Mjolnir.SDK.Terrain;
using Mjolnir.SDK.UI;
using SSSGame;
using SSSGame.UI;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using GameInput = SSSGame.InputManager;

namespace Mjolnir.UiLab;

/// <summary>
/// The "tools" scenario, for a mod's tool on the ground: the player's controls captured (in the main menu it ends at once;
/// in the world the character's actions, the camera's zoom and Esc's pause off, the cursor shown, a hotkey still firing, a
/// window opened and closed over it, then given back), the mouse read and moved, a ray from the middle of the screen to the
/// ground beside the camera's own, a grid of cells drawn on the ground (shades from 0 to 1, marked and empty cells, the
/// mod's colours and the game's, moved, hidden and taken away) and an outline lower and thinner. Changes nothing of the
/// save.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> ToolsScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        var menuCapture = PlayerControls.Capture();
        int menuEnds = 0;
        menuCapture.Ended += (_, _) => menuEnds++;
        yield return 0.5f;
        Log($"Tools: in the main menu a capture ended {menuEnds} time(s), active {menuCapture.IsActive}, captured {PlayerControls.IsCaptured}; the mouse {MouseInput.IsAvailable} at {MouseInput.Position}; {CursorProbe.Describe()}");

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Tools: no world");
            yield break;
        }
        var home = character.transform.position;
        foreach (float wait in GoAbove(character, home, s_probe6Biome)) yield return wait;
        var spot = s_probe6Biome;
        spot.y = Landscape.HeightAt(spot.x, spot.z);
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 26f, -6f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        camera.FieldOfView = 45;
        yield return 3f;
        Log($"Tools: in the world, {Actions()}; {CursorProbe.Describe()}");

        // The controls captured: the character's actions off, the cursor shown, a hotkey still firing.
        int hotkeys = 0;
        using var f9 = HotKeys.Add(KeyGesture.Parse("F9"), () => hotkeys++);
        var capture = PlayerControls.Capture();
        int ends = 0;
        capture.Ended += (_, _) => ends++;
        var start = character.transform.position;
        yield return 0.5f;
        Log($"Tools: captured {PlayerControls.IsCaptured}, active {capture.IsActive}; {Actions()}; {CursorProbe.Describe()}");
        PressNextFrame("F9");
        yield return 0.3f;
        Log($"Tools: F9 under the capture fired {hotkeys} time(s)");

        // The mouse: put at the middle of the screen, read back; over the ground, then over a window.
        var middle = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Mouse.current?.WarpCursorPosition(middle);
        yield return 0.3f;
        Log($"Tools: the mouse at {MouseInput.Position} (moved to {middle}), wheel {MouseInput.Wheel}, over the interface {MouseInput.IsOverInterface}, left {MouseInput.IsPressed(MouseButton.Left)}");
        var window = new UiWindow { Title = "UiLab tools", Width = 600, Height = 300, Content = new UiTextBlock { Text = "over the capture" } };
        window.Show();
        yield return 1.5f;
        Log($"Tools: a window open {window.IsOpen}, the mouse over the interface {MouseInput.IsOverInterface}; {Actions()}; {CursorProbe.Describe()}");
        window.Close();
        yield return 1f;
        Log($"Tools: the window closed; {Actions()}; {CursorProbe.Describe()}");
        window.Dispose();

        // A ray from the middle of the screen to the ground, against the camera's own.
        var ray = GameCamera.ScreenPointToRay(middle);
        var watch = Stopwatch.StartNew();
        bool hit = false;
        Vector3 point = default;
        for (int i = 0; i < 100; i++) hit = Landscape.TryRaycast(ray, 500, out point);
        watch.Stop();
        bool cameraHit = GameCamera.TryRaycast(500, out var cameraPoint, out var target);
        Log(F($"Tools: the ray meets the ground {hit} at {P(point)} ({watch.Elapsed.TotalMilliseconds / 100:0.000} ms), the camera's {cameraHit} at {P(cameraPoint)} on '{target?.name}', {(point - cameraPoint).magnitude:0.000} m apart; the ground there {Landscape.HeightAt(point.x, point.z):0.000}; up the ray {Landscape.TryRaycast(new Ray(spot, Vector3.up), 100, out _)}"));

        // A grid: shades from 0 to 1 along the rows, a marked row, an empty one.
        const int columns = 12, rows = 6;
        var grid = Landscape.ShowGrid(spot, columns, rows, cellSize: 1.5f);
        for (int c = 0; c < columns; c++)
        {
            float f = c / (float)(columns - 1);
            grid.SetCell(c, 0, f);
            grid.SetCell(c, 1, f);
            grid.MarkCell(c, 2);
            grid.SetCell(c, 4, f);
            if (c % 2 == 0) grid.MarkCell(c, 5);
        }
        yield return 1.5f;
        Log($"Tools: {grid} drawn, the game's colours; {Decals()}");
        Shot("tools-grid-game");
        yield return AfterShot;
        grid.NearColor = new Color(0.1f, 0.9f, 0.1f, 0.8f);
        grid.FarColor = new Color(0.1f, 0.3f, 1f, 0.8f);
        yield return 1f;
        var screen = new List<string>();
        for (int c = 0; c < columns; c++)
        {
            var s = GameCamera.WorldToScreen(OnTheGround(grid.CellCenter(c, 0)));
            screen.Add(F($"{s.x:0},{Screen.height - s.y:0}"));
        }
        Log($"Tools: the mod's colours (green, blue, red); row 0's cells on the screen (x,y from the top) {string.Join(" ", screen)}");
        Shot("tools-grid-colours");
        yield return AfterShot;
        grid.Rotation = 30;
        grid.Center = spot + new Vector3(3, 0, 2);
        yield return 1f;
        Log($"Tools: turned 30° and moved; {Decals()}");
        Shot("tools-grid-turned");
        yield return AfterShot;
        grid.IsVisible = false;
        yield return 0.5f;
        string hidden = Decals();
        grid.IsVisible = true;
        yield return 0.5f;
        Log($"Tools: hidden {hidden}; shown {Decals()}");
        grid.Dispose();
        yield return 1f;
        Log($"Tools: disposed {Decals()}");

        // An outline 0.15 m over the ground with a line 0.1 m wide.
        var outline = Landscape.ShowOutline(LandscapeArea.Circle(spot, 4, 1), Color.yellow);
        outline.Height = 0.15f;
        outline.LineWidth = 0.1f;
        yield return 1f;
        Log($"Tools: an outline {Outlines(spot)}");
        Shot("tools-outline");
        yield return AfterShot;
        outline.Dispose();

        // Given back.
        Log(F($"Tools: the player moved {(character.transform.position - start).magnitude:0.00} m while captured"));
        capture.Dispose();
        yield return 0.5f;
        Log($"Tools: given back: ended {ends} time(s), captured {PlayerControls.IsCaptured}; {Actions()}; {CursorProbe.Describe()}");
        camera.Dispose();
        yield return 1f;
        Log("Autopilot done");
        if (QuitWhenDone) Application.Quit();
    }

    private static string F(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static Vector3 OnTheGround(Vector3 at)
    {
        at.y = Landscape.HeightAt(at.x, at.z);
        return at;
    }

    // The player's actions the capture switches off.
    private static string Actions()
    {
        var controls = GameInput.controls;
        if (controls == null) return "no controls";
        return $"attack {controls.LandCharacter.AttackLight.enabled}, move {controls.Movement.Movement.enabled}, zoom {controls.Camera.CameraZoom.enabled}, " +
               $"pause {controls.PauseMenu.Pause.enabled}, inventory {controls.MenuShortcuts.Inventory.enabled}, UI click {controls.UI.Click.enabled}";
    }

    private static string Decals()
    {
        var found = Resources.FindObjectsOfTypeAll<DecalProjector>().Where(d => d != null && d.gameObject.name == "Mjolnir_LandscapeGrid").ToList();
        return found.Count == 0 ? "no grid decal" : string.Join("; ", found.Select(d => F(
            $"decal active {d.gameObject.activeSelf}, size {d.size}, at {P(d.transform.position)}, turned {d.transform.rotation.eulerAngles}, shader '{d.material?.shader?.name}'")));
    }

    private static string Outlines(Vector3 spot)
    {
        var lines = Resources.FindObjectsOfTypeAll<LineRenderer>().Where(l => l != null && l.transform.parent != null && l.transform.parent.name == "Mjolnir_LandscapeOutline" && l.gameObject.activeInHierarchy).ToList();
        return string.Join("; ", lines.Select(l =>
        {
            float lift = 0;
            for (int i = 0; i < l.positionCount; i++)
            {
                var p = l.GetPosition(i);
                lift += p.y - Landscape.HeightAt(p.x, p.z);
            }
            return F($"{l.gameObject.name}: width {l.widthMultiplier:0.###}, {l.positionCount} points {lift / Math.Max(1, l.positionCount):0.###} m over the ground");
        }));
    }
}
