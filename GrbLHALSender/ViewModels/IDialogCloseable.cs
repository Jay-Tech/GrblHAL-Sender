using System;

namespace GrbLHALSender.ViewModels;

/// <summary>
/// Interface for ViewModels hosted in a dialog overlay that need a close button.
/// Set <see cref="CloseAction"/> when the dialog is shown; the ViewModel's
/// close command invokes it to dismiss the overlay.
/// </summary>
public interface IDialogCloseable
{
    Action? CloseAction { get; set; }
}
