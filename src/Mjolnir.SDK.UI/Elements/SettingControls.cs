using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mjolnir.SDK.Config;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The control for a config setting, as the Mods window shows it, for your own windows:
/// <c>panel.Children.Add(SettingControls.For(speed))</c>. It is bound to the setting both ways, headed with its display
/// name (its key without one) and described by the first line of its description, which says so when a change applies
/// only after a restart (<see cref="ConfigEntry{T}.WithRestartRequired"/>):
/// <list type="bullet">
/// <item>bool → <see cref="UiToggleSwitch"/>;</item>
/// <item>enum → <see cref="UiStepper"/>, a <see cref="UiComboBox"/> above 6 names;</item>
/// <item>number with <see cref="ConfigEntry{T}.WithRange"/> → <see cref="UiSlider"/>;</item>
/// <item>other numbers and text → <see cref="UiTextBox"/> (text that is not a number goes back to the setting's value);</item>
/// <item><see cref="KeyGesture"/> → <see cref="UiHotKeyBox"/>.</item>
/// </list>
/// The name and description come from your mod's lang files when they have them, in the player's language:
/// <c>[config.&lt;section&gt;.&lt;key&gt;] name = "…"</c> and <c>description = "…"</c> (the Mods window reads the
/// same, and <c>[sections] &lt;section&gt; = "…"</c> for its headers). The row's Unity object is named
/// <c>Setting &lt;section&gt;.&lt;key&gt;</c>.
/// </summary>
public static class SettingControls
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static HeaderedControl For<T>(ConfigEntry<T> entry) => For(entry, ModStringsRegistry.ForAssembly(CallingMod.Of(Assembly.GetCallingAssembly())));

    /// <summary>The control for a setting, its texts from <paramref name="strings"/> (the mod's; null for none).</summary>
    internal static HeaderedControl For<T>(ConfigEntry<T> entry, ModStrings? strings)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var type = typeof(T);
        var kind = SettingKinds.Of(entry);
        switch (kind)
        {
            case SettingKind.Toggle:
            {
                var toggle = Headed(new UiToggleSwitch(), entry, strings);
                toggle.Bind(UiToggleSwitch.IsCheckedProperty, entry);
                return toggle;
            }
            case SettingKind.Choice:
            case SettingKind.Dropdown:
            {
                SelectingItemsControl choice = kind == SettingKind.Choice ? new UiStepper() : new UiComboBox();
                Headed(choice, entry, strings);
                choice.ItemsSource = Enum.GetValues(type);
                choice.Bind(SelectingItemsControl.SelectedItemProperty, entry);
                return choice;
            }
            case SettingKind.Slider:
            {
                entry.TryGetRange(out var min, out var max);
                double low = SettingKinds.ToSlider(min!), high = SettingKinds.ToSlider(max!);
                bool whole = SettingKinds.IsWhole(type);
                int decimals = whole ? 0 : SettingKinds.Decimals(low, high);
                var slider = Headed(new UiSlider(), entry, strings);
                slider.Minimum = low;
                slider.Maximum = high;
                slider.IsSnapToTickEnabled = whole;
                slider.TickFrequency = whole ? 1 : 0;
                slider.ValueFormat = SettingKinds.NumberFormat(decimals);
                // Written back rounded to what the slider shows, so the file gets 1.25, not 1.2499999.
                slider.Bind(UiSlider.ValueProperty, entry, convert: v => SettingKinds.ToSlider(v!),
                            convertBack: d => (T)SettingKinds.FromSlider(d, type, decimals));
                return slider;
            }
            case SettingKind.HotKey:
            {
                var box = Headed(new UiHotKeyBox(), entry, strings);
                box.Bind(UiHotKeyBox.GestureProperty, entry);
                return box;
            }
            case SettingKind.Number:
            {
                var box = Headed(new UiTextBox(), entry, strings);
                box.ContentType = SettingKinds.IsWhole(type) ? TextInputContentType.Digits : TextInputContentType.Number;
                // Shown the way the config file writes it; the player's "1,5" is read too.
                box.Bind(UiTextBox.TextProperty, entry, convert: v => SettingKinds.Show(v!));
                return box;
            }
            default:
            {
                var box = Headed(new UiTextBox(), entry, strings);
                box.Bind(UiTextBox.TextProperty, entry);
                return box;
            }
        }
    }

    /// <summary>The same for a setting known only as <see cref="IConfigEntry"/> (the Mods window), its texts from the mod's <paramref name="strings"/>.</summary>
    internal static HeaderedControl For(IConfigEntry entry, ModStrings? strings) => entry.Accept(new Visitor(strings));

    private static TControl Headed<TControl, T>(TControl control, ConfigEntry<T> entry, ModStrings? strings) where TControl : HeaderedControl
    {
        control.Name = SettingTexts.RowName(entry.Section, entry.Key);
        control.Header = SettingTexts.NameOf(entry.Section, entry.Key, entry.DisplayName, strings);
        control.Description = DescriptionOf(SettingTexts.DescriptionOf(entry.Section, entry.Key, entry.Description, strings), entry.IsRestartRequired);
        return control;
    }

    /// <summary>A row's description line: the first line of the setting's description, and the restart note if it needs one.</summary>
    internal static string? DescriptionOf(string? description, bool restartRequired)
    {
        if (!restartRequired) return description;
        string line = description?.Split('\n')[0].Trim() ?? "";
        return line.Length == 0 ? SdkTexts.Get("setting.restart_alone") : SdkTexts.Get("setting.restart", line);
    }

    private sealed class Visitor(ModStrings? strings) : IConfigEntryVisitor<HeaderedControl>
    {
        public HeaderedControl Visit<T>(ConfigEntry<T> entry) => For(entry, strings);
    }
}
