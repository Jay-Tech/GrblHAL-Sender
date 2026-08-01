namespace GrbLHALSender.Views;

/// <summary>
/// A root canvas that owns the two in-window dialog overlays.
/// <para>
/// Dialogs are panels inside the main window rather than child windows, so whatever opens one
/// has to walk up the visual tree to find the host. Looking for a concrete root view would tie
/// that search to a single canvas — and fail silently on the other, since a missing host just
/// makes the open request return early. Both <see cref="MainView"/> and
/// <see cref="MainPortraitView"/> implement this so the search finds whichever is live.
/// </para>
/// </summary>
internal interface IDialogCanvas
{
    /// <summary>
    /// Single-slot host for the tool dialogs — probe, macro, surfacing and config replace
    /// each other.
    /// </summary>
    DialogHostView ToolDialogHost { get; }

    /// <summary>
    /// Independent host, so the console can stay open for monitoring while a tool dialog is up.
    /// </summary>
    DialogHostView ConsoleDialogHost { get; }
}
