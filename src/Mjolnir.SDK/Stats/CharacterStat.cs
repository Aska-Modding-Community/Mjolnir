using System;
using System.Globalization;
using System.Threading;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// One attribute of a character: <see cref="CharacterSheet.Strength"/>, a skill's level, or anything else by its asset name.
/// It is either a sum (a base value and modifiers: strength, armor, a skill's level) or a pool (a value between limits:
/// health, food, experience). It is a binding source, so a UI element follows it by itself. It reads 0 while the character
/// does not have it, and once the character is gone.
/// </summary>
public sealed class CharacterStat : IBindable<float>
{
    private const string LogSource = "SDK.Stats";
    private readonly Func<ICharacterStatSource?> _find;
    private readonly Func<bool> _alive;
    private readonly ModHandlers<EventHandler<ValueChangedEventArgs<float>>> _changed;
    private ICharacterStatSource? _source; // main thread only, with the subscription
    private IDisposable? _watch;
    private bool _lookingAgain;
    private float _value;
    private float _base;

    internal CharacterStat(string assetName, Func<ICharacterStatSource?> find, Func<bool> alive)
    {
        AssetName = assetName;
        _find = find;
        _alive = alive;
        _changed = new($"{assetName}.Changed");
    }

    /// <summary>The game's name of the attribute (<c>Attribute_Strength</c>), the same in every language.</summary>
    public string AssetName { get; }

    /// <summary>Its name in the player's language, else the asset name. Main thread.</summary>
    public string DisplayName => Source() is { Name.Length: > 0 } source ? source.Name : AssetName;

    /// <summary>What it does, in the player's language; "" without a text. Main thread.</summary>
    public string Description => Source()?.Description ?? "";

    /// <summary>Whether it is a pool: a value between limits, with no modifiers.</summary>
    public bool IsPool => Source()?.IsPool ?? false;

    /// <summary>The value, modifiers included. Setting it sets <see cref="BaseValue"/>.</summary>
    public float Value
    {
        get
        {
            Sync(raise: IsWatched);
            return Volatile.Read(ref _value);
        }
        set => BaseValue = value;
    }

    /// <summary>
    /// The value before modifiers, a pool's current value. Setting it changes the game's value; the game keeps a pool within
    /// its limits, so reading it again shows what it kept.
    /// </summary>
    public float BaseValue
    {
        get
        {
            Sync(raise: IsWatched);
            return Volatile.Read(ref _base);
        }
        set => Dispatcher.Run(() =>
        {
            if (Source() is not { } source) return;
            source.BaseValue = value;
            Sync(raise: true);
        });
    }

    /// <summary>A pool's lowest value, 0 for a sum. Main thread.</summary>
    public float Minimum => Source()?.Minimum ?? 0f;

    /// <summary>A pool's highest value, 0 for a sum. Main thread.</summary>
    public float Maximum => Source()?.Maximum ?? 0f;

    /// <summary>A pool's value as a part of its range, from 0 at <see cref="Minimum"/> to 1 at <see cref="Maximum"/>; 0 for a sum. Main thread.</summary>
    public float Fraction
    {
        get
        {
            if (Source() is not { IsPool: true } source) return 0f;
            float low = source.Minimum, high = source.Maximum;
            return high > low ? Math.Clamp((source.Value - low) / (high - low), 0f, 1f) : 0f;
        }
    }

    /// <summary>
    /// Raised on the main thread after the value changed, with the old and the new value: when the game changes it, when it
    /// is set, and when a modifier comes or goes. Food and water drop all the time, so theirs is raised every frame. An
    /// attribute the character does not have yet is looked for every frame while a mod listens; its first value is no change.
    /// </summary>
    public event EventHandler<ValueChangedEventArgs<float>>? Changed
    {
        add
        {
            _changed.Add(value);
            Dispatcher.Run(UpdateWatch);
        }
        remove
        {
            _changed.Remove(value);
            Dispatcher.Run(UpdateWatch);
        }
    }

    /// <summary>
    /// Adds to the value until the modifier is disposed: <see cref="ModifierKind.Add"/> an amount, <see cref="ModifierKind.Multiply"/>
    /// a factor, <see cref="ModifierKind.Percent"/> a part of the sum (0.2 for 20 %). The game does not save it, and it ends with
    /// the character: add it again after a load. A character that does not have the attribute gets nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">The attribute is a pool (<see cref="IsPool"/>). On another thread a pool is
    /// told apart on the next frame: the modifier is refused then, with a warning in the log.</exception>
    public StatModifier AddModifier(float amount, ModifierKind kind = ModifierKind.Add)
    {
        if (IsPool) throw new InvalidOperationException(PoolMessage);
        var modifier = new StatModifier(amount, kind);
        Dispatcher.Run(() =>
        {
            var source = Source();
            if (source is { IsPool: true }) SdkLog.Warn(LogSource, $"{PoolMessage} The modifier asked for on another thread does nothing.");
            else if (source != null && !modifier.IsDisposed) modifier.Attach(source.AddModifier(modifier.Amount, kind), () => Sync(raise: true));
            Sync(raise: true);
        });
        return modifier;
    }

    // While a mod listens, a change a read sees first is reported too: the next report from the game would compare with the
    // value the read already took, and the listener would never hear of it.
    private bool IsWatched => _watch != null;

    private string PoolMessage => $"{AssetName} is a pool: it has no modifiers. Set its BaseValue instead.";

    /// <summary>The attribute and the value last seen (it never reads the game).</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{AssetName} {Volatile.Read(ref _value):0.##}");

    /// <summary>Stops watching the game: the character is gone (main thread).</summary>
    internal void Release()
    {
        _watch?.Dispose();
        _watch = null;
    }

    // The game's attribute, on the main thread while the character is there; never one of a character the game destroyed.
    private ICharacterStatSource? Source()
    {
        if (!Dispatcher.IsMainThread || !_alive()) return null;
        return _source ??= _find();
    }

    private void Sync(bool raise)
    {
        float old = Volatile.Read(ref _value), value = 0f, baseValue = 0f;
        if (Source() is { } source)
        {
            value = source.Value;
            baseValue = source.BaseValue;
        }
        else if (!Dispatcher.IsMainThread)
        {
            return;
        }
        Volatile.Write(ref _base, baseValue);
        if (old.Equals(value)) return;
        Volatile.Write(ref _value, value);
        if (!raise) return;
        var args = new ValueChangedEventArgs<float>(old, value);
        _changed.Raise(h => h(this, args));
    }

    // The game is watched only while a mod listens: food and water change every frame. An attribute the character does not
    // have yet is looked for again on the next frame, until it comes, the character goes or nobody listens.
    private void UpdateWatch()
    {
        if (_changed.Count == 0 || !_alive())
        {
            Release();
            return;
        }
        if (_watch != null) return;
        if (Source() is { } source)
        {
            Sync(raise: false); // nobody saw the old value
            _watch = source.Watch(() => Sync(raise: true));
        }
        else if (!_lookingAgain)
        {
            _lookingAgain = true;
            Dispatcher.Enqueue(() =>
            {
                _lookingAgain = false;
                UpdateWatch();
            }, $"{AssetName}.Changed");
        }
    }
}

/// <summary>How a modifier counts (<see cref="CharacterStat.AddModifier"/>).</summary>
public enum ModifierKind
{
    /// <summary>Adds its amount.</summary>
    Add,

    /// <summary>Multiplies by its amount: 1.25 is a quarter more.</summary>
    Multiply,

    /// <summary>Adds a part of the sum at the end: 0.2 is 20 %. The game adds such parts up.</summary>
    Percent,
}

/// <summary>A modifier on an attribute (<see cref="CharacterStat.AddModifier"/>). Disposing it takes it off.</summary>
public sealed class StatModifier : IDisposable
{
    private IStatModifierSource? _source;
    private Action? _refresh;
    private float _amount;
    private bool _disposed;

    internal StatModifier(float amount, ModifierKind kind)
    {
        _amount = amount;
        Kind = kind;
    }

    /// <summary>How it counts.</summary>
    public ModifierKind Kind { get; }

    /// <summary>Its amount. Changing it changes the attribute at once.</summary>
    public float Amount
    {
        get => Volatile.Read(ref _amount);
        set
        {
            Volatile.Write(ref _amount, value);
            Dispatcher.Run(() =>
            {
                if (_source == null) return;
                _source.Amount = value;
                _refresh?.Invoke();
            });
        }
    }

    /// <summary>Takes the modifier off. Doing it twice does nothing.</summary>
    public void Dispose() => Dispatcher.Run(() =>
    {
        if (_disposed) return;
        _disposed = true;
        _source?.Remove();
        _source = null;
        _refresh?.Invoke();
    });

    // Disposed before it reached the game (asked for on another thread, disposed on the main thread): it never goes on.
    internal bool IsDisposed => _disposed;

    internal void Attach(IStatModifierSource source, Action refresh)
    {
        _source = source;
        _refresh = refresh;
    }
}
