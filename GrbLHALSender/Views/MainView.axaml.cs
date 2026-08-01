using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GrbLHALSender.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GrbLHALSender.Views;

public partial class MainView : UserControl
{
    private MainViewModel _viewModel;

    // Virtual keyboard — overlay panel inside this window, single VM instance
    private readonly VirtualKeyboardViewModel _keyboardViewModel = new();


    public MainView()
    {
        InitializeComponent();

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

        // While the keyboard is up it follows focus, so tapping another field retargets it.
        // Without this a single tap gave the field an accent border while the keys went on
        // editing the previous one.
        AddHandler(InputElement.GotFocusEvent, OnGlobalGotFocus,
            RoutingStrategies.Bubble, handledEventsToo: true);

        // Wire the keyboard overlay: it inherits MainViewModel as DataContext
        // by default, so give it its own VM, and let ✕ hide the panel.
        KeyboardOverlay.DataContext = _keyboardViewModel;
        _keyboardViewModel.CloseAction = () => KeyboardOverlay.IsVisible = false;
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e) =>
        e.Handled = true;

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

    public void OnGlobalDoubleTapped(object? sender, TappedEventArgs e)
    {
        var targetTextBox = ResolveTextBox(e.Source);
        if (targetTextBox == null) return;

        // The keyboard is an overlay panel inside this window — showing it is
        // just a visibility flip. No OS window, no focus/activation involved.
        _keyboardViewModel.SetTarget(targetTextBox);
        KeyboardOverlay.IsVisible = true;
    }

    /// <summary>
    /// Retargets the open keyboard at whatever field just took focus.
    /// <para>
    /// A single tap focuses a field and draws its accent border, which reads as "this is the
    /// one you are editing" — but the keyboard only retargeted on a double tap, so it went on
    /// typing into the previous field. The operator gets a field that looks selected and values
    /// landing somewhere else, with nothing on screen saying so.
    /// </para>
    /// <para>
    /// Only while the keyboard is already up. Focus alone must not open it, or tabbing or
    /// clicking through fields with a mouse would raise it unasked.
    /// </para>
    /// </summary>
    private void OnGlobalGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!KeyboardOverlay.IsVisible) return;

        var targetTextBox = ResolveTextBox(e.Source);
        if (targetTextBox == null) return;

        _keyboardViewModel.SetTarget(targetTextBox);
    }

    /// <summary>
    /// The TextBox an event belongs to, or null. The source is usually the inner TextPresenter
    /// rather than the TextBox itself, and the keyboard's own read-only surfaces never count.
    /// </summary>
    private static TextBox? ResolveTextBox(object? source)
    {
        var textBox = source as TextBox ?? (source as Visual)?.FindAncestorOfType<TextBox>();
        if (textBox == null) return null;

        return textBox.FindAncestorOfType<VirtualKeyboardView>() != null ? null : textBox;
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
