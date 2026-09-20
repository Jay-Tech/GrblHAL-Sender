using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace GrbLHALSender.Views;

/// <summary>
/// Holds whichever of the two fixed canvases matches the screen, scaled to fit, and applies
/// the two presentation settings that have to be made against the top level rather than
/// against a view.
/// </summary>
/// <remarks>
/// All of this used to live in <see cref="MainWindow"/>, where it was unreachable from any
/// host that is not a Window. Avalonia's DRM/framebuffer backend has no windows at all — it
/// drives a single-view lifetime whose root is a plain control — so a Pi running without an
/// X server would have got the landscape canvas at its authored 1920x1080, unscaled, on
/// whatever panel was actually attached. None of the logic was ever window-specific: it needs
/// a top level, and every host has one.
/// <para>
/// <see cref="MainWindow"/> is now a shell that supplies the window chrome and this control.
/// </para>
/// </remarks>
public partial class RootCanvasHost : UserControl
{
    public RootCanvasHost()
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
    /// Both top-level settings are applied here rather than from a window event, because
    /// attachment is the earliest point at which this control has a top level and an
    /// ancestor chain — and it is the one moment that happens under every host.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        ApplyRenderOverlays();
        DisableTouchTextHandles();
    }

    /// <summary>
    /// Applies the overlays once the renderer is up. DirtyRects is the interesting one on
    /// this app: the status poll repaints the DRO several times a second, and whether that
    /// costs a small region or the whole frame including the 3D viewport is the difference
    /// the renderer settings actually make.
    /// </summary>
    private void ApplyRenderOverlays()
    {
        var overlays = ParseRenderOverlays(Environment.GetEnvironmentVariable(RenderOverlayVariable));
        if (overlays == RendererDebugOverlays.None) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        topLevel.RendererDiagnostics.DebugOverlays = overlays;
        Console.Error.WriteLine($"{RenderOverlayVariable}: renderer overlays enabled ({overlays})");
    }

    /// <summary>
    /// Turns off Avalonia's touch text handles — the teardrop that appears under the caret,
    /// and the pair that bracket a selection.
    /// <para>
    /// They are drawn by the VisualLayerManager in the host's template, so one switch covers
    /// every field in the app including the dialogs, which are overlays in this same tree.
    /// On a touchscreen they sit on top of the fields they belong to and there is nothing to
    /// do with them here: text entry goes through the virtual keyboard, and the context menu
    /// they pair with is already suppressed.
    /// </para>
    /// <para>
    /// Searched upwards, where the same code searched downwards when it ran on the window.
    /// The layer manager belongs to whatever is hosting this control — the Window template
    /// on the desktop, the single-view root without one — so from here it is always an
    /// ancestor, and looking up is the form that holds under both.
    /// </para>
    /// <para>
    /// Done in code because <c>EnableTextSelectorLayer</c> is a plain CLR property rather than
    /// a styled one, so no XAML setter can reach it.
    /// </para>
    /// </summary>
    private void DisableTouchTextHandles()
    {
        var layerManager = this.GetVisualAncestors().OfType<VisualLayerManager>().FirstOrDefault();
        if (layerManager != null)
            layerManager.EnableTextSelectorLayer = false;
    }

    /// <summary>
    /// The two canvases every view is authored against. The app does not reflow: the whole
    /// canvas is scaled to fit the host, so each orientation gets its own fixed design size
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
    /// Chooses the canvas for the host's orientation and scales it to fit, uniformly on both
    /// axes so nothing is distorted.
    /// <para>
    /// The smaller of the two ratios is used, which letterboxes a host that is not the design
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
    /// inherits this control's MainViewModel — the same instance either orientation would get.
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
