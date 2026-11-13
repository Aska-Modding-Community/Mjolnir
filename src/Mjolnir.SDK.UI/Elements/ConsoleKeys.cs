namespace Mjolnir.SDK.UI;

/// <summary>
/// Which console keys close the console from its own field, where the player types (pure): a key that types nothing a
/// command needs (<c>`</c>, the F keys) or a gesture with Ctrl or Alt. A plain letter, digit or space types into the
/// command instead, as any hotkey waits while the player types.
/// </summary>
internal static class ConsoleKeys
{
    public static bool ClosesWhileTyping(KeyGesture gesture)
    {
        if (gesture.IsNone) return false;
        if ((gesture.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0) return true;
        string key = gesture.Key;
        if (key.Equals("Backquote", System.StringComparison.OrdinalIgnoreCase)) return true;
        return key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out int number) && number is >= 1 and <= 24;
    }
}
