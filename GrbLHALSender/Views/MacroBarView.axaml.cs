using Avalonia.Controls;

namespace GrbLHALSender.Views;

/// <summary>
/// The macro buttons as a horizontal strip alongside the tab headers. Used by the landscape
/// canvas; the portrait control block uses <see cref="MacroGridView"/>, which flows them into
/// a narrow column instead. Binds to MainViewModel, inherited from its host.
/// </summary>
public partial class MacroBarView : UserControl
{
    public MacroBarView()
    {
        InitializeComponent();
    }
}
