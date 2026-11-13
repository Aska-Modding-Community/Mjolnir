using System;
using Mjolnir.SDK;
using Mjolnir.SDK.UI;
using UnityEngine;

namespace Mjolnir.UiExample.Views;

/// <summary>
/// The example's HUD column (a UiUserControl: a piece of UI of its own, built in its constructor): the configured
/// line, hidden while switched off, a clock, a bar filling up with the seconds of the minute, and the key that opens the
/// example's window in the game's key pictures (it follows a rebinding in the Mods window).
/// </summary>
internal sealed class ExampleHud : UiUserControl
{
    public ExampleHud(ExampleSettings settings)
    {
        var line = new UiTextBlock { Name = "ExampleHudLine" };
        line.Bind(UiTextBlock.TextProperty, settings.HudText);
        line.Bind(Control.IsVisibleProperty, settings.ShowHudLine);

        var clock = new UiTextBlock { Name = "ExampleHudClock", FontSize = 18 };
        // Polled while shown, four times a second rather than every frame: each call makes a new string.
        clock.Bind(UiTextBlock.TextProperty, () => $"{DateTime.Now:HH:mm:ss}", TimeSpan.FromSeconds(0.25));

        // A bar in the game's health bar look, amber, with the game's "value/max" kind of text.
        var minute = new UiProgressBar
        {
            Name = "ExampleHudMinute", Maximum = 59, ShowProgressText = true, ProgressTextFormat = "{0:0}/{3:0} s",
            Foreground = new Color(1f, 0.7f, 0.11f),
        };
        minute.Bind(UiProgressBar.ValueProperty, () => DateTime.Now.Second, TimeSpan.FromSeconds(0.25));

        var key = new UiKeyHint { Name = "ExampleHudKey" }
            .WithBinding(UiKeyHint.GestureProperty, settings.WindowKey)
            .WithBinding(UiKeyHint.TextProperty, Localization.GetBindable("hud.key"));

        Content = new UiStackPanel { Spacing = 2, Children = { line, clock, minute, key } };
    }
}
