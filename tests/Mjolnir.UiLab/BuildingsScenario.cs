using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using TMPro;
using UnityEngine;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "buildings" scenario (autopilot.once containing "buildings", with -Examples): Mjolnir.SDK.Content's buildings,
/// with Mjolnir.ItemsExample's twine arch (the game's flimsy arch with a cost of its own) and this mod's lab hut (the
/// builder's hut, three construction stages, three sticks; added only for this scenario). In the main menu every
/// structure template is dumped (shots/structures.txt), and the arch's place in the database and the start blueprints
/// and the costs the build menu shows are logged. In the world: whether the player knows both; the arch in the build
/// menu's category, selected, with the materials its preview shows; both placed unfinished in front of the player
/// (their build sites' parts: the cost on the first, the others free); the hut taken away, the arch finished and what
/// dismantling it gives back; the save written, the game back in the main menu and the save loaded again: the arch
/// still standing with its template; then it is taken away. It writes the user's save: back it up first, put it back
/// after.
/// </summary>
internal static partial class Autopilot
{
    private const string ArchAsset = "Item_mjolnir.itemsexample_twine_arch";
    private const string HutAsset = "Item_mjolnir.test.uilab_lab_hut";

    private static IEnumerator<float> BuildingsScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var start = ListNamed("StartBlueprints");
        var arch = db?.GetItemInfo(ArchAsset)?.TryCast<NodeTemplate>();
        var hut = db?.GetItemInfo(HutAsset)?.TryCast<NodeTemplate>();
        var archBase = db?.GetItemInfo("Item_Decoration_ArchFlimsy")?.TryCast<NodeTemplate>();
        if (db == null || start == null || arch == null || hut == null || archBase == null)
        {
            Log($"Buildings: missing (database {db != null}, start blueprints {start != null}, arch {arch != null}, hut {hut != null}, the game's arch {archBase != null})");
            yield break;
        }
        DumpStructures(db, start);
        Log($"Buildings: the arch '{arch.Name}' ({arch.GetIl2CppType().Name}) id {arch.id}, index {arch.databaseIndex}, found by id {db.GetItemInfoFromID(arch.id)?.name == ArchAsset}, " +
            $"description '{arch.Description}', icon {arch.icon?.rect.width}x{arch.icon?.rect.height} (128x128 expected), category {arch.category?.name}, structure '{arch.nodeStructure?.name}', " +
            $"in the start blueprints {Entries(start).Any(e => e.Pointer == arch.Pointer)}, unlocked {SafeTemplateUnlocked(arch)}");
        Log($"Buildings: costs shown: the arch {Cost(arch)} (sticks x5, twine x4 expected), the game's arch {Cost(archBase)}, the hut {Cost(hut)} (sticks x3 expected), " +
            $"the game's hut {Cost(db.GetItemInfo("Item_Structures_BuilderHutL1")!.Cast<StructureTemplate>())}");

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 4f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var inventory = character?.Inventory?.GetItemCollection();
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        if (character == null || inventory == null || settlement == null)
        {
            Log($"Buildings: in the world the player {character != null}, inventory {inventory != null}, settlement {settlement != null}");
            yield break;
        }
        Log($"Buildings: the player knows the arch {inventory.HasItem(arch)} and the hut {inventory.HasItem(hut)} (true and true expected)");

        // The build menu at the arch's category, the arch selected: its preview shows its materials.
        Menu.Show<BuildMenu>(null);
        yield return 2f;
        if (Menu.GetInstance<BuildMenu>() is { } build && Menu.IsOpen<BuildMenu>())
        {
            var buttons = build.GetComponentsInChildren<TabButtonCategory>(true);
            var category = buttons.FirstOrDefault(b => b.categoryInfos != null && Enumerable.Range(0, b.categoryInfos.Count)
                .Any(i => b.categoryInfos[i] != null && arch.category != null && b.categoryInfos[i].Pointer == arch.category.Pointer));
            if (category == null)
            {
                Log($"Buildings: no category button for {arch.category?.name}; buttons: {string.Join("; ", buttons.Select(b => $"{b.name} [{string.Join(", ", Enumerable.Range(0, b.categoryInfos?.Count ?? 0).Select(i => b.categoryInfos![i]?.name))}]"))}");
            }
            else
            {
                Click(category.gameObject);
                yield return 2f;
            }
            var panels = build.GetComponentsInChildren<ItemThumbnailPanel>(false).Where(p => p.ItemInfo != null).ToList();
            var panel = panels.FirstOrDefault(p => p.ItemInfo.Pointer == arch.Pointer);
            Log($"Buildings: build menu category '{category?.CategoryDisplayName ?? "none"}': {panels.Count} panels, the arch's {panel != null}, the game's arch's {panels.Any(p => p.ItemInfo.Pointer == archBase.Pointer)}");
            if (panel != null)
            {
                Click(panel.gameObject);
                yield return 1.5f;
                var preview = build.GetComponentInChildren<BuildPreviewTabPage>(false);
                var materials = preview == null ? [] : preview.materialsPanel.GetComponentsInChildren<ItemThumbnailPanel>(false).Where(p => p.ItemInfo != null)
                    .Select(p => $"{p.ItemInfo.name} '{string.Join(" ", p.GetComponentsInChildren<TMP_Text>(false).Select(t => t.text))}'").ToList();
                Log($"Buildings: the arch selected: preview '{preview?.previewPanel?.ItemInfo?.name}', materials {string.Join("; ", materials)} (sticks and twine expected)");
            }
            Shot("buildings-menu");
            yield return AfterShot;
            build.Close();
            yield return 1.5f;
        }

        // Both placed unfinished in front of the player: the cost on the first part, the other parts free.
        var body = character.transform;
        var forward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
        var side = Vector3.Cross(Vector3.up, forward);
        var archAt = GroundPoint(body.position + forward * 6f) ?? body.position + forward * 6f;
        var hutAt = GroundPoint(body.position + forward * 12f + side * 8f) ?? body.position + forward * 12f + side * 8f;
        var placedArch = Place(settlement, arch, archAt, forward);
        var placedHut = Place(settlement, hut, hutAt, forward);
        yield return 2.5f;
        Log($"Buildings: the arch placed: '{placedArch?.name}' template '{placedArch?.Template?.name}' id {placedArch?.TemplateID}; {SiteOf(placedArch)}");
        Log($"Buildings: the hut placed: '{placedHut?.name}' template '{placedHut?.Template?.name}'; {SiteOf(placedHut)}");
        Shot("buildings-sites");
        yield return AfterShot;
        placedHut?.Dismantle(true);
        if (placedArch?.buildSite == null)
        {
            Log("Buildings: no arch to finish");
            yield break;
        }
        placedArch.buildSite.FinishBuild(-1);
        yield return 2f;
        Log($"Buildings: the arch finished: active {placedArch.gameObject.activeInHierarchy}, build site {placedArch.buildSite != null}; dismantling gives back {Manifest(SafeDismantle(placedArch))}");
        Shot("buildings-built");
        yield return AfterShot;

        // Saved, back in the main menu, loaded again: the arch is still there, by its id.
        var storage = GameManagers.Get<StorageManager>();
        if (storage == null)
        {
            Log("Buildings: no storage manager");
            yield break;
        }
        var task = storage.SaveCurrentSession();
        foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
        Log($"Buildings: saved, {storage.LastSaveOperationCode}");
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
        yield return 5f;
        var standing = Object.FindObjectsOfType<Structure>().Where(s => s.TemplateID == arch.id).ToList();
        Log($"Buildings: after loading the save again {standing.Count} arch(es) standing (1 expected): " +
            string.Join("; ", standing.Select(s => $"'{s.name}' template '{s.Template?.name}' '{s.Template?.Cast<ItemInfo>().Name}' at {P(s.transform.position)}, {Vector3.Distance(s.transform.position, archAt):0.0} m from where it was put")));
        Shot("buildings-reloaded");
        yield return AfterShot;
        foreach (var structure in standing) structure.Dismantle(true);
        yield return 2f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static Structure? Place(Settlement settlement, StructureTemplate template, Vector3 at, Vector3 forward)
    {
        var placement = new PointPlacement();
        placement.position = at;
        placement.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        try
        {
            return settlement.CreateStructure(template, placement, null, StructureTemplate.Options.None, 0);
        }
        catch (System.Exception e)
        {
            Log($"Buildings: CreateStructure('{template.name}') failed: {e}");
            return null;
        }
    }

    private static ItemManifest? SafeDismantle(Structure structure)
    {
        try { return structure.GetDismantleManifest(); } catch (System.Exception e) { Log($"Buildings: GetDismantleManifest failed: {e.Message}"); return null; }
    }

    // What a structure's build site asks for, layer by layer: each part's config, cost and whether it is supplied.
    private static string SiteOf(Structure? structure)
    {
        var site = structure?.buildSite;
        if (site == null) return "no build site";
        var layers = site.layers;
        var text = new StringBuilder($"build site at layer {site.GetCurrentLayerIndex()} of {layers?.Count ?? 0}");
        for (int i = 0; layers != null && i < layers.Count; i++)
        {
            var parts = layers[i]?.parts;
            text.Append($"; layer {i}:");
            for (int k = 0; parts != null && k < parts.Count; k++)
            {
                var part = parts[k];
                var container = part?.container;
                text.Append($" [{part?.name}: config '{container?.itemConfig?.name}', needs {Manifest(container?._manifest)}, supplied {part?.IsSupplied()}]");
            }
        }
        return text.ToString();
    }

    private static string Manifest(ItemManifest? manifest)
    {
        if (manifest == null) return "-";
        var items = new List<string>();
        var each = manifest._items.GetEnumerator();
        while (each.MoveNext())
        {
            var pair = each._current; // the property Current reads as ambiguous in an IDE: the interop has two of that name
            items.Add($"{pair.Key?.name} x{pair.Value}");
        }
        return items.Count == 0 ? "nothing" : string.Join(", ", items);
    }

    // The cost the build menu shows for a template.
    private static string Cost(StructureTemplate template)
    {
        try
        {
            var manifest = new ItemManifest();
            int multiplier = 1;
            template.FillManifest(manifest, ref multiplier);
            return Manifest(manifest);
        }
        catch (System.Exception e)
        {
            return $"<{e.GetType().Name}>";
        }
    }

    private static string SafeTemplateUnlocked(NodeTemplate template)
    {
        try { return template.IsUnlocked().ToString(); } catch (System.Exception e) { return $"<{e.GetType().Name}>"; }
    }

    private static ItemInfoList? ListNamed(string name)
    {
        var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<ItemInfoList>());
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i]?.name == name) return all[i].TryCast<ItemInfoList>();
        }
        return null;
    }

    private static IEnumerable<ItemInfo> Entries(ItemInfoList list)
    {
        var items = list.itemInfoList;
        for (int i = 0; items != null && i < items.Count; i++)
        {
            if (items[i] != null) yield return items[i];
        }
    }

    // Every structure template: what it is, costs, builds and is called (shots/structures.txt).
    private static void DumpStructures(ItemInfoDatabase db, ItemInfoList start)
    {
        var text = new StringBuilder();
        int count = 0;
        foreach (var info in Items(db))
        {
            var template = info.TryCast<StructureTemplate>();
            if (template == null) continue;
            count++;
            var node = template.TryCast<NodeTemplate>();
            var site = node?.nodeStructure == null ? null : node.nodeStructure.GetComponentInChildren<BuildSite>(true);
            text.Append($"{template.id}\t{template.name}\t{template.GetIl2CppType().Name}\tcat {template.category?.name}\tstart {Entries(start).Any(e => e.Pointer == template.Pointer)}\t")
                .Append($"cost {Cost(template)}\tstructure {node?.nodeStructure?.name}\tsite layers {site?.layers?.Count}\ttool {template.placementTool?.GetIl2CppType().Name}\t")
                .Append($"unique {template.uniqueBuilding}\tupgrades {template.structureUpgrades?.Count ?? 0}\tkey {SafeKey(template)} -> '{SafeName(template)}'\tdesc '{Short(template.Description)}'\n");
        }
        File.WriteAllText(Path.Combine(ShotsDir, "structures.txt"), text.ToString());
        Log($"Buildings: dumped {count} structure templates to structures.txt");
    }
}
