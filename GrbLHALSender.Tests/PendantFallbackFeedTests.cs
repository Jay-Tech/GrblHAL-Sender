using GrbLHALSender.Pendant;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// The feed the pendant service falls back to when the handheld asks for none.
///
/// It comes from the jog rate list the operator picked from, which is built in
/// display units, and it is spent on a jog block that states G21. Metric hid
/// the mismatch because that list is already mm/min; imperial commanded a feed
/// twenty-five times too low, which on the machine is a pendant that crawls and
/// stumbles in inches while behaving in millimetres.
/// </summary>
public class PendantFallbackFeedTests
{
    [Fact]
    public void MetricRateIsAlreadyMillimetres()
    {
        Assert.Equal(5000, PendantService.ToMmPerMin(5000, useMetric: true));
    }

    [Fact]
    public void ImperialRateIsConvertedToMillimetres()
    {
        // 300 in/min is the top of the default imperial jog list. Sent as-is it
        // commanded 300 mm/min - 11.8 in/min.
        Assert.Equal(7620, PendantService.ToMmPerMin(300, useMetric: false));
    }

    [Fact]
    public void SlowestImperialRateIsConvertedToo()
    {
        Assert.Equal(254, PendantService.ToMmPerMin(10, useMetric: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoRateStaysNoRate(bool useMetric)
    {
        // Zero means "nothing configured", and scaling it would invent a feed.
        Assert.Equal(0, PendantService.ToMmPerMin(0, useMetric));
    }
}
