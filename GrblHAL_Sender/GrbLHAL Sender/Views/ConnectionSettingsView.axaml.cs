using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GrbLHAL_Sender.ViewModels;

namespace GrbLHAL_Sender.Views;

public partial class ConnectionSettingsView : UserControl
{

    public event Action? OnCloseRequested;
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

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        OnCloseRequested?.Invoke();
    }
}
