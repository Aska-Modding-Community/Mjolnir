using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;

namespace Mjolnir.SDK.UI;

internal delegate bool TryConvertFunc<in TFrom, TTo>(TFrom value, out TTo result);

/// <summary>
/// One direction of a binding's conversion: the mod's own function, or <see cref="AutoConvert{TFrom, TTo}"/>. A value the
/// automatic conversion cannot take (text that is not a number) is skipped and logged once per binding.
/// </summary>
internal sealed class BindingConverter<TFrom, TTo>
{
    private readonly Func<TFrom, TTo>? _custom;
    private readonly TryConvertFunc<TFrom, TTo>? _automatic;
    private readonly string _what;
    private int _warned;

    private BindingConverter(Func<TFrom, TTo>? custom, TryConvertFunc<TFrom, TTo>? automatic, string what)
    {
        _custom = custom;
        _automatic = automatic;
        _what = what;
    }

    /// <exception cref="ArgumentException">No automatic conversion between the two types and no function given.</exception>
    public static BindingConverter<TFrom, TTo> Create(Func<TFrom, TTo>? custom, MjolnirProperty property)
    {
        string what = $"{property} conversion {typeof(TFrom).Name} → {typeof(TTo).Name}";
        if (custom != null) return new BindingConverter<TFrom, TTo>(custom, null, what);
        var automatic = AutoConvert<TFrom, TTo>.Converter
                        ?? throw new ArgumentException($"{property}: no automatic conversion from {typeof(TFrom).Name} to {typeof(TTo).Name}; " +
                                                       "pass convert (and convertBack for a two-way binding)");
        return new BindingConverter<TFrom, TTo>(null, automatic, what);
    }

    public bool TryConvert(TFrom value, out TTo result)
    {
        if (_custom != null) return ModCall.Evaluate(_custom, value, _what, out result);
        if (_automatic!(value, out result)) return true;
        if (Interlocked.Exchange(ref _warned, 1) == 0) SdkLog.Warn(GameUi.Source, $"{_what}: '{value}' cannot be converted, kept the previous value");
        return false;
    }
}

/// <summary>
/// The automatic conversions of bindings, chosen once per pair of types:
/// <list type="bullet">
/// <item>same or assignable types (boxing to object, T to T?) pass;</item>
/// <item>anything to text, numbers with the player's culture;</item>
/// <item>numbers to numbers, saturating; a fraction to a whole number rounds half away from zero;</item>
/// <item>text to numbers ("1.5" and the player's "1,5"; NaN and infinities refused), text to bool;</item>
/// <item>enums from text (by name), from whole numbers and objects (by value, never by position), to numbers;</item>
/// <item>T? to T (null gives the default), object to anything it holds or can be converted to.</item>
/// </list>
/// </summary>
internal static class AutoConvert<TFrom, TTo>
{
    private static readonly Type Target = Nullable.GetUnderlyingType(typeof(TTo)) ?? typeof(TTo);

    /// <summary>Null when the two types do not convert.</summary>
    public static readonly TryConvertFunc<TFrom, TTo>? Converter = Build();

    private static TryConvertFunc<TFrom, TTo>? Build()
    {
        var from = typeof(TFrom);
        var to = typeof(TTo);
        var target = Nullable.GetUnderlyingType(to) ?? to;
        if (to.IsAssignableFrom(from)) return Assign;
        if (to == typeof(string)) return ToText;
        if (Nullable.GetUnderlyingType(from) == to) return FromNullable;
        if (from == typeof(object)) return FromObject;
        if (target.IsEnum && (from == typeof(string) || from.IsEnum || IsInteger(from))) return ToEnum;
        if (from.IsEnum && IsNumber(target)) return EnumToNumber;
        if (from == typeof(string) && IsNumber(target)) return Make(nameof(ParseNumber), target);
        if (from == typeof(string) && target == typeof(bool)) return ParseBool;
        if (IsNumber(from) && IsNumber(target)) return Make(nameof(NumberToNumber), from, target);
        return null;
    }

    private static TryConvertFunc<TFrom, TTo> Make(string name, params Type[] arguments) =>
        (TryConvertFunc<TFrom, TTo>)typeof(AutoConvert<TFrom, TTo>).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
                                                                 .MakeGenericMethod(arguments)
                                                                 .CreateDelegate(typeof(TryConvertFunc<TFrom, TTo>));

    private static bool Assign(TFrom value, out TTo result)
    {
        result = (TTo)(object?)value!;
        return true;
    }

    private static bool ToText(TFrom value, out TTo result)
    {
        bool ok = ModValues.TryText(value, out string? text);
        result = (TTo)(object?)text!;
        return ok;
    }

    private static bool FromNullable(TFrom value, out TTo result)
    {
        result = (object?)value is TTo held ? held : default!;
        return true;
    }

    private static bool FromObject(TFrom value, out TTo result)
    {
        object? boxed = value;
        switch (boxed)
        {
            case TTo held:
                result = held;
                return true;
            case null:
                result = default!;
                return true;
        }
        try
        {
            if (Target.IsEnum) return EnumOf(boxed, out result);
            if (Target == typeof(string))
            {
                bool ok = ModValues.TryText(boxed, out string? text);
                result = (TTo)(object?)text!;
                return ok;
            }
            if (boxed is string held && AutoConvert<string, TTo>.Converter is { } parse) return parse(held, out result);
            if (!ModValues.TryChangeType(boxed, Target, out var changed))
            {
                result = default!;
                return false;
            }
            result = (TTo)changed!;
            return true;
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            result = default!;
            return false;
        }
    }

    private static bool ToEnum(TFrom value, out TTo result) => EnumOf(value, out result);

    private static bool EnumOf(object? value, out TTo result)
    {
        result = default!;
        object? parsed = value switch
        {
            string name => Enum.TryParse(Target, name.Trim(), ignoreCase: true, out var byName) ? byName : null,
            Enum other => Enum.TryParse(Target, other.ToString(), ignoreCase: false, out var sameName) ? sameName : null,
            not null when IsInteger(value.GetType()) => Enum.ToObject(Target, value),
            _ => null,
        };
        if (parsed == null) return false;
        result = (TTo)parsed;
        return true;
    }

    // A value the target cannot hold (a long enum in an int, a negative one in a uint) is refused, not thrown.
    private static bool EnumToNumber(TFrom value, out TTo result)
    {
        try
        {
            var underlying = Convert.ChangeType(value, Enum.GetUnderlyingType(typeof(TFrom)), CultureInfo.InvariantCulture);
            result = (TTo)Convert.ChangeType(underlying, Target, CultureInfo.InvariantCulture)!;
            return true;
        }
        catch (OverflowException)
        {
            result = default!;
            return false;
        }
    }

    private static bool ParseBool(TFrom value, out TTo result)
    {
        bool ok = bool.TryParse(((string?)(object?)value)?.Trim(), out bool parsed);
        result = ok ? (TTo)(object)parsed : default!;
        return ok;
    }

    // Text as a number: the way the config file writes it ("1.5") first, then the player's own ("1,5").
    private static bool ParseNumber<TNumber>(TFrom value, out TTo result) where TNumber : INumberBase<TNumber>
    {
        result = default!;
        string? text = ((string?)(object?)value)?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        var style = IsInteger(typeof(TNumber)) ? NumberStyles.Integer : NumberStyles.Float;
        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.CurrentCulture })
        {
            if (TNumber.TryParse(text, style, culture, out var number) && TNumber.IsFinite(number))
            {
                result = (TTo)(object)number;
                return true;
            }
        }
        return false;
    }

    private static bool NumberToNumber<TA, TB>(TFrom value, out TTo result) where TA : INumberBase<TA> where TB : INumberBase<TB>
    {
        var number = (TA)(object)value!;
        if (IsInteger(typeof(TB)) && !IsInteger(typeof(TA)))
        {
            double d = double.CreateSaturating(number);
            if (!double.IsFinite(d))
            {
                result = default!;
                return false;
            }
            result = (TTo)(object)TB.CreateSaturating(Math.Round(d, MidpointRounding.AwayFromZero));
            return true;
        }
        result = (TTo)(object)TB.CreateSaturating(number);
        return true;
    }

    private static bool IsNumber(Type type) => Implements(type, typeof(INumberBase<>)) && type != typeof(char);

    private static bool IsInteger(Type type) => Implements(type, typeof(IBinaryInteger<>)) && type != typeof(char);

    private static bool Implements(Type type, Type generic) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == generic);
}

/// <summary>
/// What the automatic conversions ask of a value itself: its text (ToString, IFormattable) and IConvertible. For a value of
/// a mod's own type that is the mod's code, so it runs through <see cref="ModCall"/>: a throw blames that mod and the
/// conversion fails. The runtime's own types are converted directly (the per-frame case: numbers to text).
/// </summary>
internal static class ModValues
{
    private static readonly Func<object, string?> s_text = Text;

    public static bool TryText(object? value, out string? text)
    {
        if (value == null)
        {
            text = null;
            return true;
        }
        var type = value.GetType();
        if (IsRuntimeType(type))
        {
            text = Text(value);
            return true;
        }
        return ModCall.TryRun(type.Assembly, $"{type.Name} as text in a binding", s_text, value, out text);
    }

    /// <summary>Convert.ChangeType; false when the value cannot be the target (refused, not thrown).</summary>
    public static bool TryChangeType(object value, Type target, out object? result)
    {
        var type = value.GetType();
        if (IsRuntimeType(type)) return ChangeType(value, target, out result);
        object? changed = null;
        bool ok = ModCall.TryRun(type.Assembly, $"{type.Name} converted in a binding", () => ChangeType(value, target, out changed), out bool converted) &&
                  converted;
        result = ok ? changed : null;
        return ok;
    }

    private static bool ChangeType(object value, Type target, out object? result)
    {
        try
        {
            result = Convert.ChangeType(value, target, CultureInfo.CurrentCulture);
            return true;
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            result = null;
            return false;
        }
    }

    private static string? Text(object value) =>
        value is IFormattable formattable ? formattable.ToString(null, CultureInfo.CurrentCulture) : value.ToString();

    private static bool IsRuntimeType(Type type) => type.Assembly == typeof(object).Assembly;
}
