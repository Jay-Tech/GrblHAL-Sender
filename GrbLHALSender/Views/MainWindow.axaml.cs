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

    private void Control_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        double xScale = e.NewSize.Height / 1080;
        double yScale = e.NewSize.Width / 1920;
        var diff = Math.Abs(xScale - yScale) / 2;
        double value = Math.Min(xScale, yScale);
        var s = (double)OnCoerceScaleValue(value);
        TransformControl.LayoutTransform = new ScaleTransform(xScale, value);
    }

    private double OnCoerceScaleValue(double value)
    {
        if (double.IsNaN(value))
            return 1.0f;
        value = Math.Max(0.1, value);
        return value;
    }
}
