using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.UI;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using UnityEngine;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe18" scenario: a willow of the world as a kind of world item of its own. It finds a willow of the
/// world for its vegetation info, makes an item kind with an id and a name of its own from a world item kind of the game
/// (a lying trunk's), whose object is the willow's game object without its biome object plus the willow's model from the
/// vegetation system, puts it in the item database as the content module does, puts two down (whole and at half scale),
/// harvests one, then saves, goes back to the main menu, loads again and reads what stands. It saves the world: back it up.
/// </summary>
internal static partial class Autopilot
{
    private const int Probe18Id = 0x4D4A5701;

    private static IEnumerator<float> TerrainProbe18()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        if (character == null || db == null || !Landscape.IsAvailable)
        {
            Log("Probe18: no world");
            yield break;
        }
        var home = character.transform.position;
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();
        List<BiomeItemInstance> Biome(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();
        string Parts(GameObject o) => string.Join(", ", o.GetComponentsInChildren<Component>(true).Select(c => c != null ? c.GetIl2CppType().Name : "null").Distinct().Take(24));

        // The willow's vegetation info, from a willow of the world.
        var willowSpot = new Vector3(-200.9f, 34.4f, 394.5f);
        var go = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(willowSpot.x - 8, willowSpot.z - 8);
        foreach (float wait in WaitFor("the teleport", () => go.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var willow = Biome(willowSpot, 2f).FirstOrDefault(b => b.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo()?.Name == "tree_willow");
        var vegetation = willow?.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo();
        Log($"Probe18: the willow of the world {(willow != null ? P(willow.GetPosition()) : "none")}: vegetation prefab '{(vegetation?.VegetationPrefab != null ? vegetation.VegetationPrefab.name : "none")}' ({vegetation?.PrefabType}), function prefab '{(vegetation?.FunctionPrefab != null ? vegetation.FunctionPrefab.name : "none")}', scale {vegetation?.MinScale}–{vegetation?.MaxScale}, collider {vegetation?.ColliderType}");
        if (vegetation?.VegetationPrefab == null)
        {
            Log("Probe18: no willow model");
            yield break;
        }
        Log($"Probe18: the willow model's parts [{Parts(vegetation.VegetationPrefab)}], renderers {vegetation.VegetationPrefab.GetComponentsInChildren<Renderer>(true).Length}");

        // The kind: a trunk's item copied, with the willow's object and model.
        var baseInfo = db.GetItemInfo("Item_Wood_Fir3");
        var willowInfo = db.GetItemInfo("Item_Wood_TreeWillow");
        var list = db.CompleteItemInfoList.itemInfoList;
        ItemInfo? kind = null;
        string made = Try(() =>
        {
            var holder = new GameObject("Mjolnir_Probe18_Prefabs");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var prefab = Object.Instantiate(willowInfo.spawnObject, holder.transform);
            prefab.name = "Mjolnir_Tree_Willow";
            foreach (var b in prefab.GetComponentsInChildren<BiomeObject>(true)) Object.DestroyImmediate(b);
            var model = Object.Instantiate(vegetation.VegetationPrefab, prefab.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            kind = Object.Instantiate(baseInfo);
            kind.name = "Mjolnir_Tree_Willow";
            kind.id = Probe18Id;
            kind.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            kind.spawnObject = prefab;
            list.Add(kind);
            kind.databaseIndex = (ushort)(list.Count - 1);
            db._itemsMap[kind.id] = kind;
            kind.Configure();
            return $"made: index {kind.databaseIndex}, found by id {db.GetItemInfoFromID(Probe18Id)?.name}, by name {db.GetItemInfo("Mjolnir_Tree_Willow")?.name}; prefab [{Parts(prefab)}]";
        });
        Log($"Probe18: the kind {made}");
        if (kind == null) yield break;

        // A free spot.
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 40f, 60f, 80f, 100f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var square = LandscapeArea.Rectangle(at, 24, 14, blend: 0);
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
            Log("Probe18: no free spot");
            yield break;
        }
        var stand = spot - (spot - home).normalized * 18f;
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 12f, -24f);
        camera.Rotation = Quaternion.LookRotation(spot + Vector3.up * 5 - camera.Position, Vector3.up);

        var placed = new List<(float Scale, Vector3 At)>();
        foreach (float size in new[] { 1f, 0.5f })
        {
            var at = spot + new Vector3(size == 1f ? -5f : 5f, 0, 0);
            at.y = Landscape.HeightAt(at.x, at.z);
            string spawned = Try(() =>
            {
                var context = ItemObjectSpawnContext.WORLD;
                int quantity = 1;
                var position = at;
                var rotation = Quaternion.identity;
                var scale = Vector3.one * size;
                var one = kind.SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, true);
                return one != null ? $"{one.name}, renderers {one.GetComponentsInChildren<Renderer>(true).Length}, scale {one.transform.localScale.y}" : "null";
            });
            placed.Add((size, at));
            Log($"Probe18: a willow at scale {size} put down at {P(at)}: {spawned}");
        }
        yield return 5f;
        foreach (var (size, at) in placed)
        {
            var one = Items(at, 1.5f).FirstOrDefault(i => i.Descriptor?.itemInfo?.name == "Mjolnir_Tree_Willow");
            var harvest = one?.gameObject != null ? one.gameObject.GetComponentInChildren<HarvestInteraction>(true) : null;
            Log($"Probe18: the willow at scale {size} 5 s later: world item {one != null}, its scale {one?.GetScale().y:0.##}, object '{(one?.gameObject != null ? one.gameObject.name : "none")}', active {one?.gameObject?.activeInHierarchy}, renderers {(one?.gameObject != null ? one.gameObject.GetComponentsInChildren<Renderer>().Length : 0)}, harvest pieces {harvest?.harvestPieces?.Count}, piece {Try(() => harvest?.GetCurrentPieceIndex())}, points {Try(() => harvest?.GetHarvestHitPoints())}; SDK {NaturalObjects.Nearest(at, 1.5f)?.ToString() ?? "none"}; others round it [{string.Join(", ", Items(at, 6).Select(i => i.Descriptor?.itemInfo?.name).GroupBy(n => n).Select(g => $"{g.Key} ×{g.Count()}"))}]");
        }
        Shot("probe18-placed");
        yield return AfterShot;

        // The small one harvested.
        var small = placed.FirstOrDefault(p => p.Scale == 0.5f);
        if (NaturalObjects.Nearest(small.At, 1.5f) is { } sdk && sdk.AssetName == "Mjolnir_Tree_Willow")
        {
            bool done = sdk.Harvest();
            yield return 8f;
            Log($"Probe18: the small willow harvested: {done}; items round it [{string.Join(", ", NaturalObjects.Around(small.At, 16).Where(o => o.Kind == NaturalObjectKind.Item).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"))}]");
            Shot("probe18-harvested");
            yield return AfterShot;
        }

        // Saved, back to the menu, loaded again.
        var storage = GameManagers.Get<StorageManager>();
        if (storage != null)
        {
            var task = storage.SaveCurrentSession();
            foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
            Log($"Probe18: saved, {storage.LastSaveOperationCode}");
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
            var db2 = GameManagers.Get<ItemDatabaseManager>()?.Database;
            Log($"Probe18: after loading again the database knows the kind: by id {db2?.GetItemInfoFromID(Probe18Id)?.name ?? "no"}");
            var big = placed.First(p => p.Scale == 1f);
            var one = Items(big.At, 1.5f).FirstOrDefault();
            Log($"Probe18: after loading again at the big willow's place: {one?.Descriptor?.itemInfo?.name ?? "nothing"}, scale {one?.GetScale().y:0.##}, object '{(one?.gameObject != null ? one.gameObject.name : "none")}', renderers {(one?.gameObject != null ? one.gameObject.GetComponentsInChildren<Renderer>().Length : 0)}");
            var back = Overhead(spot);
            back.Position = spot + new Vector3(0, 12f, -24f);
            back.Rotation = Quaternion.LookRotation(spot + Vector3.up * 5 - back.Position, Vector3.up);
            yield return 1f;
            Shot("probe18-reloaded");
            yield return AfterShot;
            back.Dispose();
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
