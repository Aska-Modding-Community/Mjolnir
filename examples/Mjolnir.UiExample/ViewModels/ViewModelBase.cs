using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Mjolnir.UiExample.ViewModels;

/// <summary>
/// The base of the example's view models: <see cref="INotifyPropertyChanged"/>, which
/// <c>Bind(property, vm, x => x.Member)</c> follows, and a setter helper that raises it only for a real change.
/// </summary>
internal abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stores <paramref name="value"/> and raises <see cref="PropertyChanged"/>; false if it was the value already.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
