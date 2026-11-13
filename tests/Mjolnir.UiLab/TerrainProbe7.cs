using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.UI;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe 7" scenario, after "terrain probe 6" in the same save: on a cold start (a new game process, so nothing
/// of the world generator's is cached) the biome painted there and the two cover patches are read back; then, on the
/// sunny half of that even lawn, three patches side by side show what the grass does with dirt not cleared, natural ground
/// cleared, and dirt cleared, looked at from straight above at once and later. It changes the ground of the save without
/// saving it (the game may save by itself): back it up.
/// </summary>
internal static partial class Autopilot
{
    // Where "terrain probe 6" painted Swamp, and its cover patches (dirt not cleared; natural still cleared).
    private static readonly Vector3 s_probe6Biome = new(-116.8f, 0, 329.3f);
    private static readonly Vector3 s_probe6Dirt = new(-171.5f, 0, 479.6f);
    private static readonly Vector3 s_probe6Natural = new(-161.5f, 0, 479.6f);

    private static IEnumerator<float> TerrainProbe7()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var tsm = GameManagers.Get<TerrainSurfaceManager>();
        var data = GameManagers.Get<WorldDataManager>();
        if (character == null || tsm == null || data == null || !Landscape.IsAvailable)
        {
            Log("Probe7: no world");
            yield break;
        }
        var home = character.transform.position;

        // A. What probe 6 left, on a cold start.
        foreach (float wait in GoAbove(character, home, s_probe6Biome)) yield return wait;
        var spot = s_probe6Biome;
        spot.y = Landscape.HeightAt(spot.x, spot.z);
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 26f, -14f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        yield return 4f;
        Log($"Probe7: cold start: the painted spot {BiomeSample(data, spot)}; 13 m off {BiomeSample(data, spot + new Vector3(13, 0, 0))}");
        Shot("probe7-biome-cold");
        yield return AfterShot;
        Log($"Probe7: cold start: dirt patch {Landscape.KindAt(s_probe6Dirt.x, s_probe6Dirt.z)}, cleared {Landscape.IsCleared(s_probe6Dirt.x, s_probe6Dirt.z)}; natural patch {Landscape.KindAt(s_probe6Natural.x, s_probe6Natural.z)}, cleared {Landscape.IsCleared(s_probe6Natural.x, s_probe6Natural.z)}");

        // The sunny near half of probe 6's Swamp lawn: one even grass, so a change shows.
        var middle = s_probe6Biome + new Vector3(0, 0, -6f);
        middle.y = Landscape.HeightAt(middle.x, middle.z);
        camera.Position = s_probe6Biome + new Vector3(0, spot.y + 26f, -14f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);
        yield return 2f;
        Shot("probe7-cover-before");
        yield return AfterShot;

        // C. Left to right: dirt not cleared, natural cleared, dirt cleared; 4 × 4 m each, 6 m apart.
        var dirt = middle + new Vector3(-6f, 0, 0);
        var cleared = middle;
        var both = middle + new Vector3(6f, 0, 0);
        Log($"Probe7: at {P(middle)}: left {Flags(tsm, dirt, 4f, "dirt, not cleared", TerraformingMap.TerrainType.DIRT, false)}; middle {Flags(tsm, cleared, 4f, "natural, cleared", TerraformingMap.TerrainType.NATURAL, true)}; right {Flags(tsm, both, 4f, "dirt, cleared", TerraformingMap.TerrainType.DIRT, true)}");
        yield return 4f;
        Shot("probe7-cover-written");
        yield return AfterShot;
        yield return 20f;
        Log($"Probe7: 24 s later: left {Landscape.KindAt(dirt.x, dirt.z)} cleared {Landscape.IsCleared(dirt.x, dirt.z)}; middle {Landscape.KindAt(cleared.x, cleared.z)} cleared {Landscape.IsCleared(cleared.x, cleared.z)}; right {Landscape.KindAt(both.x, both.z)} cleared {Landscape.IsCleared(both.x, both.z)}");
        Shot("probe7-cover-later");
        yield return AfterShot;

        // D. All three back to natural, not cleared: does the grass come back where it was taken?
        Log($"Probe7: back: {Flags(tsm, middle, 18f, "natural, not cleared", TerraformingMap.TerrainType.NATURAL, false)}");
        yield return 4f;
        Shot("probe7-cover-back");
        yield return AfterShot;
        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
