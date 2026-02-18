using System;

namespace GrbLHALSender.ViewModels;

/// <summary>
/// Interface for ViewModels hosted in a DialogWindow that need a close button.
/// Set <see cref="CloseAction"/> after the DialogWindow is created, then
/// <see cref="CloseCommand"/> will invoke it to close the window.
/// </summary>
public interface IDialogCloseable
{
    Action? CloseAction { get; set; }
}
