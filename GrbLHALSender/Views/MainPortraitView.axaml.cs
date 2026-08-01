using Avalonia.Controls;
using GrbLHALSender.ViewModels;
using System;

namespace GrbLHALSender.Views;

/// <summary>
/// The portrait canvas, 1080x1920: a two-row header, the workspace, then the DRO and jog panels
/// side by side in a control block along the bottom. <see cref="MainView"/> arranges the same
/// panels for 1920x1080.
/// </summary>
public partial class MainPortraitView : UserControl, IDialogCanvas
{
    private readonly RootCanvasBehaviour _behaviour;

    // Explicit implementation: the generated x:Name fields already own these names.
    DialogHostView IDialogCanvas.ToolDialogHost => DialogHost;
    DialogHostView IDialogCanvas.ConsoleDialogHost => ConsoleHost;

    public MainPortraitView()
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
