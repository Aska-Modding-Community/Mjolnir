using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The text an item shows in a selector when no <see cref="SelectingItemsControl.ItemText"/> is given: an enum value by
/// the mod's string <c>&lt;enum&gt;.&lt;value&gt;</c> in the player's language (the mod that declares the enum, its
/// lang files), else by its <see cref="DescriptionAttribute"/>, else by its name; anything else by its text.
/// </summary>
internal static class DefaultItemText
{
    private static readonly ConcurrentDictionary<(Type Type, string Name), string?> s_descriptions = new();

    public static string Of(object? item, string what) => item is Enum value ? OfEnum(value) : ModCall.TextOf(item, what);

    public static string OfEnum(Enum value)
    {
        var type = value.GetType();
        string name = value.ToString();
        if (Localization.TryGetModString(type.Assembly, $"{type.Name}.{name}", out string text)) return text;
        return s_descriptions.GetOrAdd((type, name), static key =>
                   key.Type.GetField(key.Name, BindingFlags.Public | BindingFlags.Static)?.GetCustomAttribute<DescriptionAttribute>()?.Description)
               ?? name;
    }
}
