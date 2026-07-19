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
