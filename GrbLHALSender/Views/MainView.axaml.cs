using Avalonia.Controls;
using GrbLHALSender.ViewModels;
using System;

namespace GrbLHALSender.Views;

/// <summary>
/// The landscape canvas, 1920x1080: header across the top, then DRO, workspace and jog panels
/// in three columns. <see cref="MainPortraitView"/> arranges the same panels for 1080x1920.
/// </summary>
public partial class MainView : UserControl, IDialogCanvas
{
    private readonly RootCanvasBehaviour _behaviour;

    // Explicit implementation: the generated x:Name fields already own these names.
    DialogHostView IDialogCanvas.ToolDialogHost => DialogHost;
    DialogHostView IDialogCanvas.ConsoleDialogHost => ConsoleHost;

    public MainView()
    {
        InitializeComponent();
        _behaviour = new RootCanvasBehaviour(this, KeyboardOverlay);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        _behaviour.Bind(DataContext as MainViewModel);
        base.OnDataContextChanged(e);
    }
}
