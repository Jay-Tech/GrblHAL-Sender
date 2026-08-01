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

/// <summary>
/// The behaviour a root canvas owns regardless of how it arranges its panels: the virtual
/// keyboard overlay, the input suppression the touchscreen needs, and the g-code file picker.
/// <para>
/// Composed by both <see cref="MainView"/> and <see cref="MainPortraitView"/>. None of it
/// depends on the layout, so neither canvas should carry its own copy.
/// </para>
/// </summary>
internal sealed class RootCanvasBehaviour
{
    private readonly UserControl _root;
    private readonly VirtualKeyboardView _keyboard;
    private readonly VirtualKeyboardViewModel _keyboardViewModel = new();

    private MainViewModel? _viewModel;
    private IDisposable? _selectFilesInteraction;

    public RootCanvasBehaviour(UserControl root, VirtualKeyboardView keyboard)
    {
        _root = root;
        _keyboard = keyboard;

        // Global handler: double-tap on any TextBox opens the virtual keyboard.
        // Must use handledEventsToo: true because TextBox handles DoubleTapped internally
        // (word select).
        _root.AddHandler(InputElement.DoubleTappedEvent, OnGlobalDoubleTapped,
            RoutingStrategies.Bubble, handledEventsToo: true);

        // Kill the text context menu at the source. Nulling TextBox.ContextFlyout removed the
        // full Cut/Copy/Paste menu most of the time but left a lone "Paste" still appearing
        // over the fields on touch, so the property is not the only route to it.
        //
        // Registered for both tunnel and bubble on purpose: tunnel is what gets there before
        // the TextBox acts on it, but the event may be declared bubble-only, in which case a
        // tunnel handler would silently never fire. Nothing in this app wants a context menu —
        // text entry goes through the virtual keyboard — so suppressing every route is safe.
        _root.AddHandler(Control.ContextRequestedEvent, OnContextRequested,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        // While the keyboard is up it follows focus, so tapping another field retargets it.
        // Without this a single tap gave the field an accent border while the keys went on
        // editing the previous one.
        _root.AddHandler(InputElement.GotFocusEvent, OnGlobalGotFocus,
            RoutingStrategies.Bubble, handledEventsToo: true);

        // The overlay inherits MainViewModel as DataContext by default, so give it its own
        // VM, and let ✕ hide the panel.
        _keyboard.DataContext = _keyboardViewModel;
        _keyboardViewModel.CloseAction = () => _keyboard.IsVisible = false;
    }

    /// <summary>
    /// Re-points the file-picker interaction at the current view model. The previous
    /// registration is disposed first, so a re-bind cannot leave two handlers registered.
    /// </summary>
    public void Bind(MainViewModel? vm)
    {
        _selectFilesInteraction?.Dispose();
        _selectFilesInteraction = null;
        _viewModel = vm;

        if (vm != null)
            _selectFilesInteraction =
                vm.JobViewModel.SelectFilesInteraction.RegisterHandler(InteractionHandler);
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e) =>
        e.Handled = true;

    private void OnGlobalDoubleTapped(object? sender, TappedEventArgs e)
    {
        var targetTextBox = ResolveTextBox(e.Source);
        if (targetTextBox == null) return;

        // The keyboard is an overlay panel inside this window — showing it is just a
        // visibility flip. No OS window, no focus/activation involved.
        _keyboardViewModel.SetTarget(targetTextBox);
        _keyboard.IsVisible = true;
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
        if (!_keyboard.IsVisible) return;

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
        var topLevel = TopLevel.GetTopLevel(_root);
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
