using System;
using System.Threading;

namespace Mjolnir.SDK;

/// <summary>
/// A key's text in the player's language as a binding source (<see cref="Localization.GetBindable"/>): bound to a text
/// property, it shows the text and follows a change of language. Read-only: the text comes from the language files.
/// </summary>
public sealed class TranslatedText : IReadOnlyBindable<string>
{
    private readonly ModStrings? _owner;
    private readonly object?[] _args;
    private readonly object _lock = new();
    private EventHandler<ValueChangedEventArgs<string>>? _changed;
    private string? _last;

    internal TranslatedText(ModStrings? owner, string key, object?[] args)
    {
        _owner = owner;
        _args = args;
        Key = key;
    }

    /// <summary>The key the text is looked up by.</summary>
    public string Key { get; }

    /// <summary>The text in the player's language now.</summary>
    public string Value
    {
        get
        {
            string text = Localization.Resolve(_owner, Key, _args);
            Volatile.Write(ref _last, text);
            return text;
        }
    }

    /// <summary>Raised on the main thread when a change of language changed the text, with the old and the new text.</summary>
    public event EventHandler<ValueChangedEventArgs<string>>? Changed
    {
        add
        {
            if (value == null) return;
            lock (_lock)
            {
                if (_changed == null)
                {
                    // Watched only while someone listens, so a short-lived text never stays alive through the language.
                    Localization.Switched += OnSwitched;
                    _ = Value;
                }
                _changed += value;
            }
        }
        remove
        {
            if (value == null) return;
            lock (_lock)
            {
                _changed -= value;
                if (_changed == null) Localization.Switched -= OnSwitched;
            }
        }
    }

    /// <summary>The text last shown, or the key before it was read (it never looks the key up).</summary>
    public override string ToString() => Volatile.Read(ref _last) ?? Key;

    private void OnSwitched()
    {
        var handlers = Volatile.Read(ref _changed);
        if (handlers == null) return;
        string old = Volatile.Read(ref _last) ?? Key;
        string now = Value;
        if (old == now) return;
        ModCall.InvokeEach(handlers, this, new ValueChangedEventArgs<string>(old, now), "TranslatedText.Changed");
    }
}
