using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Fusion;
using SSSGame;
using SSSGame.Network;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SSSGame.UI;
using SandSailorStudio.UI;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using FusionPrefabTable = Fusion.NetworkPrefabTable;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "netprefab research" scenario (with -Examples, AltarExample turned on): whether a mod's building can have a network
/// prefab of its own. In the main menu the altar's structure prefab (its base's) is copied under an inactive object that
/// lives the whole game, the copy gets a network guid of its own (from the mod id and the name) and goes into Fusion's
/// prefab table (<c>NetworkPrefabTable.TryAdd</c>); the altar's template then points at it. In the world the altar is
/// placed and must be an instance of the copy (its guid, the spawner's prefab), then dismantled; back in the main menu the
/// table must still know the copy, and in the world again the altar is placed and dismantled once more. Nothing is saved
/// by the scenario, but the game saves a loaded world by itself: back the save up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> NetPrefabResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var template = db?.GetItemInfo(AltarAsset)?.TryCast<StructureTemplate>();
        var node = template?.TryCast<NodeTemplate>();
        FusionPrefabTable? table = null;
        try
        {
            table = NetworkProjectConfig.Global?.PrefabTable;
        }
        catch (System.Exception e)
        {
            Log($"NetPrefab: no config: {e.Message}");
        }
        if (node == null || table == null)
        {
            Log($"NetPrefab: missing: the altar's template {node != null}, the table {table != null}");
            yield break;
        }
        Log($"NetPrefab: the table in the main menu: {table.Count} prefabs, the last id {table.LastId.Value}");
        var original = node.nodeStructure;
        var originalObject = original.GetComponent<NetworkObject>();
        Log($"NetPrefab: the altar's prefab {original.name}: guid {originalObject.NetworkGuid}, flags {originalObject.Flags}, " +
            $"nested {originalObject.NestedObjects?.Length ?? -1}, behaviours {originalObject.NetworkedBehaviours?.Length ?? -1}, " +
            $"in the table {(table.TryGetId(originalObject.NetworkGuid, out var originalId) ? originalId.Value.ToString() : "no")}");
        Log($"NetPrefab: the altar's preview {node.nodePreview?.name}: network object {node.nodePreview?.GetComponent<NetworkObject>() != null}");

        if (original.name == AltarAsset)
        {
            // Since the SDK gives every mod building its own prefab, the research's copy is that one.
            Log($"NetPrefab: the altar has the SDK's own prefab already (guid {originalObject.NetworkGuid}): nothing to copy");
            if (QuitWhenDone) Application.Quit();
            yield break;
        }
        // The copy: under an inactive object living the whole game, so it never wakes.
        var holder = new GameObject("Mjolnir NetPrefabs");
        holder.SetActive(false);
        Object.DontDestroyOnLoad(holder);
        holder.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        var copy = Object.Instantiate(original.gameObject, holder.transform);
        copy.name = "Mjolnir altar prefab";
        copy.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        var copyObject = copy.GetComponent<NetworkObject>();
        var nested = copyObject.NestedObjects;
        int nestedInside = nested == null ? 0 : nested.Count(n => n != null && n.transform.IsChildOf(copy.transform));
        var behaviours = copyObject.NetworkedBehaviours;
        int behavioursInside = behaviours == null ? 0 : behaviours.Count(b => b != null && b.transform.IsChildOf(copy.transform));
        Log($"NetPrefab: the copy: flags {copyObject.Flags}, nested {nested?.Length ?? -1} ({nestedInside} inside the copy), " +
            $"behaviours {behaviours?.Length ?? -1} ({behavioursInside} inside the copy)");

        var guidBytes = MD5.HashData(Encoding.UTF8.GetBytes("mjolnir.altarexample/building/altar"));
        var guidText = string.Concat(guidBytes.Select(b => b.ToString("x2")));
        NetworkObjectGuid guid = default;
        try
        {
            guid = new NetworkObjectGuid(guidText);
        }
        catch (System.Exception e)
        {
            Log($"NetPrefab: the guid failed: {e.Message}");
            yield break;
        }
        copyObject.NetworkGuid = guid;
        Log($"NetPrefab: the copy's guid {copyObject.NetworkGuid} (from {guidText}), valid {guid.IsValid}");
        NetworkPrefabId id = default;
        bool added = false;
        try
        {
            var source = new NetworkPrefabSourceStatic { PrefabReference = copyObject };
            added = table.TryAdd(guid, source.Cast<INetworkPrefabSource>(), out id);
        }
        catch (System.Exception e)
        {
            Log($"NetPrefab: TryAdd failed: {e}");
        }
        Log($"NetPrefab: TryAdd {added}, id {id.Value}, the table {table.Count} prefabs, the last id {table.LastId.Value}");
        try
        {
            bool found = table.TryGetPrefab(id, out var back);
            Log($"NetPrefab: TryGetPrefab {found}: {(back == null ? "null" : back.name)}, the copy {back != null && back.Pointer == copyObject.Pointer}");
        }
        catch (System.Exception e)
        {
            Log($"NetPrefab: TryGetPrefab failed: {e.Message}");
        }
        if (!added) yield break;
        node.nodeStructure = copy.GetComponent<Structure>();

        for (int round = 1; round <= 2; round++)
        {
            foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
            yield return 8f;
            KeepDaytime();
            yield return 2f;
            var runner = GameManagers.Get<NetworkSpawner>();
            Log($"NetPrefab: round {round}: the table {table.Count} prefabs, ours {(table.TryGetId(guid, out var nowId) ? nowId.Value.ToString() : "gone")}, " +
                $"the config's table the same {NetworkProjectConfig.Global?.PrefabTable?.Pointer == table.Pointer}");
            var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
            var cam = Camera.main!.transform;
            var forward = cam.forward;
            forward.y = 0;
            forward.Normalize();
            var at = cam.position + forward * 3.2f;
            at = GroundPoint(at) ?? at;
            var placement = new PointPlacement();
            placement.position = at;
            placement.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            Structure? built = null;
            try
            {
                built = settlement?.CreateStructure(template, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
            }
            catch (System.Exception e)
            {
                Log($"NetPrefab: round {round}: building failed: {e}");
            }
            yield return 3f;
            if (built == null)
            {
                Log($"NetPrefab: round {round}: nothing built");
                yield break;
            }
            var builtObject = built.GetComponent<NetworkObject>();
            string spawnerPrefab = "?";
            try
            {
                spawnerPrefab = runner != null && runner.TryGetPrefabFromSpawnedObject(builtObject, out var prefab) ? $"{prefab?.name} (the copy {prefab != null && prefab.Pointer == copyObject.Pointer})" : "none";
            }
            catch (System.Exception e)
            {
                spawnerPrefab = e.Message;
            }
            var model = built.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Mjolnir Model"));
            Log($"NetPrefab: round {round}: built {built.name}: guid {builtObject.NetworkGuid} (ours {builtObject.NetworkGuid == guid}), " +
                $"flags {builtObject.Flags}, id {builtObject.Id}, valid {builtObject.IsValid}, template {built.TemplateID} (the altar's {template.id}), " +
                $"the spawner's prefab {spawnerPrefab}, active {built.IsActive}, build site {built.buildSite != null}, the mod's model {model}");
            Shot($"netprefab-{round}");
            yield return 1f;
            try
            {
                built.Dismantle(true);
            }
            catch (System.Exception e)
            {
                Log($"NetPrefab: round {round}: dismantling failed: {e.Message}");
            }
            yield return 2f;
            if (round == 2) break;
            MainMenuReady = false;
            HudReady = false;
            Menu.Show<GamePauseMenu>(null);
            yield return 1.5f;
            Menu.GetInstance<GamePauseMenu>().BtnQuitToMainMenu();
            yield return 1.5f;
            if (Menu.GetInstance<ConfirmActionMenu>() is { } confirm) Click(confirm.ConfirmButton.gameObject);
            foreach (float wait in WaitFor("main menu again", () => MainMenuReady, 180)) yield return wait;
            yield return 3f;
            Log($"NetPrefab: back in the main menu: the table {table.Count} prefabs, ours {(table.TryGetId(guid, out var menuId) ? menuId.Value.ToString() : "gone")}, " +
                $"the config's table the same {NetworkProjectConfig.Global?.PrefabTable?.Pointer == table.Pointer}, the copy alive {copy != null}");
        }
        node.nodeStructure = original;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
