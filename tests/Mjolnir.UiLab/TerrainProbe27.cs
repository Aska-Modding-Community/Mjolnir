using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.Combat;
using SSSGame.Weather;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using SSSGame.UI;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe27" scenario: what becomes of a placed bush gathered to the end once the world is saved and loaded,
/// and how the game grows a planted sapling (its processes, their fields, the sapling's properties and scale while the
/// world's time runs fast). Every destruction of a placed object is logged with its stack (probe26's patch). It saves the
/// world: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe27()
    {
        Log($"Probe27: {Try(() => $"{UiLab.Instance.Harmony.CreateClassProcessor(typeof(Probe26Patches)).Patch()?.Count ?? 0} method(s) patched")}");
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        var database = GameManagers.Get<ItemDatabaseManager>()?.Database;
        ItemInfo? sapling = null;
        try { sapling = database?.GetItemInfo("Item_Wood_TreeBirch1Sapling"); } catch (Exception e) { Log($"Probe27: no sapling kind: {e.Message}"); }
        Log($"Probe27: the sapling kind {sapling?.name}, processes: {Try(() => string.Join("; ", (sapling?.processes ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ItemProcess>(0L)).Select(DescribeProcess)))}");
        Log($"Probe27: the sapling kind's attributes: {Try(() => string.Join(", ", (sapling?.attributes ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SandSailorStudio.Attributes.AttributeData>(0L)).Select(a => $"{a?.attribute?.name} {a?.value} ×{a?.multiplier}")))}");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Probe27: no world");
            yield break;
        }
        var spot = PlaceSpot(character.transform.position);
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(spot.x, spot.z - 6);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 3f;

        // A. A bush gathered to the end.
        var bushAt = spot;
        var bush = NaturalObjects.Place("bush_sloe_berries", bushAt);
        yield return 2f;
        for (int charge = 0; charge < 8 && bush != null && bush.GatherCount > 0; charge++)
        {
            bush.Gather(1);
            yield return 1f;
        }
        var bushItem = PlacedItem(bushAt, "Mjolnir_World_bush_sloe_berries");
        var gather = bushItem?.gameObject != null ? bushItem.gameObject.GetComponentInChildren<GatherInteraction>(true) : null;
        Log($"Probe27: the bush gathered: count {Try(() => gather?.CheckAvailableItemCount())}, destroyed {bushItem?.Destroyed}, removable {Try(() => DescribeRemovable(bushItem))}");

        // B. A sapling planted, the time run fast.
        var saplingAt = spot + new Vector3(4, 0, 0);
        if (Landscape.TryGetHeight(saplingAt.x, saplingAt.z, out float ground)) saplingAt.y = ground;
        var context = ItemObjectSpawnContext.WORLD;
        int one = 1;
        var turn = Quaternion.identity;
        var size = Vector3.one;
        try { sapling?.SpawnObject(ref context, ref one, ref saplingAt, ref turn, ref size, null, false); } catch (Exception e) { Log($"Probe27: the sapling not planted: {e.Message}"); }
        yield return 3f;
        var planted = PlacedItem(saplingAt, "Item_Wood_TreeBirch1Sapling");
        var weather = WeatherSystem.Instance;
        Log($"Probe27: the sapling planted: {planted != null}, scale {planted?.GetScale()}, properties {Try(() => DescribeProperties(planted))}; day {Try(() => weather?.GetDaysPassed())}, time {weather?.TimeOfDay}, speed {weather?.TimeSpeedMultiplier}, day length {weather?.dayLength}");
        float speed = weather != null ? weather.TimeSpeedMultiplier : 1f;
        if (weather != null) weather.TimeSpeedMultiplier = 200f;
        for (int i = 0; i < 6; i++)
        {
            yield return 10f;
            Log($"Probe27: the sapling after {(i + 1) * 10} s fast: day {Try(() => weather?.GetDaysPassed())}, time {weather?.TimeOfDay:0.000}, scale {planted?.GetScale()}, properties {Try(() => DescribeProperties(planted))}");
        }
        if (weather != null) weather.TimeSpeedMultiplier = speed;
        Log($"Probe27: the bush after the fast time: count {Try(() => gather?.CheckAvailableItemCount())}, destroyed {bushItem?.Destroyed}");

        // C. Saved, back to the menu, loaded again.
        var storage = GameManagers.Get<StorageManager>();
        if (storage != null)
        {
            var task = storage.SaveCurrentSession();
            foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
            Log($"Probe27: saved, {storage.LastSaveOperationCode}");
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
            var again = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(spot.x, spot.z - 6);
            foreach (float wait in WaitFor("the teleport", () => again.IsCompleted, 30)) yield return wait;
            yield return 4f;
            var bushAgain = PlacedItem(bushAt, "Mjolnir_World_bush_sloe_berries");
            var gatherAgain = bushAgain?.gameObject != null ? bushAgain.gameObject.GetComponentInChildren<GatherInteraction>(true) : null;
            Log($"Probe27: after loading again the bush: {bushAgain != null}, count {Try(() => gatherAgain?.CheckAvailableItemCount())}; the sapling: {PlacedItem(saplingAt, "Item_Wood_TreeBirch1Sapling")?.GetScale().ToString() ?? "none"}");
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static InventoryItemInstance? PlacedItem(Vector3 at, string name) =>
        Objects(at, 1.5f, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).FirstOrDefault(i => i?.Descriptor?.itemInfo?.name == name);

    private static string DescribeRemovable(InventoryItemInstance? item)
    {
        if (item == null) return "none";
        return string.Join(", ", new[] { InstanceDestructionLevel.SYSTEM, InstanceDestructionLevel.BOMBS, InstanceDestructionLevel.TERRAFORMING }.Select(l => $"{l} {item.IsRemovable(l)}"));
    }

    private static string DescribeProperties(InventoryItemInstance? item)
    {
        var list = new Il2CppSystem.Collections.Generic.List<SandSailorStudio.Attributes.Property>();
        if (item?.GetItem() is not { } it || !it.GetProperties(list)) return "none";
        var parts = new List<string>();
        for (int i = 0; i < list.Count; i++) parts.Add($"{Try(() => list[i].NameToString())}={list[i].GetValue():0.###}");
        return string.Join(", ", parts);
    }

    private static string DescribeProcess(ItemProcess process)
    {
        if (process == null) return "null";
        string text = $"{process.GetIl2CppType().Name} '{process.name}' tick {process.tickInterval} batch {process.batchSize}";
        var growth = process.TryCast<SaplingGrowthProcess>();
        if (growth != null)
            text += $" scale {growth.saplingScaleRange} jitter {growth.grownTreeScaleJitter} mature {growth.matureTreeFlag} reverse {growth.reverseGrowthFlag} ignore scale {growth.ignoreScaleChanges}, deltas [{string.Join(", ", (growth.attributesDeltaWhileGrowing ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SandSailorStudio.Attributes.AttributeData>(0L)).Select(a => $"{a?.attribute?.name} {a?.value} ×{a?.multiplier}"))}]";
        return text;
    }
}
