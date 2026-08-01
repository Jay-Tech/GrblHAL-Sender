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
    /// <summary>
    /// Whether the macro bar is shown alongside the tab strip. The portrait canvas turns it off
    /// and hosts <see cref="MacroBarView"/> in its control block instead, which gives the tab
    /// headers the full width — they are tight at 1080.
    /// </summary>
    public static readonly StyledProperty<bool> ShowMacroBarProperty =
        AvaloniaProperty.Register<WorkspacePanelView, bool>(nameof(ShowMacroBar), defaultValue: true);

    public bool ShowMacroBar
    {
        get => GetValue(ShowMacroBarProperty);
        set => SetValue(ShowMacroBarProperty, value);
    }

    public WorkspacePanelView()
    {
        InitializeComponent();

        MacroBarRow.IsVisible = ShowMacroBar;

        // Touch can leave :pressed/:pointerover stuck on aux output buttons
        // (no pointer-leave event after a finger lifts), which masks their
        // state styling. Clear both when the pointer really has gone away.
        AuxOutputRepeater.AddHandler(PointerReleasedEvent,
            (_, e) => ClearStuckTouchState(e.Source), RoutingStrategies.Tunnel, handledEventsToo: true);
        AuxOutputRepeater.AddHandler(PointerCaptureLostEvent,
            (_, e) => ClearStuckTouchState(e.Source), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Applied here rather than by binding: the row is a named child of this control, so a
        // compiled binding would have to reach back out through the MainViewModel context.
        if (change.Property == ShowMacroBarProperty)
            MacroBarRow.IsVisible = ShowMacroBar;
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
