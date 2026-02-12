using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GrbLHAL_Sender.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;


namespace GrbLHAL_Sender.Views;

public partial class MainView : UserControl
{
    private MainViewModel _viewModel;
    private DispatcherTimer? _longPressTimer;
    private bool _longPressTriggered;
    private Flyout? _connectionFlyout;
    private ConnectionSettingsView? _connectionSettingsView;

    public MainView()
    {
        InitializeComponent();

        // Get the Flyout and its content from the Connect button
        _connectionFlyout = ConnectButton.Flyout as Flyout;
        _connectionSettingsView = _connectionFlyout?.Content as ConnectionSettingsView;

        // Set up long-press on Connect button
        ConnectButton.AddHandler(PointerPressedEvent, ConnectButton_PointerPressed, handledEventsToo: true);
        ConnectButton.AddHandler(PointerReleasedEvent, ConnectButton_PointerReleased, handledEventsToo: true);

        // Wire up the ConnectRequested event from the ConnectionSettingsView
        if (_connectionSettingsView != null)
            _connectionSettingsView.OnCloseRequested += OnCloseRequested;
    }

    IDisposable? _selectFilesInteractionDisposable;

    protected override void OnDataContextChanged(EventArgs e)
    {
        _selectFilesInteractionDisposable?.Dispose();

        if (DataContext is MainViewModel vm)
        {
            _viewModel = vm;
            _selectFilesInteractionDisposable =
                vm.JobViewModel.SelectFilesInteraction.RegisterHandler(InteractionHandler);
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
            _connectionFlyout?.ShowAt(ConnectButton);
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
        _connectionFlyout?.Hide();
    }

    private void ToolLb_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        SplitB.Flyout?.Hide();
    }

    private async Task<IReadOnlyList<IStorageFile>?> InteractionHandler(string input)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var storageFiles = await topLevel!.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions()
            {
                AllowMultiple = true,
                Title = input
            });
        return storageFiles;
    }
}
