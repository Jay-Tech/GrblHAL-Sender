using Avalonia.Controls;

namespace GrbLHALSender.Views;

/// <summary>
/// Home, DRO readouts, WCS selection and MDI. Binds to MainViewModel, inherited from
/// whichever canvas hosts it.
/// </summary>
public partial class DroPanelView : UserControl
{
    public DroPanelView()
    {
        InitializeComponent();
    }
}
