using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.Pooling;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "models" scenario (autopilot.once containing "models"): what a model of the game is made of, for AssetBundle models of
/// mods. In the main menu: the render pipeline and its shaders, the world objects
/// (<c>spawnObject</c>) of a few items of each kind with every component, mesh, material, shader and texture
/// (shots/models.txt), the flimsy arch's structure and placing preview (shots/arch.txt), then the rope spawned the game's way
/// in the menu's world and dumped as it lives there (shots/rope-spawned.txt), and given back to its pool. Then
/// Mjolnir.ItemsExample's twine spool, whose model comes from the mod's bundle: its world object (shots/spool-world.txt), the
/// spool beside the rope in front of the menu's camera, then in the world (the save loaded: back it up) in its light, the
/// picking highlight's renderers, and two spools given and taken out again.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> ModelsResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        var pipeline = GraphicsSettings.currentRenderPipeline;
        Log($"Models: render pipeline {pipeline?.GetIl2CppType().FullName ?? "none"} '{pipeline?.name}', quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', " +
            $"HDRP/Lit {(Shader.Find("HDRP/Lit") is { } lit ? $"found ({lit.name}, {lit.passCount} passes)" : "not found")}");

        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        if (db == null)
        {
            Log("Models: no item database in the main menu");
            yield break;
        }
        var items = Items(db).ToList();
        var chosen = new List<ItemInfo>();
        foreach (string name in new[] { "Item_Materials_BasicRope", "Item_Wood_Sticks", "Item_Tools_WoodHammer" })
        {
            if (db.GetItemInfo(name) is { } info) chosen.Add(info);
        }
        // One of each kind of item with a world object: a weapon, a ranged weapon, something worn, something eaten, a plant.
        foreach (string kind in new[] { "WeaponizedItemInfo", "RangedWeaponInfo", "WearableItemInfo", "ConsumableInfo", "PlantableItemInfo", "ContainerInfo" })
        {
            if (items.FirstOrDefault(i => i.GetIl2CppType().Name == kind && i.spawnObject != null && !chosen.Contains(i)) is { } info) chosen.Add(info);
        }

        var text = new StringBuilder();
        var shaders = new SortedDictionary<string, int>();
        Log($"Models: {items.Count(i => i.spawnObject != null)} of {items.Count} items have a world object; dumping {string.Join(", ", chosen.Select(i => i.name))}");
        foreach (var info in chosen)
        {
            var model = info.spawnObject;
            text.Append($"=== {info.name} ({info.GetIl2CppType().Name}, id {info.id})\n");
            if (model == null)
            {
                text.Append("no spawnObject\n\n");
                continue;
            }
            text.Append($"spawnObject '{model.name}', an asset {!model.scene.IsValid()}, layer {LayerMask.LayerToName(model.layer)}, active {model.activeSelf}, " +
                        $"bounds {Bounds(model)}\n");
            ModelDump.Walk(model.transform, 0, text, shaders);
            text.Append('\n');
        }
        text.Append("=== shaders used: ").Append(string.Join(", ", shaders.Select(s => $"{s.Key} x{s.Value}"))).Append('\n');
        File.WriteAllText(Path.Combine(ShotsDir, "models.txt"), text.ToString());
        Log($"Models: shaders of those models {string.Join(", ", shaders.Select(s => $"{s.Key} x{s.Value}"))}");

        var arch = db.GetItemInfo("Item_Decoration_ArchFlimsy")?.TryCast<NodeTemplate>();
        if (arch != null)
        {
            var archText = new StringBuilder();
            var archShaders = new SortedDictionary<string, int>();
            archText.Append($"=== nodeStructure '{arch.nodeStructure?.name}' bounds {(arch.nodeStructure == null ? "-" : Bounds(arch.nodeStructure.gameObject))}\n");
            if (arch.nodeStructure != null) ModelDump.Walk(arch.nodeStructure.transform, 0, archText, archShaders);
            archText.Append($"\n=== nodePreview '{arch.nodePreview?.name}'\n");
            if (arch.nodePreview != null) ModelDump.Walk(arch.nodePreview.transform, 0, archText, archShaders);
            archText.Append("\n=== shaders used: ").Append(string.Join(", ", archShaders.Select(s => $"{s.Key} x{s.Value}"))).Append('\n');
            File.WriteAllText(Path.Combine(ShotsDir, "arch.txt"), archText.ToString());
            Log($"Models: the arch's structure '{arch.nodeStructure?.name}' and preview '{arch.nodePreview?.name}' dumped; shaders {string.Join(", ", archShaders.Keys)}");
        }

        // The rope as the game spawns it, in the menu's world: what the pool and the spawn add to the prefab.
        var rope = db.GetItemInfo("Item_Materials_BasicRope");
        var camera = Camera.main;
        if (rope != null && camera != null)
        {
            var context = ItemObjectSpawnContext.WORLD;
            int quantity = 1;
            var position = camera.transform.position + camera.transform.forward * 3f;
            var rotation = Quaternion.identity;
            var scale = Vector3.one;
            GameObject? spawned = null;
            try
            {
                spawned = rope.SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, false);
            }
            catch (Exception e)
            {
                Log($"Models: spawning the rope failed: {e.Message}");
            }
            yield return 1f;
            if (spawned != null)
            {
                var spawnedText = new StringBuilder();
                spawnedText.Append($"=== spawned '{spawned.name}' in scene '{spawned.scene.name}', the prefab's own object {spawned.Pointer == rope.spawnObject.Pointer}, parent '{spawned.transform.parent?.name}'\n");
                ModelDump.Walk(spawned.transform, 0, spawnedText, new SortedDictionary<string, int>());
                File.WriteAllText(Path.Combine(ShotsDir, "rope-spawned.txt"), spawnedText.ToString());
                Log($"Models: the rope spawned as '{spawned.name}' at {position}, components {string.Join(", ", spawned.GetComponents<Component>().Select(c => c.GetIl2CppType().Name))}");
                PrefabPool.Release(spawned);
            }
        }

        // ItemsExample's twine spool with its model from the mod's bundle: its world object, then the spool in front of the
        // camera beside the rope.
        var spool = db.GetItemInfo(SpoolAsset);
        if (spool?.spawnObject != null && camera != null)
        {
            var world = spool.spawnObject;
            var spoolText = new StringBuilder();
            spoolText.Append($"=== {spool.name}: spawnObject '{world.name}', an asset {!world.scene.IsValid()}, bounds {Bounds(world)}\n");
            ModelDump.Walk(world.transform, 0, spoolText, new SortedDictionary<string, int>());
            File.WriteAllText(Path.Combine(ShotsDir, "spool-world.txt"), spoolText.ToString());
            var renderer = world.GetComponentsInChildren<Renderer>(true).FirstOrDefault();
            var gameLit = Shader.Find("HDRP/Lit");
            Log($"Models: the spool's world object '{world.name}' (the rope's {world.name == rope?.spawnObject?.name}), model {world.transform.Find("resource_rope1/Model") != null || world.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Model")}, " +
                $"bounds {Bounds(world)}, its shader '{renderer?.sharedMaterial?.shader?.name}' the game's own {renderer?.sharedMaterial?.shader?.Pointer == gameLit?.Pointer}, " +
                $"colliders {string.Join("; ", world.GetComponentsInChildren<BoxCollider>(true).Select(b => string.Create(CultureInfo.InvariantCulture, $"{b.name} {b.size.x:0.###}×{b.size.y:0.###}×{b.size.z:0.###}")))}");
            // The spool beside the rope, as the game spawns them, in front of the menu's camera.
            var twine = db.GetItemInfo(TwineAsset);
            string loreKey = twine?.GetKey("lore") ?? "-";
            Log($"Models: the twine's world object '{twine?.spawnObject?.name}' with a model {twine?.spawnObject?.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Model")}, bounds {(twine?.spawnObject == null ? "-" : Bounds(twine.spawnObject))}; " +
                $"its lore {loreKey}: '{Mjolnir.SDK.Localization.Get(loreKey)}'");
            var shown = Showcase(twine == null ? [spool, rope!] : [twine, spool, rope!],
                camera.transform.position + camera.transform.forward * 0.9f - camera.transform.up * 0.08f, camera.transform.right, 0.26f);
            yield return 2f;
            Log($"Models: {shown.Count} items in front of the menu's camera");
            Shot("models-menu");
            yield return AfterShot;
            foreach (var one in shown) PrefabPool.Release(one);
        }
        else
        {
            Log($"Models: ItemsExample's spool is {(spool == null ? "not in the database" : "without a world object")}");
            yield break;
        }

        // In the world: the spool in its light, its picking highlight, the inventory. Loading the save writes it: back it up.
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 5f;
        db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var worldSpool = db?.GetItemInfo(SpoolAsset);
        var worldRope = db?.GetItemInfo("Item_Materials_BasicRope");
        var worldCamera = Camera.main;
        if (worldSpool == null || worldRope == null || worldCamera == null)
        {
            Log($"Models: in the world the spool {worldSpool != null}, the rope {worldRope != null}, a camera {worldCamera != null}");
        }
        else
        {
            Log($"Models: in the world the spool's world object is the menu's {worldSpool.spawnObject?.Pointer == spool.spawnObject?.Pointer}");
            var worldTwine = db!.GetItemInfo(TwineAsset);

            // Dropped as the game drops them, on the paved path where the world loads: where each comes to rest, its
            // colliders' and its look's lowest points, what a ray finds under it, the terrain's height, and the look of the
            // path itself there (renderers round the place: a path is drawn above the ground the physics knows?).
            var start = Mjolnir.SDK.Player.LocalPlayer.Position;
            var forward = Vector3.ProjectOnPlane(worldCamera.transform.forward, Vector3.up).normalized;
            var dropped = new List<GameObject>();
            int n = 0;
            foreach (var info in new[] { worldRope, worldTwine, worldSpool, db.GetItemInfo("Item_Wood_Sticks") })
            {
                if (info == null) continue;
                var context = ItemObjectSpawnContext.WORLD;
                int quantity = 1;
                var position = start + forward * 4.5f + worldCamera.transform.right * (1.4f + 0.5f * n++) + Vector3.up * 0.4f;
                var rotation = Quaternion.identity;
                var scale = Vector3.one;
                var one = info.SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, true);
                if (one != null) dropped.Add(one);
            }
            yield return 5f;
            foreach (var one in dropped)
            {
                var place = one.transform.position;
                float look = one.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).Select(r => r.bounds.min.y).DefaultIfEmpty(float.NaN).Min();
                float body = one.GetComponentsInChildren<Collider>(false).Where(c => !c.isTrigger).Select(c => c.bounds.min.y).DefaultIfEmpty(float.NaN).Min();
                var hits = Physics.RaycastAll(place + Vector3.up * 2f, Vector3.down, 6f, -1, QueryTriggerInteraction.Ignore)
                                  .Where(h => !h.collider.transform.IsChildOf(one.transform)).OrderBy(h => -h.point.y)
                                  .Select(h => string.Create(CultureInfo.InvariantCulture, $"{h.collider.name} ({LayerMask.LayerToName(h.collider.gameObject.layer)}) at {h.point.y:0.###}"));
                var terrain = Terrain.activeTerrains.FirstOrDefault(t => Contains(t, place));
                float ground = terrain == null ? float.NaN : terrain.SampleHeight(place) + terrain.transform.position.y;
                var under = Object.FindObjectsOfType<MeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && !r.transform.IsChildOf(one.transform)
                                                                               && r.bounds.min.x <= place.x && r.bounds.max.x >= place.x && r.bounds.min.z <= place.z && r.bounds.max.z >= place.z
                                                                               && r.bounds.max.y > ground - 0.5f && r.bounds.max.y < ground + 0.6f && r.bounds.size.y < 3f)
                                                         .Take(6)
                                                         .Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.name} ({LayerMask.LayerToName(r.gameObject.layer)}, collider {r.GetComponent<Collider>() != null}) top {r.bounds.max.y:0.###}"));
                Log(string.Create(CultureInfo.InvariantCulture, $"Models: dropped {one.name} rests at {place.y:0.###}, {Vector3.Distance(place, start):0.##} m from the player, on the screen at {worldCamera.WorldToScreenPoint(place).x:0},{worldCamera.WorldToScreenPoint(place).y:0}: its look from {look:0.###}, {look - body:0.###} above its body (from {body:0.###}), the terrain at {ground:0.###}; ray: {string.Join(", ", hits)}; renderers there: {string.Join(", ", under)}"));
            }
            Shot("models-dropped");
            yield return AfterShot;
            foreach (var one in dropped) PrefabPool.Release(one);

            // On the ground in front of the player, where the camera looks.
            var player = Mjolnir.SDK.Player.LocalPlayer.Position;
            var ahead = Vector3.ProjectOnPlane(worldCamera.transform.forward, Vector3.up).normalized;
            var shown = Showcase(worldTwine == null ? [worldSpool, worldRope] : [worldTwine, worldSpool, worldRope],
                player + ahead * 1.3f + Vector3.up * 0.15f, worldCamera.transform.right, 0.45f);
            yield return 2f;
            foreach (var one in shown)
            {
                var pickables = one.GetComponentsInChildren<SSSGame.Render.Pickable>(true);
                Log($"Models: '{one.name}' awake {one.activeInHierarchy}, pickables {pickables.Length}: " +
                    string.Join("; ", pickables.Select(p => $"objects [{string.Join(", ", Names(p.pickableObjects))}] renderers {p._pickableRendererList?.Count ?? -1}")));
            }
            Shot("models-world");
            yield return AfterShot;
            foreach (var one in shown) PrefabPool.Release(one);

            var inventory = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.Inventory?.GetItemCollection();
            if (inventory != null)
            {
                int before = inventory.GetItemQuantity(worldSpool);
                int given = Mjolnir.SDK.Content.Items.Give(SpoolAsset, 2);
                Log($"Models: Give gave {given} spools, the inventory holds {inventory.GetItemQuantity(worldSpool)} ({before + 2} expected)");
                inventory.RemoveItems(worldSpool, given);
                Log($"Models: the spools given taken out again, {inventory.GetItemQuantity(worldSpool)} left ({before} expected)");
            }
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // Items spawned the game's way in a row round a place, along right, held still (kinematic) for a screenshot.
    private static List<GameObject> Showcase(ItemInfo[] items, Vector3 middle, Vector3 right, float spacing)
    {
        var shown = new List<GameObject>();
        for (int i = 0; i < items.Length; i++)
        {
            var context = ItemObjectSpawnContext.WORLD;
            int quantity = 1;
            var position = middle + right * ((i - (items.Length - 1) / 2f) * spacing);
            var rotation = Quaternion.LookRotation(right) * Quaternion.Euler(20f, 30f, 0f);
            var scale = Vector3.one;
            try
            {
                var one = items[i].SpawnObject(ref context, ref quantity, ref position, ref rotation, ref scale, null, false);
                if (one == null) continue;
                foreach (var body in one.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
                shown.Add(one);
            }
            catch (Exception e)
            {
                Log($"Models: spawning {items[i].name} failed: {e.Message}");
            }
        }
        return shown;
    }

    private static bool Contains(Terrain terrain, Vector3 place)
    {
        var origin = terrain.transform.position;
        var size = terrain.terrainData.size;
        return place.x >= origin.x && place.x <= origin.x + size.x && place.z >= origin.z && place.z <= origin.z + size.z;
    }

    private static IEnumerable<string> Names(Il2CppSystem.Collections.Generic.List<GameObject>? objects)
    {
        if (objects == null) yield break;
        for (int i = 0; i < objects.Count; i++) yield return objects[i] == null ? "-" : objects[i].name;
    }

    private static string Bounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return "no renderer";
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return string.Create(CultureInfo.InvariantCulture, $"size {bounds.size.x:0.##}×{bounds.size.y:0.##}×{bounds.size.z:0.##} centre {bounds.center.x:0.##},{bounds.center.y:0.##},{bounds.center.z:0.##}");
    }
}

/// <summary>A 3D object's hierarchy with its components: meshes, renderers with their materials and shaders, colliders, LODs.</summary>
internal static class ModelDump
{
    private static readonly string[] s_textures = ["_BaseColorMap", "_NormalMap", "_MaskMap", "_EmissiveColorMap", "_MainTex", "_DetailMap"];

    public static void Walk(Transform t, int depth, StringBuilder text, SortedDictionary<string, int> shaders)
    {
        if (depth > 12) return;
        string indent = new(' ', depth * 2);
        var components = t.GetComponents<Component>().Where(c => c != null).ToList();
        text.Append(indent).Append(t.name);
        if (!t.gameObject.activeSelf) text.Append(" (off)");
        text.Append(string.Create(CultureInfo.InvariantCulture, $" layer {LayerMask.LayerToName(t.gameObject.layer)} pos {t.localPosition.x:0.###},{t.localPosition.y:0.###},{t.localPosition.z:0.###} rot {t.localEulerAngles.x:0.#},{t.localEulerAngles.y:0.#},{t.localEulerAngles.z:0.#} scale {t.localScale.x:0.###},{t.localScale.y:0.###},{t.localScale.z:0.###}"));
        text.Append(" [").Append(string.Join(", ", components.Select(c => c.GetIl2CppType().Name).Where(n => n != "Transform"))).Append("]\n");
        foreach (var c in components)
        {
            string? detail;
            try
            {
                detail = Detail(c, shaders);
            }
            catch (Exception e)
            {
                detail = $"{c.GetIl2CppType().Name}: {e.GetType().Name} {e.Message}";
            }
            if (detail != null) text.Append(indent).Append("  · ").Append(detail).Append('\n');
        }
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, text, shaders);
    }

    private static string? Detail(Component c, SortedDictionary<string, int> shaders)
    {
        var invariant = CultureInfo.InvariantCulture;
        if (c.TryCast<MeshFilter>() is { } filter)
        {
            var mesh = filter.sharedMesh;
            return mesh == null ? "mesh none" : string.Create(invariant, $"mesh '{mesh.name}' {mesh.vertexCount} vertices, {mesh.subMeshCount} submeshes, readable {mesh.isReadable}, bounds {mesh.bounds.size.x:0.##}×{mesh.bounds.size.y:0.##}×{mesh.bounds.size.z:0.##}");
        }
        if (c.TryCast<SkinnedMeshRenderer>() is { } skinned)
            return $"skinned mesh '{skinned.sharedMesh?.name}' bones {skinned.bones?.Length ?? 0}; {Materials(skinned, shaders)}";
        if (c.TryCast<Renderer>() is { } renderer)
            return $"{renderer.GetIl2CppType().Name} enabled {renderer.enabled}, shadows {renderer.shadowCastingMode}, layer mask {renderer.renderingLayerMask}; {Materials(renderer, shaders)}";
        if (c.TryCast<LODGroup>() is { } lod)
        {
            var lods = lod.GetLODs();
            return $"LODGroup {lods.Length} levels: " + string.Join("; ", lods.Select((l, i) => string.Create(invariant, $"{i} at {l.screenRelativeTransitionHeight:0.###} ({l.renderers?.Length ?? 0} renderers: {string.Join(", ", Renderers(l.renderers).Select(r => r.name))})")));
        }
        if (c.TryCast<BoxCollider>() is { } box)
            return string.Create(invariant, $"BoxCollider trigger {box.isTrigger} centre {box.center.x:0.##},{box.center.y:0.##},{box.center.z:0.##} size {box.size.x:0.##}×{box.size.y:0.##}×{box.size.z:0.##}");
        if (c.TryCast<MeshCollider>() is { } meshCollider)
            return $"MeshCollider trigger {meshCollider.isTrigger} convex {meshCollider.convex} mesh '{meshCollider.sharedMesh?.name}'";
        if (c.TryCast<Collider>() is { } collider)
            return $"{collider.GetIl2CppType().Name} trigger {collider.isTrigger}";
        if (c.TryCast<Rigidbody>() is { } body)
            return string.Create(invariant, $"Rigidbody mass {body.mass:0.##} kinematic {body.isKinematic} gravity {body.useGravity}");
        return null;
    }

    private static IEnumerable<Renderer> Renderers(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Renderer>? renderers)
    {
        if (renderers == null) yield break;
        foreach (var r in renderers)
        {
            if (r != null) yield return r;
        }
    }

    private static string Materials(Renderer renderer, SortedDictionary<string, int> shaders)
    {
        var parts = new List<string>();
        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null)
            {
                parts.Add("(no material)");
                continue;
            }
            string shader = material.shader?.name ?? "-";
            shaders[shader] = shaders.GetValueOrDefault(shader) + 1;
            var textures = s_textures.Where(p => material.HasProperty(p)).Select(p => (p, material.GetTexture(p))).Where(t => t.Item2 != null).Select(t => $"{t.p}={t.Item2.name}");
            string keywords = material.shaderKeywords is { } words ? string.Join(" ", words) : "";
            parts.Add($"material '{material.name}' shader '{shader}' queue {material.renderQueue} keywords [{keywords}] {string.Join(" ", textures)}");
        }
        return string.Join(" | ", parts);
    }
}
