using Avalonia.Controls;
using GrbLHALSender.ViewModels;

namespace GrbLHALSender.Views;

public partial class ConnectionSettingsView : UserControl
{

   
    public ConnectionSettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdatePanelVisibility();

        if (DataContext is ConnectionViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ConnectionViewModel.SelectedConnectionType))
                    UpdatePanelVisibility();
            };
        }
    }

    private void UpdatePanelVisibility()
    {
        if (DataContext is ConnectionViewModel vm)
        {
            SerialPanel.IsVisible = vm.SelectedConnectionType == 0;
            TcpPanel.IsVisible = vm.SelectedConnectionType == 1;
            WsPanel.IsVisible = vm.SelectedConnectionType == 2;
        }
    }

}
