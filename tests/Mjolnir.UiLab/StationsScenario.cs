using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "stations" scenario (autopilot.once containing "stations", with -Examples): Mjolnir.SDK.Content's recipes at the
/// game's crafting stations, with Mjolnir.ItemsExample's twine (based on the rope: by hand and at the workshop) and twine
/// spool (At the workshop of level 0). In the main menu every structure with crafting tables is logged with its tables'
/// recipe lists, upgrades and add-ons, and the lists holding the two recipes. In the world a workshop of level 1 is built
/// in front of the player as the game's auto-builder builds one (Settlement.CreateStructure, then BuildSite.FinishBuild);
/// what its station and table know is logged, its craft menu and task page are opened through the player's interaction
/// agent and logged, a spool is crafted at its table through the game's own end of a craft
/// (CraftInteraction._OnCraftingSuccess) from twine and a stick given for it, then the items are taken out and the
/// workshop is dismantled. The run may write the save: back it up first.
/// </summary>
internal static partial class Autopilot
{
    private const string SpoolAsset = "Item_mjolnir.itemsexample_twine_spool";
    private const string SpoolRecipeAsset = "Item_mjolnir.itemsexample_twine_spool_Recipe";

    private static IEnumerator<float> StationsScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        if (db == null)
        {
            Log("Stations: no item database in the main menu");
            yield break;
        }
        foreach (var info in Items(db))
        {
            var template = info.TryCast<NodeTemplate>();
            CraftInteraction[] tables = template?.nodeStructure == null ? [] : template.nodeStructure.GetComponentsInChildren<CraftInteraction>(true);
            if (template == null || tables.Length == 0) continue;
            var station = template.nodeStructure!.GetComponentInChildren<CraftingStation>(true);
            Log($"Stations: '{template.name}' builds '{template.nodeStructure.name}' (station {(station == null ? "none" : $"'{station.name}' {station.CraftingType}")}), " +
                $"tables {string.Join("; ", tables.Select(t => $"{t.name} '{t.localBlueprintsList?.name}' ({t.localBlueprintsList?.itemInfoList?.Count ?? 0})"))}; " +
                $"upgrades {Names(template.structureUpgrades)}; add-ons {Names(template.structureAddons)}");
        }
        var twineRecipe = db.GetItemInfo(TwineRecipeAsset);
        var spoolRecipe = db.GetItemInfo(SpoolRecipeAsset);
        if (twineRecipe == null || spoolRecipe == null)
        {
            Log($"Stations: ItemsExample's recipes missing (twine {twineRecipe != null}, spool {spoolRecipe != null})");
            yield break;
        }
        Log($"Stations: lists holding the twine recipe: {ListsHolding(twineRecipe)} (StartBlueprints and WorkshopBlueprints_T0/T1/T2 expected, as the rope's: {ListsHolding(db.GetItemInfo("Item_Binders_BasicRope"))})");
        Log($"Stations: lists holding the spool recipe: {ListsHolding(spoolRecipe)} (WorkshopBlueprints_T0/T1/T2 expected, not StartBlueprints)");

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 3f;
        db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var inventory = character?.Inventory?.GetItemCollection();
        var spool = db?.GetItemInfo(SpoolAsset);
        var twine = db?.GetItemInfo(TwineAsset);
        var sticks = db?.GetItemInfo("Item_Wood_Sticks");
        spoolRecipe = db?.GetItemInfo(SpoolRecipeAsset);
        if (db == null || character == null || inventory == null || spool == null || twine == null || sticks == null || spoolRecipe == null)
        {
            Log($"Stations: in the world something is missing (database {db != null}, player {character != null}, inventory {inventory != null}, spool {spool != null}, twine {twine != null}, sticks {sticks != null})");
            yield break;
        }
        yield return 3f; // the SDK gives hand recipes to the player every two seconds
        var twineBlueprint = db.GetItemInfo(TwineRecipeAsset);
        Log($"Stations: the spool '{spool.Name}' id {spool.id}, recipe unlocked {SafeUnlocked(spoolRecipe.Cast<CraftBlueprintInfo>())}; the player knows the twine recipe {twineBlueprint != null && inventory.HasItem(twineBlueprint)} (true expected) " +
            $"and the spool recipe {inventory.HasItem(spoolRecipe)} (false expected: a station's)");

        // A workshop of level 1: the spool was placed at level 0, and reaches level 1 as its upgrade.
        var workshop = db.GetItemInfo("Item_Structures_WorkshopL1")?.TryCast<StructureTemplate>();
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        if (workshop == null || settlement == null)
        {
            Log($"Stations: no workshop template ({workshop != null}) or world settlement ({settlement != null})");
            yield break;
        }
        var body = character.transform;
        var forward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
        var at = body.position + forward * 8f;
        at = GroundPoint(at) ?? at;
        var placement = new PointPlacement();
        placement.position = at;
        placement.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        Structure? built = null;
        try
        {
            built = settlement.CreateStructure(workshop, placement, null, StructureTemplate.Options.None, 0);
            built?.buildSite?.FinishBuild(-1);
        }
        catch (System.Exception e)
        {
            Log($"Stations: building the workshop failed: {e}");
        }
        Log($"Stations: CreateStructure('{workshop.name}') at {P(at)} gave '{built?.name}'");
        if (built == null) yield break;
        yield return 3f;

        var craft = Object.FindObjectsOfType<CraftInteraction>().FirstOrDefault(t => t.localBlueprintsList != null && Vector3.Distance(t.transform.position, at) < 15);
        if (craft == null)
        {
            Log("Stations: no crafting table in the built workshop");
            yield break;
        }
        CraftBlueprint spoolBlueprint = null!;
        bool knowsSpool = craft.KnowsBlueprintForItem(spool, out spoolBlueprint);
        bool knowsTwine = craft.KnowsBlueprintForItem(twine, out CraftBlueprint twineAtTable);
        Log($"Stations: table '{craft.name}' list '{craft.localBlueprintsList.name}' of station '{craft.craftStationHost?.name}': knows the spool {knowsSpool} ({spoolBlueprint?.info?.name}), " +
            $"the twine {knowsTwine} ({twineAtTable?.info?.name}) (true and true expected)");

        var agent = character.GetComponentInChildren<PlayerInteractionAgent>(true) ?? Object.FindObjectsOfType<PlayerInteractionAgent>().FirstOrDefault();
        if (agent == null)
        {
            Log("Stations: no interaction agent of the player");
            yield break;
        }
        var interactor = agent.Cast<IInteractionAgent>();

        // The craft menu, as the player opens it at the table: the interaction starts its session, the menu opens with it.
        bool remote = agent.TryRemoteInteract(craft);
        yield return 2f;
        var session = agent.GetInteractionSession(craft);
        if (!Menu.IsOpen<CraftMenu>() && session != null) Menu.Show<CraftMenu>(new CraftMenu.CraftMenuParameters { session = session });
        yield return 2f;
        Log($"Stations: TryRemoteInteract {remote}, session {session?.GetIl2CppType().Name}, craft menu open {Menu.IsOpen<CraftMenu>()}");
        if (Menu.GetInstance<CraftMenu>() is { } menu && Menu.IsOpen<CraftMenu>())
        {
            LogRecipes("craft menu", menu.SelectItemTabPage, spoolRecipe);
            Shot("stations-craft-menu");
            yield return AfterShot;
            menu.Close();
            yield return 1.5f;
        }

        // The game's own end of a craft at the table: the parts taken, the spool given. The save's own twine, sticks and
        // spools stay: only what the scenario gives or makes is counted and taken out.
        int twineCount = inventory.GetItemQuantity(twine), stickCount = inventory.GetItemQuantity(sticks), spoolCount = inventory.GetItemQuantity(spool);
        Mjolnir.SDK.Content.Items.Give(TwineAsset, 5);
        Mjolnir.SDK.Content.Items.Give("Item_Wood_Sticks", 1);
        yield return 0.5f;
        Log($"Stations: before crafting the player holds {inventory.GetItemQuantity(twine)} twine, {inventory.GetItemQuantity(sticks)} sticks, {inventory.GetItemQuantity(spool)} spools; " +
            $"the table's inventory '{craft.ItemInventory?._name}', the agent's inventory used {craft.useAgentInventory}");
        try
        {
            Log($"Stations: ActivateBlueprint {craft.ActivateBlueprint(spoolBlueprint)}, the active blueprint {craft.ActiveBlueprint?.info?.name}");
            craft._OnCraftingSuccess(interactor);
        }
        catch (System.Exception e)
        {
            Log($"Stations: crafting failed: {e}");
        }
        yield return 1f;
        Log($"Stations: after crafting the player holds {inventory.GetItemQuantity(twine)} twine ({twineCount} expected), {inventory.GetItemQuantity(sticks)} sticks ({stickCount} expected), " +
            $"{inventory.GetItemQuantity(spool)} spools ({spoolCount + 1} expected)");
        Shot("stations-crafted");
        yield return AfterShot;
        try
        {
            craft.Finish(interactor, InteractionSessionState.ABORTED);
        }
        catch (System.Exception e)
        {
            Log($"Stations: finishing the session failed: {e.Message}");
        }

        // The task page, where the player sets what the station's villagers make.
        var work = Object.FindObjectsOfType<WorkstationInteraction>().FirstOrDefault(w => Vector3.Distance(w.transform.position, at) < 15);
        if (work != null && agent.TryRemoteInteract(work))
        {
            yield return 2.5f;
            if (Menu.GetInstance<WorkstationMenu>() is { } tasks && Menu.IsOpen<WorkstationMenu>())
            {
                LogRecipes("task page", tasks.SelectItemTabPage, spoolRecipe);
                tasks.Close();
                yield return 1.5f;
            }
            else Log("Stations: the workstation menu did not open");
        }
        else Log($"Stations: no workstation interaction ({work != null})");

        foreach (var (info, count) in new[] { (spool, spoolCount), (twine, twineCount), (sticks, stickCount) })
        {
            int extra = inventory.GetItemQuantity(info) - count;
            if (extra > 0) inventory.RemoveItems(info, extra);
        }
        yield return 0.5f;
        built.Dismantle(true);
        yield return 2f;
        Log($"Stations: what the scenario gave and made taken out ({inventory.GetItemQuantity(spool)} spools, {inventory.GetItemQuantity(twine)} twine and {inventory.GetItemQuantity(sticks)} sticks left, " +
            $"{spoolCount}, {twineCount} and {stickCount} before), the workshop dismantled");
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static string Names(Il2CppSystem.Collections.Generic.List<StructureTemplate>? templates) =>
        templates == null || templates.Count == 0 ? "-" : string.Join(", ", Enumerable.Range(0, templates.Count).Select(i => templates[i]?.name));

    // The crafting lists among the loaded assets that hold an entry.
    private static string ListsHolding(ItemInfo? info)
    {
        if (info == null) return "-";
        var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<ItemInfoList>());
        var names = new List<string>();
        for (int i = 0; i < all.Length; i++)
        {
            var list = all[i]?.TryCast<ItemInfoList>();
            var items = list?.itemInfoList;
            if (items != null && Enumerable.Range(0, items.Count).Any(k => items[k] != null && items[k].Pointer == info.Pointer)) names.Add(list!.name);
        }
        names.Sort(System.StringComparer.Ordinal);
        return string.Join(", ", names);
    }

    // What a craft page offers: the recipes the station can make now and those it cannot yet, the test among them.
    private static void LogRecipes(string where, CreateItemsTabPage? page, ItemInfo recipe)
    {
        if (page == null)
        {
            Log($"Stations: {where}: no item page");
            return;
        }
        bool Has(Il2CppSystem.Collections.Generic.List<ItemInfo>? list) =>
            list != null && Enumerable.Range(0, list.Count).Any(i => list[i] != null && list[i].Pointer == recipe.Pointer);
        var panels = page.GetComponentsInChildren<ItemThumbnailPanel>(true).Where(p => p.ItemInfo != null).ToList();
        Log($"Stations: {where}: {page.AvailableBlueprints?.Count} available, {page.UnavailableBlueprints?.Count} unavailable; the spool recipe available {Has(page.AvailableBlueprints)} " +
            $"(true expected), unavailable {Has(page.UnavailableBlueprints)}; {panels.Count} panels, the spool's {panels.Any(p => p.ItemInfo.Pointer == recipe.Pointer)}");
    }
}
