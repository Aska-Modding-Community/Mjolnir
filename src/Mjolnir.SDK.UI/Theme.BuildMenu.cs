using System;
using System.Linq;
using SandSailorStudio.UI;
using SSSGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>The game's build menu, kept as a look for a palette of a mod's own (<see cref="BuildPalette"/>).</summary>
internal static partial class Theme
{
    /// <summary>
    /// The build menu (BuildMenuReworked) without its scripts, pages and buttons: its canvas and scaler, the two rows of
    /// category diamonds (Row1, Row2) emptied, the category's name (CategoryDisplayDiv/CategLabel), the ✕ (Close), the
    /// strip of tiles (Viewport/Content) emptied.
    /// </summary>
    public static GameObject? BuildMenu { get; private set; }

    /// <summary>A category's diamond (BuildCategoryButton): Background (turned 45°; its Highlight), Image (the icon).</summary>
    public static GameObject? BuildCategory { get; private set; }

    /// <summary>A tile (BuildItem): Container/Background with Image Div/Image, ItemHighlight (orange), Tween; TextDiv/Text.</summary>
    public static GameObject? BuildTile { get; private set; }

    /// <summary>The category diamonds' colours: as they are, under the pointer, chosen (the menu's TabGroup).</summary>
    public static (Color Normal, Color Highlight, Color Selected) BuildCategoryColors { get; private set; } =
        (new Color(0.6f, 0.65f, 0.63f), Color.white, Color.white);

    private static void LoadBuildMenuParts()
    {
        try
        {
            var prefab = MenuManager.Instance?.GetPrefab<SSSGame.UI.BuildMenu>();
            if (prefab == null)
            {
                SdkLog.Warn(GameUi.Source, "The build menu's prefab not found: no build palettes");
                return;
            }
            var group = prefab.GetComponentsInChildren<TabGroup>(true).FirstOrDefault(g => g.GetComponentInChildren<TabButtonCategory>(true) != null);
            if (group != null) BuildCategoryColors = (group.normalColor, group.highlightColor, group.selectColor);
            BuildTile = KeepGraphics(prefab.GetComponentInChildren<BuildItemsTabPage>(true)?.itemButton?.transform);
            if (BuildTile != null)
            {
                if (BuildTile.transform.Find("Container/Upgrades") is { } upgrades) Object.DestroyImmediate(upgrades.gameObject);
                if (BuildTile.transform.Find("Container/NewStatus") is { } status) status.gameObject.SetActive(false);
            }
            BuildCategory = KeepGraphics(prefab.GetComponentsInChildren<TabButtonCategory>(true).FirstOrDefault()?.transform);
            if (BuildCategory != null)
            {
                foreach (var t in BuildCategory.GetComponentsInChildren<Transform>(true))
                    if (t.name is "Text" or "NewItemsHighlight" or "Image_h") t.gameObject.SetActive(false);
                // The prefab may be kept in the middle of a hover tween (its icon turned 325 degrees).
                if (BuildCategory.transform.Find("Image") is { } icon) icon.localEulerAngles = Vector3.zero;
            }
            BuildMenu = KeepMenu(prefab.transform);
            SdkLog.Info(GameUi.Source, $"Build menu parts: frame {Has(BuildMenu)}, category {Has(BuildCategory)}, tile {Has(BuildTile)}");
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"Build menu parts not loaded: {e}");
        }
    }

    // A copy with only what draws and lays out: images, texts, masks, layout groups and fitters stay; every other script
    // (the game's tabs, its sounds, its hover animations, its scroll rect) goes.
    private static GameObject? KeepGraphics(Transform? source)
    {
        var copy = Keep(source);
        if (copy == null) return null;
        // A script another one requires cannot go before it: a few passes take them in order.
        for (int pass = 0; pass < 3; pass++)
        {
            bool left = false;
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (Draws(behaviour)) continue;
                Object.DestroyImmediate(behaviour);
                left = true;
            }
            if (!left) break;
        }
        foreach (var fade in copy.GetComponentsInChildren<CanvasGroup>(true))
        {
            fade.alpha = 1;
            fade.interactable = false;
            fade.blocksRaycasts = false;
        }
        return copy;
    }

    private static bool Draws(MonoBehaviour behaviour) =>
        behaviour.TryCast<Graphic>() != null || behaviour.TryCast<LayoutGroup>() != null || behaviour.TryCast<ContentSizeFitter>() != null
        || behaviour.TryCast<LayoutElement>() != null || behaviour.TryCast<RectMask2D>() != null || behaviour.TryCast<Mask>() != null
        || behaviour.TryCast<BaseMeshEffect>() != null || behaviour.TryCast<CanvasScaler>() != null;

    private static GameObject? KeepMenu(Transform source)
    {
        var copy = KeepGraphics(source);
        if (copy == null) return null;
        var root = copy.transform;
        foreach (var name in new[] { "Panel/SupplyListPanel/BuildPreviewPage", "Panel/SupplyListPanel/BuildPreviewPage Sec", "TooltipsGroup", "Panel/Title", "Panel/Divider (1)" })
            if (root.Find(name) is { } part) Object.DestroyImmediate(part.gameObject);
        foreach (var row in new[] { "Panel/Categories/PanelCategories/Row1", "Panel/Categories/PanelCategories/Row2" })
            if (root.Find(row) is { } r)
                for (int i = r.childCount - 1; i >= 0; i--) Object.DestroyImmediate(r.GetChild(i).gameObject);
        if (ViewFactory.FindDeep(root, "Content") is { } content)
            for (int i = content.childCount - 1; i >= 0; i--) Object.DestroyImmediate(content.GetChild(i).gameObject);
        if (ViewFactory.FindDeep(root, "Scrollbar") is { } bar) bar.gameObject.SetActive(false);
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name is "Page" or "CategoryDisplayDiv") t.gameObject.SetActive(true);
        return copy;
    }
}
