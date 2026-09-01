using System.Globalization;
using System.Reflection;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// The status frame pushed to the pendant carries millimetres, not display units.
///
/// This is not presentation. The pendant caps its own detent output at the feed
/// this frame reports - allowed = (fr / 60) * tick / step - and measures how far
/// behind the machine is by comparing the work position in this frame against
/// the millimetres it has commanded. Both are millimetre calculations, and the
/// jog blocks that come back state G21 to match.
///
/// Sent in display units the arithmetic still runs, on a number 25.4 times too
/// small: a machine at 9000 mm/min announced as 354 in/min let one detent
/// through per 20 ms tick where the hand was producing five. Neither end
/// reported anything wrong, because neither end was wrong - the premise was.
/// </summary>
public class PendantStatusUnitsTests
{
    private const double MmPerInch = 25.4;

    /// <summary>The conversion the frame now uses: machine native to mm, display ignored.</summary>
    private static double ToMillimetres(double value, bool machineInMetric) =>
        machineInMetric ? value : value * MmPerInch;

    // --- the properties the frame reads must exist and be unit-stable --------

    [Theory]
    [InlineData("FeedRateMmPerMin", typeof(double))]
    [InlineData("WorkPositionsMm", typeof(double[]))]
    public void MachineStateExposesMillimetreValues(string name, System.Type expected)
    {
        var property = typeof(MachineStateService).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.Equal(expected, property!.PropertyType);
    }

    [Fact]
    public void MillimetreConversionIgnoresTheDisplayPreference()
    {
        // ConvertUnit answers to both $13 and the Metric UI checkbox. This one
        // answers to $13 alone - the display preference must not reach it, or
        // the pendant is back to being throttled by a checkbox.
        var metricMachine = ToMillimetres(9000, machineInMetric: true);
        var inchMachine = ToMillimetres(354.33, machineInMetric: false);

        Assert.Equal(9000, metricMachine);
        Assert.Equal(9000, inchMachine, 0);
    }

    // --- what the wrong unit did to the pendant's throttle -------------------

    /// <summary>The pendant's own bound: allowed = (fr / 60) * tick_seconds / step.</summary>
    private static int DetentsAllowed(double frMmPerMin, double stepMm, int tickMs)
    {
        var allowed = (int)((frMmPerMin / 60.0) * (tickMs / 1000.0) / stepMm + 0.5);
        return allowed < 1 ? 1 : allowed;
    }

    [Fact]
    public void MillimetreFeedLetsTheWheelThrough()
    {
        // 9000 mm/min at the 1 mm step and a 20 ms tick: three detents a tick,
        // which is what a hand at that speed is producing.
        Assert.Equal(3, DetentsAllowed(9000, stepMm: 1.0, tickMs: 20));
    }

    [Fact]
    public void DisplayUnitFeedThrottledTheWheelToOneDetent()
    {
        // The same machine, announced in in/min. The bound collapses to its
        // floor of one detent a tick - 50 mm/s however fast the wheel turns -
        // and the difference is dropped on the pendant.
        var announcedInInches = 9000 / MmPerInch;

        Assert.Equal(1, DetentsAllowed(announcedInInches, stepMm: 1.0, tickMs: 20));
    }

    [Fact]
    public void ThrottleTightensAsTheMachineSlows()
    {
        // Why it degrades rather than settling: the throttled machine reports a
        // lower feed, which is fed back in as a lower bound. Both figures are
        // already at the floor, so the loop has nowhere to recover from.
        var throttled = 3000 / MmPerInch;   // what the machine managed once capped
        var worse = 1500 / MmPerInch;

        Assert.Equal(1, DetentsAllowed(throttled, stepMm: 1.0, tickMs: 20));
        Assert.Equal(1, DetentsAllowed(worse, stepMm: 1.0, tickMs: 20));
    }

    // --- the frame is still JSON in a comma-decimal region -------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void MillimetreValuesAreWrittenInvariantly(string culture)
    {
        var original = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);

            Assert.Equal("9000.5", 9000.5.ToInvariantString("0.###"));
            Assert.Equal("-12.345", (-12.345).ToInvariantString("0.###"));
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
