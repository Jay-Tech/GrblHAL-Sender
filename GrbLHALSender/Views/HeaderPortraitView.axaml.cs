using Avalonia.Controls;
using GrbLHALSender.ViewModels;
using System;

namespace GrbLHALSender.Views;

/// <summary>
/// The status strip for the portrait canvas — the same values as <see cref="HeaderView"/>
/// arranged in two rows, because the landscape strip's fixed columns cannot fit 1080px.
/// </summary>
public partial class HeaderPortraitView : UserControl
{
    private readonly ConnectButtonLongPress _connect;

    public HeaderPortraitView()
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
