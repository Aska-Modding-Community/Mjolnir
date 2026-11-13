using System;
using System.Globalization;
using Mjolnir.SDK.Config;

namespace Mjolnir.SDK.UI;

/// <summary>Which control the Mods window shows for a config setting.</summary>
internal enum SettingKind
{
    Toggle,   // bool
    Choice,   // enum with a few names: "‹ name ›" selector
    Dropdown, // enum with many names
    Slider,   // number with a range (WithRange)
    Number,   // number without a range: text input
    Text,     // string: text input
    HotKey,   // KeyGesture: a key the player records
}

/// <summary>
/// The pure part of the generated settings page (no Unity, unit-tested): the control for a setting and the conversions
/// between a setting's value and what its control shows.
/// </summary>
internal static class SettingKinds
{
    /// <summary>Enums with more names than this get a dropdown list instead of the selector.</summary>
    public const int MaxSelectorOptions = 6;

    public static SettingKind Of(IConfigEntry entry)
    {
        var type = entry.ValueType;
        if (type == typeof(bool)) return SettingKind.Toggle;
        if (type == typeof(KeyGesture)) return SettingKind.HotKey;
        if (type.IsEnum) return Enum.GetNames(type).Length > MaxSelectorOptions ? SettingKind.Dropdown : SettingKind.Choice;
        if (ModConfig.IsNumber(type)) return entry.Range != null ? SettingKind.Slider : SettingKind.Number;
        return SettingKind.Text;
    }

    public static bool IsWhole(Type type) => type == typeof(int) || type == typeof(long);

    /// <summary>Decimals a slider keeps: about two significant digits of its span (0..5 → 2, 0..100 → 0, 0..0.05 → 4).</summary>
    public static int Decimals(double min, double max)
    {
        double span = max - min;
        if (!(span > 0) || double.IsInfinity(span)) return 2;
        return Math.Clamp(2 - (int)Math.Floor(Math.Log10(span)), 0, 6);
    }

    /// <summary>The .NET format that shows a number with up to that many decimals.</summary>
    public static string NumberFormat(int decimals) => decimals == 0 ? "0" : "0." + new string('#', decimals);

    public static double ToSlider(object value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);

    /// <summary>A slider position as the setting's type, rounded to what the slider shows, so the file gets 1.25, not 1.2499999.</summary>
    public static object FromSlider(double value, Type type, int decimals) =>
        // Each branch boxed on its own: a plain ?: chain would widen them all to double.
        type == typeof(int) ? (object)(int)Math.Round(value, MidpointRounding.AwayFromZero)
        : type == typeof(long) ? (object)(long)Math.Round(value, MidpointRounding.AwayFromZero)
        : type == typeof(float) ? (object)(float)Math.Round(value, decimals, MidpointRounding.AwayFromZero)
        : type == typeof(double) ? (object)Math.Round(value, decimals, MidpointRounding.AwayFromZero)
        : throw new NotSupportedException($"{type.Name} is not a number type");

    /// <summary>A value as its text input shows it: numbers the way the config file writes them.</summary>
    public static string Show(object value) => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? "";
}
