using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.UI;
using SandSailorStudio.UI;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The UI scenario's HUD additions in the world: elements over points in the world (one over the player's character,
/// examples/Mjolnir.UiExample's over the spot ahead when the world loaded), a counting notification, and one with a
/// picture of ours.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerable<float> WorldExtras()
    {
        var hint = GameObject.Find("ExampleHudKey");
        Log($"Key hint: key '{hint?.transform.Find("Key")?.GetComponent<TMPro.TMP_Text>()?.text}' in " +
            $"'{hint?.transform.Find("Key")?.GetComponent<TMPro.TMP_Text>()?.spriteAsset?.name}', text '{hint?.transform.Find("Name")?.GetComponent<TMPro.TMP_Text>()?.text}' " +
            "(<sprite name=\"F7\"> in KbdMouseGlyphs expected)");

        var character = Object.FindAnyObjectByType<PlayerCharacter>();
        var marker = new UiTextBlock { Name = "LabMarker", Text = "UiLab", FontSize = 18 };
        if (character != null) Hud.AddAt(marker, character.transform, new Vector3(0, 2.2f, 0));
        else Log("No PlayerCharacter: the marker over the player is not checked");
        yield return 0.5f;
        Log($"Markers: over the player {Marker("LabMarker")} (shown, a little above the middle expected); the example's {Marker("ExampleMarker")}");

        Shot("world-markers");
        yield return AfterShot;

        // The game's notifications as it draws them: each panel's look by type, every quantity style through its own
        // call (DisplayStacking(CUSTOM, style, …), as it counts pickups), then ours, which should look the same.
        NotificationLooks();
        var styles = new[] { StackingType.Generic, StackingType.PositiveQty, StackingType.NegativeQty, StackingType.EffectAdd,
                             StackingType.EffectRemove, StackingType.Durability };
        for (int i = 0; i < styles.Length; i++)
        {
            string text = $"Game {styles[i]}";
            int id = 7100 + i;
            float quantity = 3;
            NotificationMenu.DisplayStacking(NotificationType.CUSTOM, styles[i], ref text, ref id, ref quantity, null);
        }
        yield return 1f;
        Log($"The game's quantity styles: {Notifications()}");
        Shot("notify-game-styles");
        yield return AfterShot;
        yield return 8f; // they fade

        Notify.Counter("lab", "UiLab: counted", 2);
        Notify.Counter("lab", "UiLab: counted", 3);
        Notify.Counter("lab-picture", "UiLab: counted with a picture", 1, Picture());
        Notify.Message("UiLab: a picture", Picture());
        Notify.Message("UiLab: a plain message");
        yield return 1f;
        Log($"Ours: {Notifications()} (one 'UiLab: counted' at x5, the game's look expected)");
        Shot("notify-ours");
        yield return AfterShot;
        Notify.Counter("lab", "UiLab: counted", -1);
        yield return 0.5f;
        Log($"After counting down: {Notifications()}");

        Hud.Remove(marker);
        yield return 0.5f;
        Log($"Marker removed: object left {Exists("Mjolnir_Hud_Marker LabMarker")}");

        // The game's journal as it draws it (its list buttons get their colours at run time), beside our copies.
        foreach (float wait in OpenForResearch<SSSGame.UI.PlayerMenu>("journal")) yield return wait;
        if (Menu.GetInstance<SSSGame.UI.PlayerMenu>() is { } journal && Menu.IsOpen<SSSGame.UI.PlayerMenu>())
        {
            // Its «Journal» tab: blueprints, items and creatures listed on the left.
            var tabs = journal.MainTabGroup?.buttons;
            for (int i = 0; tabs != null && i < tabs.Count; i++)
            {
                if (!tabs[i].name.StartsWith("Journal", System.StringComparison.Ordinal)) continue;
                Click(tabs[i].gameObject);
                yield return 1.5f;
                break;
            }
            HierarchyDump.Write(journal.gameObject, System.IO.Path.Combine(ShotsDir, "ui-journal-live.txt"));
            Shot("journal");
            yield return AfterShot;
            journal.Close();
            yield return 1.5f;
        }

        // The example's page in the journal (the game makes the journal anew: this is another instance).
        foreach (float wait in JournalTabs()) yield return wait;

        // Mjolnir.ItemsExample's twine and its hand recipe.
        foreach (float wait in ItemsCheck()) yield return wait;
    }

    // A HUD marker's container: shown (its CanvasGroup), where it sits from the canvas's middle, its size.
    private static string Marker(string name)
    {
        var rect = Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).FirstOrDefault(r => r.name == $"Mjolnir_Hud_Marker {name}");
        if (rect == null) return "none";
        var group = rect.GetComponent<CanvasGroup>();
        return $"shown {group != null && group.alpha > 0.5f} at {rect.anchoredPosition.x:0},{rect.anchoredPosition.y:0}, {rect.rect.width:0}x{rect.rect.height:0}";
    }

    // The notifications on screen: text and its colour, a counting one's amount, the picture (its size, colour, shown).
    private static string Notifications() =>
        string.Join(" | ", Object.FindObjectsByType<NotificationTextPanel>(FindObjectsSortMode.None).Select(panel =>
        {
            var sprite = panel.icon != null ? panel.icon.sprite : null;
            string count = panel.TryCast<StackingNotificationTextPanel>() is { } stacking
                ? $" amount '{stacking.QuantityLabel?.text}' ({stacking.Quantity}, {stacking.CurrentStackingType})" : "";
            string picture = sprite != null
                ? $"{sprite.name} {sprite.rect.width:0}x{sprite.rect.height:0} {Hex(panel.icon!.color)}{(panel.icon.gameObject.activeInHierarchy ? "" : " hidden")}"
                : "none";
            return $"'{panel.text?.text}' {Hex(panel.text?.color ?? Color.clear)}{count} picture {picture}";
        }));

    private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

    // Each notification panel's look by type (NotificationTextPanel.genericType…: picture, its colour, the text's colour).
    private static void NotificationLooks()
    {
        var menu = MenuManager.Instance.GetPrefab<NotificationMenu>();
        if (menu == null)
        {
            Log("No NotificationMenu prefab");
            return;
        }
        static string Look(NotificationTextPanel.IconTypes? t) =>
            t == null ? "none" : $"{(t.icon != null ? t.icon.name : "no picture")} {Hex(t.iconColor)} text {Hex(t.textColor)}";
        foreach (var (name, panel) in new (string, NotificationTextPanel?)[]
                 {
                     ("small", menu.smallNotificationPrefab), ("stacking", menu.smallStackingNotificationPrefab),
                     ("icon", menu.iconNotificationPrefab), ("large", menu.largeNotificationPrefab),
                 })
        {
            if (panel == null) continue;
            Log($"Notification panel '{name}' ({panel.name}): generic {Look(panel.genericType)}; warning {Look(panel.warningType)}; " +
                $"error {Look(panel.errorType)}; custom {Look(panel.customType)}; item broke {Look(panel.itembrokeType)}");
        }
    }

    // An 8×8 amber square: a picture of the mod's own.
    private static Texture2D Picture()
    {
        var texture = new Texture2D(8, 8) { filterMode = FilterMode.Point };
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++) texture.SetPixel(x, y, new Color(1f, 0.7f, 0.11f));
        }
        texture.Apply();
        return texture;
    }
}
