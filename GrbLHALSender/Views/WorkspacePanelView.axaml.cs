using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace GrbLHALSender.Views;

/// <summary>
/// The tabbed work area — 3D render with the job and g-code overlays, GrblHAL settings,
/// reference, and the macro bar. Binds to MainViewModel, inherited from whichever canvas
/// hosts it.
/// </summary>
public partial class WorkspacePanelView : UserControl
{
    public WorkspacePanelView()
    {
        InitializeComponent();

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
}
