using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace GrbLHALSender.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Turns off Avalonia's touch text handles — the teardrop that appears under the caret,
    /// and the pair that bracket a selection.
    /// <para>
    /// They are drawn by the VisualLayerManager in the window template, so one switch covers
    /// every field in the app including the dialogs, which are overlays in this same window.
    /// On a touchscreen they sit on top of the fields they belong to and there is nothing to
    /// do with them here: text entry goes through the virtual keyboard, and the context menu
    /// they pair with is already suppressed.
    /// </para>
    /// <para>
    /// Done in code because <c>EnableTextSelectorLayer</c> is a plain CLR property rather than
    /// a styled one, so no XAML setter can reach it. OnApplyTemplate is the earliest point the
    /// layer manager exists.
    /// </para>
    /// </summary>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var layerManager = this.GetVisualDescendants().OfType<VisualLayerManager>().FirstOrDefault();
        if (layerManager != null)
            layerManager.EnableTextSelectorLayer = false;
    }

    /// <summary>The canvas every view is authored against. The whole app is scaled to fit the window.</summary>
    private const double DesignWidth = 1920;
    private const double DesignHeight = 1080;

    /// <summary>
    /// Scales the design canvas to fit the window, uniformly on both axes so nothing is distorted.
    /// <para>
    /// The smaller of the two ratios is used, which letterboxes a window that is not the design
    /// aspect ratio rather than stretching to fill it.
    /// </para>
    /// </summary>
    private void Control_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            return;

        double scale = OnCoerceScaleValue(Math.Min(e.NewSize.Width / DesignWidth,
                                                   e.NewSize.Height / DesignHeight));
        TransformControl.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private double OnCoerceScaleValue(double value)
    {
        if (double.IsNaN(value))
            return 1.0f;
        value = Math.Max(0.1, value);
        return value;
    }
}
