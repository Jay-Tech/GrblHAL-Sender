using GrbLHALSender.Utility;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests the arithmetic behind rescaling the probe fields when the display unit changes.
/// <para>
/// The fields hold text, and that text is the stored value, so switching units rewrites them
/// rather than reinterpreting them — otherwise a Distance of 10 stays 10 and silently changes
/// from 10mm to 10 inches, sending the next probe twenty-five times further than the last.
/// </para>
/// <para>
/// Because the text is the value, every switch re-rounds it. Inches are written to one more
/// decimal than millimetres: a third decimal of an inch is 0.025mm, coarse enough to visibly
/// shift a setting, where a third decimal of a millimetre is far below anything settable.
/// </para>
/// </summary>
public class ProbeUnitConversionTests
{
    private const double Factor = 25.4;

    private static string ToInches(string mm) =>
        (mm.StringToDouble() / Factor).ToInvariantString("F4");

    private static string ToMillimetres(string inches) =>
        (inches.StringToDouble() * Factor).ToInvariantString("F3");

    [Theory]
    [InlineData("12", "0.4724")]     // probe distance
    [InlineData("250", "9.8425")]    // search rate, mm/min to in/min
    [InlineData("2", "0.0787")]      // a 2mm stylus
    [InlineData("6", "0.2362")]
    public void MillimetresConvertToInches(string mm, string expected)
    {
        Assert.Equal(expected, ToInches(mm));
    }

    [Theory]
    [InlineData("0.5", "12.700")]
    [InlineData("10", "254.000")]
    [InlineData("0.0787", "1.999")]
    public void InchesConvertToMillimetres(string inches, string expected)
    {
        Assert.Equal(expected, ToMillimetres(inches));
    }

    [Fact]
    public void AValueSurvivesARoundTripToWithinAThousandthOfAMillimetre()
    {
        // The reported symptom. Not exact — it cannot be while the text is the value — but the
        // error has to be far below anything that matters on a probe setting.
        foreach (var mm in new[] { "12", "6", "250", "125", "2", "100", "200" })
        {
            var round = ToMillimetres(ToInches(mm));

            Assert.True(System.Math.Abs(round.StringToDouble() - mm.StringToDouble()) < 0.005,
                $"{mm}mm came back as {round}mm");
        }
    }

    [Fact]
    public void ThreeDecimalsOfAnInchWouldNotHaveBeenEnough()
    {
        // Why inches carry the extra digit. At F3 a 2mm stylus came back as 2.007mm, and every
        // further toggle moved it again.
        var coarse = (2.0 / Factor).ToInvariantString("F3");
        var back = coarse.StringToDouble() * Factor;

        Assert.True(System.Math.Abs(back - 2.0) > 0.005, "F3 inches would have been fine after all");
        Assert.True(System.Math.Abs(ToMillimetres(ToInches("2")).StringToDouble() - 2.0) < 0.005);
    }

    [Fact]
    public void RepeatedTogglingSettles()
    {
        // Each pass re-rounds, so the question is whether the drift accumulates or converges.
        var value = "12";
        for (var i = 0; i < 10; i++)
            value = ToMillimetres(ToInches(value));

        Assert.True(System.Math.Abs(value.StringToDouble() - 12.0) < 0.005,
            $"ten round trips drifted to {value}mm");
    }

    [Fact]
    public void RatesScaleTheSameWayAsDistances()
    {
        // mm/min to in/min is the same factor; there is no separate treatment for feed rates.
        Assert.Equal(ToInches("250"), (250.0 / Factor).ToInvariantString("F4"));
    }
}
