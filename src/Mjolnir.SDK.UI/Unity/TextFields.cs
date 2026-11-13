using System;
using System.Collections.Generic;
using HarmonyLib;
using SandSailorStudio;
using SSSGame;
using TMPro;
using Context = SSSGame.InputContext.Context;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The SDK's own text fields (the console's line, a <see cref="UiTextBox"/>) while the player types into one.
/// The game's keys stand aside: a field the SDK focuses itself (the console's)
/// holds a typing context of the SDK's own, above every game context, with only the UI map (the pointer and the wheel)
/// on; and Space, the UI map's Submit, types a space instead of submitting the field and letting go of it.
/// </summary>
internal static class TextFields
{
    private static readonly HashSet<IntPtr> s_fields = [];
    private static InputManager? s_input;
    private static int s_holders;

    /// <summary>A field of the SDK's own (main thread).</summary>
    public static void Add(TMP_InputField field) => s_fields.Add(field.Pointer);

    public static void Remove(TMP_InputField field) => s_fields.Remove(field.Pointer);

    public static bool IsOurs(TMP_InputField field) => s_fields.Contains(field.Pointer);

    /// <summary>
    /// Whether a field's owner holds the typing context, checked once a frame (<paramref name="held"/> is the owner's
    /// own flag): it goes on while the field has the focus and off when it lets go or its window closes.
    /// </summary>
    public static void Hold(ref bool held, bool typing)
    {
        if (held == typing) return;
        held = typing;
        if (typing)
        {
            if (s_holders++ > 0) return;
            s_input = GameManagers.Get<InputManager>();
            s_input?.AddContext(Theme.TypingContext);
        }
        else
        {
            if (s_holders == 0 || --s_holders > 0) return;
            // The input manager it went on (a scene loaded since may have another one, which never had it).
            if (s_input != null) s_input.RemoveContext(Theme.TypingContext);
            s_input = null;
        }
    }
}

/// <summary>Hidden patches behind <see cref="TextFields"/> (the hybrid Harmony rule: mods never see them).</summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class TextFieldPatches
{
    // The UI map's Submit is Space: TMP takes it as "submit" and lets go of the field. On a field of ours that the player
    // types into, Space is a space (Enter still submits: TMP reads it as a key, not through the UI map).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TMP_InputField), nameof(TMP_InputField.OnSubmit))]
    private static bool OnSubmit(TMP_InputField __instance)
    {
        try
        {
            return !(__instance.isFocused && TextFields.IsOurs(__instance));
        }
        catch (Exception e)
        {
            SdkLog.Error(GameUi.Source, $"Text field submit check failed: {e}");
            return true;
        }
    }
}
