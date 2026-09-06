using GrbLHALSender.Pendant;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// The floor under the commanded feed: the slowest rate the pendant can
/// actually deliver at the current step.
///
/// The handheld throttles its own detents from the feed this end reports, so
/// commanding low is normally self-correcting - right up to its
/// one-detent-per-20 ms-tick clamp (the allowed &lt; 1 guard in jog.py), below
/// which it cannot slow down however it is asked. That clamp is step x 3000
/// mm/min: 1500 at a 0.5 mm step, 3000 at 1 mm. Command under it while the
/// wheel turns and the surplus has nowhere to go but the planner, arriving
/// later as travel after the hand has stopped.
///
/// Measured with the feed capped at 2000 and a 1 mm step: 431 mm arrived in
/// 7.9 s (3273 mm/min) against 2000 commanded, and the planner filled to four
/// blocks free - about 124 mm still to run when the hand stopped.
///
/// The bound against the request is the half these tests exist for, because
/// getting it wrong was worse than not having the floor at all. Written first
/// against the measured arrival rate, it pushed the feed to 12000 at a 0.5 mm
/// step whose firmware ceiling is 8000 - that estimate reads about twice the
/// true rate, between the span bias, the headroom and taking the higher of two
/// windows - and snapping up from a noisy number changed the commanded feed
/// every few blocks: 106 changes in 207 dispatches, against 9 in 143 with the
/// floor off.
/// </summary>
public class PendantDeliveryFloorTests
{
    // STEP_MAX_FEED from jog.py, for the two steps in normal use.
    private const double AskAtHalfMm = 8000;
    private const double AskAtOneMm = 10000;

    [Fact]
    public void RaisesAFeedBelowWhatTheHandheldCanDeliver()
    {
        // 1 mm step floors at 3000. The 2000 that filled the planner.
        Assert.Equal(3000, PendantService.ApplyDeliveryFloor(
            feed: 2000, step: 1.0, askedFor: AskAtOneMm, quantum: 500));
    }

    [Fact]
    public void LeavesAFeedAboveTheFloorAlone()
    {
        Assert.Equal(6000, PendantService.ApplyDeliveryFloor(
            feed: 6000, step: 1.0, askedFor: AskAtOneMm, quantum: 500));
    }

    [Fact]
    public void TheHalfMillimetreStepFloorsLower()
    {
        // 0.5 x 3000 = 1500, which is why that step stayed well behaved under
        // the same 2000 cap that broke the 1 mm one.
        Assert.Equal(1500, PendantService.ApplyDeliveryFloor(
            feed: 1000, step: 0.5, askedFor: AskAtHalfMm, quantum: 500));
        Assert.Equal(2000, PendantService.ApplyDeliveryFloor(
            feed: 2000, step: 0.5, askedFor: AskAtHalfMm, quantum: 500));
    }

    [Fact]
    public void NeverCommandsAboveWhatThePendantAskedFor()
    {
        // The regression. The request carries STEP_MAX_FEED, so a floor that
        // walks through it commands a speed the firmware ruled out for that
        // step - 12000 at 0.5 mm, where the ceiling is 8000.
        var feed = PendantService.ApplyDeliveryFloor(
            feed: 600, step: 0.5, askedFor: 600, quantum: 500);

        Assert.Equal(600, feed);
    }

    [Fact]
    public void ASlowTurnIsNotDraggedUpToTheFloor()
    {
        // Asking for less than the floor means the wheel is too slow to reach
        // the one-detent clamp, so nothing is accumulating and there is nothing
        // to correct. Raising it here would command motion nobody asked for.
        Assert.Equal(800, PendantService.ApplyDeliveryFloor(
            feed: 800, step: 1.0, askedFor: 800, quantum: 500));
    }

    [Fact]
    public void TheRaisedFeedLandsOnTheGrid()
    {
        // Or the floor would reintroduce the off-grid values the quantum exists
        // to remove. 0.1 mm floors at 300, which is not a multiple of 500.
        Assert.Equal(500, PendantService.ApplyDeliveryFloor(
            feed: 100, step: 0.1, askedFor: 2500, quantum: 500));
    }

    [Fact]
    public void NoStepYetMeansNoFloor()
    {
        // Before the first jog message carries a step there is nothing to
        // derive a floor from, and inventing one would command a feed from no
        // information at all.
        Assert.Equal(1200, PendantService.ApplyDeliveryFloor(
            feed: 1200, step: 0, askedFor: AskAtOneMm, quantum: 500));
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(0.01)]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void TheFloorNeverExceedsTheRequestAtAnyStep(double step)
    {
        // The invariant, across the whole STEP_SIZES ladder.
        for (var asked = 100.0; asked <= 12000; asked += 137)
        for (var feed = 0.0; feed <= asked; feed += asked / 4)
        {
            var result = PendantService.ApplyDeliveryFloor(feed, step, asked, 500);
            Assert.True(result <= asked,
                $"step {step}, asked {asked}, feed {feed} -> {result}");
        }
    }
}
