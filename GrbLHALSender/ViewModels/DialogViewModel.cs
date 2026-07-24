using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels;

public enum DialogType
{
    Console,
    Probe,
    Macro,
    AppConfig,
    Surfacing
}

public class DialogViewModel : ViewModelBase
{
    /// <summary>
    /// Raised when a dialog should be opened. The View subscribes
    /// and shows the content in the matching overlay host.
    /// </summary>
    public event Action<DialogType>? OpenDialogRequested;

    /// <summary>
    /// Raised when an already-open dialog's button is pressed again —
    /// the buttons toggle, so the View closes the overlay.
    /// </summary>
    public event Action<DialogType>? CloseDialogRequested;


    // Track open dialogs to prevent duplicates
    private readonly HashSet<DialogType> _openDialogs = new();

    public ICommand OpenConsoleCommand { get; }
    public ICommand OpenProbeCommand { get; }
    public ICommand OpenMacroCommand { get; }
    public ICommand OpenUtilityCommand { get; }
    public ICommand OpenSurfacingCommand { get; }

    public DialogViewModel()
    {
        OpenConsoleCommand = ReactiveCommand.Create(() => RequestOpenDialog(DialogType.Console));
        OpenProbeCommand = ReactiveCommand.Create(() => RequestOpenDialog(DialogType.Probe));
        OpenMacroCommand = ReactiveCommand.Create(() => RequestOpenDialog(DialogType.Macro));
        OpenUtilityCommand = ReactiveCommand.Create(() => RequestOpenDialog(DialogType.AppConfig));
        OpenSurfacingCommand = ReactiveCommand.Create(() => RequestOpenDialog(DialogType.Surfacing));
    }

    private void RequestOpenDialog(DialogType dialogType)
    {
        if (_openDialogs.Contains(dialogType))
        {
            CloseDialogRequested?.Invoke(dialogType);
            return;
        }
        OpenDialogRequested?.Invoke(dialogType);
    }

    public void MarkDialogOpened(DialogType dialogType)
    {
        _openDialogs.Add(dialogType);
    }

    public void MarkDialogClosed(DialogType dialogType)
    {
        _openDialogs.Remove(dialogType);
    }
}
