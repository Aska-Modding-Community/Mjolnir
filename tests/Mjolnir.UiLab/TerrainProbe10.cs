using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.UI;
using SandSailorStudio;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using UnityEngine;
using GamePauseMenu = SSSGame.UI.PauseMenu;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe10" scenario: felling trees and taking boulders away from code. On a wooded spot it reads what each
/// destruction level of the game allows for a tree, a boulder and a bush, takes a tree away at the system's level and a
/// boulder at the bombs', reads them and the items dropped a few seconds after, then saves, goes back to the main menu,
/// loads the save again and looks whether they stayed away. It saves the world: back it up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe10()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Probe10: no world");
            yield break;
        }
        var home = character.transform.position;
        List<BiomeItemInstance> Biome(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();
        string ItemNames(Vector3 p, float r) => string.Join(", ", Items(p, r).Select(i => i.Descriptor?.itemInfo?.name ?? "?").GroupBy(n => n).Select(g => $"{g.Key} ×{g.Count()}"));
        string Kind(BiomeItemInstance b) => b.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo()?.Name ?? "?";
        string Levels(BiomeItemInstance b) => string.Join(" ", new[] { InstanceDestructionLevel.SYSTEM, InstanceDestructionLevel.BOMBS, InstanceDestructionLevel.TERRAFORMING }.Select(l => $"{l}:{(b.IsRemovable(l) ? 1 : 0)}"));

        // A spot with a tree and a large stone near each other.
        Vector3 spot = default;
        BiomeItemInstance? tree = null, boulder = null;
        foreach (float distance in new[] { 40f, 60f, 80f, 100f, 130f, 160f, 200f })
        {
            for (int turn = 0; turn < 360 && boulder == null; turn += 20)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var near = NaturalObjects.Around(at, 14);
                var t = near.FirstOrDefault(o => o.Kind == NaturalObjectKind.Tree);
                var b = near.FirstOrDefault(o => o.Kind == NaturalObjectKind.LargeObject);
                if (t == null || b == null) continue;
                spot = at;
                tree = Biome(t.Position, 0.5f).FirstOrDefault();
                boulder = Biome(b.Position, 0.5f).FirstOrDefault();
                Log($"Probe10: spot {P(at)} at {distance} m, {turn}°: tree {t}, boulder {b}");
            }
            if (boulder != null) break;
        }
        if (tree == null || boulder == null)
        {
            Log("Probe10: no tree beside a boulder within 200 m");
            yield break;
        }
        var treeAt = tree.GetPosition();
        var boulderAt = boulder.GetPosition();
        string treeKind = Kind(tree), boulderKind = Kind(boulder);
        var stand = spot - (spot - home).normalized * 12f;
        var teleport = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => teleport.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 18f, -16f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);

        // A. What each level allows.
        var bush = Biome(spot, 14f).FirstOrDefault(b => b.IsRemovable(InstanceDestructionLevel.TERRAFORMING));
        Log($"Probe10: levels (SYSTEM BOMBS TERRAFORMING) — tree '{treeKind}' {Levels(tree)}, boulder '{boulderKind}' {Levels(boulder)}, bush '{(bush != null ? Kind(bush) : "none")}' {(bush != null ? Levels(bush) : "")}");
        Log($"Probe10: tree health {tree.GetHealth()}, quantity {tree.GetQuantity()}, object '{(tree.gameObject != null ? tree.gameObject.name : "none")}'; boulder health {boulder.GetHealth()}, object '{(boulder.gameObject != null ? boulder.gameObject.name : "none")}'");
        Shot("probe10-before");
        yield return AfterShot;

        // B. The tree at the system's level, the boulder at the bombs'.
        string itemsBefore = ItemNames(spot, 16f);
        string treeTaken = Try(() => { bool silent = false; var level = InstanceDestructionLevel.SYSTEM; tree.Destroy(ref silent, ref level); return $"destroyed {tree.Destroyed}"; });
        string boulderTaken = Try(() => { bool silent = false; var level = InstanceDestructionLevel.BOMBS; boulder.Destroy(ref silent, ref level); return $"destroyed {boulder.Destroyed}"; });
        yield return 4f;
        Log($"Probe10: the tree taken at SYSTEM: {treeTaken}; the boulder at BOMBS: {boulderTaken}; 4 s later objects at their places: tree {Biome(treeAt, 0.5f).Count}, boulder {Biome(boulderAt, 0.5f).Count}; through the SDK {NaturalObjects.Nearest(treeAt, 0.5f)?.ToString() ?? "none"} / {NaturalObjects.Nearest(boulderAt, 0.5f)?.ToString() ?? "none"}; items near before [{itemsBefore}], after [{ItemNames(spot, 16f)}]");
        Shot("probe10-taken");
        yield return AfterShot;

        // C. Saved, back to the menu, loaded again.
        var storage = GameManagers.Get<StorageManager>();
        if (storage == null)
        {
            Log("Probe10: no storage manager");
            yield break;
        }
        var task = storage.SaveCurrentSession();
        foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
        Log($"Probe10: saved, {storage.LastSaveOperationCode}");
        camera.Dispose();
        MainMenuReady = false;
        HudReady = false;
        Menu.Show<GamePauseMenu>(null);
        yield return 1.5f;
        Menu.GetInstance<GamePauseMenu>().BtnQuitToMainMenu();
        yield return 1.5f;
        if (Menu.GetInstance<ConfirmActionMenu>() is { } confirm) Click(confirm.ConfirmButton.gameObject);
        foreach (float wait in WaitFor("main menu again", () => MainMenuReady, 180)) yield return wait;
        yield return 3f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var again = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => again.IsCompleted, 30)) yield return wait;
        yield return 4f;
        var back = Overhead(spot);
        back.Position = spot + new Vector3(0, 18f, -16f);
        back.Rotation = Quaternion.LookRotation(spot - back.Position, Vector3.up);
        yield return 1f;
        Log($"Probe10: after loading again: at the tree's place {string.Join(", ", Biome(treeAt, 0.6f).Select(Kind))}; at the boulder's {string.Join(", ", Biome(boulderAt, 0.6f).Select(Kind))}; items near [{ItemNames(spot, 16f)}]");
        Shot("probe10-reloaded");
        yield return AfterShot;
        back.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
