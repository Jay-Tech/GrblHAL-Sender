using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Rendering;
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
    /// Environment variable that turns on Avalonia's renderer overlays. Unset in normal
    /// use, so this costs nothing; it exists because the overlays are the only direct way
    /// to see what the renderer is actually repainting.
    /// <para>
    /// Accepts flag names - <c>Fps</c>, <c>DirtyRects</c>, <c>RenderTimeGraph</c>,
    /// <c>LayoutTimeGraph</c>, or a comma-separated combination - or <c>1</c>/<c>true</c>/
    /// <c>all</c> for the three worth watching together.
    /// </para>
    /// </summary>
    internal const string RenderOverlayVariable = "GRBLHAL_RENDER_OVERLAY";

    /// <summary>
    /// Maps the variable to a set of overlays. Anything unrecognised turns them off rather
    /// than throwing: a typo on a shop machine should not stop the app starting.
    /// </summary>
    internal static RendererDebugOverlays ParseRenderOverlays(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return RendererDebugOverlays.None;

        var text = value.Trim();

        if (text == "1" ||
            text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return RendererDebugOverlays.Fps
                 | RendererDebugOverlays.DirtyRects
                 | RendererDebugOverlays.RenderTimeGraph;
        }

        return Enum.TryParse<RendererDebugOverlays>(text, ignoreCase: true, out var parsed)
            ? parsed
            : RendererDebugOverlays.None;
    }

    /// <summary>
    /// Applies the overlays once the renderer is up. DirtyRects is the interesting one on
    /// this app: the status poll repaints the DRO several times a second, and whether that
    /// costs a small region or the whole frame including the 3D viewport is the difference
    /// the renderer settings actually make.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var overlays = ParseRenderOverlays(Environment.GetEnvironmentVariable(RenderOverlayVariable));
        if (overlays == RendererDebugOverlays.None) return;

        RendererDiagnostics.DebugOverlays = overlays;
        Console.Error.WriteLine($"{RenderOverlayVariable}: renderer overlays enabled ({overlays})");
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

    /// <summary>
    /// The two canvases every view is authored against. The app does not reflow: the whole
    /// canvas is scaled to fit the window, so each orientation gets its own fixed design size
    /// and its own root view.
    /// </summary>
    private static readonly Size LandscapeCanvas = new(1920, 1080);
    private static readonly Size PortraitCanvas = new(1080, 1920);

    /// <summary>
    /// Which canvas <see cref="CanvasHost"/> currently holds, or null before the first layout
    /// pass. Latched so ordinary resizes never rebuild the root view — rebuilding would tear
    /// down and recreate the 3D viewport.
    /// </summary>
    private bool? _isPortrait;

    private Size _canvas = LandscapeCanvas;

    /// <summary>
    /// Chooses the canvas for the window's orientation and scales it to fit, uniformly on both
    /// axes so nothing is distorted.
    /// <para>
    /// The smaller of the two ratios is used, which letterboxes a window that is not the design
    /// aspect ratio rather than stretching to fill it.
    /// </para>
    /// </summary>
    private void Control_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            return;

        SetCanvasOrientation(e.NewSize.Height > e.NewSize.Width);

        double scale = OnCoerceScaleValue(Math.Min(e.NewSize.Width / _canvas.Width,
                                                   e.NewSize.Height / _canvas.Height));
        TransformControl.LayoutTransform = new ScaleTransform(scale, scale);
    }

    /// <summary>
    /// Swaps in the root view for the given orientation, building it only when the orientation
    /// actually changes. Only one root view is ever alive: both bind to the same MainViewModel,
    /// so keeping a spare would double every subscription behind it.
    /// </summary>
    /// <remarks>
    /// DataContext is deliberately not set here. The host is in the visual tree, so the view
    /// inherits the window's MainViewModel — the same instance either orientation would get.
    /// </remarks>
    private void SetCanvasOrientation(bool isPortrait)
    {
        if (_isPortrait == isPortrait)
            return;

        _isPortrait = isPortrait;
        _canvas = isPortrait ? PortraitCanvas : LandscapeCanvas;
        CanvasHost.Content = isPortrait ? new MainPortraitView() : new MainView();
    }

    private double OnCoerceScaleValue(double value)
    {
        if (double.IsNaN(value))
            return 1.0f;
        value = Math.Max(0.1, value);
        return value;
    }
}
