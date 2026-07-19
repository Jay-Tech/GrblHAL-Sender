using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

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
        // moves it instead, so it can be dragged clear of the field being
        // edited. Manual position tracking rather than Window.BeginMoveDrag:
        // BeginMoveDrag delegates to the window manager's move protocol, which
        // ignores touch input on some Linux WMs (observed on the Pi).
        DragHandle.PointerPressed += (_, e) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window) return;
            _dragging = true;
            _dragStart = e.GetPosition(this);
            e.Pointer.Capture(DragHandle);
            e.Handled = true;
        };

        DragHandle.PointerMoved += (_, e) =>
        {
            if (!_dragging || TopLevel.GetTopLevel(this) is not Window window) return;

            // Position is relative to the window, which moves with each update,
            // so the reported point stays near the press point and the delta is
            // the incremental movement since the last reposition.
            var delta = e.GetPosition(this) - _dragStart;
            var scale = window.DesktopScaling;
            window.Position = new PixelPoint(
                window.Position.X + (int)(delta.X * scale),
                window.Position.Y + (int)(delta.Y * scale));
            e.Handled = true;
        };

        DragHandle.PointerReleased += (_, e) =>
        {
            _dragging = false;
            e.Pointer.Capture(null);
        };
        DragHandle.PointerCaptureLost += (_, _) => _dragging = false;
    }

    private bool _dragging;
    private Point _dragStart;

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
