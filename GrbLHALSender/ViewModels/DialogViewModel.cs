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
    AppConfig
}

public class DialogViewModel : ViewModelBase
{
    /// <summary>
    /// Raised when a dialog should be opened. The View subscribes
    /// and handles the actual Window creation.
    /// </summary>
    public event Action<DialogType>? OpenDialogRequested;

    public event Action<DialogType>? BringDialogFrontRequested;


    // Track open dialogs to prevent duplicates
    private readonly HashSet<DialogType> _openDialogs = new();

    public ICommand OpenConsoleCommand { get; }
    public ICommand OpenProbeCommand { get; }
    public ICommand OpenMacroCommand { get; }
    public ICommand OpenUtilityCommand { get; }

    public DialogViewModel()
    {
        OpenConsoleCommand = ReactiveCommand.Create(() => RequestOpenDialog(DialogType.Console));
        OpenProbeCommand = ReactiveCommand.Create(() => RequestOpenDialog(DialogType.Probe));
        OpenMacroCommand = ReactiveCommand.Create(() => RequestOpenDialog(DialogType.Macro));
        OpenUtilityCommand = ReactiveCommand.Create(() => RequestOpenDialog(DialogType.AppConfig));
    }

    private void RequestOpenDialog(DialogType dialogType)
    {
        if (_openDialogs.Contains(dialogType))
        {
            BringDialogFrontRequested?.Invoke(dialogType);
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
