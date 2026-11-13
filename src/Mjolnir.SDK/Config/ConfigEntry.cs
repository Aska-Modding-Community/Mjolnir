using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mjolnir.SDK.Config;

/// <summary>
/// One setting of a <see cref="ModConfig"/>. It is also an <see cref="IBindable{T}"/>: bind a UI control to it
/// (<c>toggle.Bind(UiToggleSwitch.IsCheckedProperty, entry)</c>) or show it with <c>SettingControls.For(entry)</c>.
/// </summary>
public sealed class ConfigEntry<T> : IConfigEntry, IBindable<T>
{
    private readonly ModConfig _config;
    private readonly ModHandlers<EventHandler<ValueChangedEventArgs<T>>> _changed;
    private T _value;
    private bool _hasRange;
    private T _min = default!;
    private T _max = default!;

    internal ConfigEntry(ModConfig config, string section, string key, T defaultValue, T value, string? description)
    {
        _config = config;
        Section = section;
        Key = key;
        DefaultValue = defaultValue;
        _value = value;
        Description = description;
        _changed = new ModHandlers<EventHandler<ValueChangedEventArgs<T>>>($"Config [{section}] {key}.Changed");
    }

    public string Section { get; }
    public string Key { get; }
    public string? Description { get; }
    public T DefaultValue { get; }

    /// <summary>The setting's name in the Mods window (<see cref="WithDisplayName"/>); null shows the <see cref="Key"/>.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>
    /// Where the setting stands in its section of the Mods window (<see cref="WithOrder"/>): lower first, equal ones in
    /// binding order. 0 by default. The config file keeps binding order.
    /// </summary>
    public int Order { get; private set; }

    /// <summary>A change takes effect only after the game restarts (<see cref="WithRestartRequired"/>).</summary>
    public bool IsRestartRequired { get; private set; }

    /// <summary>
    /// The current value. Setting it marks the config changed (it is written on Save() or when the game quits) and raises
    /// <see cref="Changed"/>; a value outside the range is clamped.
    /// </summary>
    public T Value
    {
        get => _value;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            value = Clamp(value);
            var old = _value;
            if (EqualityComparer<T>.Default.Equals(old, value)) return;
            _value = value;
            _config.MarkChanged();
            var args = new ValueChangedEventArgs<T>(old, value);
            _changed.Raise(h => h(this, args));
        }
    }

    /// <summary>
    /// Raised after <see cref="Value"/> changed, from code or by the player in the Mods window, with the old and the new
    /// value: <c>entry.Changed += (s, e) => Apply(e.NewValue)</c>. Runs on the thread that set the value (the main thread
    /// for the window). If a handler throws, its mod is disabled.
    /// </summary>
    public event EventHandler<ValueChangedEventArgs<T>>? Changed
    {
        add => _changed.Add(value);
        remove => _changed.Remove(value);
    }

    /// <summary>
    /// Limits a number setting (int, long, float, double) to min..max: values outside, the file's included, are clamped,
    /// and the Mods window shows a slider instead of a text field. Returns this entry, to chain after Bind:
    /// <c>Config.Bind("Stamina", "Multiplier", 1f).WithRange(0f, 5f)</c>.
    /// </summary>
    public ConfigEntry<T> WithRange(T min, T max)
    {
        if (!ModConfig.IsNumber(typeof(T)))
            throw new NotSupportedException($"[{Section}] {Key}: a range needs a number setting (int, long, float, double), not {typeof(T).Name}");
        var compare = Comparer<T>.Default;
        if (compare.Compare(min, max) > 0) throw new ArgumentException($"[{Section}] {Key}: min {Show(min)} is greater than max {Show(max)}");
        if (compare.Compare(DefaultValue, min) < 0 || compare.Compare(DefaultValue, max) > 0)
            throw new ArgumentOutOfRangeException(nameof(min), $"[{Section}] {Key}: the default {Show(DefaultValue)} is outside {Show(min)} .. {Show(max)}");
        _min = min;
        _max = max;
        _hasRange = true;
        var clamped = Clamp(_value);
        if (!EqualityComparer<T>.Default.Equals(clamped, _value))
        {
            _config.Warn($"[{Section}] {Key} = {Show(_value)} is outside {Show(min)} .. {Show(max)}; using {Show(clamped)}");
            _value = clamped;
            _config.MarkChanged();
        }
        return this;
    }

    /// <summary>
    /// The name the player sees in the Mods window instead of the key, which stays the name in the file:
    /// <c>Config.Bind("HUD", "ShowText", true).WithDisplayName("Show the HUD line")</c>. Returns this entry.
    /// </summary>
    public ConfigEntry<T> WithDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
        return this;
    }

    /// <summary>
    /// Moves the setting within its section of the Mods window: lower first, equal ones in binding order (all are 0 by
    /// default, so binding order alone decides). Returns this entry.
    /// </summary>
    public ConfigEntry<T> WithOrder(int order)
    {
        Order = order;
        return this;
    }

    /// <summary>
    /// Marks a setting the mod reads only at start (in <see cref="Mod.OnLoad"/>, say): the Mods window says that a change
    /// takes effect after a restart, and so does the config file. Returns this entry.
    /// </summary>
    public ConfigEntry<T> WithRestartRequired()
    {
        IsRestartRequired = true;
        return this;
    }

    private T Clamp(T value)
    {
        if (!_hasRange) return value;
        var compare = Comparer<T>.Default;
        return compare.Compare(value, _min) < 0 ? _min : compare.Compare(value, _max) > 0 ? _max : value;
    }

    private static string Show(T value) => ModConfig.Display(value);

    Type IConfigEntry.ValueType => typeof(T);
    object IConfigEntry.TomlValue => ModConfig.ToToml(_value!);
    string IConfigEntry.DefaultDisplay => ModConfig.Display(DefaultValue);
    string? IConfigEntry.RangeDisplay => _hasRange ? $"{Show(_min)} .. {Show(_max)}" : null;
    object IConfigEntry.BoxedDefault => DefaultValue!;
    (double Minimum, double Maximum)? IConfigEntry.Range =>
        _hasRange ? (Convert.ToDouble(_min, CultureInfo.InvariantCulture), Convert.ToDouble(_max, CultureInfo.InvariantCulture)) : null;

    object IConfigEntry.BoxedValue
    {
        get => _value!;
        set => Value = (T)value;
    }

    TResult IConfigEntry.Accept<TResult>(IConfigEntryVisitor<TResult> visitor) => visitor.Visit(this);

    /// <summary>The range from <see cref="WithRange"/>, if any.</summary>
    internal bool TryGetRange(out T min, out T max)
    {
        min = _min;
        max = _max;
        return _hasRange;
    }

    public override string ToString() => $"[{Section}] {Key} = {ModConfig.Display(_value)}";
}
