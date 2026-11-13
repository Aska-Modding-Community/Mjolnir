using System;

namespace Mjolnir.SDK.UI;

/// <summary>
/// A key the player sets (a hotkey's row in the Mods window): it shows the <see cref="Gesture"/>, and a click waits for
/// the next key, with the modifiers held. Esc cancels, Backspace or Delete clears it. <see cref="Gesture"/> binds
/// two-way by default: <c>box.Bind(UiHotKeyBox.GestureProperty, config.ToggleKey)</c>, or
/// <c>SettingControls.For(config.ToggleKey)</c>, which makes this box for a <see cref="KeyGesture"/> setting.
/// </summary>
public sealed class UiHotKeyBox : HeaderedControl
{
    public static readonly MjolnirProperty<KeyGesture> GestureProperty =
        MjolnirProperty.Register<UiHotKeyBox, KeyGesture>(nameof(Gesture), KeyGesture.None, DirtyFlags.Value, BindingMode.TwoWay);

    /// <summary>Waiting for the player's key (internal: the box itself starts and ends it).</summary>
    internal static readonly MjolnirProperty<bool> IsRecordingProperty =
        MjolnirProperty.Register<UiHotKeyBox, bool>(nameof(IsRecording), false, DirtyFlags.Value);

    private ModHandlers<EventHandler<ValueChangedEventArgs<KeyGesture>>>? _gestureChanged;
    private Action? _cancel;

    /// <summary>The key the box shows; <see cref="KeyGesture.None"/> for none.</summary>
    public KeyGesture Gesture
    {
        get => GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    /// <summary>Raised after the gesture changed: by the player, by code or by a binding.</summary>
    public event EventHandler<ValueChangedEventArgs<KeyGesture>>? GestureChanged
    {
        add => ModHandlers<EventHandler<ValueChangedEventArgs<KeyGesture>>>.Of(ref _gestureChanged, $"{this}.GestureChanged").Add(value);
        remove => _gestureChanged?.Remove(value);
    }

    internal bool IsRecording => GetValue(IsRecordingProperty);

    /// <summary>What the box shows: the gesture, "None", or the call to press a key while it records.</summary>
    internal string ShownText =>
        IsRecording ? SdkTexts.Get("hotkey.press") : Gesture.IsNone ? SdkTexts.Get("hotkey.none") : Gesture.ToString();

    /// <summary>The player clicked the box: wait for their key (its view calls this).</summary>
    internal void StartRecording()
    {
        if (IsRecording) return;
        SetValue(IsRecordingProperty, true);
        Action cancel = () => SetValue(IsRecordingProperty, false);
        _cancel = cancel;
        HotKeys.Record(gesture =>
        {
            SetValue(IsRecordingProperty, false);
            Gesture = gesture;
        }, cancel, () => IsOnScreen);
    }

    private protected override void Detached()
    {
        // Off screen (the window closed): no key goes to a box the player cannot see.
        if (_cancel is { } cancel) HotKeys.StopRecording(cancel);
        _cancel = null;
    }

    private protected override void Changed(MjolnirPropertyChangedEventArgs e, DirtyFlags dirty)
    {
        base.Changed(e, dirty);
        if (ReferenceEquals(e.Property, GestureProperty) && _gestureChanged != null)
        {
            var args = new ValueChangedEventArgs<KeyGesture>(e.GetOldValue<KeyGesture>(), e.GetNewValue<KeyGesture>());
            _gestureChanged.Raise(h => h(this, args));
        }
    }

    public override string ToString() => GetStored(HeaderProperty) is { } header ? $"UiHotKeyBox '{header}'" : "UiHotKeyBox";
}
