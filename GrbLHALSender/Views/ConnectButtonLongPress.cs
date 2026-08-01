using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GrbLHALSender.ViewModels;
using System;

namespace GrbLHALSender.Views;

/// <summary>
/// Long-press on the Connect button opens the connection settings flyout; a normal tap runs
/// ConnectCommand.
/// <para>
/// Composed rather than inherited so each header layout keeps its own compile-time reference to
/// its Connect button. Landscape and portrait arrange the status strip differently but must not
/// carry two copies of this.
/// </para>
/// </summary>
internal sealed class ConnectButtonLongPress
{
    private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(500);

    private readonly Button _button;
    private readonly Flyout _flyout;
    private DispatcherTimer? _timer;
    private bool _triggered;
    private ConnectionViewModel? _connectionViewModel;

    public ConnectButtonLongPress(Button button)
    {
        _button = button;

        // Created programmatically — NOT on the Button.Flyout property, so it does NOT
        // auto-show on every click.
        _flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new ConnectionSettingsView()
        };

        _button.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
        _button.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
    }

    /// <summary>
    /// Points the flyout at the current view model. Safe to call whenever DataContext changes:
    /// the previous subscription is dropped first, so a re-bind cannot stack handlers.
    /// </summary>
    public void Bind(MainViewModel? vm)
    {
        if (_connectionViewModel != null)
            _connectionViewModel.OnCloseRequested -= OnCloseRequested;

        _connectionViewModel = vm?.ConnectionViewModel;

        if (_connectionViewModel != null)
            _connectionViewModel.OnCloseRequested += OnCloseRequested;

        if (_flyout.Content is ConnectionSettingsView csv)
            csv.DataContext = _connectionViewModel;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _triggered = false;

        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = HoldTime };
        _timer.Tick += (_, _) =>
        {
            _timer!.Stop();
            _triggered = true;
            _flyout.ShowAt(_button);
        };
        _timer.Start();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _timer?.Stop();

        // The flyout is already open, so swallow the release rather than also connecting.
        if (_triggered)
            e.Handled = true;
    }

    private void OnCloseRequested() => _flyout.Hide();
}
