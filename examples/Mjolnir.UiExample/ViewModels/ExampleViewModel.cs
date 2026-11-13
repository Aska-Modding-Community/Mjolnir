using System.Collections.ObjectModel;
using Mjolnir.SDK.UI;

namespace Mjolnir.UiExample.ViewModels;

/// <summary>
/// The example window's state, the MVVM way: properties raise PropertyChanged, actions are commands, a list is an
/// ObservableCollection. No UI type in here: the window binds to it with <c>Bind(property, vm, x => x.Clicks)</c>,
/// <c>Command = vm.CountCommand</c> and <c>ItemsSource = vm.Notes</c>.
/// </summary>
internal sealed class ExampleViewModel : ViewModelBase
{
    private int _clicks;

    public ExampleViewModel()
    {
        CountCommand = new Command(() => Clicks++);
        ResetCommand = new Command(() => Clicks = 0, () => Clicks > 0);
        AddNoteCommand = new Command(() => Notes.Add($"Note {Notes.Count + 1}"));
        RemoveNoteCommand = new Command(() => Notes.RemoveAt(Notes.Count - 1), () => Notes.Count > 0);
        Notes.CollectionChanged += (s, e) => RemoveNoteCommand.RaiseCanExecuteChanged();
    }

    public int Clicks
    {
        get => _clicks;
        set
        {
            if (SetProperty(ref _clicks, value)) ResetCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<string> Notes { get; } = ["The first note"];

    public Command CountCommand { get; }

    /// <summary>Resets the clicks; it cannot while there are none, and its button is dimmed then.</summary>
    public Command ResetCommand { get; }

    public Command AddNoteCommand { get; }

    /// <summary>Removes the last note; it cannot while there are none.</summary>
    public Command RemoveNoteCommand { get; }
}
