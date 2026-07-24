using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;

namespace GrbLHALSender.Views;

/// <summary>
/// Single-slot overlay panel that hosts one dialog view at a time inside the
/// main window. Showing a new dialog while one is open closes the current one
/// first (running its close callback so save-on-close side effects still fire).
/// </summary>
public partial class DialogHostView : UserControl
{
    private Action? _onClosed;

    private const double TitleBarHeight = 38;

    public DialogHostView()
    {
        InitializeComponent();

        CloseButton.Click += (_, _) => CloseHost();

        // Drag = render-transform translation, same as the keyboard overlay:
        // no window manager involved, so live finger-following drag works on
        // every platform. Deltas are measured against the stationary parent.
        RenderTransform = _translate;

        DragHandle.PointerPressed += (_, e) =>
        {
            if (this.GetVisualParent() is not Visual parent) return;
            _dragging = true;
            _dragParent = parent;
            _dragStart = e.GetPosition(parent);
            _baseX = _translate.X;
            _baseY = _translate.Y;
            GripBar.Background = Brushes.DodgerBlue; // drag armed
            e.Pointer.Capture(DragHandle);
            e.Handled = true;
        };

        DragHandle.PointerMoved += (_, e) =>
        {
            if (!_dragging || _dragParent == null) return;

            var delta = e.GetPosition(_dragParent) - _dragStart;
            var newX = _baseX + delta.X;
            var newY = _baseY + delta.Y;

            // Keep the panel inside the parent so it is always reachable.
            var parentBounds = _dragParent.Bounds;
            newX = Math.Clamp(newX, -Bounds.X, parentBounds.Width - Bounds.Width - Bounds.X);
            newY = Math.Clamp(newY, -Bounds.Y, parentBounds.Height - Bounds.Height - Bounds.Y);

            _translate.X = newX;
            _translate.Y = newY;
            e.Handled = true;
        };

        DragHandle.PointerReleased += (_, e) =>
        {
            EndDrag();
            e.Pointer.Capture(null);
            e.Handled = true;
        };
        DragHandle.PointerCaptureLost += (_, _) => EndDrag();
    }

    private readonly TranslateTransform _translate = new();
    private bool _dragging;
    private Visual? _dragParent;
    private Point _dragStart;
    private double _baseX;
    private double _baseY;

    private static readonly IBrush GripIdleBrush = Brush.Parse("#666");

    private void EndDrag()
    {
        _dragging = false;
        GripBar.Background = GripIdleBrush;
    }

    /// <summary>
    /// Shows <paramref name="content"/> in the host. Width/height are the
    /// content size (the title bar is added on top). <paramref name="onClosed"/>
    /// runs exactly once when the dialog is closed or replaced.
    /// </summary>
    public void ShowDialogContent(string title, Control content, double width, double height, Action? onClosed = null)
    {
        // Replacing an open dialog counts as closing it.
        if (IsVisible)
            CloseHost();

        TitleText.Text = title;
        HostContent.Content = content;
        Width = width;
        Height = height + TitleBarHeight;
        _onClosed = onClosed;

        // Open at the default (XAML-defined) position every time so a dialog
        // dragged off to the side doesn't reopen somewhere unexpected.
        _translate.X = 0;
        _translate.Y = 0;

        IsVisible = true;
    }

    public void CloseHost()
    {
        if (!IsVisible && _onClosed == null)
            return;

        IsVisible = false;
        HostContent.Content = null;

        var callback = _onClosed;
        _onClosed = null;
        callback?.Invoke();
    }
}
