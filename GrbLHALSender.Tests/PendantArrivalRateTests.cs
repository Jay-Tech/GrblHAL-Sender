using System.Collections.Generic;
using GrbLHALSender.Pendant;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// The ceiling the pendant's commanded feed is held under: the rate movement is
/// actually turning up at.
///
/// It exists because commanding above that rate cannot make the machine cover
/// more ground - the distance in each block is already fixed - it can only make
/// the axis arrive early and stand still until the next block, which is felt as
/// the machine running on after the wheel stops. Measured with the ceiling
/// switched off entirely, run-on was reported as clearly worse, most of all at
/// the 0.5 mm step where the same travel takes twice the wheel speed.
///
/// The tuning it needs is the point of these tests. Two windows are taken, a
/// long one and a short one, and the higher wins - the short one being what
/// stops a trailing average lagging a hand that is speeding up. But the short
/// window was guarded on elapsed time alone where the long one is guarded on
/// time and sample count both, and the pendant only emits on ticks that carry a
/// detent: a hesitant turn arrives at 60 ms rather than 27, and a 200 ms window
/// then holds three messages where it normally holds seven. An estimate from
/// three swings by several times on one extra detent, and because the windows
/// combine by taking the higher, that noise only ratchets the ceiling up before
/// the next block drops it again.
///
/// On the machine: arriving 778-3764 mm/min inside one 0.7 s burst, a commanded
/// feed swinging five to one block over block, and a planner that cannot chain
/// blocks asking for different velocities so decelerates into every one. Felt
/// as a stutter on a tentative start that clears as soon as the wheel is turned
/// confidently, which is exactly how it was reported.
/// </summary>
public class PendantArrivalRateTests
{
    /// <summary>A stream of equal blocks arriving every <paramref name="cadenceMs"/>.</summary>
    private static (List<(long, double)> Window, long Now) Stream(
        int cadenceMs, int count, double mmEach)
    {
        var window = new List<(long, double)>();
        long now = 100_000;
        for (var i = count - 1; i >= 0; i--)
            window.Add((now - i * (long)cadenceMs, mmEach));
        return (window, now);
    }

    [Fact]
    public void TooLittleHistorySaysNothing()
    {
        // Zero matters as much as a number does: with no measurement the
        // pendant's own figure stands, which is the behaviour this had before
        // any ceiling existed. Inventing a rate from one block is what commanded
        // single-digit feeds and left the machine crawling through a full planner.
        var (window, now) = Stream(cadenceMs: 30, count: 2, mmEach: 1);
        Assert.Equal(0, PendantService.ArrivalRate(window, now));
    }

    [Fact]
    public void ASteadyStreamMeasuresItsOwnRate()
    {
        // 1 mm every 30 ms is 33.3 mm/s, which is 2000 mm/min of movement - but
        // the estimate is deliberately above that, and by how much is worth
        // writing down rather than rounding away.
        //
        // Each sample is paired with the interval ending at it, so n samples
        // cover n intervals and the distance is divided by the time it actually
        // arrived over. The long window wins here: 17 mm over 480 ms, with the
        // 1.15 headroom on top, giving 2444 mm/min against 2000 of real
        // movement - 22% high, most of which is the headroom doing its job.
        //
        // It read 2683 (34% high) while the span was taken as oldest-to-now,
        // which gave n samples only n-1 intervals. Building the span from the
        // intervals themselves removed that, and it fell out of the stall
        // handling rather than being chased separately.
        var (window, now) = Stream(cadenceMs: 30, count: 17, mmEach: 1);
        var rate = PendantService.ArrivalRate(window, now);
        Assert.InRange(rate, 2400, 2480);
    }

    [Fact]
    public void ASparseStreamStillMeasuresARate()
    {
        // The long window carries it. Withholding the ceiling entirely on a slow
        // turn would bring back the run-on it exists to prevent.
        var (window, now) = Stream(cadenceMs: 60, count: 9, mmEach: 1);
        Assert.True(PendantService.ArrivalRate(window, now) > 0);
    }

    [Fact]
    public void ASparseStreamIsNotThrownByOneExtraDetent()
    {
        // The regression. At 60 ms the short window holds three or four blocks,
        // and before the sample floor a single larger one carried the whole
        // estimate upward through the max.
        var (steady, now) = Stream(cadenceMs: 60, count: 9, mmEach: 1);

        var spiked = new List<(long, double)>(steady);
        spiked[^1] = (spiked[^1].Item1, 6.0);   // one block six times the rest

        var calm = PendantService.ArrivalRate(steady, now);
        var jumpy = PendantService.ArrivalRate(spiked, now);

        // The long window still notices the extra distance, so this is not a
        // demand that the estimate be unchanged - only that it not multiply.
        Assert.True(jumpy < calm * 2.0,
            $"sparse estimate swung from {calm:0} to {jumpy:0} mm/min on one detent");
    }

    [Fact]
    public void AStallDoesNotCollapseTheEstimate()
    {
        // The defect this replaced. Taken as raw elapsed time, a gap adds its
        // whole duration to the denominator and no distance to the numerator,
        // so the rate collapses in proportion - and the ceiling is taken from
        // this, so a hiccup on the radio reached the machine as a speed change.
        // Measured as "arriving 640-12827 mm/min" in one burst carrying a
        // single 406 ms gap.
        var (steady, now) = Stream(cadenceMs: 30, count: 17, mmEach: 1);
        var clean = PendantService.ArrivalRate(steady, now);

        // The same hand, the same distance, with 400 ms of silence part way
        // through: every sample before the gap shifted back in time.
        var stalled = new List<(long, double)>();
        for (var i = 0; i < steady.Count; i++)
        {
            var (tick, mm) = steady[i];
            stalled.Add((i < 8 ? tick - 400 : tick, mm));
        }

        var gapped = PendantService.ArrivalRate(stalled, now);

        Assert.True(gapped > clean * 0.75,
            $"a 400 ms stall dropped the estimate from {clean:0} to {gapped:0} mm/min");
    }

    [Fact]
    public void ADenseStreamStillReactsToAHandSpeedingUp()
    {
        // The short window has to keep working where it has the samples to, or a
        // trailing average lags an accelerating wheel and the machine dips part
        // way into every ramp.
        var (window, now) = Stream(cadenceMs: 25, count: 20, mmEach: 1);
        var steady = PendantService.ArrivalRate(window, now);

        // Same cadence, but the most recent 200 ms travels four times as far.
        var ramp = new List<(long, double)>();
        foreach (var (tick, mm) in window)
            ramp.Add((tick, now - tick <= 200 ? mm * 4 : mm));

        Assert.True(PendantService.ArrivalRate(ramp, now) > steady * 2.0,
            "the short window should lift the ceiling for a hand speeding up");
    }
}
