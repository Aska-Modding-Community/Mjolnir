using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe17" scenario: a kind of the world turned into a world item made in code. It copies a world item's
/// kind of the game (a lying trunk's), gives the copy the object of a kind of the world (a willow, an oak, a berry bush,
/// a stone clump) — kept whole, or without its biome object — and puts each down as the game drops loot, then reads
/// whether the game took it as a world item, what draws it, and whether it is harvested or gathered. It changes the
/// save's world: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe17()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        if (character == null || db == null || !Landscape.IsAvailable)
        {
            Log("Probe17: no world");
            yield break;
        }
        var home = character.transform.position;
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();

        Vector3 spot = default;
        bool found = false;
        foreach (float distance in new[] { 40f, 60f, 80f, 100f })
        {
            for (int turn = 0; turn < 360 && !found; turn += 30)
            {
                var at = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                if (!Landscape.TryGetHeight(at.x, at.z, out at.y)) continue;
                var square = LandscapeArea.Rectangle(at, 44, 14, blend: 0);
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
            Log("Probe17: no free spot");
            yield break;
        }
        var stand = spot - (spot - home).normalized * 16f;
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(stand.x, stand.z);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 3f;
        var camera = Overhead(spot);
        camera.Position = spot + new Vector3(0, 18f, -22f);
        camera.Rotation = Quaternion.LookRotation(spot - camera.Position, Vector3.up);

        var holder = new GameObject("Mjolnir_Probe17_Prefabs");
        holder.SetActive(false);
        Object.DontDestroyOnLoad(holder);
        var baseInfo = db.GetItemInfo("Item_Wood_Fir3");
        Log($"Probe17: the base kind {baseInfo?.name} ({baseInfo?.GetIl2CppType().Name}, id {baseInfo?.id}), its object '{(baseInfo?.spawnObject != null ? baseInfo.spawnObject.name : "none")}'");
        if (baseInfo == null)
        {
            yield break;
        }
        string Parts(GameObject o) => string.Join(", ", o.GetComponentsInChildren<Component>(true).Select(c => c != null ? c.GetIl2CppType().Name : "null").Distinct().Take(20));

        var kinds = new[] { "Item_Wood_TreeWillow", "Item_Wood_TreeOak", "Item_Food_BiomeBerries", "Harvest_Stone_BiomeIronDeposit" };
        var placed = new List<(string Name, bool Biome, Vector3 At)>();
        int n = 0;
        foreach (var kind in kinds)
        {
            var source = db.GetItemInfo(kind);
            if (source?.spawnObject == null)
            {
                Log($"Probe17: no '{kind}'");
                continue;
            }
            foreach (bool keepBiome in new[] { true, false })
            {
                var at = spot + new Vector3(-20 + 5.5f * n++, 0, 0);
                at.y = Landscape.HeightAt(at.x, at.z);
                string result = Try(() =>
                {
                    var prefab = Object.Instantiate(source.spawnObject, holder.transform);
                    prefab.name = source.spawnObject.name + (keepBiome ? "_Item" : "_ItemNoBiome");
                    if (!keepBiome)
                        foreach (var b in prefab.GetComponentsInChildren<BiomeObject>(true)) Object.DestroyImmediate(b);
                    var info = Object.Instantiate(baseInfo);
                    info.name = "Mjolnir_Probe_" + prefab.name;
                    info.spawnObject = prefab;
                    var context = ItemObjectSpawnContext.WORLD;
                    int quantity = 1;
                    var position = at;
                    var rotation = Quaternion.identity;
                    var scale = Vector3.one;
                    var one = info.SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, true);
                    return one != null ? $"{one.name}, renderers {one.GetComponentsInChildren<Renderer>(true).Length}, parts [{Parts(one)}]" : "null";
                });
                placed.Add((kind, keepBiome, at));
                Log($"Probe17: '{kind}' {(keepBiome ? "whole" : "without its biome object")} at {P(at)}: {result}");
            }
        }
        yield return 5f;
        foreach (var (name, biome, at) in placed)
        {
            var items = Items(at, 1.5f);
            var sdk = NaturalObjects.Nearest(at, 1.5f);
            var one = items.FirstOrDefault();
            Log($"Probe17: '{name}' {(biome ? "whole" : "no biome")} 5 s later: world items [{string.Join(", ", items.Select(i => i.Descriptor?.itemInfo?.name))}], object '{(one?.gameObject != null ? one.gameObject.name : "none")}', active {one?.gameObject?.activeInHierarchy}, harvest {one?.gameObject?.GetComponentInChildren<HarvestInteraction>(true) != null}, gather {one?.gameObject?.GetComponentInChildren<GatherInteraction>(true) != null}; SDK {sdk?.ToString() ?? "none"}");
        }
        Shot("probe17-placed");
        yield return AfterShot;

        foreach (var (name, biome, at) in placed)
        {
            var sdk = NaturalObjects.Nearest(at, 1.5f);
            if (sdk == null) continue;
            bool harvested = sdk.Harvest();
            int gathered = harvested ? 0 : sdk.Gather(3);
            yield return 4f;
            Log($"Probe17: '{name}' {(biome ? "whole" : "no biome")}: Harvest {harvested}, Gather {gathered}; loot round it [{string.Join(", ", NaturalObjects.Around(at, 8).Where(o => o.Kind == NaturalObjectKind.Item && !o.AssetName.StartsWith("Mjolnir_", StringComparison.Ordinal)).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"))}]");
        }
        Shot("probe17-harvested");
        yield return AfterShot;
        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
