using System;
using System.Globalization;
using System.Threading;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.Player;

/// <summary>
/// One stat of the local player (<see cref="LocalPlayer.Health"/>, <see cref="LocalPlayer.Food"/>…): the pool of the character
/// of now, its <see cref="CharacterStat"/> in <see cref="LocalPlayer.Sheet"/>, followed from one character to the next, so a
/// binding to it outlives a world's load. It reads 0 while there is no character. It is a binding source, so a HUD bar follows
/// it by itself: <c>bar.Bind(UiProgressBar.ValueProperty, LocalPlayer.Health)</c>.
/// </summary>
/// <remarks>
/// Use it on the main thread (mod callbacks, events, UI handlers). On another thread reading gives the value last seen on
/// the main thread, and a value set is written on the next frame. It reads the game as every character's attributes are
/// read, through Mjolnir's stats module.
/// </remarks>
public class PlayerStat : IBindable<float>
{
    private readonly ModHandlers<EventHandler<ValueChangedEventArgs<float>>> _changed;
    // The character's sheet, its pool for this stat and whether the pool is watched: main thread only.
    private CharacterSheet? _sheet;
    private CharacterStat? _stat;
    private bool _found;
    private bool _watching;
    private float _value;
    private float _max;

    internal PlayerStat(StatKind kind)
    {
        Kind = kind;
        _changed = new($"LocalPlayer.{kind}.Changed");
    }

    internal StatKind Kind { get; }

    /// <summary>
    /// The current value, 0 without a character. Setting it changes the game's value (the game keeps it within its
    /// limits; reading it again shows what it kept); without a character it is ignored.
    /// </summary>
    public float Value
    {
        get
        {
            if (Dispatcher.IsMainThread) Sync();
            return Volatile.Read(ref _value);
        }
        set => Dispatcher.Run(() => Write(value));
    }

    /// <summary>The highest value the stat can have now (it grows with the character), 0 without a character.</summary>
    public float Maximum
    {
        get
        {
            if (Dispatcher.IsMainThread) Sync();
            return Volatile.Read(ref _max);
        }
    }

    /// <summary><see cref="Value"/> as a part of <see cref="Maximum"/>, from 0 to 1; 0 without a character.</summary>
    public float Fraction
    {
        get
        {
            float value = Value, max = Maximum;
            return max > 0 ? Math.Clamp(value / max, 0f, 1f) : 0f;
        }
    }

    /// <summary>
    /// Raised on the main thread after the value changed, with the old and the new value: when the game changes it, when
    /// it is set, and when a character spawns or despawns (from and to 0). Food and water drop all the time, so theirs is
    /// raised every frame while a character is in the world.
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

    /// <summary>The stat and the value last seen, as "Health 150 of 200" (it never reads the game).</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Kind} {Volatile.Read(ref _value):0.##} of {Volatile.Read(ref _max):0.##}");

    /// <summary>The id of the stat's attribute, by which the game's own HUD finds it (PlayerWidget).</summary>
    internal static int AttributeId(StatKind kind) => kind switch
    {
        StatKind.Health => 0x898,
        StatKind.Stamina => 0x899,
        StatKind.Warmth => 0x8A2,
        StatKind.Water => 0x8A3,
        StatKind.Food => 0x8A4,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>A character spawned: this stat now reads its pool in the character's sheet (once the character has it).</summary>
    internal void Attach(CharacterSheet sheet)
    {
        _sheet = sheet;
        Resolve();
    }

    /// <summary>The character despawned: the stat reads 0 again.</summary>
    internal void Detach()
    {
        Unwatch();
        _sheet = null;
        _stat = null;
        _found = false;
        Report(0f, 0f, raise: true);
    }

    /// <summary>
    /// Finds the stat's pool on the character if it was not there yet (a character may get its attributes after it
    /// spawned); <see cref="LocalPlayer"/> retries every frame until it is.
    /// </summary>
    internal void Resolve()
    {
        if (_sheet == null || _found) return;
        _stat ??= _sheet.StatById(AttributeId(Kind));
        if (_stat is not { IsPool: true }) return; // the character does not have it yet
        _found = true;
        Sync();
        UpdateWatch();
    }

    private void Write(float value)
    {
        Resolve();
        if (!_found) return;
        _stat!.BaseValue = value;
        Sync(); // the pool's change event usually reported it already; then this finds nothing new
    }

    private void Sync()
    {
        if (_found) Report(_stat!.Value, _stat.Maximum, raise: true);
    }

    private void Report(float value, float max, bool raise)
    {
        Volatile.Write(ref _max, max);
        float old = _value;
        if (old.Equals(value)) return;
        Volatile.Write(ref _value, value);
        if (!raise) return;
        var args = new ValueChangedEventArgs<float>(old, value);
        _changed.Raise(h => h(this, args));
    }

    // The pool (and through it the game) is watched only while a mod listens and the character has it: food and water change
    // every frame.
    private void UpdateWatch()
    {
        bool want = _changed.Count > 0 && _found;
        if (want == _watching) return;
        if (!want)
        {
            Unwatch();
            return;
        }
        _watching = true;
        _stat!.Changed += OnPoolChanged;
        // The first listener came, and nobody saw the old value: start from the current one without an event. (When
        // the pool was just found instead, Resolve has reported its value already.)
        Report(_stat.Value, _stat.Maximum, raise: false);
    }

    private void Unwatch()
    {
        if (!_watching) return;
        _watching = false;
        _stat!.Changed -= OnPoolChanged;
    }

    private void OnPoolChanged(object? sender, ValueChangedEventArgs<float> e) => Report(e.NewValue, _stat?.Maximum ?? 0f, raise: true);
}
