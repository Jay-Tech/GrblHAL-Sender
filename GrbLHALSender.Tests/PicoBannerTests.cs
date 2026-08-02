using GrbLHALSender.Gpio;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// The banner decides whether the app writes to a serial port at all, and the port next to
/// it may be the CNC controller — so anything that is not unambiguously a PICOGPIO device
/// has to be rejected.
/// </summary>
public class PicoBannerTests
{
    [Fact]
    public void ParsesAFullBanner()
    {
        Assert.True(PicoBanner.TryParse("PICOGPIO 1 pins=0-22,26-28 wd=5000", out var banner));

        Assert.Equal(1, banner.Version);
        Assert.Equal(5000, banner.WatchdogMs);
        Assert.True(banner.IsSupported);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(22, true)]
    [InlineData(26, true)]
    [InlineData(28, true)]
    [InlineData(23, false)]   // gap between the ranges
    [InlineData(25, false)]
    [InlineData(29, false)]
    [InlineData(-1, false)]
    public void PinRangesAreInclusiveAndHonourGaps(int pin, bool expected)
    {
        Assert.True(PicoBanner.TryParse("PICOGPIO 1 pins=0-22,26-28 wd=5000", out var banner));
        Assert.Equal(expected, banner.IsValidPin(pin));
    }

    [Fact]
    public void AcceptsBarePinNumbers()
    {
        Assert.True(PicoBanner.TryParse("PICOGPIO 1 pins=5,6,16 wd=4000", out var banner));

        Assert.True(banner.IsValidPin(5));
        Assert.True(banner.IsValidPin(16));
        Assert.False(banner.IsValidPin(7));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ok")]
    [InlineData("Grbl 1.1f ['$' for help]")]            // a controller on the wrong port
    [InlineData("<Idle|MPos:0.000,0.000,0.000|FS:0,0>")] // a controller status report
    [InlineData("PICOGPIO")]                             // no version
    [InlineData("PICOGPIO x pins=0-22")]                 // unparseable version
    [InlineData("PICOGPIO 1 wd=5000")]                   // no pins, so nothing is drivable
    [InlineData("picogpio 1 pins=0-22")]                 // marker is case-sensitive
    public void RejectsAnythingThatIsNotADevice(string line)
    {
        Assert.False(PicoBanner.TryParse(line, out _));
    }

    [Fact]
    public void UnknownMajorParsesButIsNotSupported()
    {
        // Parsed so the app can say which version it found rather than "no device".
        Assert.True(PicoBanner.TryParse("PICOGPIO 9 pins=0-22 wd=5000", out var banner));

        Assert.Equal(9, banner.Version);
        Assert.False(banner.IsSupported);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        // Lets a later firmware add a field without this build refusing to talk to it.
        Assert.True(PicoBanner.TryParse("PICOGPIO 1 pins=0-22 wd=5000 serial=E66 hw=pico2w", out var banner));

        Assert.True(banner.IsValidPin(10));
        Assert.Equal(5000, banner.WatchdogMs);
    }

    [Fact]
    public void HeartbeatIsHalfTheWatchdog()
    {
        // Half, so a single dropped line cannot trip it.
        Assert.True(PicoBanner.TryParse("PICOGPIO 1 pins=0-22 wd=5000", out var banner));
        Assert.Equal(2500, banner.HeartbeatMs);
    }

    [Fact]
    public void NoWatchdogMeansNoHeartbeat()
    {
        Assert.True(PicoBanner.TryParse("PICOGPIO 1 pins=0-22 wd=0", out var banner));
        Assert.Equal(0, banner.HeartbeatMs);
    }

    [Fact]
    public void AbsurdlyShortWatchdogIsFloored()
    {
        // Stops a malformed or hostile wd= turning into a busy-loop on the serial port.
        Assert.True(PicoBanner.TryParse("PICOGPIO 1 pins=0-22 wd=10", out var banner));
        Assert.Equal(250, banner.HeartbeatMs);
    }
}
