using System.Collections.Generic;
using System.Linq;
using GrbLHALSender.Probe;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests the motion an outside centre probe makes, for a round boss or a rectangular one.
/// <para>
/// The version this replaces stepped relative to wherever the previous probe stopped, the same
/// dead reckoning that drove the bore cycle into a wall. It also folded the probe distance into
/// the stand-off, which put the approximate size in a narrow band: too large and the probe could
/// never reach the face, too small by more than the probe distance and the stylus dropped onto
/// the feature. Only one end of that failed safely.
/// </para>
/// <para>
/// Stand-off is now half the approximate size plus the clearance, and the probe travels
/// separately, so the size only has to be close enough that clearance covers the error.
/// </para>
/// </summary>
public class OutsideCenterSequenceTests
{
    private const double StartX = -100;
    private const double StartY = -60;
    private const double StartZ = -10;

    // Clearance 5 and Depth 3, so safe Z is -5 and probing Z is -13.
    private const string SafeZ = "G53G0Z-5.000";
    private const string ProbeZ = "G53G0Z-13.000";

    private static ProbeJobBuilder Builder() => new()
    {
        ProbeSearchRate = "100",
        ProbeLatchRate = "20",
        ProbeDistance = "10",
        LatchDistance = "1",
        ClearanceHeight = "5",
        ProbeDepth = "3",
        UnitSystem = "G21"
    };

    // A 40 x 20 boss: stand-off is 20+5 across X and 10+5 across Y.
    private static List<List<string>> Phases(double width = 40, double height = 20) =>
        Builder().ProbeOutsideCenter(width, height, StartX, StartY, StartZ);

    [Fact]
    public void ThereAreFourLegs()
    {
        Assert.Equal(4, Phases().Count);
    }

    [Fact]
    public void EachLegStandsOffBeyondTheFaceAndProbesBackIn()
    {
        var phases = Phases();

        // +X face: stand off to +25 and probe in the -X direction.
        Assert.Equal(
            new[]
            {
                "G21", "G90", SafeZ, "G53G0X-75.000Y-60.000", ProbeZ,
                "G21", "G91", "G38.3F100X-10", "G0X1", "G38.3F20X-1"
            },
            phases[0]);
    }

    [Fact]
    public void TheFourLegsTouchTheFourFacesInOrder()
    {
        var phases = Phases();

        // OnProbeCenterComplete reads these positionally as +X, -X, +Y, -Y.
        Assert.Contains("G53G0X-75.000Y-60.000", phases[0]);   // +X side, 25 out
        Assert.Contains("G53G0X-125.000Y-60.000", phases[1]);  // -X side
        Assert.Contains("G53G0X-100.000Y-45.000", phases[2]);  // +Y side, 15 out
        Assert.Contains("G53G0X-100.000Y-75.000", phases[3]);  // -Y side
    }

    [Fact]
    public void TheCrossAxisStaysOnTheCentreLine()
    {
        // A rectangle does not care, but touching a circle away from its centre line reads a
        // chord instead of the diameter.
        var phases = Phases();

        Assert.Contains("Y-60.000", phases[0].Single(c => c.StartsWith("G53G0X")));
        Assert.Contains("Y-60.000", phases[1].Single(c => c.StartsWith("G53G0X")));
        Assert.Contains("X-100.000", phases[2].Single(c => c.StartsWith("G53G0X")));
        Assert.Contains("X-100.000", phases[3].Single(c => c.StartsWith("G53G0X")));
    }

    [Fact]
    public void WidthAndHeightAreUsedIndependently()
    {
        // The reason a rectangle needs its own mode: one size cannot serve both axes.
        var phases = Phases(width: 40, height: 20);

        Assert.Contains("G53G0X-75.000Y-60.000", phases[0]);   // 20 + 5 across X
        Assert.Contains("G53G0X-100.000Y-45.000", phases[2]);  // 10 + 5 across Y
    }

    [Fact]
    public void EveryLegLiftsThenMovesAcrossThenDrops()
    {
        foreach (var phase in Phases())
        {
            var lift = phase.IndexOf(SafeZ);
            var across = phase.FindIndex(c => c.StartsWith("G53G0X"));
            var drop = phase.IndexOf(ProbeZ);

            Assert.True(lift >= 0 && across >= 0 && drop >= 0, "missing a move");
            Assert.True(lift < across, "moved across before lifting");
            Assert.True(across < drop, "dropped before moving across");
        }
    }

    [Fact]
    public void EveryApproachMoveIsAbsolute()
    {
        // Relative stepping from contact points is what made the old cycle unsafe.
        foreach (var phase in Phases())
        {
            var approach = phase.GetRange(0, phase.FindIndex(c => c.StartsWith("G38")));

            Assert.Contains("G90", approach);
            Assert.All(approach.Where(c => c.StartsWith("G0")), c =>
                Assert.Fail($"relative approach move: {c}"));
        }
    }

    [Fact]
    public void TheStandOffDoesNotIncludeTheProbeDistance()
    {
        // Folding Distance in is what made an over-estimated size unreachable. Stand-off is
        // half the size plus clearance only: 20 + 5, not 20 + 10.
        Assert.Contains("G53G0X-75.000Y-60.000", Phases()[0]);
        Assert.DoesNotContain("G53G0X-70.000Y-60.000", Phases()[0]);
    }

    [Fact]
    public void ARoundBossPassesTheSameSizeTwice()
    {
        // How the view model drives a round boss: width for both axes, so the stand-off is
        // equal on each.
        var phases = Builder().ProbeOutsideCenter(40, 40, StartX, StartY, StartZ);

        Assert.Contains("G53G0X-75.000Y-60.000", phases[0]);
        Assert.Contains("G53G0X-100.000Y-35.000", phases[2]);
    }
}
