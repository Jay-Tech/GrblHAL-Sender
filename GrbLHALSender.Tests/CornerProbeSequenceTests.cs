using GrbLHALSender.Probe;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests the motion a corner probe makes, which is the part that has to be right before a
/// stylus is anywhere near it.
/// <para>
/// Reported from hardware: bringing the probe to the corner and starting the cycle made it
/// lift and then immediately probe sideways. The old sequence retracted Z and probed from
/// there, so with the stylus over the top face the sideways move passed above the edge and
/// touched nothing. There was no lateral move clear of the stock and no plunge.
/// </para>
/// <para>
/// Each leg is now lift, step clear, drop below the top face, probe back in. The order is
/// the safety property: dropping before stepping clear puts the stylus into the material.
/// </para>
/// </summary>
public class CornerProbeSequenceTests
{
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

    [Fact]
    public void TheFirstLegLiftsThenStepsClearThenDropsThenProbes()
    {
        var phases = Builder().ProbeCorner(CornerDirection.FrontLeft, includeZ: false);

        // Front left: stock is to the +X and +Y, so step clear toward -X and probe back +X.
        Assert.Equal(
            new[] { "G21", "G91", "G0Z5", "G0X-5", "G0Z-8", "G21", "G91", "G38.3F100X10", "G0X-1", "G38.3F20X1" },
            phases[0]);
    }

    [Fact]
    public void TheDropIsTheLiftPlusTheDepth()
    {
        // Net result is ProbeDepth below where the operator left the stylus, not below the
        // safe height — otherwise raising the clearance would quietly probe shallower.
        var phases = Builder().ProbeCorner(CornerDirection.FrontLeft, includeZ: false);

        Assert.Contains("G0Z5", phases[0]);    // lift
        Assert.Contains("G0Z-8", phases[0]);   // 5 + 3
    }

    [Fact]
    public void TheSecondLegStepsIntoTheStockBeforeSteppingClear()
    {
        // The first leg leaves the machine on the edge it just found. Probing Y from there
        // would run along the X face instead of into the Y one.
        var phases = Builder().ProbeCorner(CornerDirection.FrontLeft, includeZ: false);

        Assert.Equal(
            new[] { "G21", "G91", "G0Z5", "G0X5", "G0Y-5", "G0Z-8", "G21", "G91", "G38.3F100Y10", "G0Y-1", "G38.3F20Y1" },
            phases[1]);
    }

    [Fact]
    public void EveryLegLiftsBeforeItMovesLaterally()
    {
        foreach (var corner in new[] { CornerDirection.FrontLeft, CornerDirection.FrontRight,
                                       CornerDirection.BackLeft, CornerDirection.BackRight })
        {
            var phases = Builder().ProbeCorner(corner, includeZ: false);
            foreach (var phase in phases)
            {
                var lift = phase.IndexOf($"G0Z5");
                var firstLateral = phase.FindIndex(c => c.StartsWith("G0X") || c.StartsWith("G0Y"));
                Assert.True(lift >= 0, $"{corner}: no lift");
                Assert.True(lift < firstLateral, $"{corner}: moved sideways before lifting");
            }
        }
    }

    [Fact]
    public void EveryLegDropsOnlyAfterItIsClearOfTheStock()
    {
        foreach (var corner in new[] { CornerDirection.FrontLeft, CornerDirection.FrontRight,
                                       CornerDirection.BackLeft, CornerDirection.BackRight })
        {
            var phases = Builder().ProbeCorner(corner, includeZ: false);
            foreach (var phase in phases)
            {
                // Only the approach counts. The probe itself retracts sideways between its
                // search and latch passes, which is after the drop and entirely correct.
                var firstProbe = phase.FindIndex(c => c.StartsWith("G38"));
                var approach = phase.GetRange(0, firstProbe);

                var drop = approach.IndexOf("G0Z-8");
                var lastLateral = approach.FindLastIndex(c =>
                    c.StartsWith("G0X") || c.StartsWith("G0Y"));

                Assert.True(drop >= 0, $"{corner}: no drop in the approach");
                Assert.True(drop > lastLateral, $"{corner}: dropped before stepping clear");
            }
        }
    }

    [Theory]
    // Stock lies toward the probe direction, so the step clear is always the other way.
    [InlineData(CornerDirection.FrontLeft, "G0X-5", "G38.3F100X10")]
    [InlineData(CornerDirection.FrontRight, "G0X5", "G38.3F100X-10")]
    [InlineData(CornerDirection.BackLeft, "G0X-5", "G38.3F100X10")]
    [InlineData(CornerDirection.BackRight, "G0X5", "G38.3F100X-10")]
    public void TheStepClearOpposesTheProbeDirection(
        CornerDirection corner, string expectedStepClear, string expectedProbe)
    {
        var phases = Builder().ProbeCorner(corner, includeZ: false);

        Assert.Contains(expectedStepClear, phases[0]);
        Assert.Contains(expectedProbe, phases[0]);
    }

    [Fact]
    public void IncludingZPutsTheTopFaceProbeFirst()
    {
        var phases = Builder().ProbeCorner(CornerDirection.FrontLeft, includeZ: true);

        Assert.Equal(3, phases.Count);
        Assert.Contains("G38.3F100Z-10", phases[0]);
    }

    [Fact]
    public void WithoutZThereAreOnlyTheTwoEdgeLegs()
    {
        Assert.Equal(2, Builder().ProbeCorner(CornerDirection.FrontLeft, includeZ: false).Count);
    }
}
