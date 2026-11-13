using System;
using System.Collections.Generic;
using System.Linq;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using Context = SSSGame.InputContext.Context;

namespace Mjolnir.UiLab;

/// <summary>
/// The "inputresearch" scenario (autopilot.once containing "inputresearch"): the game's input while the developer console
/// is open. In the world it logs the input contexts and which action maps are on: before the console, with the console
/// open and its field focused in code, after that field lets go (as after Enter), after the game's own way into a field (InputFieldHelper.OnPointerClick: its typing
/// context), after the field lets go (end of edit), and after the console closed; also the field helper's typing context
/// and the bindings of the UI map's Submit (what Space does in a field).
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> InputResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 3f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 4f;
        var input = GameManagers.Get<InputManager>();
        if (input == null)
        {
            Log("Input: no InputManager");
            yield break;
        }
        LogInput(input, "in the world");

        foreach (float wait in OpenConsole("input research")) yield return wait;
        yield return 0.5f;
        var field = ConsoleField();
        LogInput(input, $"console open, field focused {field?.isFocused}");
        var helper = field == null ? null : field.GetComponentInParent<InputFieldHelper>();
        Log($"Input: the field's helper {(helper == null ? "none" : $"on '{helper.name}', the field's own object {helper.gameObject.Pointer == field!.gameObject.Pointer}, field '{helper.inputField?.name}'")}; " +
            $"its typing context {Describe(helper?.inputFieldContext)}");
        LogSubmit();
        LogMap("UI");
        LogMap("MenuControls");

        // A command whose answer the game also shows: does its notification take the console's input context away?
        Mjolnir.SDK.DevConsole.Execute("give Item_Wood_Sticks 3");
        foreach (float after in new[] { 0.1f, 0.5f, 2f })
        {
            yield return after;
            LogInput(input, $"after give, field focused {field?.isFocused}; menu stack {MenuStack()}");
        }
        var sticks = GameManagers.Get<SandSailorStudio.Inventory.ItemDatabaseManager>()?.Database?.GetItemInfo("Item_Wood_Sticks");
        var bag = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter?.Inventory?.GetItemCollection();
        if (sticks != null && bag != null) bag.RemoveItems(sticks, 3);

        if (field != null && helper != null)
        {
            // As Enter does after a command: the field lets go, and the helper removes a typing context it never added.
            field.DeactivateInputField();
            yield return 0.6f; // past the helper's activation cooldown after an end of edit
            LogInput(input, $"after an end of edit without the helper's context, field focused {field.isFocused}");
            helper.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            yield return 0.5f;
            LogInput(input, $"after the helper's click path, field focused {field.isFocused}");
            field.DeactivateInputField();
            yield return 0.5f;
            LogInput(input, $"after the end of edit, field focused {field.isFocused}");
        }

        MenuManager.Instance.BackPressTopMenu();
        yield return 1.5f;
        LogInput(input, "console closed");

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static void LogInput(InputManager input, string when)
    {
        var contexts = new List<string>();
        for (int i = 0; i < input.contexts.Count; i++) contexts.Add(Describe(input.contexts[i]));
        var maps = Enum.GetValues<InputManager.InputMaps>().Distinct().Select(map =>
        {
            try
            {
                var actionMap = InputManager.GenInputActionMap(map);
                return actionMap == null ? $"{map}:none" : $"{map}:{(actionMap.enabled ? "on" : "off")}";
            }
            catch (Exception e)
            {
                return $"{map}:{e.GetType().Name}";
            }
        });
        Log($"Input ({when}): contexts [{string.Join("; ", contexts)}]; maps {string.Join(" ", maps)}");
    }

    // Every action of a map with its keyboard and mouse bindings.
    private static void LogMap(string name)
    {
        try
        {
            var map = InputManager.controls?.asset?.FindActionMap(name, false);
            if (map == null)
            {
                Log($"Input: no map {name}");
                return;
            }
            var actions = map.actions;
            var lines = new List<string>();
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                var bindings = action.bindings;
                var paths = new List<string>();
                for (int b = 0; b < bindings.Count; b++)
                {
                    string path = bindings[b].effectivePath ?? "";
                    if (path.StartsWith("<Keyboard>", StringComparison.Ordinal) || path.StartsWith("<Mouse>", StringComparison.Ordinal)) paths.Add(path);
                }
                lines.Add($"{action.name}={string.Join("|", paths)}");
            }
            Log($"Input: map {name} ({lines.Count} actions): {string.Join("; ", lines)}");
        }
        catch (Exception e)
        {
            Log($"Input: map {name} could not be read: {e.GetType().Name}: {e.Message}");
        }
    }

    private static string MenuStack()
    {
        var stack = MenuManager.Instance?._menuStack;
        if (stack == null) return "none";
        var names = new List<string>();
        var menus = stack.ToArray();
        for (int i = 0; i < menus.Length; i++) names.Add(menus[i] == null ? "null" : menus[i].name);
        return string.Join(" > ", names);
    }

    private static string Describe(Context? context) =>
        context == null ? "none"
            : $"{context.name} p{context.priority}{(context.additive ? " additive" : "")} [{string.Join(",", (context.inputMaps ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<InputManager.InputMaps>(0L)).Select(m => m.ToString()))}]";

    private static void LogSubmit()
    {
        try
        {
            var submit = InputManager.controls?.asset?.FindAction("UI/Submit", false);
            if (submit == null)
            {
                Log("Input: no UI/Submit action");
                return;
            }
            var bindings = submit.bindings;
            var paths = new List<string>();
            for (int i = 0; i < bindings.Count; i++) paths.Add(bindings[i].effectivePath);
            Log($"Input: UI/Submit enabled {submit.enabled}, bindings {string.Join(", ", paths)}");
        }
        catch (Exception e)
        {
            Log($"Input: UI/Submit could not be read: {e.GetType().Name}: {e.Message}");
        }
    }
}
