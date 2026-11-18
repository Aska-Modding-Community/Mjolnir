using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.UI;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe29" scenario: how the levelling field's decal colours its cells, for a grid of cells on the ground.
/// On the lawn of "terrain probe 6", looked at from above, a decal of the probe's own with a copy of the field's material
/// and a linear cell map of its own (as the game's): a row per cell state (R 0, 26, 51, 77, 102, 128, 153), a column per height (G 0…255), with
/// a colour of its own in each colour of the material. Then the same with the blue and alpha channels set, and with the
/// reference moved. Also logs how the mouse wheel is scaled. Changes nothing of the save.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe29()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Grid probe: the wheel's scroll is {InputSystem.settings.scrollDeltaBehavior}");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Grid probe: no world");
            yield break;
        }
        var home = character.transform.position;
        foreach (float wait in GoAbove(character, home, s_probe6Biome)) yield return wait;
        var spot = s_probe6Biome;
        spot.y = Landscape.HeightAt(spot.x, spot.z);
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 26f, -6f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        yield return 3f;

        FarmCropGridRenderer? field = null;
        foreach (var f in Resources.FindObjectsOfTypeAll<FarmCropGridRenderer>())
            if (f != null && f.decalProjector != null && f.transform.parent?.parent?.parent?.name == "TerrainLevel_Field") field = f;
        if (field == null)
        {
            Log("Grid probe: no levelling field's decal");
            yield break;
        }
        var source = field.decalProjector.material;
        Log($"Grid probe: material '{source.name}', shader '{source.shader.name}', layers {field.decalProjector.decalLayerMask}");

        byte[] states = [0, 26, 51, 77, 102, 128, 153];
        const int columns = 9;
        int rows = states.Length;
        var go = new GameObject("UiLab grid probe");
        go.transform.position = spot + Vector3.up * 8;
        go.transform.rotation = Quaternion.Euler(90, 0, 0);
        var decal = go.AddComponent(Il2CppType.Of<DecalProjector>()).Cast<DecalProjector>();
        var material = new Material(source);
        material.SetColor("_CellValidColor", new Color(0.1f, 0.3f, 1f, 1f));
        material.SetColor("_CellInvalidColor", new Color(1f, 0.1f, 0.1f, 1f));
        material.SetColor("_CellTilledColor", new Color(1f, 0.1f, 1f, 1f));
        material.SetColor("_CellIHighlightColor", new Color(1f, 0.9f, 0.1f, 1f));
        material.SetColor("_ReferenceHeightColor", new Color(0.1f, 1f, 0.1f, 0.8f));
        material.SetColor("_LimitHeightColor", new Color(1f, 1f, 1f, 0.8f));
        material.SetVector("_ReferenceHeight", new Vector4(0, 1, 0.5f, 0));
        material.SetVector("_Size_width_height", new Vector4(columns, rows, 0, 0));
        decal.material = material;
        decal.size = new Vector3(columns * 1.5f, rows * 1.5f, 20);
        decal.decalLayerMask = field.decalProjector.decalLayerMask;

        Texture2D Map(byte blue, byte alpha)
        {
            var map = new Texture2D(columns, rows, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[columns * rows];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                    pixels[r * columns + c] = new Color32(states[r], (byte)(c * 255 / (columns - 1)), blue, alpha);
            map.SetPixels32(new Il2CppStructArray<Color32>(pixels));
            map.Apply();
            return map;
        }

        Log($"Grid probe: rows bottom to top R {string.Join(", ", states)}; columns left to right G 0…255; valid blue, invalid red, tilled magenta, highlight yellow, reference green, limit white, reference 0.5");
        material.SetTexture("_GridMap", Map(0, 0));
        yield return 1.5f;
        Shot("grid-states");
        yield return AfterShot;
        material.SetTexture("_GridMap", Map(0, 255));
        yield return 1f;
        Shot("grid-alpha255");
        yield return AfterShot;
        material.SetTexture("_GridMap", Map(255, 0));
        yield return 1f;
        Shot("grid-blue255");
        yield return AfterShot;
        material.SetTexture("_GridMap", Map(0, 0));
        material.SetVector("_ReferenceHeight", new Vector4(0, 1, 0.1f, 0));
        yield return 1f;
        Shot("grid-reference01");
        yield return AfterShot;
        material.SetVector("_ReferenceHeight", new Vector4(0.25f, 0.75f, 0.5f, 0));
        yield return 1f;
        Shot("grid-range");
        yield return AfterShot;
        Object.Destroy(go);
        camera.Dispose();
        yield return 1f;
        Log("Autopilot done");
        if (QuitWhenDone) Application.Quit();
    }
}
