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
        // moves it instead, so it can be dragged clear of the field being
        // edited. Manual position tracking rather than Window.BeginMoveDrag:
        // BeginMoveDrag delegates to the window manager's move protocol, which
        // ignores touch input on some Linux WMs (observed on the Pi).
        //
        // Repositioning is one-move-in-flight: on X11 the position update is
        // asynchronous, so pointer deltas measured before the window actually
        // moved would re-include the previous move and compound into runaway
        // acceleration off the screen. We wait for PositionChanged (with a
        // timeout fallback) before applying the next delta, and clamp to the
        // screen so the keyboard can never become unreachable.
        DragHandle.PointerPressed += (_, e) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window window) return;
            _dragging = true;
            _moveInFlight = false;
            _dragStart = e.GetPosition(this);
            window.PositionChanged += OnWindowPositionChanged;
            e.Pointer.Capture(DragHandle);
            e.Handled = true;
        };

        DragHandle.PointerMoved += (_, e) =>
        {
            if (!_dragging || TopLevel.GetTopLevel(this) is not Window window) return;

            // Wait for the previous reposition to be acknowledged; without this
            // the delta below would still be measured against the old origin.
            if (_moveInFlight && _moveStopwatch.ElapsedMilliseconds < 200) return;
            _moveInFlight = false;

            var delta = e.GetPosition(this) - _dragStart;
            if (Math.Abs(delta.X) < 1 && Math.Abs(delta.Y) < 1) return;

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

            _moveInFlight = true;
            _moveStopwatch.Restart();
            window.Position = new PixelPoint(newX, newY);
            e.Handled = true;
        };

        DragHandle.PointerReleased += (_, e) =>
        {
            EndDrag();
            e.Pointer.Capture(null);
        };
        DragHandle.PointerCaptureLost += (_, _) => EndDrag();
    }

    private bool _dragging;
    private bool _moveInFlight;
    private Point _dragStart;
    private readonly System.Diagnostics.Stopwatch _moveStopwatch = new();

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        _moveInFlight = false;
    }

    private void EndDrag()
    {
        _dragging = false;
        _moveInFlight = false;
        if (TopLevel.GetTopLevel(this) is Window window)
            window.PositionChanged -= OnWindowPositionChanged;
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
