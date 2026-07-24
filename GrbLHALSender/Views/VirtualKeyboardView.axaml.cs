using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;

namespace GrbLHALSender.Views;

public partial class VirtualKeyboardView : UserControl
{
    public VirtualKeyboardView()
    {
        InitializeComponent();

        // Touch on this virtual keyboard sometimes fails to deliver a clean
        // PointerReleased to the pressed Button — the caller often refocuses
        // the target TextBox on press, which can eat the release event.
        // Result: the button's :pressed pseudo-class sticks visually until
        // another button is touched. We handle release + capture-lost at the
        // UserControl root (tunneling so we see the event first) and force
        // the pseudo-class off on any keyboard button.
        AddHandler(PointerReleasedEvent, OnAnyPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnAnyPointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);

        // The keyboard is an overlay inside the main window, so dragging it is
        // a render-transform translation — no window manager involved, which
        // means live finger-following drag works on every platform. Deltas are
        // measured against the (stationary) parent visual, so the coordinate
        // frame is stable while this control moves.
        RenderTransform = _translate;

        DragHandle.PointerPressed += (_, e) =>
        {
            if (this.GetVisualParent() is not Visual parent) return;
            _dragging = true;
            _dragParent = parent;
            _dragStart = e.GetPosition(parent);
            _baseX = _translate.X;
            _baseY = _translate.Y;
            GripBar.Classes.Set("dragging", true); // drag armed
            e.Pointer.Capture(DragHandle);
            e.Handled = true;
        };

        DragHandle.PointerMoved += (_, e) =>
        {
            if (!_dragging || _dragParent == null) return;

            var delta = e.GetPosition(_dragParent) - _dragStart;
            var newX = _baseX + delta.X;
            var newY = _baseY + delta.Y;

            // Keep the keyboard inside the parent so it is always reachable.
            // Bounds is the untransformed arranged rect within the parent.
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

    private readonly Avalonia.Media.TranslateTransform _translate = new();
    private bool _dragging;
    private Visual? _dragParent;
    private Point _dragStart;
    private double _baseX;
    private double _baseY;

    private void EndDrag()
    {
        _dragging = false;
        GripBar.Classes.Set("dragging", false);
    }

    private static void OnAnyPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ClearPressed(e.Source);
    }

    private static void OnAnyPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ClearPressed(e.Source);
    }

    private static void ClearPressed(object? source)
    {
        if (source is Control c)
        {
            var target = c as Button ?? c.FindAncestorOfType<Button>();
            if (target != null && target.Classes.Contains("kb"))
                ((IPseudoClasses)target.Classes).Set(":pressed", false);
        }
    }
}
