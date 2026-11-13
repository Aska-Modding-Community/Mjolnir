using System;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.UI;
using UnityEngine;

namespace Mjolnir.PlayerExample.Views;

/// <summary>
/// The example's HUD column: a bar per stat of the local player, in the look of the game's own health bar. Each bar is
/// bound to its stat, so it follows the game by itself; nothing is updated by hand.
/// </summary>
internal sealed class StatBars : UiUserControl
{
    public StatBars(PlayerSettings settings)
    {
        Width = 240;
        Bind(IsVisibleProperty, settings.ShowBars);
        Content = new UiStackPanel
        {
            Spacing = 2,
            Children =
            {
                Bar("Health", LocalPlayer.Health, new Color(0.78f, 0.22f, 0.2f)),
                Bar("Stamina", LocalPlayer.Stamina, new Color(0.36f, 0.74f, 0.3f)),
                Bar("Food", LocalPlayer.Food, new Color(0.86f, 0.55f, 0.2f)),
                Bar("Water", LocalPlayer.Water, new Color(0.26f, 0.56f, 0.9f)),
                Bar("Warmth", LocalPlayer.Warmth, new Color(0.95f, 0.8f, 0.3f)),
            },
        };
    }

    private static UiProgressBar Bar(string name, PlayerStat stat, Color color)
    {
        var bar = new UiProgressBar
        {
            Name = $"PlayerExample{name}", ShowProgressText = true, ProgressTextFormat = $"{name} {{0:0}}/{{3:0}}", Foreground = color,
        };
        // The value follows the stat's Changed event; the limit grows slowly, so it is polled twice a second.
        bar.Bind(UiProgressBar.ValueProperty, stat);
        bar.Bind(UiProgressBar.MaximumProperty, () => stat.Maximum, TimeSpan.FromSeconds(0.5));
        return bar;
    }
}
