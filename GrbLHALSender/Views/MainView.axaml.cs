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
using System.Threading.Tasks;

namespace GrbLHALSender.Views;

public partial class MainView : UserControl
{
    private MainViewModel _viewModel;
    private DispatcherTimer? _longPressTimer;
    private bool _longPressTriggered;
    private Flyout? _connectionFlyout;

    // Jog press-and-hold state
    private DispatcherTimer? _jogHoldTimer;
    private bool _jogHoldActive;
    private string? _jogHoldAxis;
    private bool _jogHoldPositive;

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

        // Set up press-and-hold for continuous jog on all jog buttons
        SetupJogButton(XDown, "X", false);
        SetupJogButton(Xup, "X", true);
        SetupJogButton(YUp, "Y", true);
        SetupJogButton(YDown, "Y", false);
        SetupJogButton(ZUp, "Z", true);
        SetupJogButton(ZDown, "Z", false);

        // Touch can leave :pressed/:pointerover stuck on aux output buttons
        // (no pointer-leave event after a finger lifts), which masks their
        // state styling. Clear both when the pointer really has gone away.
        AuxOutputRepeater.AddHandler(PointerReleasedEvent,
            (_, e) => ClearStuckTouchState(e.Source), RoutingStrategies.Tunnel, handledEventsToo: true);
        AuxOutputRepeater.AddHandler(PointerCaptureLostEvent,
            (_, e) => ClearStuckTouchState(e.Source), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void ClearStuckTouchState(object? source)
    {
        if (source is not Visual v) return;
        var button = v as Button ?? v.FindAncestorOfType<Button>();
        if (button == null) return;
        var pseudo = (IPseudoClasses)button.Classes;
        pseudo.Set(":pressed", false);
        pseudo.Set(":pointerover", false);
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

        // If keyboard window already open, just retarget. Do NOT Activate()
        // the keyboard window — that steals focus from the main window and
        // forces a double-touch to get it back. But DO make sure it is still
        // actually on screen: the WM can hide/unmap it without our Closed
        // handler firing, which used to leave a live-but-invisible window that
        // blocked the keyboard from ever showing again until app restart.
        if (_keyboardWindow != null && _keyboardViewModel != null)
        {
            try
            {
                _keyboardViewModel.SetTarget(targetTextBox);
                if (!_keyboardWindow.IsVisible)
                    _keyboardWindow.Show(parentWindow);
                return;
            }
            catch
            {
                // Window is in a broken state — drop it and build a fresh one below.
                try { _keyboardWindow.Close(); } catch { /* already dead */ }
                _keyboardWindow = null;
                _keyboardViewModel = null;
            }
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
            height: 285
        );
        _keyboardWindow.CanResize = false;
        // Remove the OS title bar entirely: CanMinimize/CanMaximize are only
        // WM hints and the Linux WM ignores them. BorderOnly keeps the frame;
        // the keyboard's own ✕ key handles closing.
        _keyboardWindow.WindowDecorations = WindowDecorations.BorderOnly;
        // Hand focus to the main window BEFORE the keyboard dies. On X11 the
        // close is async: activating from the Closed handler races the WM's
        // own focus-revert (focused window destroyed → focus to nothing) and
        // loses, leaving the next touch consumed by re-activation.
        _keyboardViewModel.CloseAction = () =>
        {
            parentWindow.Activate();
            _keyboardWindow?.Close();
        };
        _keyboardWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        _keyboardWindow.Position = new PixelPoint(
            (int)(parentWindow.Position.X + (parentWindow.Bounds.Width - _keyboardWindow.Width) / 2),
            (int)(parentWindow.Bounds.Height - _keyboardWindow.Height - 100));
        _keyboardWindow.Closed += (_, _) =>
        {
            _keyboardWindow = null;
            _keyboardViewModel = null;

            // Fallback for the CloseAction pre-activation: re-activate again
            // shortly AFTER the window is actually gone. An immediate call here
            // races the WM's focus-revert on window destruction and loses;
            // deferred, it runs once the WM has settled.
            DispatcherTimer.RunOnce(parentWindow.Activate, TimeSpan.FromMilliseconds(150));
        };

        // Show without taking focus — the target TextBox in the main window
        // keeps its focus and caret.
        _keyboardWindow.ShowActivated = false;
        _keyboardWindow.Show(parentWindow);
    }

    private void SetupJogButton(Button button, string axis, bool positive)
    {
        button.AddHandler(PointerPressedEvent, (_, e) => JogButton_PointerPressed(axis, positive), RoutingStrategies.Tunnel);
        button.AddHandler(PointerReleasedEvent, (_, e) => JogButton_PointerReleased(e), RoutingStrategies.Tunnel);
        button.AddHandler(PointerCaptureLostEvent, (_, _) => JogButton_CaptureLost(), RoutingStrategies.Tunnel);
    }

    private void JogButton_PointerPressed(string axis, bool positive)
    {
        _jogHoldActive = false;
        _jogHoldAxis = axis;
        _jogHoldPositive = positive;

        _jogHoldTimer?.Stop();
        _jogHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _jogHoldTimer.Tick += (_, _) =>
        {
            _jogHoldTimer!.Stop();
            _jogHoldActive = true;

            if (_jogHoldPositive)
                _viewModel?.JogContinuousPos(_jogHoldAxis!);
            else
                _viewModel?.JogContinuousNeg(_jogHoldAxis!);
        };
        _jogHoldTimer.Start();
    }

    private void JogButton_PointerReleased(PointerReleasedEventArgs e)
    {
        _jogHoldTimer?.Stop();

        if (_jogHoldActive)
        {
            _viewModel?.JogCancel();
            _jogHoldActive = false;
            e.Handled = true;
        }
    }

    private void JogButton_CaptureLost()
    {
        _jogHoldTimer?.Stop();

        if (_jogHoldActive)
        {
            _viewModel?.JogCancel();
            _jogHoldActive = false;
        }
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
