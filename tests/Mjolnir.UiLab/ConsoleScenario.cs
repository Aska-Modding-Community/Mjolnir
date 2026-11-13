using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mjolnir.SDK;
using Mjolnir.SDK.Player;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using SSSGame;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mjolnir.UiLab;

/// <summary>
/// The "console" scenario (autopilot.once containing "console"; with -Examples): the SDK's developer console in game. In
/// the main menu the window opens (as its key would: ConsoleWindow.Toggle, by reflection), its field takes the focus,
/// and lines typed and submitted as Enter does run help, pos, items, give and an unknown command; Esc closes it, and so
/// does its key pressed while its field has the focus (GameKeyboard.PressOverride), the typed line kept. In the world pos,
/// give (an asset name, an English name, a mod's item by its full name, a name several items share), the
/// ItemsExample's own twine command, tp a few metres, far away onto the ground (the sea, then inland) and back exactly;
/// the items given are taken out again.
/// </summary>
internal static partial class Autopilot
{
    private const string ConsoleWindowName = "Mjolnir_Window_Console";

    private static readonly Type? s_consoleWindow = typeof(Mjolnir.SDK.UI.UiWindow).Assembly.GetType("Mjolnir.SDK.UI.ConsoleWindow");
    private static readonly PropertyInfo? s_consoleLines = typeof(DevConsole).GetProperty("Lines", BindingFlags.Static | BindingFlags.NonPublic);

    // The SDK's stand-in for a key press (GameKeyboard.PressOverride, internal): set by reflection.
    private static readonly FieldInfo? s_pressOverride = typeof(Mjolnir.SDK.UI.UiWindow).Assembly
        .GetType("Mjolnir.SDK.UI.GameKeyboard")?.GetField("PressOverride", BindingFlags.Static | BindingFlags.NonPublic);

    private static IEnumerator<float> ConsoleScenario()
    {
        // The game waits for its online login before the main menu: up to two minutes when the service refuses it.
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        Log($"Console: {DevConsole.Commands.Count} commands: {string.Join(", ", DevConsole.Commands.Select(c => c.FullName))}");

        foreach (float wait in OpenConsole("menu")) yield return wait;
        foreach (string line in new[] { "help", "pos", "items rope", "give rope", "nope" })
            foreach (float wait in Type(line)) yield return wait;
        Shot("console-menu");
        yield return AfterShot;
        Log($"Console: the window's last lines: {WindowText(4)}");
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;
        Log($"Console: after Esc the window is open {ConsoleOpen()}");

        // The console's key closes it from its own field too (hotkeys wait while the player types), keeping what was typed.
        foreach (float wait in OpenConsole("menu again")) yield return wait;
        var field = ConsoleField();
        if (field != null) field.text = "pos";
        yield return 0.5f;
        PressNextFrame(ConsoleKey());
        yield return 1f;
        Log($"Console: after its key in the field the window is open {ConsoleOpen()} (False expected), the field '{field?.text}' focused {field?.isFocused}");
        foreach (float wait in OpenConsole("after its key")) yield return wait;
        Log($"Console: opened again, the field '{ConsoleField()?.text}' (pos expected)");
        if (ConsoleField() is { } again) again.text = "";
        yield return 0.3f;
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;

        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 4f;
        var inventory = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.Inventory?.GetItemCollection();
        var db = GameManagers.Get<ItemDatabaseManager>()?.Database;
        var sticks = db?.GetItemInfo("Item_Wood_Sticks");
        var twine = db?.GetItemInfo(TwineAsset);
        int Count(ItemInfo? info) => inventory != null && info != null ? inventory.GetItemQuantity(info) : -1;
        int sticksBefore = Count(sticks), twineBefore = Count(twine);
        var start = LocalPlayer.Position;
        Log($"Console: in the world at {P(start)}, {sticksBefore} sticks, {twineBefore} twine");

        foreach (float wait in OpenConsole("world")) yield return wait;
        foreach (float wait in KeysWhileTyping()) yield return wait;
        foreach (string line in new[] { "pos", "give Item_Wood_Sticks 5", "give twine 2", "give mjolnir.itemsexample.twine", "give stone", "give Бечёвка", "items rope", "twine 3", "help twine" })
            foreach (float wait in Type(line)) yield return wait;
        Log($"Console: the inventory holds {Count(sticks)} sticks ({sticksBefore} + 5 expected), {Count(twine)} twine ({twineBefore} + 6 expected)");
        Shot("console-world");
        yield return AfterShot;

        foreach (float wait in Type("tp ~6 ~")) yield return wait;
        foreach (float wait in WaitFor("the near landing", () => LastLines(1).Any(l => l.Contains("On the ground") || l.Contains("No ground")), 12)) yield return wait;
        Log($"Console: near: {P(LocalPlayer.Position)}, {Vector3.Distance(LocalPlayer.Position, start):0.0} m from the start");
        // Far away twice: 300 m east is the sea (its bottom is the ground, the player swims up), 300 m west of the start inland.
        foreach (string far in new[] { "tp ~300 ~", "tp ~-600 ~" })
        {
            foreach (float wait in Type(far)) yield return wait;
            foreach (float wait in WaitFor($"the landing of '{far}'", () => LastLines(1).Any(l => l.Contains("On the ground") || l.Contains("No ground")), 15)) yield return wait;
            Log($"Console: '{far}' landed: {LastLines(1).First()}; at once {P(LocalPlayer.Position)}");
            yield return 2f;
            Log($"Console: '{far}' two seconds later {P(LocalPlayer.Position)}, health {LocalPlayer.Health.Value:0}");
        }
        MenuManager.Instance.BackPressTopMenu();
        yield return 1.5f;
        Shot("console-far");
        yield return AfterShot;

        foreach (float wait in OpenConsole("world again")) yield return wait;
        foreach (float wait in Type(FormattableString.Invariant($"tp {start.x} {start.y} {start.z}"))) yield return wait;
        yield return 1.5f;
        Log($"Console: back: {P(LocalPlayer.Position)}, {Vector3.Distance(LocalPlayer.Position, start):0.00} m from the start");
        MenuManager.Instance.BackPressTopMenu();
        yield return 1f;
        if (GameManagers.Get<InputManager>() is { } closed) LogInput(closed, "the console closed");

        if (inventory != null && sticks != null && twine != null)
        {
            inventory.RemoveItems(sticks, Count(sticks) - sticksBefore);
            inventory.RemoveItems(twine, Count(twine) - twineBefore);
            yield return 0.5f;
            Log($"Console: given items taken out: {Count(sticks)} sticks, {Count(twine)} twine left");
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    // While the player types into the console: the game's keys stand aside (the typing context: no walking, menu keys or
    // menu controls), Space (the UI map's Submit) types instead of submitting, W (Navigate) keeps the focus; the window
    // moves when its panel is dragged, inside the screen, and the field takes the focus again after the drag.
    private static IEnumerable<float> KeysWhileTyping()
    {
        var input = GameManagers.Get<InputManager>();
        var field = ConsoleField();
        var window = TryWindow(n => n == ConsoleWindowName);
        if (input == null || field == null || window == null)
        {
            Log("Console: keys while typing: no input manager, field or window");
            yield break;
        }
        LogInput(input, $"typing in the console, field focused {field.isFocused}, navigation {field.navigation.mode}");

        int lines = ConsoleLines().Count;
        field.text = "pos";
        yield return 0.3f;
        ExecuteEvents.Execute(field.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        yield return 0.3f;
        Log($"Console: after the UI's Submit (Space) the field '{field.text}' focused {field.isFocused}, {ConsoleLines().Count - lines} new console lines (0 expected)");
        ExecuteEvents.Execute(field.gameObject, new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Up }, ExecuteEvents.moveHandler);
        yield return 0.3f;
        Log($"Console: after the UI's Navigate (W) the field focused {field.isFocused}, selected '{EventSystem.current?.currentSelectedGameObject?.name}'");
        field.text = "";

        var panel = window.transform.Find("Panel")?.GetComponent<RectTransform>();
        var canvas = window.GetComponent<Canvas>();
        if (panel == null || canvas == null) yield break;
        var before = panel.anchoredPosition;
        Drag(panel.gameObject, new Vector2(200, 100) * canvas.scaleFactor);
        yield return 0.5f;
        Log($"Console: dragged by (200, 100): {P2(before)} -> {P2(panel.anchoredPosition)}, size {SizeOf(window)}, field focused {field.isFocused} after the drag");
        Drag(panel.gameObject, new Vector2(5000, 5000) * canvas.scaleFactor);
        yield return 0.5f;
        Log($"Console: dragged far: {P2(panel.anchoredPosition)} (inside the screen expected: at most (428, 268) on 1080p)");
        Drag(panel.gameObject, -panel.anchoredPosition * canvas.scaleFactor);
        yield return 0.5f;
        Log($"Console: dragged back to {P2(panel.anchoredPosition)}");
    }

    private static void Drag(GameObject target, Vector2 delta)
    {
        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, delta = delta };
        ExecuteEvents.Execute(target, data, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(target, data, ExecuteEvents.endDragHandler);
    }

    private static string P2(Vector2 v) => $"({v.x:0}, {v.y:0})";

    // Opens the console as its key does, and waits for its field to take the focus.
    private static IEnumerable<float> OpenConsole(string where)
    {
        s_consoleWindow?.GetMethod("Toggle", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
        yield return 1f;
        var window = TryWindow(n => n == ConsoleWindowName);
        var field = window?.GetComponentInChildren<TMP_InputField>(true);
        Log($"Console ({where}): window {(window == null ? "missing" : $"'{window.name}' {SizeOf(window)}")}, field focused {field?.isFocused}, " +
            $"placeholder '{field?.placeholder?.TryCast<TMP_Text>()?.text}'");
    }

    // A line typed into the console's field and submitted, as Enter does.
    private static IEnumerable<float> Type(string line)
    {
        var field = TryWindow(n => n == ConsoleWindowName)?.GetComponentInChildren<TMP_InputField>(true);
        if (field == null)
        {
            Log($"Console: no field for '{line}'");
            yield break;
        }
        int before = ConsoleLines().Count;
        field.text = line;
        field.onSubmit.Invoke(line);
        yield return 0.5f;
        var lines = ConsoleLines();
        Log($"Console: '{line}' → {string.Join(" | ", lines.Skip(Math.Max(0, Math.Min(before, lines.Count - 12))).Take(12))}; " +
            $"the field '{field.text}' focused {field.isFocused}");
    }

    private static List<string> ConsoleLines()
    {
        var lines = new List<string>();
        if (s_consoleLines?.GetValue(null) is not IEnumerable all) return lines;
        foreach (object line in all)
        {
            var type = line.GetType();
            lines.Add($"{type.GetProperty("Kind")?.GetValue(line)}: {type.GetProperty("Text")?.GetValue(line)}");
        }
        return lines;
    }

    private static IEnumerable<string> LastLines(int count) => ConsoleLines().TakeLast(count);

    private static bool ConsoleOpen() => TryWindow(n => n == ConsoleWindowName) != null;

    private static TMP_InputField? ConsoleField() => TryWindow(n => n == ConsoleWindowName)?.GetComponentInChildren<TMP_InputField>(true);

    // The console's key, the player's setting [Console] Key (the stand-in presses no modifiers).
    private static string ConsoleKey()
    {
        object? entry = s_consoleWindow?.GetProperty("KeyEntry", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
        var gesture = entry?.GetType().GetProperty("Value")?.GetValue(entry) is KeyGesture value ? value : KeyGesture.None;
        Log($"Console: its key is '{gesture}'");
        return gesture.Key;
    }

    // A key the SDK reads as pressed in the next frame, whatever runs first in it.
    private static void PressNextFrame(string key)
    {
        s_pressOverride?.SetValue(null, (key, Time.frameCount + 1));
        Log($"Console: {key} pressed in frame {Time.frameCount + 1} (stand-in {(s_pressOverride == null ? "missing" : "set")})");
    }

    // The last lines the window draws (its text component, rich text as given to it).
    private static string WindowText(int count)
    {
        var window = TryWindow(n => n == ConsoleWindowName);
        var text = window?.GetComponentsInChildren<TMP_Text>(true).Where(t => t.text.Contains("noparse")).OrderByDescending(t => t.text.Length).FirstOrDefault();
        return text == null ? "none" : string.Join(" | ", text.text.Split('\n').TakeLast(count));
    }
}
