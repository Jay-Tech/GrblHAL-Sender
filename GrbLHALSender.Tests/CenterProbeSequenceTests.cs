using System.Collections.Generic;
using System.Linq;
using GrbLHALSender.Probe;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests the motion an inside center probe makes.
/// <para>
/// The old sequence stepped back by ProbeDistance in G91 between directions, on the
/// assumption that unwound the probe move. A probe stops early, on contact, so the step
/// overshot past center by however far short of ProbeDistance the wall actually was — and
/// once that overshoot exceeded the radius it was a <em>rapid</em> into the opposite wall.
/// Any bore narrower than twice the probe distance did it, which with the default 10 means
/// anything under 20.
/// </para>
/// <para>
/// Each phase now returns to the point the cycle started from, absolutely, by machine
/// coordinate. That point cannot be unsafe: the machine was standing on it.
/// </para>
/// </summary>
public class CenterProbeSequenceTests
{
    private const double StartX = -100.5;
    private const double StartY = -60.25;

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

    private static List<List<string>> Phases() =>
        Builder().ProbeInsideCenter(StartX, StartY);

    [Fact]
    public void ThereAreFourPhases()
    {
        Assert.Equal(4, Phases().Count);
    }

    [Fact]
    public void TheFirstPhaseProbesFromWhereTheOperatorLeftIt()
    {
        // No return move needed — the machine is already on the start point.
        Assert.Equal(
            new[] { "G21", "G91", "G38.3F100X10", "G0X-1", "G38.3F20X1" },
            Phases()[0]);
    }

    [Fact]
    public void EveryLaterPhaseReturnsToTheStartBeforeProbing()
    {
        var phases = Phases();

        // X is put back for the X- probe and again before Y is probed at all; Y is only
        // disturbed by the Y+ probe, so it is put back once.
        Assert.Equal("G53G0X-100.500", phases[1][2]);
        Assert.Equal("G53G0X-100.500", phases[2][2]);
        Assert.Equal("G53G0Y-60.250", phases[3][2]);
    }

    [Fact]
    public void TheReturnIsAbsoluteNotAStepBack()
    {
        // The whole point. A relative step cannot know how far the probe actually travelled.
        foreach (var phase in Phases().Skip(1))
        {
            Assert.Equal("G90", phase[1]);
            Assert.StartsWith("G53G0", phase[2]);
        }
    }

    [Fact]
    public void NoPhaseStepsBackByTheProbeDistance()
    {
        // The exact command that drove into the far wall.
        foreach (var phase in Phases())
        {
            Assert.DoesNotContain("G0X-10", phase);
            Assert.DoesNotContain("G0Y-10", phase);
        }
    }

    [Fact]
    public void TheParserIsPutBackIntoRelativeForEachProbe()
    {
        // The return needs G90; the probe moves are all incremental. Leaving G90 in force
        // would send the probe to an absolute coordinate instead of a distance.
        foreach (var phase in Phases().Skip(1))
        {
            var probeIndex = phase.FindIndex(c => c.StartsWith("G38"));
            var g91Index = phase.LastIndexOf("G91");

            Assert.True(g91Index >= 0, "no G91 before the probe");
            Assert.True(g91Index < probeIndex, "probe ran while still in G90");
        }
    }

    [Fact]
    public void TheFourPhasesProbeTheFourDirectionsInOrder()
    {
        var phases = Phases();

        // OnProbeCenterComplete reads results positionally as X+, X-, Y+, Y-.
        Assert.Contains("G38.3F100X10", phases[0]);
        Assert.Contains("G38.3F100X-10", phases[1]);
        Assert.Contains("G38.3F100Y10", phases[2]);
        Assert.Contains("G38.3F100Y-10", phases[3]);
    }
}
