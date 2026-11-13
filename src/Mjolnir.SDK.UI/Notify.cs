using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Il2CppInterop.Runtime;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>The game's notifications. Shown in the world (the HUD's notification area), not in the main menu.</summary>
public static class Notify
{
    /// <summary>A small notification on the left, like the game's "item added".</summary>
    public static void Message(string text) => Message(text, null);

    /// <summary>
    /// A small notification on the left with a picture, as the game shows a ripe crop or a sprung trap (null: the
    /// game's generic one, as <see cref="Message(string)"/>).
    /// </summary>
    public static void Message(string text, Bitmap? icon)
    {
        GameUi.EnsureInstalled();
        Dispatcher.Run(() =>
        {
            if (!GameUi.CanUseUnity("Notify.Message")) return;
            GameUi.Guard("Notify.Message", () => ShowMessage(text, icon));
        });
    }

    /// <summary>
    /// A counting notification, as the game's for what the player picks up and the effects on them («x3», the text, the
    /// picture on the right): notifications with the same <paramref name="key"/> (your mod's own; another mod's never
    /// mixes in) show as one, whose amount adds up while they come before it fades. A negative amount shows in red
    /// («-1»), as the game's losses; a change of sign starts the count again. Without a picture, the game's green tick.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Counter(string key, string text, double amount, Bitmap? icon = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        int id = NotifyIds.Of(CallingMod.Of(Assembly.GetCallingAssembly()).GetName().Name ?? "", key);
        GameUi.EnsureInstalled();
        Dispatcher.Run(() =>
        {
            if (!GameUi.CanUseUnity("Notify.Counter")) return;
            GameUi.Guard("Notify.Counter", () => ShowCount(id, text, amount, icon));
        });
    }

    /// <summary>A large notification at the top centre, like the game's season and discovery messages.</summary>
    public static void Banner(string title, string text, float seconds = 3f)
    {
        GameUi.EnsureInstalled();
        Dispatcher.Run(() =>
        {
            if (!GameUi.CanUseUnity("Notify.Banner")) return;
            GameUi.Guard("Notify.Banner", () => ShowBanner(title, text, seconds));
        });
    }

    // As the game shows its own pictures (a crop, a trap: Display(CUSTOM, text, sprite)); GENERIC would tint the picture
    // its green.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShowMessage(string text, Bitmap? icon)
    {
        if (Pictures.SpriteOf(icon) is { } sprite) NotificationMenu.Display(NotificationType.CUSTOM, ref text, sprite, false);
        else NotificationMenu.Display(NotificationType.GENERIC, ref text, false);
    }

    // The game's stacking notification as it counts pickups (PlayerCharacter.Submit: DisplayStacking(CUSTOM, Generic, …),
    // "x3"): one panel per id, its quantity adding up while the stacking type stays (StackingNotificationTextPanel.SetMessage).
    // Without a picture CUSTOM would show its own, a villager's head: GENERIC shows the game's green tick instead.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShowCount(int id, string text, double amount, Bitmap? icon)
    {
        var style = amount < 0 ? StackingType.NegativeQty : StackingType.Generic;
        float quantity = (float)Math.Abs(amount);
        var sprite = Pictures.SpriteOf(icon);
        var type = sprite != null ? NotificationType.CUSTOM : NotificationType.GENERIC;
        NotificationMenu.DisplayStacking(type, style, ref text, ref id, ref quantity, sprite);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShowBanner(string title, string text, float seconds) =>
        NotificationMenu.DisplayLarge(ref title, ref text, null, ref seconds);
}
