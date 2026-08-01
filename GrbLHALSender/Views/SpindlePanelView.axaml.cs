using Avalonia.Controls;

namespace GrbLHALSender.Views;

/// <summary>
/// Spindle direction, speed and override. Binds to MainViewModel, inherited from its host.
/// </summary>
public partial class SpindlePanelView : UserControl
{
    public SpindlePanelView()
    {
        InitializeComponent();
    }
}
