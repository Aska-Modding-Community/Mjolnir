using System;
using System.Collections.Generic;
using HarmonyLib;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Mjolnir.SDK.UI;

/// <summary>
/// Applies <see cref="InterfaceScale"/> to the game:
/// <list type="bullet">
/// <item>every menu MenuManager.OpenMenu opens, mod windows included, gets its CanvasScaler's own reference resolution
/// divided by its scale (<see cref="UiScale.MenuScale"/>: a game menu grows no bigger than its own size; the main menu's
/// 1280×720 keeps its proportion);</item>
/// <item>the HUD and its notifications get the game's own HUD scale times the HUD's factor, in the game's own
/// PlayerHUDMenu.SetHUDScale / NotificationMenu.RefreshScale (re-called when they open and when the factor changes).</item>
/// </list>
/// A change (once the pointer is up) reaches every scaler seen that still exists. The game's settings are never written.
/// </summary>
internal static class ScaleService
{
    private static readonly Dictionary<IntPtr, (CanvasScaler Scaler, Vector2 Original, bool ModWindow)> s_menus = [];
    private static bool s_ticking;

    /// <summary>Starts following the settings (main thread, every frame). Called with the facade's install.</summary>
    public static void EnsureTicking()
    {
        if (s_ticking) return;
        s_ticking = true;
        GameLoop.Update += Tick;
    }

    private static void Tick()
    {
        if (!GameLoop.IsGameReady) return;
        var (menus, hud) = UiScale.Update(UiScale.MenusEntry.Value, UiScale.HudEntry.Value, GameKeyboard.PointerHeld());
        if (menus) GameUi.Guard("interface scale", ApplyMenus);
        if (hud) GameUi.Guard("HUD scale", ApplyHud);
    }

    /// <summary>A menu opened (MenuManager.OpenMenu): scaled as the settings say.</summary>
    internal static void Opened(Menu menu)
    {
        if (menu.TryCast<PlayerHUDMenu>() is { } hud)
        {
            hud.SetHUDScale(GameHudScale()); // the patch below multiplies
            return;
        }
        if (menu.TryCast<NotificationMenu>() is { } notifications)
        {
            notifications.RefreshScale(GameHudScale());
            return;
        }
        var scaler = menu.CanvasScaler ?? menu.GetComponent<CanvasScaler>();
        if (scaler == null) return;
        if (!s_menus.TryGetValue(scaler.Pointer, out var seen))
        {
            SweepGone(); // the game makes some menus anew on each open: those it destroyed go before a new one comes
            s_menus[scaler.Pointer] = seen = (scaler, scaler.referenceResolution, scaler.gameObject.name.StartsWith(UiWindow.ObjectNamePrefix, StringComparison.Ordinal));
        }
        Scale(seen.Scaler, seen.Original, UiScale.MenuScale(seen.ModWindow));
    }

    /// <summary>The game set the HUD's scale (or the notifications'): times the HUD's factor.</summary>
    internal static void HudScaled(CanvasScaler? scaler, Vector2 initial, float gameScale)
    {
        if (scaler == null || gameScale <= 0) return;
        Scale(scaler, initial, gameScale * UiScale.Hud);
    }

    private static void ApplyMenus()
    {
        SweepGone();
        foreach (var (scaler, original, modWindow) in s_menus.Values) Scale(scaler, original, UiScale.MenuScale(modWindow));
    }

    private static void SweepGone()
    {
        List<IntPtr>? gone = null;
        foreach (var (key, seen) in s_menus)
        {
            if (seen.Scaler == null) (gone ??= []).Add(key);
        }
        if (gone == null) return;
        foreach (var key in gone) s_menus.Remove(key);
    }

    private static void ApplyHud()
    {
        float game = GameHudScale();
        Menu.GetInstance<PlayerHUDMenu>()?.SetHUDScale(game);
        Menu.GetInstance<NotificationMenu>()?.RefreshScale(game);
    }

    private static void Scale(CanvasScaler scaler, Vector2 original, float scale)
    {
        var wanted = new Vector2(original.x / scale, original.y / scale);
        if (scaler.referenceResolution != wanted) scaler.referenceResolution = wanted;
    }

    // The game's own HUD scale setting (its slider: 0.5…1).
    private static float GameHudScale()
    {
        try
        {
            return GameManagers.Get<SettingsManager>()?.HUDScaleSetting?.Value ?? 1f;
        }
        catch (Exception)
        {
            return 1f;
        }
    }
}

/// <summary>Hidden patches behind <see cref="InterfaceScale"/>.</summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class ScalePatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.OpenMenu))]
    private static void MenuOpened(Menu __0)
    {
        if (__0 != null) GameUi.Guard("interface scale", () => ScaleService.Opened(__0));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerHUDMenu), nameof(PlayerHUDMenu.SetHUDScale))]
    private static void HudScaled(PlayerHUDMenu __instance, float __0) =>
        GameUi.Guard("HUD scale", () => ScaleService.HudScaled(__instance._canvasScaler, __instance._initialReferenceResolution, __0));

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NotificationMenu), nameof(NotificationMenu.RefreshScale))]
    private static void NotificationsScaled(NotificationMenu __instance, float __0) =>
        GameUi.Guard("notifications scale", () => ScaleService.HudScaled(__instance._canvasScaler, __instance._initialReferenceResolution, __0));
}
