using System.Collections.Generic;
using System.Linq;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "itemsave" scenario (autopilot.once containing "itemsave"): mod items in a save, played over several runs. Each
/// run loads the save and logs the player's inventory, the twine and whether the twine recipe is known. With
/// Mjolnir.ItemsExample loaded and no twine yet, it gives three and saves the session as the game's own save does;
/// without the mod (turned off in mod-list.json) it saves too, to show what the game keeps of items it no longer knows.
/// The save is the user's: back it up before the first run and put it back after the last.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> ItemSaveScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 3f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 6f; // the save's inventory loads after the player appears, the mod's recipe check runs every 2 s

        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var inventory = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.Inventory?.GetItemCollection();
        if (db == null || inventory == null)
        {
            Log($"Item save: missing (database {db != null}, inventory {inventory != null})");
            yield break;
        }
        var twine = db.GetItemInfo(TwineAsset);
        var recipe = db.GetItemInfo(TwineRecipeAsset);
        var infos = inventory.GetItemInfos();
        Log($"Item save: ItemsExample {(twine != null ? "loaded" : "not loaded")}; the inventory holds " +
            string.Join(", ", Enumerable.Range(0, infos.Count).Select(i => infos[i]).Where(i => i.TryCast<BlueprintInfo>() == null).Select(i => $"{i.name} x{inventory.GetItemQuantity(i)}")) +
            $"; {Enumerable.Range(0, infos.Count).Count(i => infos[i].TryCast<BlueprintInfo>() != null)} recipes known");
        int count = twine == null ? 0 : inventory.GetItemQuantity(twine);
        if (twine != null)
            Log($"Item save: {count} twine, the recipe known {recipe != null && inventory.HasItem(recipe)}");

        if (twine == null || count == 0)
        {
            if (twine != null)
            {
                int given = Mjolnir.SDK.Content.Items.Give(TwineAsset, 3);
                yield return 1f;
                Log($"Item save: gave {given} twine, the inventory holds {inventory.GetItemQuantity(twine)}");
            }
            var storage = GameManagers.Get<StorageManager>();
            if (storage == null)
            {
                Log("Item save: no storage manager");
                yield break;
            }
            var task = storage.SaveCurrentSession();
            foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
            Log($"Item save: saved, {storage.LastSaveOperationCode}");
        }

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
