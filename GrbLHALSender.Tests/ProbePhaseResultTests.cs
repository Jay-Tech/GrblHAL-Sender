using System.Collections.Generic;
using GrbLHALSender.Probe;
using GrbLHALSender.ViewModels;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests which probe report belongs to which phase.
/// <para>
/// Found on hardware: a bore centre probe made all four touches correctly and then rapided
/// into the right-hand wall. Every <c>ProbeSingleAxis</c> probes twice — a fast search pass
/// and a slow latch pass — so each phase reports two <c>[PRB:]</c> lines, while the handlers
/// indexed results one per phase. The centre finder therefore read results 0 and 1 as X+ and
/// X− when they were really the search and latch of X+ alone, so the "centre" it computed was
/// the midpoint of two touches on the same wall, and it drove there.
/// </para>
/// <para>
/// The corner probe read its Y datum off the X phase for the same reason, and the Z probe used
/// the fast search pass instead of the accurate latch.
/// </para>
/// </summary>
public class ProbePhaseResultTests
{
    private static ProbeState Result(string x, string y, string z = "0", bool ok = true) =>
        new() { XOffset = x, YOffset = y, ZOffset = z, ProbeSuccessful = ok };

    [Fact]
    public void APhaseThatProbesTwiceLeavesOneEntry()
    {
        var results = new List<ProbeState>();

        ProbeViewModel.RecordPhaseResult(results, 0, Result("1", "0"));   // search
        ProbeViewModel.RecordPhaseResult(results, 0, Result("1.5", "0")); // latch

        Assert.Single(results);
    }

    [Fact]
    public void TheEntryHeldIsTheLatchPass()
    {
        // The search pass stops at the fast feed rate; the latch is the one to trust.
        var results = new List<ProbeState>();

        ProbeViewModel.RecordPhaseResult(results, 0, Result("1", "0"));
        ProbeViewModel.RecordPhaseResult(results, 0, Result("1.5", "0"));

        Assert.Equal("1.5", results[0].XOffset);
    }

    [Fact]
    public void FourProbedPhasesGiveFourResultsInPhaseOrder()
    {
        // The centre finder's assumption, which now actually holds. Eight reports in, four out.
        var results = new List<ProbeState>();
        var touches = new[] { "10", "-10", "20", "-20" };

        for (var phase = 0; phase < 4; phase++)
        {
            ProbeViewModel.RecordPhaseResult(results, phase, Result("search", "search"));
            ProbeViewModel.RecordPhaseResult(results, phase, Result(touches[phase], touches[phase]));
        }

        Assert.Equal(4, results.Count);
        Assert.Equal("10", results[0].XOffset);
        Assert.Equal("-10", results[1].XOffset);
        Assert.Equal("20", results[2].YOffset);
        Assert.Equal("-20", results[3].YOffset);
    }

    [Fact]
    public void TheCentreOfTwoOppositeWallsIsNotTheWall()
    {
        // The reported symptom, arithmetically. With the old indexing the first two entries
        // were both X+ touches, so their midpoint sat on the +X wall rather than between it
        // and the -X wall.
        var results = new List<ProbeState>();

        ProbeViewModel.RecordPhaseResult(results, 0, Result("9.9", "0"));   // X+ search
        ProbeViewModel.RecordPhaseResult(results, 0, Result("10", "0"));    // X+ latch
        ProbeViewModel.RecordPhaseResult(results, 1, Result("-9.9", "0"));  // X- search
        ProbeViewModel.RecordPhaseResult(results, 1, Result("-10", "0"));   // X- latch

        var centre = (double.Parse(results[0].XOffset) + double.Parse(results[1].XOffset)) / 2;

        Assert.Equal(0, centre);
    }

    [Fact]
    public void APhaseWithNoProbeLeavesAPlaceholderRatherThanShiftingLaterPhases()
    {
        // The tool reference cycle's first phase is an approach move with no probe in it. What
        // matters is that the probing phase keeps its own index, so [^1] still finds the latch.
        var results = new List<ProbeState>();

        ProbeViewModel.RecordPhaseResult(results, 1, Result("search", "search"));
        ProbeViewModel.RecordPhaseResult(results, 1, Result("real", "real"));

        Assert.Equal(2, results.Count);
        Assert.Equal("real", results[^1].XOffset);
    }

    [Fact]
    public void AFailedLatchReplacesASuccessfulSearch()
    {
        // The phase's verdict is its last word — a search that touched but a latch that did
        // not must not read as a good phase.
        var results = new List<ProbeState>();

        ProbeViewModel.RecordPhaseResult(results, 0, Result("1", "0"));
        ProbeViewModel.RecordPhaseResult(results, 0, Result("0", "0", ok: false));

        Assert.False(results[0].ProbeSuccessful);
    }
}
