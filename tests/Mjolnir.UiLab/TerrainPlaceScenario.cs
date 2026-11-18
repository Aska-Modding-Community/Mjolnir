using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK;
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
/// The "terrain place" scenario: kinds of the world placed through Mjolnir.SDK.Terrain (<c>NaturalObjects.Place</c>). In
/// the main menu it lists the kinds. On a free spot it places a willow grown and young, a birch, a fir, a berry bush, a
/// cabbage, a boulder of the scenery, a stone clump and lupins, then fells the young willow (its trunk chopped), gathers the
/// bush, mines the clump, clears the boulder, places an oak through the console, saves, goes back to the main menu, loads
/// again and reads what stands. "terrain place check" loads the save in a fresh game and reads what stands there. Both
/// change the save's world: back the save up and put it back after the check.
/// </summary>
internal static partial class Autopilot
{
    private static Vector3 PlaceSpot(Vector3 home)
    {
        foreach (float distance in new[] { 40f, 60f, 80f, 100f })
            for (int turn = 0; turn < 360; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var square = LandscapeArea.Rectangle(at, 44, 16, blend: 0);
                var m = Landscape.Measure(square);
                if (!m.IsKnown || m.Minimum < Landscape.WaterLevel + 1 || m.Maximum - m.Minimum > 3f || Landscape.Check(square, LandscapeOperation.Level) != LandscapeResult.Done) continue;
                if (NaturalObjects.In(square).Any(o => o.Kind is NaturalObjectKind.Tree or NaturalObjectKind.LargeObject)) continue;
                return at;
            }
        return new Vector3(float.NaN, 0, 0);
    }

    private static string Placed(Vector3 spot) => string.Join(", ", NaturalObjects.Around(spot, 30).Where(o => o.AssetName.StartsWith("Mjolnir_World_", StringComparison.Ordinal)).GroupBy(o => o.AssetName.Substring("Mjolnir_World_".Length)).Select(g => $"{g.Key} ×{g.Count()}"));

    private static IEnumerator<float> TerrainPlaceScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        var kinds = NaturalObjects.Kinds;
        Log($"Place: in the main menu {kinds.Count} kinds: {string.Join("; ", kinds.GroupBy(k => k.Kind).Select(g => $"{g.Key} {g.Count()} ({string.Join(", ", g.Take(12).Select(k => k.Name))})"))}");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Place: no world");
            yield break;
        }
        var home = character.transform.position;
        var spot = PlaceSpot(home);
        if (float.IsNaN(spot.x))
        {
            Log("Place: no free spot");
            yield break;
        }
        Log($"Place: the spot {P(spot)}");
        var stand = spot - (spot - home).normalized * 16f;
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 14f, -26f);
        camera.Rotation = Quaternion.LookRotation(spot + Vector3.up * 3 - camera.Position, Vector3.up);

        // A. One of each.
        var plan = new (string Kind, float X, float Scale)[]
        {
            ("tree_willow", -20, 1f), ("tree_willow", -14, 0.5f), ("tree_birch1", -9, 1f), ("tree_fir3", -4, 1f),
            ("bush_sloe_berries", 0, 1f), ("cabbage", 3, 1f), ("rock_shoreline_big", 8, 1f), ("rock_mountain4", 14, 1f), ("flowers_lupins", 19, 1f),
        };
        var placed = new Dictionary<string, (NaturalObject? Object, Vector3 At)>();
        foreach (var (kind, x, scale) in plan)
        {
            var at = spot + new Vector3(x, 0, 0);
            NaturalObject? one = null;
            string error = Try(() =>
            {
                one = NaturalObjects.Place(kind, at, 20f * x, scale);
                return "";
            });
            placed[Inv($"{kind}@{scale}")] = (one, at);
            Log($"Place: {kind} at scale {scale}: {(one != null ? $"{one}, harvestable {one.IsHarvestable}, gather {one.GatherCount}, resource {one.ResourceName}" : "nothing")} {error}");
        }
        yield return 5f;
        Log($"Place: 5 s later round the spot [{Placed(spot)}]");
        Shot("place-placed");
        yield return AfterShot;

        // B. The young willow felled (its trunk chopped), the bush gathered, the clump mined, the boulder cleared.
        string Loot(Vector3 at, float r) => string.Join(", ", NaturalObjects.Around(at, r).Where(o => o.Kind == NaturalObjectKind.Item).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"));
        var young = placed["tree_willow@0.5"];
        string before = Loot(young.At, 10);
        bool felled = young.Object?.Harvest() ?? false;
        yield return 8f;
        Log($"Place: the young willow felled: {felled}; loot round it before [{before}], after [{Loot(young.At, 10)}]");
        var trunks = Objects(young.At, 12f, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(i => i != null && i.Descriptor?.itemInfo?.name == "Item_Wood_Willow").Cast<InventoryItemInstance>().ToList();
        Log($"Place: trunks round it: {trunks.Count} listed, {trunks.Select(t => t.Pointer).Distinct().Count()} distinct, {trunks.Count(t => t.gameObject != null)} with an object, places [{string.Join(" ", trunks.Take(6).Select(t => P(t.GetPosition())))}], scales [{string.Join(" ", trunks.Take(6).Select(t => t.GetScale().y.ToString("0.##")))}]");
        // The big willow felled to its stump only (its tree part struck as a villager strikes): the stump stays.
        var big = placed["tree_willow@1"];
        var bigItem = Objects(big.At, 1.5f, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).FirstOrDefault(i => i?.Descriptor?.itemInfo?.name == "Mjolnir_World_tree_willow");
        var bigHarvest = bigItem?.gameObject != null ? bigItem.gameObject.GetComponentInChildren<HarvestInteraction>(true) : null;
        if (bigHarvest != null)
        {
            var dealer = character.GetComponentInChildren<SSSGame.Combat.MeleeManager>(true)?.TryCast<SSSGame.Combat.IDamageDealer>();
            var blow = new SSSGame.Combat.DamageData
            {
                damageDealer = dealer,
                damageReceiver = bigHarvest.TryCast<SSSGame.Combat.IDamageReceiver>(),
                baseDamage = bigHarvest.GetHarvestHitPoints() + 1,
                damageMultiplier = 1,
                damageFlags = SSSGame.Combat.DamageFlags.IgnoreHarvestTool,
                hitPosition = bigHarvest.transform.position + Vector3.up,
                hitDirection = Vector3.forward,
            };
            bigHarvest.TakeDamage(blow);
            yield return 4f;
            var root = bigItem!.gameObject.transform;
            Log($"Place: the big willow felled to its stump: piece {bigHarvest.GetCurrentPieceIndex()}, points {bigHarvest.GetHarvestHitPoints()}, model shown {root.Find("Model")?.gameObject.activeSelf}, stump shown {root.Find("Stump")?.gameObject.activeSelf}; trunks lying round it {Objects(big.At, 16f, null, InstanceActivationContext.ALL).Count(i => i.TryCast<InventoryItemInstance>()?.Descriptor?.itemInfo?.name == "Item_Wood_Willow")}");
        }
        var bush = placed["bush_sloe_berries@1"].Object;
        int gathered = bush?.Gather(1) ?? -1;
        yield return 2f;
        Log($"Place: the bush gathered {gathered}, left {bush?.GatherCount}; loot [{Loot(placed["bush_sloe_berries@1"].At, 3)}]");
        var clump = placed["rock_mountain4@1"];
        var clumpItem = Objects(clump.At, 1.5f, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).FirstOrDefault(i => i?.Descriptor?.itemInfo?.name == "Mjolnir_World_rock_mountain4");
        var clumpObject = clumpItem?.gameObject;
        Log($"Place: the clump's object: {(clumpObject != null ? $"harvest spawners {clumpObject.GetComponentsInChildren<HarvestSpawner>(true).Length}, loot spawners {clumpObject.GetComponentsInChildren<LootSpawner>(true).Length}, item spawners {clumpObject.GetComponentsInChildren<ItemSpawner>(true).Length}, pieces {clumpObject.GetComponentInChildren<HarvestInteraction>(true)?.harvestPieces?.Count}, points {Try(() => clumpObject.GetComponentInChildren<HarvestInteraction>(true)?.GetHarvestHitPoints())}" : "none")}");
        string clumpBefore = Loot(clump.At, 5);
        bool mined = clump.Object?.Harvest() ?? false;
        yield return 3f;
        Log($"Place: the clump mined: {mined}; loot before [{clumpBefore}], after [{Loot(clump.At, 5)}]");
        var boulder = placed["rock_shoreline_big@1"].Object;
        bool cleared = boulder?.Clear() ?? false;
        yield return 2f;
        Log($"Place: the boulder cleared: {cleared}, gone {boulder?.IsGone}");

        // C. The console.
        var by = spot + new Vector3(0, 0, -8);
        var go = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(by.x, by.z);
        foreach (float wait in WaitFor("the teleport", () => go.IsCompleted, 30)) yield return wait;
        yield return 2f;
        DevConsole.Execute("place tree_oak 1.2");
        yield return 3f;
        Log($"Place: after the console's place round the spot [{Placed(spot)}]");
        Shot("place-worked");
        yield return AfterShot;

        // D. Saved, back to the menu, loaded again.
        var storage = GameManagers.Get<StorageManager>();
        if (storage != null)
        {
            var task = storage.SaveCurrentSession();
            foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
            Log($"Place: saved, {storage.LastSaveOperationCode}");
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
            Log($"Place: after loading again round the spot [{Placed(spot)}]");
            var stumpItem = Objects(big.At, 1.5f, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).FirstOrDefault(i => i?.Descriptor?.itemInfo?.name == "Mjolnir_World_tree_willow");
            var stumpRoot = stumpItem?.gameObject?.transform;
            Log($"Place: after loading again the big willow: {(stumpRoot != null ? $"piece {Try(() => stumpItem!.gameObject.GetComponentInChildren<HarvestInteraction>(true)?.GetCurrentPieceIndex())}, model shown {stumpRoot.Find("Model")?.gameObject.activeSelf}, stump shown {stumpRoot.Find("Stump")?.gameObject.activeSelf}" : "not found")}");
            var back = Overhead(spot);
            back.Position = spot + new Vector3(0, 14f, -26f);
            back.Rotation = Quaternion.LookRotation(spot + Vector3.up * 3 - back.Position, Vector3.up);
            yield return 1f;
            Shot("place-reloaded");
            yield return AfterShot;
            back.Dispose();
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static IEnumerator<float> TerrainPlaceCheckScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Place check: in the main menu {NaturalObjects.Kinds.Count} kinds");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Place check: no world");
            yield break;
        }
        var home = character.transform.position;
        // The spot of "terrain place": the same search from the same home, now with its objects standing, so it is
        // looked for again without them in the way: the free square's place is logged by the first run.
        var spot = new Vector3(PlaceCheckX, 0, PlaceCheckZ);
        spot.y = Landscape.HeightAt(spot.x, spot.z);
        var stand = spot - (spot - home).normalized * 16f;
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 5f;
        Log($"Place check: after a fresh start round {P(spot)} [{Placed(spot)}]");
        var willowItem = Objects(spot + new Vector3(-20, 0, 0), 1.5f, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).FirstOrDefault(i => i?.Descriptor?.itemInfo?.name == "Mjolnir_World_tree_willow");
        var willowRoot = willowItem?.gameObject?.transform;
        Log($"Place check: the big willow: {(willowRoot != null ? $"model shown {willowRoot.Find("Model")?.gameObject.activeSelf}, stump shown {willowRoot.Find("Stump")?.gameObject.activeSelf}" : "not found")}");
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 14f, -26f);
        camera.Rotation = Quaternion.LookRotation(spot + Vector3.up * 3 - camera.Position, Vector3.up);
        yield return 1f;
        Shot("place-fresh");
        yield return AfterShot;
        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    /// <summary>The spot "terrain place" used, from its log (set before running "terrain place check").</summary>
    private const float PlaceCheckX = -144.7f, PlaceCheckZ = 322.8f;
}
