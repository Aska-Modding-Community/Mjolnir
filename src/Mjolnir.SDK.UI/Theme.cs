using System;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SandSailorStudio.Localization;
using SandSailorStudio.UI;
using SSSGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Context = SSSGame.InputContext.Context;
using GameInput = SSSGame.InputManager;
using GameSettingsMenu = SSSGame.UI.SettingsMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The game's look, taken from its menu prefabs (MenuManager.GetPrefab, available without opening any menu):
/// <list type="bullet">
/// <item>ConfirmActionMenu: window and frame sprites, the title and text fonts, a dialog button to clone;</item>
/// <item>SettingsMenu: the rows of its settings pages (slider, "‹ value ›" selector, dropdown, section header) and its
/// scroll view;</item>
/// <item>InputFieldConfirmActionMenu (the rename dialog): its text input;</item>
/// <item>PlayerHUDMenu (the world's HUD, its prefab is there in the main menu too): the health bar;</item>
/// <item>PlayerMenu (the journal): its icon tab, its list button and its hover card;</item>
/// <item>MainMenu (its prefab, else the main menu on screen): the save-slot row of its «Load game» page.</item>
/// </list>
/// Loaded on first use in phase B. Anything missing falls back to plain elements, so a game update degrades the look,
/// not the mod.
/// </summary>
internal static partial class Theme
{
    private static bool s_loaded;
    private static bool s_waitLogged;
    private static GameObject? s_holder;
    private static Context? s_inputContext;
    private static Context? s_typingContext;

    /// <summary>
    /// The game's main windows' chrome (the settings menu's Window_Dark1, as the inventory's): the background
    /// UI_WindowDefault_DarkGray and its frame UI_WindowFrame1, both sliced at ppu 3, the corner ornaments and the diamond
    /// in the top right corner, kept whole so their slicing, colours and places come with them. Stretched over a window.
    /// </summary>
    public static GameObject? MenuChrome { get; private set; }

    /// <summary>The main windows' X (UI_Close, 36×36), at the top right corner in the diamond; its click is ours.</summary>
    public static GameObject? MenuCloseButton { get; private set; }

    /// <summary>What the game's main menus put behind themselves, over the whole screen: the world blurred, then darkened at the edges.</summary>
    public static GameObject? MenuBlur { get; private set; }

    /// <inheritdoc cref="MenuBlur"/>
    public static GameObject? MenuVignette { get; private set; }

    /// <summary>The ornaments either side of the settings menu's tabs on the top edge (UI_Ornament1, UI_Ornament2).</summary>
    public static GameObject? HeaderLeft { get; private set; }

    /// <inheritdoc cref="HeaderLeft"/>
    public static GameObject? HeaderRight { get; private set; }

    /// <summary>The plate behind the inventory's tab on the top edge (UI_WindowDefault, sliced at ppu 4).</summary>
    public static GameObject? HeaderPlate { get; private set; }

    /// <summary>The confirm dialog's chrome (Window_Dark: UI_WindowDefault_Dark at ppu 4, the faint frame UI_WindowFrame2 at ppu 5), kept whole.</summary>
    public static GameObject? DialogChrome { get; private set; }
    public static TMP_FontAsset? TitleFont { get; private set; }
    public static TMP_FontAsset? TextFont { get; private set; }
    public static int DialogSortingOrder { get; private set; } = 100;

    /// <summary>A hidden copy of the dialog's confirm button (persistent listeners and localization removed).</summary>
    public static Button? DialogButton { get; private set; }

    // Hidden, stripped copies of the settings menu's parts (see Find* below for how each one is recognised).
    public static GameObject? SliderRow { get; private set; }
    public static GameObject? SelectorRow { get; private set; }
    public static GameObject? DropdownRow { get; private set; }
    public static GameObject? HeaderRow { get; private set; }
    public static GameObject? ScrollView { get; private set; }

    /// <summary>The settings selector's arrow sprite (its Increase button; the sprite points up): an expander's header points with it.</summary>
    public static Sprite? Arrow { get; private set; }
    public static GameObject? InputField { get; private set; }

    /// <summary>
    /// The HUD's health bar: its frame with the parts named by role, "Fill" (the filled image), "Text" and "Trail" (the
    /// game's lagging bar, switched off), and without the game's ProgressBar behaviour (KeepBar).
    /// </summary>
    public static GameObject? ProgressBar { get; private set; }

    /// <summary>The HUD's food ring (PlayerWidget.foodBar's frame), its parts named Fill, Icon, IconFrame and Text.</summary>
    public static GameObject? ProgressRing { get; private set; }

    /// <summary>The key of a HUD prompt (GlyphDisplayComponent.KeyTargetText): its font, size and colour; null without the HUD prefab.</summary>
    public static TextLook? KeyHintKey { get; private set; }

    /// <summary>The action's name of a HUD prompt (GlyphDisplayComponent.NameTargetText).</summary>
    public static TextLook? KeyHintName { get; private set; }

    /// <summary>The journal's icon tab (95×80): Background, Frame_Selected, Icon_Shadow, Icon, Frame.</summary>
    public static GameObject? IconTab { get; private set; }

    /// <summary>The journal's list button (294×42): Background, Icon, Frame (amber while selected), Title.</summary>
    public static GameObject? ListTab { get; private set; }

    /// <summary>The journal's hover card: Arrow, Background, Title, Text, in a column as tall as its content.</summary>
    public static GameObject? ToolTipCard { get; private set; }

    /// <summary>
    /// The journal's item slot (an ItemThumbnailPanel; the inventory's, 70×70), its parts named by role: Background, Icon, Strip (its
    /// Fill: a filled image), CountPanel (its Count text and the wing), Highlight; without the game's scripts.
    /// </summary>
    public static GameObject? Slot { get; private set; }

    /// <summary>
    /// The deeds page's god card (a DeedCategoryPanel, 187 wide), its parts named by role: Background, Frame, Image (in
    /// its Portrait, beside Highlight), Title, Subtitle; the lore and progress lines hidden, without the game's scripts.
    /// </summary>
    public static GameObject? Card { get; private set; }

    /// <summary>
    /// The inventory's card of an item (an ItemDetailsPanel, from the journal's item page): its own script stays,
    /// so giving it an ItemInfo fills the title, the kind, the tier, the picture and the attributes; what ties it to
    /// the page it came from is gone, and the inventory's key hints are hidden.
    /// </summary>
    public static GameObject? ItemCard { get; private set; }

    /// <summary>
    /// The panel under a category's header in the journal (a DropdownElement's Content: its Background,
    /// UI_WindowDefault_Inventory sliced at ppu 4), for an expander's body.
    /// </summary>
    public static ImageLook? ExpanderPanel { get; private set; }

    /// <summary>
    /// The lighter panel a journal page holds its content on (Window_Light2: UI_WindowDefault_Inventory sliced at ppu 2 with
    /// a faint tiled overlay), for a mod's page in the journal.
    /// </summary>
    public static GameObject? JournalPanel { get; private set; }

    /// <summary>The «Load game» page's save-slot row: its frames, ToggleEffect (shown while selected), an UpgradedToggle.</summary>
    public static GameObject? ListRow
    {
        get
        {
            if (s_listRow == null && !s_listRowTried) LoadListRow(); // the main menu may not have been on screen at Load
            return s_listRow;
        }
    }

    private static GameObject? s_listRow;
    private static bool s_listRowTried;

    internal static Transform Holder
    {
        get
        {
            if (s_holder == null)
            {
                s_holder = new GameObject("Mjolnir.SDK.UI templates");
                Object.DontDestroyOnLoad(s_holder);
                s_holder.SetActive(false);
            }
            return s_holder.transform;
        }
    }

    /// <summary>
    /// Takes the game's parts once the game's menus are there (MenuManager): called before a mod's UI is first built; until
    /// then it does nothing and is tried again at the next use. Each kind of part on its own: one the game no longer has
    /// leaves the others.
    /// </summary>
    public static void Load()
    {
        if (s_loaded) return;
        if (!MenusReady())
        {
            if (!s_waitLogged) SdkLog.Warn(GameUi.Source, "The game's menus are not up yet: the look is taken at the next use of the UI");
            s_waitLogged = true;
            return;
        }
        s_loaded = true;
        LoadDialogParts();
        LoadSettingsParts();
        LoadWindowParts();
        LoadHudParts();
        LoadJournalParts();
        LoadListRow();
    }

    private static bool MenusReady()
    {
        try
        {
            return MenuManager.Instance != null;
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"MenuManager check failed: {e}");
            return false;
        }
    }

    /// <summary>A string of the game by its localization key (common.on, common.reset…) in the player's language, or the fallback.</summary>
    public static string Loc(string key, string fallback)
    {
        try
        {
            string? text = LocalizationManager.Instance?.Loc(key, false);
            return string.IsNullOrWhiteSpace(text) || text == key ? fallback : text;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>
    /// Input context of mod windows, like the game's TutorialContext: UI and menu controls only, so the player does not
    /// move or turn the camera and Esc goes to the window, not to the pause menu.
    /// </summary>
    public static Context InputContext
    {
        get
        {
            if (s_inputContext != null) return s_inputContext;
            s_inputContext = ScriptableObject.CreateInstance<Context>();
            s_inputContext.name = "Mjolnir.SDK.UI window";
            s_inputContext.hideFlags = HideFlags.DontUnloadUnusedAsset;
            s_inputContext.inputMaps = new Il2CppStructArray<GameInput.InputMaps>(new[] { GameInput.InputMaps.UI, GameInput.InputMaps.MenuControls });
            s_inputContext.priority = 16;
            s_inputContext.dontChangeCamera = true;
            s_inputContext.contextCamera = GameInput.CameraStates.LOCK;
            s_inputContext.additive = false;
            return s_inputContext;
        }
    }

    /// <summary>
    /// Input context of the SDK's text fields while the player types (<see cref="TextFields"/>): only the UI map (the
    /// pointer, the wheel), above every game context. The game's own typing context (Only_UI_Context) has priority 20.
    /// </summary>
    public static Context TypingContext
    {
        get
        {
            if (s_typingContext != null) return s_typingContext;
            s_typingContext = ScriptableObject.CreateInstance<Context>();
            s_typingContext.name = "Mjolnir.SDK.UI typing";
            s_typingContext.hideFlags = HideFlags.DontUnloadUnusedAsset;
            s_typingContext.inputMaps = new Il2CppStructArray<GameInput.InputMaps>(new[] { GameInput.InputMaps.UI });
            s_typingContext.priority = 100;
            s_typingContext.dontChangeCamera = true;
            s_typingContext.contextCamera = GameInput.CameraStates.LOCK;
            s_typingContext.additive = false;
            return s_typingContext;
        }
    }
}
