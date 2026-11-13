using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mjolnir.SDK.UI;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GameContextMenu = SSSGame.UI.ContextMenu;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using GameSettingsMenu = SSSGame.UI.SettingsMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// Plays the UI scenario without a person at the keyboard, so the facade can be checked from the log and from the
/// screenshots in Mods/Mjolnir.UiLab/shots. Clicks go through the EventSystem like the mouse's; Esc is
/// MenuManager.BackPressTopMenu, which is what the game's back action calls.
/// </summary>
internal static partial class Autopilot
{
    // Names the SDK gives the buttons (ButtonRegistry): its own Mods button, examples/Mjolnir.UiExample's and this mod's.
    private const string ModsMain = "Mjolnir_Main_Mjolnir.SDK_0";
    private const string ModsPause = "Mjolnir_Pause_Mjolnir.SDK_0";
    private const string ExamplePause = "Mjolnir_Pause_Mjolnir.UiExample_0"; // «UI Example», a menu item with a level
    private const string ExampleWindowItem = ExamplePause + "_0"; // its first item opens the example window
    private const string LabPause = "Mjolnir_Pause_Mjolnir.UiLab_0";
    private const string ModsList = "Mjolnir_Window_Mjolnir"; // the SDK's Mods window, titled with the platform's name
    private const string ExampleWindow = "Mjolnir_Window_UI Example";
    private const string ExampleSettings = "Mjolnir_Window_UI Example ·"; // "· Settings" in the game's language
    private const float AfterShot = 0.3f; // ScreenCapture writes at the end of the frame

    public static bool Enabled;
    public static string Scenario = "ui";
    public static string SaveName = "";
    public static bool Multiplayer;
    public static bool QuitWhenDone;
    public static string ShotsDir = "";
    public static string ConfigDir = "";
    public static bool MainMenuReady;
    public static bool HudReady;

    private static IEnumerator<float>? s_script;
    private static float s_wakeAt;
    private static int s_shot;

    private static void Log(string message) => UiLab.Instance.Log.Info(message);

    public static void Start()
    {
        if (Directory.Exists(ShotsDir)) Directory.Delete(ShotsDir, true);
        Directory.CreateDirectory(ShotsDir);
        s_script = Scenario switch { "research" => Research(), "player" => PlayerScenario(), "localization" => LocalizationScenario(), "scale" => ScaleScenario(), "uiresearch" => UiResearch(), "windows" => WindowsResearch(), "expander" => ExpanderResearch(), "gametabs" => GameTabsResearch(), "items" => ItemsResearch(), "itemsave" => ItemSaveScenario(), "consoleresearch" => ConsoleResearch(), "console" => ConsoleScenario(), "inputresearch" => InputResearch(), "stations" => StationsScenario(), "buildings" => BuildingsScenario(), "villagers" => VillagersScenario(), "settlement" => SettlementResearch(), "cloud" => CloudScenario(), "stats" => StatsScenario(), "models" => ModelsResearch(), "catalog research" => CatalogResearch(), "catalog" => CatalogScenario(), "net" => NetScenario(), "nethost" => NetHostScenario(), "animations" => AnimationResearch(), "rigs" => RigsResearch(), "animprobe" => AnimationProbe(), "animation" => AnimationScenario(), "rigcheck" => RigCheck(), "blender" => BlenderScenario(), "partial" => PartialScenario(), "mixamo" => MixamoScenario(), "colors" => ColorsScenario(), "bodies" => BodiesResearch(), "clips" => ClipsResearch(), "combat research" => CombatResearch(), "combat" => CombatScenario(), "interactions research" => InteractionsResearch(), "interactions player" => InteractionsPlayerResearch(), "altar" => AltarScenario(), "structure research" => StructureInteractionResearch(), "well research" => WellResearch(), "structures research" => StructuresResearch(), "construction research" => ConstructionResearch(), "netprefab research" => NetPrefabResearch(), "cart research" => CartResearch(), "stages" => StagesScenario(), "equipment research" => EquipmentResearch(), "equipment" => EquipmentScenario(), "camera research" => CameraResearch(), "camera" => CameraScenario(), "terrain" => TerrainScenario(), "terrain probe" => TerrainProbe(), "terrain probe2" => TerrainProbe2(), "terrain probe3" => TerrainProbe3(), "terrain probe4" => TerrainProbe4(), "terrain probe5" => TerrainProbe5(), "terrain probe6" => TerrainProbe6(), "terrain probe7" => TerrainProbe7(), "terrain probe8" => TerrainProbe8(), "terrain research" => TerrainResearch(), "terrain tools" => TerrainToolsScenario(), "terrain cover" => TerrainCoverScenario(), "terrain objects" => TerrainObjectsScenario(), "terrain fields" => TerrainFieldsScenario(), _ => Script() };
        Log($"Autopilot started: scenario '{Scenario}'");
    }

    public static void Update()
    {
        if (s_script == null || Time.realtimeSinceStartup < s_wakeAt) return;
        try
        {
            if (!s_script.MoveNext())
            {
                s_script = null;
                Ended("ended");
                return;
            }
            s_wakeAt = Time.realtimeSinceStartup + s_script.Current;
        }
        catch (Exception e)
        {
            UiLab.Instance.Log.Error($"Autopilot failed: {e}");
            s_script = null;
            Ended("failed");
        }
    }

    // However a scenario ends (done, an early yield break, an exception), the game it started does not stay open: a run
    // that stopped half way left the game running until the user closed it (2026-11-11).
    private static void Ended(string how)
    {
        if (!QuitWhenDone) return;
        Log($"Autopilot {how}: quitting the game");
        Application.Quit();
    }

    private static IEnumerator<float> Script()
    {
        // Main menu: the SDK's Mods button → the mod list → UI Example's generated settings page.
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 3f;
        var mainMenu = Menu.GetInstance<MainMenu>();
        Log($"Main menu: {Count(mainMenu, "Mjolnir_Main_")} mod button(s) (2 expected: Mjolnir, UI Example)");
        Click(Find(mainMenu, ModsMain));
        yield return 1.5f;
        var list = Window(n => n == ModsList);
        Log($"Mods window: {Describe(list)}");
        Log($"Mods window icons: {Icons(list)}");
        Log($"Mods window layout: window {SizeOf(list)}, title '{list.transform.Find("Panel/Header/Plate/Title")?.GetComponent<TMP_Text>()?.text}'; " +
            $"footer {FooterLayout(list)} (the note hidden in its place, the platform's Settings and Close on the right)");
        Shot("mods-list");
        yield return AfterShot;
        Click(Find(ModRowOf(list, "mjolnir.uiexample"), "Settings"));
        yield return 1.5f;
        var page = Window(n => n.StartsWith(ExampleSettings, StringComparison.Ordinal));
        Log($"Settings page '{page.name}': {Describe(page)}");
        Shot("example-settings");
        yield return AfterShot;

        // Change settings like the player: the toggle twice (off, on again), the slider, the text.
        Click(Find(Setting(page, "HUD.ShowText"), "Increase"));
        yield return 0.3f;
        Click(Find(Setting(page, "HUD.ShowText"), "Increase"));
        yield return 0.3f;
        Setting(page, "Banner.Seconds").GetComponentInChildren<Slider>().value = 5f;
        yield return 0.3f;
        var input = Setting(page, "HUD.Text").GetComponentInChildren<TMP_InputField>(true);
        input.text = "Hello from the settings";
        input.SendOnEndEdit();
        yield return 0.5f;
        Log($"Settings page changed: {Describe(page)}");
        var keyRow = Setting(page, "Keys.Window");
        Log($"Hotkey row: '{ButtonText(keyRow)}'");
        Click(Find(keyRow, "Button"));
        yield return 0.3f;
        Log($"Hotkey row after a click: '{ButtonText(keyRow)}' (waits for a key)");
        Shot("example-settings-changed");
        yield return AfterShot;
        MenuManager.Instance.BackPressTopMenu(); // closes the page: the config is written
        yield return 1f;
        Log($"Example config after the page closed: {ConfigValues("mjolnir.uiexample")} (Window = \"F7\" kept: closing the page cancelled the recording)");

        // A setting read only at start (Infinite Stamina's): its row says so, and changing it brings up the restart note,
        // which goes again when the value is back as it was.
        Click(Find(ModRowOf(list, "mjolnir.infinitestamina"), "Settings"));
        yield return 1.5f;
        var stamina = Window(n => n.StartsWith("Mjolnir_Window_Infinite Stamina", StringComparison.Ordinal));
        Log($"Infinite Stamina page: {Describe(stamina)}; restart note {Shown(stamina, "RestartNote")}; " +
            $"row description '{RowDescription(Setting(stamina, "General.Enabled"))}'");
        Click(Find(Setting(stamina, "General.Enabled"), "Increase"));
        yield return 0.6f; // the note is polled four times a second
        Log($"Infinite stamina switched to {RowValue(Setting(stamina, "General.Enabled").transform)}: restart note {Shown(stamina, "RestartNote")}");
        Shot("restart-note");
        yield return AfterShot;
        Click(Find(Setting(stamina, "General.Enabled"), "Increase"));
        yield return 0.6f;
        Log($"Infinite stamina switched back to {RowValue(Setting(stamina, "General.Enabled").transform)}: restart note {Shown(stamina, "RestartNote")}; config {ConfigValues("mjolnir.infinitestamina")}");
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;

        // Turning mods on and off, at the next start. Before this start mod-list.json turned PlayerExample off.
        var player = ModRowOf(list, "mjolnir.playerexample");
        Log($"Player Example row: {RowValue(player.transform)}, '{RowDescription(player)}', settings button {player.transform.Find("Settings") != null}; " +
            $"loaded {Mjolnir.SDK.ModRegistry.IsLoaded("mjolnir.playerexample")}; restart note {Shown(list, "RestartNote")} (Off, turned off, no button, not loaded, no note expected)");
        Log($"Mod row layout: {ModRowLayout(ModRowOf(list, "mjolnir.uiexample"))}");
        Click(Find(player, "Increase"));
        yield return 0.6f; // the note is polled four times a second
        Log($"Player Example turned on: {RowValue(player.transform)}, '{RowDescription(player)}'; restart note {Shown(list, "RestartNote")}; list {ModListFile()}");
        Log($"Mods window with the note: window {SizeOf(list)} (the same size expected), footer {FooterLayout(list)}");
        Shot("mods-turned-on");
        yield return AfterShot;
        Click(Find(player, "Increase"));
        yield return 0.6f;
        Log($"Player Example turned off again: {RowValue(player.transform)}, '{RowDescription(player)}'; restart note {Shown(list, "RestartNote")}; list {ModListFile()}");

        // Turning off a mod another one needs asks first: FaultMod needs SmokeMod (both deployed with -SmokeMod).
        if (TryFind(list, "Mod mjolnir.test.smokemod") is { } smoke)
        {
            Click(Find(smoke, "Increase"));
            yield return 1.5f;
            Log($"SmokeMod turned off: question '{DialogText()}'");
            Shot("mods-required-by");
            yield return AfterShot;
            MenuManager.Instance.BackPressTopMenu(); // Esc: no
            yield return 1f;
            Log($"After Esc: SmokeMod {RowValue(smoke.transform)}, '{RowDescription(smoke)}'; list {ModListFile()} (On, unchanged expected)");
        }
        else Log("SmokeMod is not deployed: the question about mods that need it is not checked");
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;
        Log($"After two Backs: Mods window open {TryWindow(n => n == ModsList) != null}");

        // Menu levels in the main menu, and the example's tabs window opened from one.
        foreach (float wait in MainMenuLevels(mainMenu)) yield return wait;

        // The example's tab in the game's settings menu.
        foreach (float wait in SettingsTabs(mainMenu)) yield return wait;
        ItemsInMenu(); // Mjolnir.ItemsExample's twine in the main menu's item database

        foreach (float wait in LoadSave(mainMenu)) yield return wait;

        // World: the banner, and the HUD column (the configured line, a clock that ticks by itself).
        yield return 1.5f;
        Shot("world-banner");
        yield return 5f;
        Log($"In the world: HUD {HudTexts()}; {CursorProbe.Describe()}");
        yield return 2f;
        Log($"Two seconds later: HUD {HudTexts()}");
        Log($"HUD minute bar: {Progress(GameObject.Find("ExampleHudMinute"))}");
        Shot("world-hud");
        yield return AfterShot;
        foreach (float wait in WorldExtras()) yield return wait;

        // Pause: three mod buttons; the example's window, an element tree with every element.
        Menu.Show<GamePauseMenu>(null);
        yield return 1.5f;
        var pause = Menu.GetInstance<GamePauseMenu>();
        Log($"Pause menu: {Count(pause, "Mjolnir_Pause_")} mod button(s) (3 expected: Mjolnir, UI Example, UiLab)");
        Shot("pause");
        yield return AfterShot;
        foreach (float wait in PauseLevels()) yield return wait;
        Click(Find(pause, ExamplePause));
        yield return 1f;
        Click(Find(pause, ExampleWindowItem));
        yield return 1.5f;
        var window = Window(n => n == ExampleWindow);
        Log($"Example window open: {CursorProbe.Describe()}");
        Log($"Example window: {Describe(window)}");
        Log($"Progress bar: {Progress(Find(window, "ExampleProgress"))} (80% expected: the volume's 0.8)");
        Shot("example-window");
        yield return AfterShot;

        // The simple form: a name in the text field, then the button greets with it.
        var yourName = Row(window, "Your name").GetComponentInChildren<TMP_InputField>(true);
        yourName.text = "Bjorn";
        yourName.SendOnEndEdit();
        ClickLabeled(window, "Say hello");
        yield return 0.3f;
        Log($"Simple form: button now reads '{window.GetComponentsInChildren<TextMeshProUGUI>(false).FirstOrDefault(t => t.text.StartsWith("Hello", StringComparison.Ordinal))?.text}'");

        // A command: "Reset clicks" is dimmed until there are clicks to reset; the Clicks row follows the view model.
        Log($"Reset clicks before counting: {Interactable(window, "ResetClicks")}");
        Click(Find(Row(window, "Count clicks"), "Button"));
        Click(Find(Row(window, "Count clicks"), "Button"));
        yield return 0.3f;
        Log($"Reset clicks after two clicks: {Interactable(window, "ResetClicks")}; Clicks row {RowValue(Row(window, "Clicks").transform)}");
        Click(Find(Row(window, "Difficulty"), "Increase"));
        Row(window, "Volume").GetComponentInChildren<Slider>().value = 0.3f;
        Click(Find(window, "AddNote"));
        Click(Find(window, "AddNote"));
        Click(Find(window, "RemoveNote"));
        yield return 0.5f; // computed texts follow on the next frames
        Log($"Example window changed: {Describe(window)}");
        Log($"Notes: {window.GetComponentsInChildren<Transform>(false).Count(t => RowLabel(t) == "Note")} (2 expected)");
        Log($"Progress bar after the volume went to 0.3: {Progress(Find(window, "ExampleProgress"))}");
        var image = Find(window, "ExampleImage").GetComponent<Image>();
        var custom = Find(window, "ExampleCustom").GetComponent<Image>();
        Log($"UiImage sprite {(image != null && image.sprite != null ? $"{image.sprite.rect.width}x{image.sprite.rect.height}" : "missing")}; " +
            $"UiUnityHost image {(custom != null ? "built" : "missing")}");
        // Left with a 24 px margin: the text sits in a slot of its own, 24 px in from the slot's left edge.
        var aside = Find(window, "ExampleAside").GetComponent<RectTransform>();
        var asideSlot = aside.parent.TryCast<RectTransform>()!;
        Log($"Aside: in '{asideSlot.name}', {aside.localPosition.x + LeftEdge(aside) - LeftEdge(asideSlot):0} px from its left, " +
            $"width {aside.rect.width:0} of {asideSlot.rect.width:0}");

        // The window's toggle is bound to the config: switching it hides the HUD line through its IsVisible binding.
        Click(Find(Row(window, "Show the HUD line"), "Increase"));
        yield return 0.5f;
        Log($"HUD line switched off in the window: config {ConfigValues("mjolnir.uiexample")}; HUD {HudTexts()}");
        Click(Find(Row(window, "Show the HUD line"), "Increase"));
        yield return 0.5f;
        Shot("example-window-changed");
        yield return AfterShot;

        // The wheel over the content scrolls it (from the top, where the window opened).
        var scroll = window.GetComponentInChildren<ScrollRect>();
        float before = scroll.verticalNormalizedPosition;
        var wheel = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -10) };
        ExecuteEvents.ExecuteHierarchy(scroll.content.GetChild(1).gameObject, wheel, ExecuteEvents.scrollHandler);
        yield return 0.5f;
        Log($"Scroll: {before:0.00} → {scroll.verticalNormalizedPosition:0.00} (content {scroll.content.rect.height:0} in viewport " +
            $"{scroll.viewport.rect.height:0}, scrollbar shown {scroll.verticalScrollbar != null && scroll.verticalScrollbar.gameObject.activeSelf})");

        // The dropdown list, with its row in view: where the checkbox frames sit (the game's prefab shifts them 5.4 px
        // right of their boxes; 0 once the game fixes it), then pick the fourth option.
        scroll.verticalNormalizedPosition = 0.35f;
        yield return 0.5f;
        var dropdown = Row(window, "Season").GetComponent<TMP_Dropdown>();
        dropdown.Show();
        yield return 1f;
        Log($"Dropdown checkboxes: {CheckboxOffsets(dropdown)}");
        Shot("example-dropdown");
        yield return AfterShot;
        var items = dropdown.GetComponentsInChildren<Toggle>(false);
        if (items.Length > 3) Click(items[3].gameObject);
        yield return 1f;
        Log($"Dropdown after the click: '{dropdown.captionText.text}', list open {dropdown.IsExpanded}");
        scroll.verticalNormalizedPosition = 0;
        yield return 0.5f;
        Shot("example-window-bottom");
        yield return AfterShot;

        var expander = Find(window, "ExampleExpander").transform;
        var expanderContent = expander.Find("Content");
        Log($"Expander before a click: content shown {expanderContent.gameObject.activeSelf}; arrow {Arrow(expander)} (-36, 0 at 270° expected)");
        Click(expander.Find("Header/Hit").gameObject);
        yield return 0.5f;
        Log($"Expander after a click: content shown {expanderContent.gameObject.activeSelf}; arrow {Arrow(expander)} (-36, 0 at 180° expected); " +
            $"grid {Cells(Find(window, "ExampleGrid").transform)}");
        var inner = Find(window, "ExampleScroll").GetComponent<ScrollRect>();
        Log($"Scroll viewer: content {inner.content.rect.height:0} in viewport {inner.viewport.rect.height:0}, " +
            $"scrollbar shown {inner.verticalScrollbar != null && inner.verticalScrollbar.gameObject.activeSelf}");
        Log($"Ring: {Ring(Find(window, "ExampleRing"))} (0.3 expected: the volume)");
        Log($"Small ring: {Ring(Find(window, "ExampleSmallRing"))} (60 x 77 and scale 0.6 expected: Width 60)");
        scroll.verticalNormalizedPosition = 0;
        yield return 0.5f;
        Shot("example-more");
        yield return AfterShot;
        MenuManager.Instance.BackPressTopMenu();
        yield return 1.5f;
        Log($"After Esc: pause open {Menu.IsOpen<GamePauseMenu>()}; {CursorProbe.Describe()}");
        Click(Find(Menu.GetInstance<GamePauseMenu>(), ExamplePause + "_Level_Back")); // up to the pause menu's own buttons
        yield return 1f;

        // Two bindings to one entry: switch "Show the HUD line" off in the Mods window, the example window shows it off too.
        pause = Menu.GetInstance<GamePauseMenu>();
        Click(Find(pause, ModsPause));
        yield return 1.5f;
        Click(Find(ModRowOf(Window(n => n == ModsList), "mjolnir.uiexample"), "Settings"));
        yield return 1.5f;
        page = Window(n => n.StartsWith(ExampleSettings, StringComparison.Ordinal));
        Click(Find(Setting(page, "HUD.ShowText"), "Increase"));
        yield return 0.5f;
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;
        pause = Menu.GetInstance<GamePauseMenu>();
        Click(Find(pause, ExamplePause));
        yield return 1f;
        Click(Find(pause, ExampleWindowItem));
        yield return 1.5f;
        window = Window(n => n == ExampleWindow);
        Log($"Example window after the Mods page switched the HUD line off: 'Show the HUD line' = {RowValue(Row(window, "Show the HUD line").transform)}");
        Click(Find(Row(window, "Show the HUD line"), "Increase"));
        yield return 0.5f;
        MenuManager.Instance.BackPressTopMenu();
        yield return 1.5f;
        Click(Find(Menu.GetInstance<GamePauseMenu>(), ExamplePause + "_Level_Back"));
        yield return 1f;

        // A round trip through Settings re-activates the pause menu: still exactly three mod buttons.
        pause = Menu.GetInstance<GamePauseMenu>();
        Click(Find(pause, "ButtonSettings"));
        yield return 1.5f;
        Menu.GetInstance<GameSettingsMenu>()?.Close();
        yield return 1.5f;
        pause = Menu.GetInstance<GamePauseMenu>();
        Log($"Pause menu after Settings: {Count(pause, "Mjolnir_Pause_")} mod button(s) (3 expected)");

        // Two windows in a stack, a dialog above them; each Back closes one layer. A value set from another thread
        // reaches the open window on the next frame.
        Click(Find(pause, LabPause));
        yield return 1.5f;
        Log($"Two windows: first open {UiLab.First.IsOpen}, second open {UiLab.Second.IsOpen}");
        // A real thread (Task.Wait may run a task inline on the waiting main thread).
        var worker = new System.Threading.Thread(() => UiLab.TaskText.Text = "set from another thread");
        worker.Start();
        worker.Join();
        yield return 0.5f;
        // The first window sits under the second, so the game has deactivated it: look among inactive objects too.
        var first = Object.FindObjectsByType<GameContextMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .First(m => m.name == "Mjolnir_Window_UiLab 1");
        var taskRow = first.GetComponentsInChildren<Transform>(true).First(t => t.name == "TaskText");
        Log($"Task text in the window: {RowValue(taskRow)}");
        Log($"Disabled panel: {Dimming(first.gameObject, "LabDisabledPanel")}; inner enabled {Dimming(first.gameObject, "LabInnerEnabled")}; " +
            $"inner disabled {Dimming(first.gameObject, "LabInnerDisabled")} (all 0.5 expected: dimmed once)");
        var second = GameObject.Find("Mjolnir_Window_UiLab 2");
        if (second != null) ClickLabeled(second, "Ask");
        yield return 1.5f;
        Shot("dialog-over-windows");
        yield return AfterShot;
        for (int back = 1; back <= 3; back++)
        {
            MenuManager.Instance.BackPressTopMenu();
            yield return 1.5f;
            Log($"After Back #{back}: first open {UiLab.First.IsOpen}, second open {UiLab.Second.IsOpen}, " +
                $"pause open {Menu.IsOpen<GamePauseMenu>()}, events opened {UiLab.Opened} closed {UiLab.Closed}");
        }

        var question = Dialog.ConfirmAsync("UiLab", "An awaited question.");
        yield return 1.5f;
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;
        Log($"ConfirmAsync after Esc: completed {question.IsCompleted}, answer {(question.IsCompleted ? question.Result.ToString() : "none")}; " +
            $"main thread context {System.Threading.SynchronizationContext.Current?.GetType().Name ?? "none"}");
        var prompt = Dialog.PromptAsync("UiLab", "Your name?", "Ragnar", placeholder: "A name", maxLength: 20);
        yield return 1.5f;
        var promptMenu = Menu.GetInstance<InputFieldConfirmActionMenu>();
        Log($"Prompt open {promptMenu != null}, its field '{promptMenu?.InputFieldHelper?.inputField?.text}'");
        Shot("prompt");
        yield return AfterShot;
        if (promptMenu != null)
        {
            promptMenu.InputFieldHelper.inputField.text = "Bjorn";
            Click(promptMenu.ConfirmButton.gameObject);
        }
        yield return 1f;
        Log($"PromptAsync: completed {prompt.IsCompleted}, answer '{(prompt.IsCompleted ? prompt.Result : "none")}' (Bjorn expected)");

        // Lifetime: what a mod adds can be taken away. A HUD text and a pause button come and go; a window made for one
        // use is disposed while open, another in the frame it was shown; their objects go once the game has closed them.
        var hudLine = new UiTextBlock { Name = "LabHudLine", Text = "UiLab: a removable line" };
        Hud.Add(hudLine);
        var extra = new UiButton { Content = "UiLab: removable" };
        Menus.Pause.Add(extra);
        yield return 0.5f;
        pause = Menu.GetInstance<GamePauseMenu>();
        Log($"Lifetime added: HUD line {Exists("LabHudLine")}, {Count(pause, "Mjolnir_Pause_")} mod button(s) (4 expected)");
        Log($"Lifetime Remove: HUD {Hud.Remove(hudLine)}, again {Hud.Remove(hudLine)}; pause {Menus.Pause.Remove(extra)}, again {Menus.Pause.Remove(extra)}");
        yield return 0.5f; // Destroy runs at the end of the frame
        Log($"Lifetime removed: HUD line {Exists("LabHudLine")}, {Count(pause, "Mjolnir_Pause_")} mod button(s) (3 expected)");

        int onceClosed = 0;
        var once = new UiWindow { Title = "UiLab once", Width = 500, Height = 300, Content = new UiTextBlock { Text = "Disposed while open" } };
        once.Closed += (_, _) => onceClosed++;
        once.Show();
        yield return 1.5f;
        Log($"Lifetime window: open {once.IsOpen}, object {Exists("Mjolnir_Window_UiLab once")}, menu stack {MenuManager.Instance.MenuStackCount}");
        once.Dispose();
        var flash = new UiWindow { Title = "UiLab flash", Width = 500, Height = 300 };
        flash.Show();
        flash.Dispose(); // in the frame it was shown: the game is still opening it
        yield return 2f;
        bool showThrows = false;
        try
        {
            once.Show();
        }
        catch (ObjectDisposedException)
        {
            showThrows = true;
        }
        Log($"Lifetime disposed: once open {once.IsOpen}, object {Exists("Mjolnir_Window_UiLab once")}, closed events {onceClosed}, " +
            $"Show throws {showThrows}; flash object {Exists("Mjolnir_Window_UiLab flash")}; menu stack {MenuManager.Instance.MenuStackCount}, " +
            $"menu manager idle {MenuManager.Instance._workRoutine == null}, pause open {Menu.IsOpen<GamePauseMenu>()}");

        // Put the example's settings back: Mods → UI Example → Reset → confirm in the game's dialog.
        pause = Menu.GetInstance<GamePauseMenu>();
        Click(Find(pause, ModsPause));
        yield return 1.5f;
        Click(Find(ModRowOf(Window(n => n == ModsList), "mjolnir.uiexample"), "Settings"));
        yield return 1.5f;
        page = Window(n => n.StartsWith(ExampleSettings, StringComparison.Ordinal));
        Click(Find(page, "Reset"));
        yield return 1.5f;
        Shot("reset-dialog");
        yield return AfterShot;
        var confirm = Menu.GetInstance<ConfirmActionMenu>();
        Click(confirm.ConfirmButton.GetComponentInChildren<Button>(true).gameObject);
        yield return 1f;
        Log($"Settings page after Reset: {Describe(page)}");
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;
        Log($"Example config after Reset: {ConfigValues("mjolnir.uiexample")}; HUD {HudTexts()}");

        Menu.GetInstance<GamePauseMenu>()?.Close();
        yield return 2f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // Loads the save named SaveName from the main menu and waits until the world's HUD is up: in single player (its Load
    // game page) or hosted (the multiplayer page of the same kind; the page's own password field is left as it is).
    private static IEnumerable<float> LoadSave(MainMenu mainMenu)
    {
        Click(Find(mainMenu, Multiplayer ? "ButtonMultiplayer" : "ButtonSingleplayer"));
        foreach (float wait in WaitFor("the load button", () => Active(mainMenu, "ButtonLoadGame"), 90)) yield return wait;
        yield return 1f;
        Click(Find(mainMenu, "ButtonLoadGame"));
        SaveSlotPanel? slot = null;
        foreach (float wait in WaitFor($"save slot '{SaveName}'", () => (slot = FindSlot(mainMenu)) != null, 20)) yield return wait;
        yield return 0.5f;
        slot!.toggle.isOn = true;
        yield return 0.5f;
        Click(Find(mainMenu, "ButtonPlay"));
        float clicked = Time.realtimeSinceStartup;
        bool looked = false;
        bool Loaded()
        {
            if (HudReady || looked || Time.realtimeSinceStartup - clicked < 30f) return HudReady;
            looked = true;
            // After a long stay in the main menu the click sometimes starts nothing (twice in three runs of the UI scenario,
            // hosted, nothing on the screen says why): the button still stands there, and a second click loads.
            if (mainMenu != null && Active(mainMenu, "ButtonPlay"))
            {
                Shot("load-not-started");
                Click(Find(mainMenu, "ButtonPlay"));
            }
            return false;
        }
        foreach (float wait in WaitFor("HUD", Loaded, 240)) yield return wait;
    }

    private static bool Active(Component root, string name) => root.GetComponentsInChildren<Transform>(false).Any(t => t.name == name);

    private static IEnumerable<float> WaitFor(string what, Func<bool> condition, float timeout)
    {
        float until = Time.realtimeSinceStartup + timeout;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > until) throw new TimeoutException($"waited {timeout} s for {what}");
            yield return 0.25f;
        }
        Log($"Autopilot: {what} ready");
    }

    /// <summary>A real click: every IPointerClickHandler on the object (Button, TabButton…), as the mouse would.</summary>
    private static void Click(GameObject go)
    {
        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        bool handled = ExecuteEvents.Execute(go, data, ExecuteEvents.pointerClickHandler);
        Log($"Click '{go.name}': {(handled ? "handled" : "no click handler")}");
    }

    private static void ClickLabeled(GameObject root, string label)
    {
        var button = root.GetComponentsInChildren<Button>(false)
                         .FirstOrDefault(b => b.GetComponentInChildren<TextMeshProUGUI>(true)?.text == label);
        if (button == null) Log($"No button '{label}' in '{root.name}'");
        else Click(button.gameObject);
    }

    private static GameObject Find(Component root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name && t.gameObject.activeInHierarchy)?.gameObject
        ?? throw new InvalidOperationException($"no active '{name}' under '{root.name}'");

    private static GameObject Find(GameObject root, string name) => Find(root.transform, name);

    // What each row shows left of its label: a picture (its sprite's size), a letter tile, or nothing.
    private static string Icons(GameObject window) =>
        string.Join(" | ", window.GetComponentsInChildren<Transform>(false).Where(t => RowLabel(t) != null).Select(row =>
        {
            var icon = row.Find("Icon");
            if (icon == null) return $"{RowLabel(row)}: none";
            var size = icon.GetComponent<RectTransform>().rect.size;
            string letter = icon.Find("Letter")?.GetComponent<TMP_Text>()?.text ?? "";
            var sprite = icon.GetComponent<Image>()?.sprite;
            string shown = sprite != null ? $"picture {sprite.rect.width:0}x{sprite.rect.height:0}" : $"letter '{letter}'";
            return $"{RowLabel(row)}: {shown} in {size.x:0}x{size.y:0}";
        }));

    // A UiProgressBar's copy: how full its "Fill" image is, its text, and its size.
    private static string Progress(GameObject? bar)
    {
        if (bar == null) return "missing";
        var fill = bar.transform.Find("Fill")?.GetComponent<Image>();
        string text = bar.transform.Find("Text")?.GetComponent<TMP_Text>()?.text ?? "";
        var size = bar.GetComponent<RectTransform>().rect.size;
        return $"fill {(fill != null ? fill.fillAmount.ToString("0.###") : "none")}, text '{text}', {size.x:0}x{size.y:0}";
    }

    // Whether the object of that name under the window is shown.
    // Active and not faded out: a footer's note keeps its place while its opacity hides it.
    private static bool Shown(GameObject window, string name) =>
        window.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) is { } part && part.gameObject.activeInHierarchy &&
        (part.GetComponent<CanvasGroup>() is not { } group || group.alpha > 0.5f);

    // Where a window's footer sits: its height, the scroll view's bottom above it, and its parts across the window's panel.
    private static string FooterLayout(GameObject window)
    {
        var panel = window.transform.Find("Panel")?.GetComponent<RectTransform>();
        var footer = panel?.Find("Footer")?.GetComponent<RectTransform>();
        if (panel == null || footer == null) return "none";
        string Span(string name)
        {
            var part = footer.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)?.GetComponent<RectTransform>();
            if (part == null) return $"{name} none";
            float left = panel.InverseTransformPoint(part.TransformPoint(new Vector3(part.rect.xMin, 0, 0))).x - panel.rect.xMin;
            float right = panel.InverseTransformPoint(part.TransformPoint(new Vector3(part.rect.xMax, 0, 0))).x - panel.rect.xMin;
            return $"{name} {left:0}..{right:0}";
        }
        var scroll = panel.Find("Scroll")?.GetComponent<RectTransform>();
        return $"height {footer.rect.height:0} at {footer.anchoredPosition.y:0}, scroll from {scroll?.offsetMin.y:0}; {Span("RestartNote")}, " +
               $"{Span("PlatformSettings")}, {Span("Reset")}, {Span("Close")} in a panel {panel.rect.width:0} wide";
    }

    // A settings row's description line (SettingRows names it "Description").
    private static string RowDescription(GameObject row) => row.transform.Find("Description")?.GetComponent<TMP_Text>()?.text ?? "";

    // Any object of that name, active or not (a destroyed one is gone from the frame after Destroy).
    private static bool Exists(string name) =>
        Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(t => t.name == name);

    // A menu's mod buttons and items (Mjolnir_<menu>_<mod>_<n>), not the levels and items inside an item (…_<n>_Level, …_<n>_<m>).
    private static int Count(Component root, string prefix) =>
        root.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith(prefix, StringComparison.Ordinal) && t.gameObject.activeSelf &&
                                                                 t.name[prefix.Length..].Count(c => c == '_') == 1);

    private static SaveSlotPanel? FindSlot(Component root) =>
        root.GetComponentsInChildren<SaveSlotPanel>(false).FirstOrDefault(s => s.NameLabel != null && s.NameLabel.text.Trim() == SaveName);

    // An open mod window (a game menu) by its object name, Mjolnir_Window_<title>.
    private static GameObject? TryWindow(Func<string, bool> name) =>
        Object.FindObjectsByType<GameContextMenu>(FindObjectsSortMode.None).FirstOrDefault(m => name(m.name))?.gameObject;

    private static GameObject Window(Func<string, bool> name) => TryWindow(name) ?? throw new InvalidOperationException("mod window not open");

    // A settings row of a mod window by the start of its label.
    private static GameObject Row(GameObject window, string label) =>
        window.GetComponentsInChildren<Transform>(false)
              .FirstOrDefault(t => RowLabel(t) is { } text && text.StartsWith(label, StringComparison.Ordinal))?.gameObject
        ?? throw new InvalidOperationException($"no row '{label}' in '{window.name}'");

    // A setting's row on a Mods window page, by its object name (Setting <section>.<key>): the same in every language.
    private static GameObject Setting(GameObject page, string sectionKey) => Find(page, $"Setting {sectionKey}");

    // A mod's row in the Mods window, by its object name (Mod <id>).
    private static GameObject ModRowOf(GameObject list, string id) => Find(list, $"Mod {id}");

    private static GameObject? TryFind(GameObject root, string name) =>
        root.GetComponentsInChildren<Transform>(false).FirstOrDefault(t => t.name == name)?.gameObject;

    // Where a mod row's parts sit across it, from its left edge: the name and status line end before the Settings button,
    // which ends before the selector's left arrow.
    private static string ModRowLayout(GameObject row)
    {
        var rect = row.GetComponent<RectTransform>();
        string Span(string name)
        {
            var part = row.transform.Find(name)?.GetComponent<RectTransform>();
            if (part == null) return $"{name} none";
            float left = rect.InverseTransformPoint(part.TransformPoint(new Vector3(part.rect.xMin, 0, 0))).x - rect.rect.xMin;
            float right = rect.InverseTransformPoint(part.TransformPoint(new Vector3(part.rect.xMax, 0, 0))).x - rect.rect.xMin;
            return $"{name} {left:0}..{right:0}";
        }
        return $"width {rect.rect.width:0}: {Span("NameLabel")}, {Span("Description")}, {Span("Settings")}, {Span("Decrease")}, " +
               $"{Span("Amount")}, {Span("Increase")}";
    }

    // The open confirm dialog's texts.
    private static string DialogText()
    {
        var menu = Menu.GetInstance<ConfirmActionMenu>();
        return menu == null ? "no dialog" : string.Join(" / ", menu.GetComponentsInChildren<TMP_Text>(false).Select(t => t.text).Where(t => t.Length > 0));
    }

    private static string ModListFile()
    {
        string path = Path.Combine(ConfigDir, "mod-list.json");
        return File.Exists(path) ? string.Concat(File.ReadAllText(path).Where(c => !char.IsWhiteSpace(c))) : "no file";
    }

    private static string? RowLabel(Transform row) => row.Find("NameLabel")?.GetComponent<TMP_Text>()?.text;

    // Whether a button takes clicks: a dimmed one has a CanvasGroup that is not interactable.
    // How dim an object is drawn: the product of the CanvasGroup alphas from it up to its canvas, and whether it takes input.
    private static string Dimming(GameObject window, string name)
    {
        var target = window.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        if (target == null) return "not found";
        float alpha = 1;
        bool interactable = true;
        foreach (var group in target.GetComponentsInParent<CanvasGroup>(true))
        {
            alpha *= group.alpha;
            interactable &= group.interactable;
            if (group.ignoreParentGroups) break;
        }
        return $"alpha {alpha:0.##}, interactable {interactable}";
    }

    private static string ButtonText(GameObject row) => row.transform.Find("Button")?.GetComponentInChildren<TMP_Text>(true)?.text ?? "none";

    // A grid's rows and the widths of the first row's cells.
    private static string Cells(Transform grid)
    {
        if (grid.childCount == 0) return "no rows";
        var first = grid.GetChild(0);
        var widths = new List<string>();
        for (int i = 0; i < first.childCount; i++) widths.Add($"{first.GetChild(i).GetComponent<RectTransform>().rect.width:0}");
        return $"{grid.childCount} row(s), first row's cells {string.Join(" + ", widths)} wide";
    }

    private static string Ring(GameObject ring)
    {
        var fill = ring.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Fill");
        var text = ring.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Text");
        var frame = ring.transform.Find("Ring");
        return $"fill {(fill != null ? fill.fillAmount.ToString("0.##") : "none")}, text '{text?.text}' in #{(text != null ? ColorUtility.ToHtmlStringRGB(text.color) : "none")}, " +
               $"{ring.GetComponent<RectTransform>().rect.width:0}x{ring.GetComponent<RectTransform>().rect.height:0}, scale {(frame != null ? frame.localScale.x.ToString("0.##") : "none")}";
    }

    // The middle of an expander's arrow as drawn, from the right middle of its header row: turning must not move it.
    private static string Arrow(Transform expander)
    {
        var header = expander.Find("Header")?.GetComponent<RectTransform>();
        var arrow = expander.Find("Header/Arrow")?.GetComponent<RectTransform>();
        if (header == null || arrow == null) return "none";
        var center = arrow.rect.center;
        var middle = header.InverseTransformPoint(arrow.TransformPoint(new Vector3(center.x, center.y, 0)));
        return $"{middle.x - header.rect.xMax:0}, {middle.y - header.rect.center.y:0} at {arrow.localEulerAngles.z:0}°";
    }

    private static string Interactable(GameObject window, string name) =>
        Find(window, name).GetComponent<CanvasGroup>() is { } group ? $"interactable {group.interactable}, alpha {group.alpha:0.#}" : "interactable True";

    // The left edge of a rect in its own local space, relative to its pivot (layout groups place by pivot).
    private static float LeftEdge(RectTransform rect) => -rect.rect.width * rect.pivot.x;

    // Every row of a window as "label = value", the value as its control shows it.
    private static string Describe(GameObject window) =>
        string.Join(" | ", window.GetComponentsInChildren<Transform>(false).Where(t => RowLabel(t) != null)
                                 .Select(t => $"{RowLabel(t)} = {RowValue(t)}"));

    private static string RowValue(Transform row)
    {
        if (row.Find("Amount")?.GetComponent<TMP_Text>() is { } selector) return selector.text;
        if ((row.Find("valueLabel") ?? row.Find("Value"))?.GetComponent<TMP_Text>() is { } value) return value.text;
        if (row.GetComponentInChildren<TMP_InputField>(false) is { } input) return $"'{input.text}'";
        if (row.GetComponent<TMP_Dropdown>() is { } dropdown) return dropdown.captionText.text;
        if (row.Find("Button")?.GetComponentInChildren<TMP_Text>() is { } button) return $"[{button.text}]";
        return "";
    }

    // Where each open item's checkbox frame sits against its tick (0 = on the box).
    private static string CheckboxOffsets(TMP_Dropdown dropdown)
    {
        var parts = dropdown.GetComponentsInChildren<Transform>(false);
        var frames = parts.Where(t => t.name == "Item CheckmarkFrame").Select(t => t.TryCast<RectTransform>()!.anchoredPosition.x).ToList();
        var boxes = parts.Where(t => t.name == "Item CheckmarkBackground").Select(t => t.TryCast<RectTransform>()!.anchoredPosition.x).ToList();
        return frames.Count == 0 ? "no open items"
            : $"{frames.Count} item(s), frame x {string.Join("/", frames.Select(x => x.ToString("0.##")).Distinct())}, " +
              $"box x {string.Join("/", boxes.Select(x => x.ToString("0.##")).Distinct())}";
    }

    // The example's HUD column: the configured line (hidden while switched off) and the clock.
    private static string HudTexts()
    {
        static string Show(string name) => GameObject.Find(name)?.GetComponent<TextMeshProUGUI>() is { } text ? $"'{text.text}'" : "hidden";
        return $"line {Show("ExampleHudLine")}, clock {Show("ExampleHudClock")}";
    }

    private static string ConfigValues(string modId)
    {
        string path = Path.Combine(ConfigDir, modId + ".toml");
        return File.Exists(path)
            ? string.Join("; ", File.ReadAllLines(path).Where(l => l.Contains('=') && !l.TrimStart().StartsWith('#')))
            : "no file";
    }

    // The settlement at work, not asleep, without moving the sun when it is day already: the clock goes to 10:00 only outside
    // working hours. Set always, it turned the picture cooler and paler at once (the save loads at 12:53, and 10:00's light
    // is another; UiLab "colors", seen by the user).
    private static void KeepDaytime()
    {
        if (!Mjolnir.SDK.World.WorldTime.IsAvailable) return;
        double hour = Mjolnir.SDK.World.WorldTime.Hour;
        if (hour is >= 8 and < 18)
        {
            Log(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"The clock left at {hour:0.00}: the settlement is at work"));
            return;
        }
        Log(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"The clock from {hour:0.00} to 10:00: the settlement was asleep"));
        Mjolnir.SDK.World.WorldTime.Hour = 10;
    }

    private static void Shot(string name)
    {
        string path = Path.Combine(ShotsDir, $"{++s_shot:00}-{name}.png");
        ScreenCapture.CaptureScreenshot(path);
        Log($"Screenshot {path}");
    }
}
