using Avalonia.Controls;
using GrbLHALSender.ViewModels;
using System;

namespace GrbLHALSender.Views;

/// <summary>
/// Status strip along the top of the landscape canvas: connect, machine state, WCS, tool, jog
/// step and feed, units, signal LEDs, alarm and unlock. Binds to MainViewModel, inherited from
/// whichever canvas hosts it.
/// <para>
/// The portrait canvas uses <see cref="HeaderPortraitView"/>, which shows the same values in two
/// rows. Both share <see cref="ConnectButtonLongPress"/>.
/// </para>
/// </summary>
public partial class HeaderView : UserControl
{
    private readonly ConnectButtonLongPress _connect;

    public HeaderView()
    {
        InitializeComponent();
        _connect = new ConnectButtonLongPress(ConnectButton);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        _connect.Bind(DataContext as MainViewModel);
        base.OnDataContextChanged(e);
    }
}
