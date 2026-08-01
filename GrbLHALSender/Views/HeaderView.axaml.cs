using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GrbLHALSender.ViewModels;
using System;

namespace GrbLHALSender.Views;

/// <summary>
/// Status strip along the top: connect, machine state, WCS, tool, jog step and feed, units,
/// signal LEDs, alarm and unlock. Binds to MainViewModel, inherited from whichever canvas
/// hosts it.
/// </summary>
public partial class HeaderView : UserControl
{
    private DispatcherTimer? _longPressTimer;
    private bool _longPressTriggered;
    private readonly Flyout _connectionFlyout;
    private ConnectionViewModel? _connectionViewModel;

    public HeaderView()
    {
        InitializeComponent();

        // Create the flyout programmatically — NOT on the Button.Flyout property
        // so it does NOT auto-show on every click
        _connectionFlyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new ConnectionSettingsView()
        };

        // Set up long-press on Connect button
        ConnectButton.AddHandler(PointerPressedEvent, ConnectButton_PointerPressed, handledEventsToo: true);
        ConnectButton.AddHandler(PointerReleasedEvent, ConnectButton_PointerReleased, handledEventsToo: true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_connectionViewModel != null)
            _connectionViewModel.OnCloseRequested -= OnCloseRequested;
        _connectionViewModel = null;

        if (DataContext is MainViewModel vm)
        {
            _connectionViewModel = vm.ConnectionViewModel;
            if (_connectionViewModel != null)
                _connectionViewModel.OnCloseRequested += OnCloseRequested;

            // Bind the flyout's ConnectionSettingsView to the ConnectionViewModel
            if (_connectionFlyout.Content is ConnectionSettingsView csv)
                csv.DataContext = vm.ConnectionViewModel;
        }
        base.OnDataContextChanged(e);
    }

    private void ConnectButton_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _longPressTriggered = false;

        _longPressTimer?.Stop();
        _longPressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _longPressTimer.Tick += (_, _) =>
        {
            _longPressTimer.Stop();
            _longPressTriggered = true;

            // Show the connection settings flyout
            _connectionFlyout.ShowAt(ConnectButton);
        };
        _longPressTimer.Start();
    }

    private void ConnectButton_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _longPressTimer?.Stop();

        if (_longPressTriggered)
        {
            // Long-press was triggered — flyout is open, don't fire ConnectCommand
            e.Handled = true;
        }
    }

    private void OnCloseRequested()
    {
        // Hide the flyout
        _connectionFlyout.Hide();
    }
}
