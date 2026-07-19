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

        // The keyboard window has no title bar — the grab strip at the top
        // moves it instead. Window.BeginMoveDrag ignores touch on this WM, and
        // continuous repositioning is unusable on it too: pointer coordinates
        // don't track the moved window promptly, so live deltas never settle
        // and the window glides away until it hits the screen edge. Instead
        // the window stays put for the whole gesture — the coordinate frame is
        // then stable and the total delta exact — and moves ONCE on release.
        DragHandle.PointerPressed += (_, e) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window) return;
            _dragging = true;
            _dragStart = e.GetPosition(this);
            _dragLast = _dragStart;
            GripBar.Background = Avalonia.Media.Brushes.DodgerBlue; // drag armed
            e.Pointer.Capture(DragHandle);
            e.Handled = true;
        };

        DragHandle.PointerMoved += (_, e) =>
        {
            if (!_dragging) return;
            _dragLast = e.GetPosition(this);
            e.Handled = true;
        };

        DragHandle.PointerReleased += (_, e) =>
        {
            if (_dragging)
                ApplyDrag();
            EndDrag();
            e.Pointer.Capture(null);
            e.Handled = true;
        };
        DragHandle.PointerCaptureLost += (_, _) => EndDrag();
    }

    private bool _dragging;
    private Point _dragStart;
    private Point _dragLast;

    private void ApplyDrag()
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        var delta = _dragLast - _dragStart;
        if (Math.Abs(delta.X) < 2 && Math.Abs(delta.Y) < 2) return;

        var scale = window.DesktopScaling;
        var newX = window.Position.X + (int)(delta.X * scale);
        var newY = window.Position.Y + (int)(delta.Y * scale);

        // Keep the window fully on its screen so it is always recoverable.
        var screen = window.Screens?.ScreenFromWindow(window)?.WorkingArea;
        if (screen is { } s)
        {
            var winW = (int)(window.Bounds.Width * scale);
            var winH = (int)(window.Bounds.Height * scale);
            newX = Math.Clamp(newX, s.X, Math.Max(s.X, s.Right - winW));
            newY = Math.Clamp(newY, s.Y, Math.Max(s.Y, s.Bottom - winH));
        }

        window.Position = new PixelPoint(newX, newY);
    }

    private void EndDrag()
    {
        _dragging = false;
        GripBar.Background = _gripIdleBrush;
    }

    private static readonly Avalonia.Media.IBrush _gripIdleBrush =
        Avalonia.Media.Brush.Parse("#666");

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
