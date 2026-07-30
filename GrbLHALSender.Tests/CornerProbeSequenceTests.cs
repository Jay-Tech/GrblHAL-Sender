using System.Collections.Generic;
using System.Linq;
using GrbLHALSender.Probe;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests the motion a corner probe makes, which is the part that has to be right before a
/// stylus is anywhere near it. All three faults below were found on hardware, in one run.
/// <para>
/// <b>It grazed the edge.</b> The X leg stood off to the left of the corner but never stepped
/// back into the stock, so the stylus sat diagonally past the corner and the probe caught the
/// very end of the face. Each leg now steps in on the axis it is <em>not</em> probing.
/// </para>
/// <para>
/// <b>The second leg plunged deeper than the first.</b> The lifts and drops were relative and
/// did not cancel: lifting Clearance and dropping Clearance plus Depth each time left every
/// leg another Depth lower. Every position is absolute now, planned from the start point.
/// </para>
/// <para>
/// <b>It stopped pressed against the last face.</b> Nothing lifted or moved to the corner it
/// had just measured; that finishing move lives in OnProbeCornerComplete.
/// </para>
/// </summary>
public class CornerProbeSequenceTests
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
        ProbeDiameter = "2",
        TouchPlateThickness = "1",
        UnitSystem = "G21"
    };

    private static List<List<string>> Phases(
        CornerDirection corner = CornerDirection.FrontLeft, bool includeZ = false) =>
        Builder().ProbeCorner(corner, includeZ, StartX, StartY, StartZ);

    [Fact]
    public void TheXLegStandsOffInXAndStepsBackIntoTheStockInY()
    {
        // Front left: stock lies to +X and +Y. Stand off to -X, step in to +Y.
        Assert.Equal(
            new[]
            {
                "G21", "G90", SafeZ, "G53G0Y-55.000X-105.000", ProbeZ,
                "G21", "G91", "G38.3F100X10", "G0X-1", "G38.3F20X1"
            },
            Phases()[0]);
    }

    [Fact]
    public void TheYLegStandsOffInYAndStepsIntoTheStockInX()
    {
        Assert.Equal(
            new[]
            {
                "G21", "G90", SafeZ, "G53G0X-95.000Y-65.000", ProbeZ,
                "G21", "G91", "G38.3F100Y10", "G0Y-1", "G38.3F20Y1"
            },
            Phases()[1]);
    }

    [Fact]
    public void BothLegsProbeAtTheSameHeight()
    {
        // The accumulation bug: the front probe ended a whole Depth below the left one.
        foreach (var phase in Phases())
            Assert.Contains(ProbeZ, phase);
    }

    [Fact]
    public void BothLegsLiftToTheSameSafeHeight()
    {
        foreach (var phase in Phases())
            Assert.Contains(SafeZ, phase);
    }

    [Theory]
    [InlineData(CornerDirection.FrontLeft)]
    [InlineData(CornerDirection.FrontRight)]
    [InlineData(CornerDirection.BackLeft)]
    [InlineData(CornerDirection.BackRight)]
    public void EveryLegTouchesBothAxesOnTheWayIn(CornerDirection corner)
    {
        // Standing off on one axis alone is what left the stylus off the end of the face.
        foreach (var phase in Phases(corner))
        {
            var approach = phase.Single(c => c.StartsWith("G53G0") && !c.Contains('Z'));
            Assert.Contains('X', approach);
            Assert.Contains('Y', approach);
        }
    }

    [Theory]
    [InlineData(CornerDirection.FrontLeft)]
    [InlineData(CornerDirection.FrontRight)]
    [InlineData(CornerDirection.BackLeft)]
    [InlineData(CornerDirection.BackRight)]
    public void EveryLegLiftsThenMovesAcrossThenDrops(CornerDirection corner)
    {
        foreach (var phase in Phases(corner))
        {
            var lift = phase.IndexOf(SafeZ);
            var across = phase.FindIndex(c => c.StartsWith("G53G0") && !c.Contains('Z'));
            var drop = phase.IndexOf(ProbeZ);

            Assert.True(lift >= 0 && across >= 0 && drop >= 0, $"{corner}: missing a move");
            Assert.True(lift < across, $"{corner}: moved across before lifting");
            Assert.True(across < drop, $"{corner}: dropped before moving across");
        }
    }

    [Theory]
    [InlineData(CornerDirection.FrontLeft)]
    [InlineData(CornerDirection.FrontRight)]
    [InlineData(CornerDirection.BackLeft)]
    [InlineData(CornerDirection.BackRight)]
    public void EveryApproachMoveIsAbsolute(CornerDirection corner)
    {
        // Relative approach moves are what accumulated. Only the probe itself is incremental.
        foreach (var phase in Phases(corner))
        {
            var approach = phase.GetRange(0, phase.FindIndex(c => c.StartsWith("G38")));
            Assert.Contains("G90", approach);
            Assert.DoesNotContain(approach, c => c.StartsWith("G0Z"));
        }
    }

    [Theory]
    // Stand off away from the stock; probe back toward it.
    [InlineData(CornerDirection.FrontLeft, "G53G0Y-55.000X-105.000", "G38.3F100X10")]
    [InlineData(CornerDirection.FrontRight, "G53G0Y-55.000X-95.000", "G38.3F100X-10")]
    [InlineData(CornerDirection.BackLeft, "G53G0Y-65.000X-105.000", "G38.3F100X10")]
    [InlineData(CornerDirection.BackRight, "G53G0Y-65.000X-95.000", "G38.3F100X-10")]
    public void TheStandOffOpposesTheProbeDirection(
        CornerDirection corner, string expectedApproach, string expectedProbe)
    {
        var phases = Phases(corner);

        Assert.Contains(expectedApproach, phases[0]);
        Assert.Contains(expectedProbe, phases[0]);
    }

    [Fact]
    public void IncludingZPutsTheTopFaceProbeFirst()
    {
        var phases = Phases(includeZ: true);

        Assert.Equal(3, phases.Count);
        Assert.Contains("G38.3F100Z-10", phases[0]);
    }

    [Fact]
    public void WithoutZThereAreOnlyTheTwoEdgeLegs()
    {
        Assert.Equal(2, Phases().Count);
    }
}
