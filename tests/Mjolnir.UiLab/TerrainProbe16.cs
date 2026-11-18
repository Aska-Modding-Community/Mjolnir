using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.Combat;
using SSSGame.UI;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using UnityEngine;
using GamePauseMenu = SSSGame.UI.PauseMenu;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe16" scenario: the world's objects that can be put down as world items. On a free spot it puts down
/// the planted saplings of a birch and a fir, two stone clumps, an iron deposit and a beehive tree, grows the saplings
/// through their own growth process in steps (reading their scale), harvests one grown tree, then saves, goes back to the
/// main menu, loads again and reads what still stands. It saves the world: back it up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe16()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        if (character == null || db == null || !Landscape.IsAvailable)
        {
            Log("Probe16: no world");
            yield break;
        }
        var home = character.transform.position;
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();
        InventoryItemInstance? At(Vector3 p, string name) => Items(p, 1.5f).FirstOrDefault(i => i.Descriptor?.itemInfo?.name == name);

        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 40f, 60f, 80f, 100f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var square = LandscapeArea.Rectangle(at, 30, 12, blend: 0);
                var m = Landscape.Measure(square);
                if (!m.IsKnown || m.Minimum < Landscape.WaterLevel + 1 || m.Maximum - m.Minimum > 3f || Landscape.Check(square, LandscapeOperation.Level) != LandscapeResult.Done) continue;
                if (NaturalObjects.In(square).Any(o => o.Kind is NaturalObjectKind.Tree or NaturalObjectKind.LargeObject)) continue;
                spot = at;
                found = true;
            }
            if (found) break;
        }
        if (!found)
        {
            Log("Probe16: no free spot");
            yield break;
        }
        var stand = spot - (spot - home).normalized * 12f;
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 16f, -18f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);

        var names = new[] { "Item_Wood_TreeBirch1Sapling", "Item_Wood_TreeFir2Sapling", "Item_Stone_StoneClump", "Item_Stone_StoneClumpSmall", "Item_Stone_IronDeposit", "Item_Wood_TreeBeehive" };
        var placed = new List<(string Name, ItemInfo Info, Vector3 At)>();
        for (int k = 0; k < names.Length; k++)
        {
            var info = db.GetItemInfo(names[k]);
            if (info == null)
            {
                Log($"Probe16: no '{names[k]}' in the database");
                continue;
            }
            var at = spot + new Vector3(-12.5f + 5 * k, 0, 0);
            at.y = Landscape.HeightAt(at.x, at.z);
            string spawned = Try(() =>
            {
                var context = ItemObjectSpawnContext.WORLD;
                int quantity = 1;
                var position = at;
                var rotation = Quaternion.Euler(0, 37 * k, 0);
                var scale = Vector3.one;
                var one = info.SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, true);
                return one != null ? one.name : "null";
            });
            placed.Add((names[k], info, at));
            Log($"Probe16: '{names[k]}' ({info.GetIl2CppType().Name}, processes [{string.Join(", ", info.processes?.ToArray().Select(p => p?.GetIl2CppType().Name) ?? [])}]) put down at {P(at)}: {spawned}");
        }
        yield return 4f;
        foreach (var (name, info, at) in placed)
        {
            var one = At(at, name);
            var sdk = NaturalObjects.Nearest(at, 1.5f);
            Log($"Probe16: '{name}' 4 s later: world item {one != null}, scale {one?.GetScale().y:0.###}, object '{(one?.gameObject != null ? one.gameObject.name : "none")}', harvest {one?.gameObject?.GetComponentInChildren<HarvestInteraction>(true) != null}; SDK {sdk?.ToString() ?? "none"}");
        }
        Shot("probe16-placed");
        yield return AfterShot;

        // The saplings grown in steps through their own process.
        foreach (var (name, info, at) in placed.Where(p => p.Name.Contains("Sapling")))
        {
            var growth = info.processes?.ToArray().Select(p => p?.TryCast<SaplingGrowthProcess>()).FirstOrDefault(p => p != null);
            var one = At(at, name);
            var item = one?.GetItem();
            Log($"Probe16: '{name}' growth {growth != null}: scale range {growth?.saplingScaleRange}, mature flag {growth?.matureTreeFlag}, reverse {growth?.reverseGrowthFlag}, time to grow {info.TryCast<PlantableItemInfo>()?.TimeToGrow}; item {item != null}");
            if (growth == null || item == null) continue;
            foreach (float dt in new[] { 30f, 300f, 3000f, 30000f, 300000f })
            {
                string ran = Try(() =>
                {
                    float d = dt;
                    growth.Run(item, ref d);
                    return "";
                });
                yield return 1.5f;
                var now = At(at, name);
                var harvest = now?.gameObject != null ? now.gameObject.GetComponentInChildren<HarvestInteraction>(true) : null;
                Log($"Probe16: '{name}' run {dt} s: {ran} scale {now?.GetScale().y:0.###}, pieces {harvest?.harvestPieces?.Count}, piece {Try(() => harvest?.GetCurrentPieceIndex())}, points {Try(() => harvest?.GetHarvestHitPoints())}");
            }
        }
        Shot("probe16-grown");
        yield return AfterShot;

        // One grown tree harvested.
        var birch = placed.FirstOrDefault(p => p.Name == "Item_Wood_TreeBirch1Sapling");
        if (birch.Info != null && NaturalObjects.Nearest(birch.At, 1.5f) is { } tree)
        {
            bool done = tree.Harvest();
            yield return 8f;
            Log($"Probe16: the grown birch harvested: {done}; items round it [{string.Join(", ", NaturalObjects.Around(birch.At, 14).Where(o => o.Kind == NaturalObjectKind.Item).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"))}]");
        }

        // Saved, back to the menu, loaded again.
        var storage = GameManagers.Get<StorageManager>();
        if (storage != null)
        {
            var task = storage.SaveCurrentSession();
            foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
            Log($"Probe16: saved, {storage.LastSaveOperationCode}");
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
            foreach (var (name, info, at) in placed)
            {
                var one = At(at, name);
                Log($"Probe16: after loading again '{name}': world item {one != null}, scale {one?.GetScale().y:0.###}; SDK {NaturalObjects.Nearest(at, 1.5f)?.ToString() ?? "none"}");
            }
            var back = Overhead(spot);
            back.Position = spot + new Vector3(0, 16f, -18f);
            back.Rotation = Quaternion.LookRotation(spot - back.Position, Vector3.up);
            yield return 1f;
            Shot("probe16-reloaded");
            yield return AfterShot;
            back.Dispose();
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
