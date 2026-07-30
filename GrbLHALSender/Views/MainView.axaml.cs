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

    // Virtual keyboard — overlay panel inside this window, single VM instance
    private readonly VirtualKeyboardViewModel _keyboardViewModel = new();


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

        // Kill the text context menu at the source. Nulling TextBox.ContextFlyout removed the
        // full Cut/Copy/Paste menu most of the time but left a lone "Paste" still appearing
        // over the fields on touch, so the property is not the only route to it.
        //
        // Registered for both tunnel and bubble on purpose: tunnel is what gets there before
        // the TextBox acts on it, but the event may be declared bubble-only, in which case a
        // tunnel handler would silently never fire. Nothing in this app wants a context menu —
        // text entry goes through the virtual keyboard — so suppressing every route is safe.
        AddHandler(Control.ContextRequestedEvent, OnContextRequested,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        // Wire the keyboard overlay: it inherits MainViewModel as DataContext
        // by default, so give it its own VM, and let ✕ hide the panel.
        KeyboardOverlay.DataContext = _keyboardViewModel;
        _keyboardViewModel.CloseAction = () => KeyboardOverlay.IsVisible = false;

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

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e) =>
        e.Handled = true;

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

        // Ignore double-taps on the keyboard's own (read-only) surfaces.
        if (targetTextBox.FindAncestorOfType<VirtualKeyboardView>() != null) return;

        // The keyboard is an overlay panel inside this window — showing it is
        // just a visibility flip. No OS window, no focus/activation involved.
        _keyboardViewModel.SetTarget(targetTextBox);
        KeyboardOverlay.IsVisible = true;
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
        if (topLevel == null) return null;

        var options = new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = input,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("G-Code Files")
                {
                    Patterns = new[] { "*.nc", "*.ngc", "*.gcode", "*.tap", "*.gc", "*.cnc" }
                },
                new("All Files") { Patterns = new[] { "*.*" } }
            }
        };

        // Open where the web server puts uploads. That is under the app data directory,
        // which on Linux is ~/.config — a leading-dot path no file picker lists, so a file
        // uploaded from a phone was effectively unreachable from the touchscreen.
        var start = _viewModel?.GcodeStartFolder;
        if (!string.IsNullOrEmpty(start))
        {
            try
            {
                options.SuggestedStartLocation =
                    await topLevel.StorageProvider.TryGetFolderFromPathAsync(start);
            }
            catch
            {
                // A start folder that cannot be resolved is a worse reason to fail than it
                // is a problem — the picker still opens, just wherever it did before.
            }
        }

        return await topLevel.StorageProvider.OpenFilePickerAsync(options);
    }
}
