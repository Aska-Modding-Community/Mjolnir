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
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe22" scenario: probe21 again, looking at how a stump is drawn (the materials of the willow's model,
/// the trunk's material helper), trying the trunk's material property on a felled willow's model, a berry bush on its own
/// kind, and a rock of the scenery without the raw stone's pick-up and harvest.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe22()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var biomes = GameManagers.Get<BiomesManager>();
        if (character == null || db == null || biomes == null || !Landscape.IsAvailable)
        {
            Log("Probe22: no world");
            yield break;
        }
        var home = character.transform.position;
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();
        string Parts(GameObject o) => string.Join(", ", o.GetComponentsInChildren<Component>(true).Select(c => c != null ? c.GetIl2CppType().Name : "null").Distinct().Take(24));

        // A. The vegetation system's kinds here.
        var infos = new Dictionary<string, AwesomeTechnologies.VegetationSystem.VegetationItemInfoPro>();
        var packages = biomes._localPlayerVegetationSet?.packages;
        for (int i = 0; packages != null && i < packages.Count; i++)
        {
            var list = packages[i]?.VegetationInfoList;
            for (int k = 0; list != null && k < list.Count; k++)
                if (list[k] != null && !infos.ContainsKey(list[k].Name)) infos[list[k].Name] = list[k];
        }
        string Shader(Renderer r) => string.Join("; ", r.sharedMaterials.Where(m => m != null).Select(m => $"{m.name} [{m.shader?.name}: {string.Join(",", Enumerable.Range(0, m.shader?.GetPropertyCount() ?? 0).Where(k => m.shader.GetPropertyType(k) is UnityEngine.Rendering.ShaderPropertyType.Float or UnityEngine.Rendering.ShaderPropertyType.Range).Select(k => $"{m.shader.GetPropertyName(k)}={m.GetFloat(m.shader.GetPropertyName(k)):0.##}"))}]"));
        if (infos.TryGetValue("tree_willow", out var willowVeg) && willowVeg.VegetationPrefab != null)
            foreach (var r in willowVeg.VegetationPrefab.GetComponentsInChildren<Renderer>(true))
                Log($"Probe22: the willow model's {r.name}: mesh {r.GetComponent<MeshFilter>()?.sharedMesh?.name}, {Shader(r)}");
        var trunkPrefab = db.GetItemInfo("Item_Wood_Willow")?.spawnObject;
        if (trunkPrefab != null)
        {
            foreach (var r in trunkPrefab.GetComponentsInChildren<Renderer>(true))
                Log($"Probe22: the trunk's {r.name}: mesh {r.GetComponent<MeshFilter>()?.sharedMesh?.name}, {Shader(r)}");
            foreach (var helper in trunkPrefab.GetComponentsInChildren<SandSailorStudio.Utils.MaterialFloatPropertyHelper>(true))
                Log($"Probe22: the trunk's material helper on '{helper.name}': renderer '{helper.propertyRenderer.coloredRenderer?.name}', property '{helper.propertyRenderer.materialFloatPropertyField}'");
        }

        // B. Three kinds of our own.
        var list0 = db.CompleteItemInfoList.itemInfoList;
        var holder = new GameObject("Mjolnir_Probe22_Prefabs");
        holder.SetActive(false);
        Object.DontDestroyOnLoad(holder);
        ItemInfo Register(ItemInfo baseInfo, GameObject prefab, string name, int id)
        {
            var info = Object.Instantiate(baseInfo);
            info.name = name;
            info.id = id;
            info.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            info.spawnObject = prefab;
            foreach (var component in prefab.GetComponentsInChildren<ItemComponent>(true))
            {
                var quantity = new ItemInfoQuantity { itemInfo = info };
                quantity.quantity = 1;
                component.data = quantity;
            }
            list0.Add(info);
            info.databaseIndex = (ushort)(list0.Count - 1);
            db._itemsMap[info.id] = info;
            info.Configure();
            return info;
        }
        GameObject FromBiome(GameObject source, string name)
        {
            var prefab = Object.Instantiate(source, holder.transform);
            prefab.name = name;
            var biome = prefab.GetComponentInChildren<BiomeObject>(true);
            if (biome != null)
            {
                var resource = biome.gameObject.AddComponent<ResourceObject>();
                resource.data = biome.data;
                resource.context = biome.context;
                resource.geometry = biome.geometry;
                resource.interactions = biome.interactions;
                resource.pickables = biome.pickables;
                resource.dataEvents = biome.dataEvents;
                Object.DestroyImmediate(biome);
            }
            foreach (var sp in prefab.GetComponentsInChildren<SubcomponentSpawner>(true)) Object.DestroyImmediate(sp);
            foreach (var sp in prefab.GetComponentsInChildren<VegetationItemSpawner>(true)) Object.DestroyImmediate(sp);
            return prefab;
        }
        void AddModel(GameObject prefab, GameObject? model)
        {
            if (model == null) return;
            var copy = Object.Instantiate(model, prefab.transform);
            copy.name = "Model";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
        }

        var made = new List<(string What, ItemInfo Info)>();
        string Made(string what, Func<ItemInfo> make)
        {
            try
            {
                var info = make();
                made.Add((what, info));
                return $"{info.name}: [{Parts(info.spawnObject)}]";
            }
            catch (Exception e)
            {
                return string.Join(" | ", e.ToString().Split(new[] { (char)13, (char)10 }, StringSplitOptions.RemoveEmptyEntries).Take(8));
            }
        }
        Log("Probe22: willow " + Made("willow", () =>
        {
            var prefab = FromBiome(db.GetItemInfo("Item_Wood_TreeWillow").spawnObject, "Mjolnir_Tree_Willow22");
            AddModel(prefab, infos.TryGetValue("tree_willow", out var v) ? v.VegetationPrefab : null);
            return Register(db.GetItemInfo("Item_Wood_Willow"), prefab, "Mjolnir_Tree_Willow22", 0x4D4A5721);
        }));
        Log("Probe22: bush " + Made("bush", () =>
        {
            var prefab = FromBiome(db.GetItemInfo("Item_Food_BiomeBerries").spawnObject, "Mjolnir_Bush_Berries22");
            return Register(db.GetItemInfo("Item_Food_BiomeBerries"), prefab, "Mjolnir_Bush_Berries22", 0x4D4A5722);
        }));
        var rockInfo = infos.Values.FirstOrDefault(v => v.Name == "rock_shoreline_big") ?? infos.Values.FirstOrDefault(v => v.Name.StartsWith("rock_", StringComparison.Ordinal) && v.VegetationType.ToString() == "LargeObjects");
        Log($"Probe22: the rock taken: {rockInfo?.Name}, collider {rockInfo?.ColliderType}, mesh {(rockInfo?.ColliderMesh != null ? rockInfo.ColliderMesh.name : "none")}");
        Log("Probe22: rock " + Made("rock", () =>
        {
            var stone = db.GetItemInfo("Item_Stone_Raw");
            var prefab = Object.Instantiate(stone.spawnObject, holder.transform);
            prefab.name = "Mjolnir_Rock22";
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(r);
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(f);
            foreach (var l in prefab.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(l);
            foreach (var c in prefab.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var body in prefab.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            foreach (var d in prefab.GetComponentsInChildren<DynamicItemObject>(true)) Object.DestroyImmediate(d);
            AddModel(prefab, rockInfo?.VegetationPrefab);
            if (rockInfo?.ColliderMesh != null) prefab.AddComponent<MeshCollider>().sharedMesh = rockInfo.ColliderMesh;
            foreach (var item in prefab.GetComponentsInChildren<ItemObject>(true)) item.interactions = new Il2CppSystem.Collections.Generic.List<Interaction>();
            foreach (var c in prefab.GetComponentsInChildren<PickupInteraction>(true)) Object.DestroyImmediate(c);
            foreach (var c in prefab.GetComponentsInChildren<HarvestInteraction>(true)) Object.DestroyImmediate(c);
            foreach (var c in prefab.GetComponentsInChildren<ItemSpawner>(true)) Object.DestroyImmediate(c);
            return Register(stone, prefab, "Mjolnir_Rock22", 0x4D4A5723);
        }));

        // C. A free spot.
        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 40f, 60f, 80f, 100f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var square = LandscapeArea.Rectangle(at, 30, 16, blend: 0);
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
            Log("Probe22: no free spot");
            yield break;
        }
        var stand = spot - (spot - home).normalized * 18f;
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 12f, -22f);
        camera.Rotation = Quaternion.LookRotation(spot + Vector3.up * 3 - camera.Position, Vector3.up);
        var placed = new Dictionary<string, Vector3>();
        int n = 0;
        foreach (var (what, info) in made)
        {
            var at = spot + new Vector3(-9 + 9 * n++, 0, 0);
            at.y = Landscape.HeightAt(at.x, at.z);
            string spawned;
            try
            {
                var context = ItemObjectSpawnContext.WORLD;
                int quantity = 1;
                var position = at;
                var rotation = Quaternion.Euler(0, 30 * n, 0);
                var scale = Vector3.one;
                var one = info.SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, true);
                spawned = one != null ? $"{one.name}, renderers {one.GetComponentsInChildren<Renderer>(true).Length}" : "null";
            }
            catch (Exception e)
            {
                spawned = string.Join(" | ", e.ToString().Split(new[] { (char)13, (char)10 }, StringSplitOptions.RemoveEmptyEntries).Take(8));
            }
            placed[what] = at;
            Log($"Probe22: the {what} put down at {P(at)}: {spawned}");
        }
        yield return 5f;
        foreach (var (what, at) in placed)
        {
            var one = Items(at, 1.5f).FirstOrDefault(i => i.Descriptor?.itemInfo?.name?.StartsWith("Mjolnir_", StringComparison.Ordinal) == true);
            var sdk = NaturalObjects.Nearest(at, 1.5f);
            Log($"Probe22: the {what} 5 s later: world item {one?.Descriptor?.itemInfo?.name ?? "none"}, object active {one?.gameObject?.activeInHierarchy}, renderers {(one?.gameObject != null ? one.gameObject.GetComponentsInChildren<Renderer>().Length : 0)}, colliders {(one?.gameObject != null ? one.gameObject.GetComponentsInChildren<Collider>().Length : 0)}, interactions {one?.gameObject?.GetComponentInChildren<ItemObject>(true)?.interactions?.Count}; SDK {sdk?.ToString() ?? "none"}, gather {sdk?.GatherCount}");
        }
        Shot("probe22-placed");
        yield return AfterShot;

        // D. The willow's tree part felled by one blow, its model hidden and the game's trunk laid where it stood.
        if (placed.TryGetValue("willow", out var willowAt) && Items(willowAt, 1.5f).FirstOrDefault(i => i.Descriptor?.itemInfo?.name == "Mjolnir_Tree_Willow22") is { } willow && willow.gameObject != null)
        {
            var harvest = willow.gameObject.GetComponentInChildren<HarvestInteraction>(true);
            var dealer = character.GetComponentInChildren<MeleeManager>(true)?.TryCast<IDamageDealer>();
            var data = new DamageData
            {
                damageDealer = dealer,
                damageReceiver = harvest.TryCast<IDamageReceiver>(),
                baseDamage = harvest.GetHarvestHitPoints() + 1,
                damageMultiplier = 1,
                damageFlags = DamageFlags.IgnoreHarvestTool,
                hitPosition = harvest.transform.position + Vector3.up,
                hitDirection = Vector3.forward,
            };
            string blow = Try(() => { harvest.TakeDamage(data); return ""; });
            yield return 1f;
            Log($"Probe22: the willow's tree part: {blow} flags {data.damageFlags}, piece now {Try(() => harvest.GetCurrentPieceIndex())}, points {Try(() => harvest.GetHarvestHitPoints())}, destroyed {willow.Destroyed}, colliders active [{string.Join(", ", willow.gameObject.GetComponentsInChildren<Collider>(true).Select(c => $"{c.name}:{c.enabled}"))}]");
            var model = willow.gameObject.transform.Find("Model");
            Shot("probe22-stump-model-on");
            yield return AfterShot;
            if (model != null)
            {
                var trunkHelper = trunkPrefab?.GetComponentInChildren<SandSailorStudio.Utils.MaterialFloatPropertyHelper>(true);
                string field = trunkHelper != null ? trunkHelper.propertyRenderer.materialFloatPropertyField : "";
                foreach (float value in new[] { 0f, 1f, 2f })
                {
                    if (field.Length == 0) break;
                    foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    {
                        var block = new MaterialPropertyBlock();
                        r.GetPropertyBlock(block);
                        block.SetFloat(field, value);
                        r.SetPropertyBlock(block);
                    }
                    yield return 1f;
                    Shot($"probe22-stump-{field}-{value}");
                    yield return AfterShot;
                }
            }
            string laid = Try(() =>
            {
                var trunk = db.GetItemInfo("Item_Wood_Willow");
                var context = ItemObjectSpawnContext.WORLD;
                int quantity = 1;
                var position = willowAt + Vector3.up * 1.5f;
                var rotation = Quaternion.Euler(0, 40, 85);
                var scale = Vector3.one;
                var one = trunk.SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, false);
                return one != null ? one.name : "null";
            });
            yield return 5f;
            var lying = Items(willowAt, 12f).FirstOrDefault(i => i.Descriptor?.itemInfo?.name == "Item_Wood_Willow");
            Log($"Probe22: the trunk laid: {laid}; 5 s later {(lying != null ? $"at {P(lying.GetPosition())}, harvest {lying.gameObject?.GetComponentInChildren<HarvestInteraction>(true) != null}, piece {Try(() => lying.gameObject?.GetComponentInChildren<HarvestInteraction>(true)?.GetCurrentPieceIndex())}" : "none")}");
            Shot("probe22-felled");
            yield return AfterShot;
        }

        // E. The bush gathered.
        if (placed.TryGetValue("bush", out var bushAt) && NaturalObjects.Nearest(bushAt, 1.5f) is { } bush && bush.AssetName.StartsWith("Mjolnir_", StringComparison.Ordinal))
        {
            int count = bush.GatherCount;
            int got = bush.Gather(1);
            yield return 3f;
            Log($"Probe22: the bush: gather count {count}, gathered {got}, now {bush.GatherCount}; loot [{string.Join(", ", NaturalObjects.Around(bushAt, 4).Where(o => o.Kind == NaturalObjectKind.Item && !o.AssetName.StartsWith("Mjolnir_", StringComparison.Ordinal)).Select(o => o.AssetName))}]");
        }

        // F. Saved and loaded again.
        var storage = GameManagers.Get<StorageManager>();
        if (storage != null)
        {
            var task = storage.SaveCurrentSession();
            foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
            Log($"Probe22: saved, {storage.LastSaveOperationCode}");
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
            foreach (var (what, at) in placed)
            {
                var one = Items(at, 1.5f).FirstOrDefault(i => i.Descriptor?.itemInfo?.name?.StartsWith("Mjolnir_", StringComparison.Ordinal) == true);
                Log($"Probe22: after loading again the {what}: {one?.Descriptor?.itemInfo?.name ?? "nothing of ours"}, model active {one?.gameObject?.transform.Find("Model")?.gameObject.activeSelf}, piece {Try(() => one?.gameObject?.GetComponentInChildren<HarvestInteraction>(true)?.GetCurrentPieceIndex())}");
            }
            var back = Overhead(spot);
            back.Position = spot + new Vector3(0, 12f, -22f);
            back.Rotation = Quaternion.LookRotation(spot + Vector3.up * 3 - back.Position, Vector3.up);
            yield return 1f;
            Shot("probe22-reloaded");
            yield return AfterShot;
            back.Dispose();
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
