using Avalonia.Rendering;
using GrbLHALSender.Views;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for the GRBLHAL_RENDER_OVERLAY switch, which turns on Avalonia's renderer
/// overlays without a rebuild. It exists to compare renderer behaviour between Avalonia
/// versions on the Pi, where DirtyRects shows directly whether a status-poll repaint
/// costs a small region or the whole frame.
/// </summary>
public class RenderOverlayVariableTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("0")]
    public void UnsetOrUnrecognised_TurnsOverlaysOff(string? value)
    {
        // A typo on a shop machine must not stop the app starting.
        Assert.Equal(RendererDebugOverlays.None, MainWindow.ParseRenderOverlays(value));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("all")]
    public void TruthyValues_EnableTheSetWorthWatching(string value)
    {
        var expected = RendererDebugOverlays.Fps
                     | RendererDebugOverlays.DirtyRects
                     | RendererDebugOverlays.RenderTimeGraph;

        Assert.Equal(expected, MainWindow.ParseRenderOverlays(value));
    }

    [Fact]
    public void ASingleFlagNameIsHonoured()
    {
        Assert.Equal(RendererDebugOverlays.DirtyRects, MainWindow.ParseRenderOverlays("DirtyRects"));
        Assert.Equal(RendererDebugOverlays.Fps, MainWindow.ParseRenderOverlays("fps"));
    }

    [Fact]
    public void FlagNamesCombine()
    {
        Assert.Equal(
            RendererDebugOverlays.Fps | RendererDebugOverlays.DirtyRects,
            MainWindow.ParseRenderOverlays("Fps,DirtyRects"));
    }
}
