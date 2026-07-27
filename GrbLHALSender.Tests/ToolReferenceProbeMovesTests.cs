using GrbLHALSender.ViewModels;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for the moves built around a tool reference probe at the G59.3 setter.
/// <para>
/// Reported on hardware: Z came back correctly but X and Y drove to the home corner. The
/// return move was built from MachineStateService's positions, which are already in display
/// units, and then put through the sequence conversion a second time. On a metric machine
/// shown in inches that divides by 25.4 twice, turning X -1.360 into -0.054 — near enough
/// machine zero to look like the machine had gone home. The $# values in the approach do
/// need that conversion, which is what made the trap easy to walk into.
/// </para>
/// </summary>
public class ToolReferenceProbeMovesTests
{
    // The reported setup: a machine reporting in millimetres, displayed in inches.
    private const bool MachineMetric = true;
    private const string Imperial = "G20";
    private const string Metric = "G21";

    // $# output, so millimetres on this machine.
    private static double[] Setter() => new[] { 100.0, 200.0, -5.0 };

    // MachineStateService output, so already inches on this display.
    private static double[] Start() => new[] { -1.360, -2.500, -3.000 };

    [Fact]
    public void TheReturnMoveUsesTheCapturedPositionUnconverted()
    {
        var (_, back) = ProbeViewModel.BuildSetterProbeMoves(
            Setter(), Start(), Imperial, MachineMetric);

        Assert.Equal("G53G0X-1.360Y-2.500", back[^1]);
    }

    [Fact]
    public void TheReturnMoveIsNotConvertedTwice()
    {
        // What the bug produced: -1.360 / 25.4 = -0.054, which is machine zero in all but
        // name. Pinned explicitly because the wrong value still looks like a coordinate.
        var (_, back) = ProbeViewModel.BuildSetterProbeMoves(
            Setter(), Start(), Imperial, MachineMetric);

        Assert.DoesNotContain("-0.054", back[^1]);
    }

    [Fact]
    public void TheApproachConvertsTheSetterPositionIntoTheSequenceUnit()
    {
        // These are $# values, so they follow $13 and do need converting.
        var (approach, _) = ProbeViewModel.BuildSetterProbeMoves(
            Setter(), Start(), Imperial, MachineMetric);

        Assert.Equal("G53G0X3.937Y7.874", approach[3]);
        Assert.Equal("G53G0Z-0.197", approach[4]);
    }

    [Fact]
    public void MatchingUnits_ConvertNeitherSet()
    {
        var (approach, back) = ProbeViewModel.BuildSetterProbeMoves(
            Setter(), new[] { -34.5, -63.5, -76.2 }, Metric, MachineMetric);

        Assert.Equal("G53G0X100.000Y200.000", approach[3]);
        Assert.Equal("G53G0X-34.500Y-63.500", back[^1]);
    }

    [Fact]
    public void TheApproachLeadsWithTheUnitWordAndRetractsBeforeTravelling()
    {
        // Order is the safety property: a long tool must not be dragged across the work.
        var (approach, _) = ProbeViewModel.BuildSetterProbeMoves(
            Setter(), Start(), Imperial, MachineMetric);

        Assert.Equal(Imperial, approach[0]);
        Assert.Equal("G90", approach[1]);
        Assert.Equal("G53G0Z0", approach[2]);
    }

    [Fact]
    public void TheReturnAlwaysRetractsZFirst()
    {
        var (_, back) = ProbeViewModel.BuildSetterProbeMoves(
            Setter(), Start(), Imperial, MachineMetric);

        Assert.Equal("G90", back[0]);
        Assert.Equal("G53G0Z0", back[1]);
    }

    [Fact]
    public void NoCapturedPosition_RetractsAndStopsThere()
    {
        // Fewer than three axes seen: there is no position to go back to, and inventing
        // one is worse than leaving the tool clear of the work.
        var (_, back) = ProbeViewModel.BuildSetterProbeMoves(
            Setter(), null, Imperial, MachineMetric);

        Assert.Equal(new[] { "G90", "G53G0Z0" }, back);
    }

    [Fact]
    public void AnImperialMachineShownInMetric_ConvertsTheOtherWay()
    {
        var (approach, _) = ProbeViewModel.BuildSetterProbeMoves(
            new[] { 1.0, 2.0, -0.5 }, Start(), Metric, machineIsMetric: false);

        Assert.Equal("G53G0X25.400Y50.800", approach[3]);
    }
}
