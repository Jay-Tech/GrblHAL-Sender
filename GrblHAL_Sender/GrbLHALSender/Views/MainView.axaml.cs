using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrbLHALSender.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GrbLHALSender.Views;

public partial class MainView : UserControl
{
    private MainViewModel _viewModel;
    private DispatcherTimer? _longPressTimer;
    private bool _longPressTriggered;
    private Flyout? _connectionFlyout;

    // Virtual keyboard — single instance
    private DialogWindow? _keyboardWindow;
    private VirtualKeyboardViewModel? _keyboardViewModel;


    public MainView()
    {
        InitializeComponent();

        // Create the flyout programmatically — NOT on the Button.Flyout property
        // so it does NOT auto-show on every click
        var connectionSettingsView = new ConnectionSettingsView();
        _connectionFlyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = connectionSettingsView
        };

        // Set up long-press on Connect button
        ConnectButton.AddHandler(PointerPressedEvent, ConnectButton_PointerPressed, handledEventsToo: true);
        ConnectButton.AddHandler(PointerReleasedEvent, ConnectButton_PointerReleased, handledEventsToo: true);

        // Global handler: double-tap on any TextBox opens the virtual keyboard
        // Must use handledEventsToo: true because TextBox handles DoubleTapped internally (word select)
        AddHandler(InputElement.DoubleTappedEvent, OnGlobalDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);

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
            _viewModel?.ConnectionViewModel?.OnCloseRequested += OnCloseRequested;

            // Bind the flyout's ConnectionSettingsView to the ConnectionViewModel
            if (_connectionFlyout?.Content is ConnectionSettingsView csv)
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

    public void OnGlobalDoubleTapped(object? sender, TappedEventArgs e)
    {
        // e.Source is often the inner TextPresenter, not the TextBox itself.
        // Walk up the visual tree to find the parent TextBox.
        var targetTextBox = e.Source as TextBox
            ?? (e.Source as Visual)?.FindAncestorOfType<TextBox>();
        if (targetTextBox == null) return;

        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow == null) return;

        // If keyboard window already open, just retarget
        if (_keyboardWindow != null && _keyboardViewModel != null)
        {
            _keyboardViewModel.SetTarget(targetTextBox);
            _keyboardWindow.Activate();
            return;
        }

        // Create new keyboard instance
        _keyboardViewModel = new VirtualKeyboardViewModel();
        _keyboardViewModel.SetTarget(targetTextBox);

        var keyboardView = new VirtualKeyboardView
        {
            DataContext = _keyboardViewModel
        };

        _keyboardWindow = new DialogWindow(
            title: "VirtualKeyBoard",
            content: keyboardView,
            width: 750,
            height: 265
        );
        _keyboardWindow.CanResize = false;
        _keyboardWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        _keyboardWindow.Position = new PixelPoint(
            (int)(parentWindow.Position.X + (parentWindow.Bounds.Width - _keyboardWindow.Width) / 2),
            (int)(parentWindow.Bounds.Height - _keyboardWindow.Height -100));
        _keyboardWindow.Closed += (_, _) =>
        {
            _keyboardWindow = null;
            _keyboardViewModel = null;
        };

        _keyboardWindow.Show(parentWindow);
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
