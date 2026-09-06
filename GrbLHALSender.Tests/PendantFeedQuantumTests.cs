using GrbLHALSender.Pendant;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Snapping the commanded feed to a grid, so a steady turn produces a steady F.
///
/// The pendant sets its feed from how fast the wheel is being turned, so below
/// that step's own ceiling every block carries a slightly different F word. A
/// planner cannot chain blocks asking for different velocities - it ramps down
/// and back up at each junction - and at a couple of hundred blocks a second
/// that is the roughness felt at any wheel speed except flat out. Flat out is
/// smooth for one reason only: the feed saturates the pendant's STEP_MAX_FEED
/// and stops varying.
///
/// Measured at the machine: a burst pinned to a constant feed ran the planner
/// down to 103 blocks free, where the same wheel speed with a feed spread of
/// 1106-5531 never got below 125 - a queue that never held more than three
/// blocks to look ahead through.
/// </summary>
public class PendantFeedQuantumTests
{
    [Fact]
    public void NoQuantumLeavesTheFeedAlone()
    {
        Assert.Equal(3273, PendantService.SnapFeed(3273, 0));
    }

    [Fact]
    public void NeighbouringFeedsCollapseOntoOneGridValue()
    {
        // The whole point: a hand holding a steady speed wanders either side of
        // a value, and every one of those blocks has to come out with the same
        // F or the planner ramps between them.
        var a = PendantService.SnapFeed(2410, 500);
        var b = PendantService.SnapFeed(2585, 500);
        var c = PendantService.SnapFeed(2499, 500);

        Assert.Equal(2500, a);
        Assert.Equal(2500, b);
        Assert.Equal(2500, c);
    }

    [Fact]
    public void RoundsToNearestRatherThanDown()
    {
        // Rounding down would bias every block slow, and a feed biased under
        // what is arriving is how motion piles up in the planner.
        Assert.Equal(3000, PendantService.SnapFeed(2900, 500));
    }

    [Fact]
    public void ASlowFeedIsNeverSnappedToNothing()
    {
        // A block under half a quantum still has its distance to travel, and an
        // F of zero is not a slow move - it is a line the controller rejects.
        Assert.Equal(500, PendantService.SnapFeed(120, 500));
        Assert.Equal(500, PendantService.SnapFeed(1, 500));
    }

    [Fact]
    public void ZeroStaysZeroSoTheFallbackStillApplies()
    {
        // Zero means "the pendant sent no feed", which is answered upstream by
        // FallbackJogFeedMmPerMin. Snapping it to a quantum here would invent a
        // feed and hide that.
        Assert.Equal(0, PendantService.SnapFeed(0, 500));
    }

    // --- staying on the grid under the pendant's ceiling --------------------

    [Fact]
    public void ComingBackUnderTheAskLandsOnTheGrid()
    {
        // The bug. Rounding 7969 to nearest gives 8000, which is above what the
        // pendant asked for - and clamping straight to 7969 put the commanded
        // feed back off the grid, where hysteresis then latched it. The value
        // below it on the grid is the right answer.
        Assert.Equal(7500, PendantService.SnapFeedDown(7969, 500));
        Assert.Equal(6000, PendantService.SnapFeedDown(7969, 2000));
    }

    [Fact]
    public void AValueAlreadyOnTheGridIsUnchanged()
    {
        Assert.Equal(8000, PendantService.SnapFeedDown(8000, 500));
        Assert.Equal(8000, PendantService.SnapFeedDown(8000, 2000));
    }

    [Fact]
    public void BelowOneQuantumTheRequestStands()
    {
        // There is no grid value under it except zero, and commanding zero is a
        // rejected line rather than a slow move. A real gap, and the coarser
        // the grid the more of the range falls into it - which is part of why
        // 2000 behaved worse than 500 rather than merely coarser.
        Assert.Equal(638, PendantService.SnapFeedDown(638, 2000));
        Assert.Equal(300, PendantService.SnapFeedDown(300, 500));
    }

    [Fact]
    public void NeverReturnsMoreThanItWasGiven()
    {
        // The invariant the ceiling depends on.
        foreach (var quantum in new[] { 250.0, 500.0, 2000.0 })
        for (var asked = 50.0; asked < 12000; asked += 23)
            Assert.True(PendantService.SnapFeedDown(asked, quantum) <= asked);
    }

    // --- hysteresis --------------------------------------------------------

    [Fact]
    public void HoldsThePreviousFeedWhileTheRequestStaysNearIt()
    {
        // The boundary flap. A bare snap puts a request drifting around 3250
        // alternately on 3000 and 3500, and each flip is a velocity change the
        // planner has to ramp through - so the grid converts small wobble into
        // whole grid steps instead of removing it.
        // Upward the band is half a step (250 here), downward a whole one.
        Assert.Equal(3000, PendantService.SnapFeed(3240, 500, previous: 3000));
        Assert.Equal(3000, PendantService.SnapFeed(3200, 500, previous: 3000));
        Assert.Equal(3000, PendantService.SnapFeed(2800, 500, previous: 3000));
        Assert.Equal(3000, PendantService.SnapFeed(2600, 500, previous: 3000));
    }

    [Fact]
    public void MovesOnceTheRequestClearsTheHeldValue()
    {
        // Holding must not become sticking. A full grid step away and it moves.
        Assert.Equal(3500, PendantService.SnapFeed(3550, 500, previous: 3000));
        Assert.Equal(2500, PendantService.SnapFeed(2450, 500, previous: 3000));
    }

    [Fact]
    public void ClimbsOnHalfAStepButOnlyFallsOnAWholeOne()
    {
        // Asymmetric on purpose. Symmetric slack made the top end unreachable
        // at a coarse grid: held at 6000 on a 2000 grid, only a request of
        // 8000 could move it - and 8000 is the pendant's own ceiling for that
        // step, so the operator had to hit the exact maximum to leave 75% of
        // it. Asking for more is answered promptly; a hand wobbling downward
        // is not.
        Assert.Equal(8000, PendantService.SnapFeed(7100, 2000, previous: 6000));
        Assert.Equal(6000, PendantService.SnapFeed(6900, 2000, previous: 6000));

        // Coming down still needs the whole step, so the hold at speed stays.
        Assert.Equal(6000, PendantService.SnapFeed(5100, 2000, previous: 6000));
        Assert.Equal(4000, PendantService.SnapFeed(3900, 2000, previous: 6000));
    }

    [Fact]
    public void ItIsAlwaysEasierToSpeedUpThanToSlowDown()
    {
        // The asymmetry stated as a property rather than at sample points, so
        // it survives any later change to the band sizes.
        foreach (var quantum in new[] { 250.0, 500.0, 2000.0 })
        foreach (var held in new[] { 2000.0, 4000.0, 6000.0 })
        {
            // The smallest rise that moves it must be smaller than the
            // smallest fall that does.
            var rise = 1.0;
            while (rise < quantum * 2 &&
                   PendantService.SnapFeed(held + rise, quantum, held) == held)
                rise += 1;

            var fall = 1.0;
            while (fall < quantum * 2 &&
                   PendantService.SnapFeed(held - fall, quantum, held) == held)
                fall += 1;

            Assert.True(rise < fall,
                $"quantum {quantum}, held {held}: rise {rise} not easier than fall {fall}");
        }
    }

    [Fact]
    public void TheBandsAreConfigurableInBothDirections()
    {
        // Widening the fall band is what buys room to ease off while holding
        // top speed - the whole of the "got to keep spinning fast" complaint at
        // a 0.5 mm step, where every 500 mm/min of band is another 17 detents a
        // second the hand has to sustain.
        Assert.Equal(8000, PendantService.SnapFeed(
            7100, 500, previous: 8000, riseSteps: 0.5, fallSteps: 2.0));

        // And at the default band the same request lets go.
        Assert.Equal(7000, PendantService.SnapFeed(
            7100, 500, previous: 8000, riseSteps: 0.5, fallSteps: 1.0));
    }

    [Fact]
    public void AZeroBandMeansNoHoldInThatDirection()
    {
        // Rise 0 makes the feed follow the request upward immediately, which is
        // the setting to reach for if the top of a step's range still feels out
        // of reach.
        Assert.Equal(8000, PendantService.SnapFeed(
            7800, 500, previous: 7500, riseSteps: 0, fallSteps: 1.0));
    }

    [Fact]
    public void TheCapIsReachableFromTheStepBelowIt()
    {
        // The failure in one line, at both grid sizes: the machine must be able
        // to reach its top speed without the request landing exactly on it.
        Assert.Equal(8000, PendantService.SnapFeed(7800, 500, previous: 7500));
        Assert.Equal(8000, PendantService.SnapFeed(7200, 2000, previous: 6000));
    }

    [Fact]
    public void ARealAccelerationIsNotHeldBack()
    {
        // Winding the wheel up has to reach the top, not creep there.
        Assert.Equal(8000, PendantService.SnapFeed(8000, 500, previous: 3000));
    }

    [Fact]
    public void NoPreviousValueBehavesAsAPlainSnap()
    {
        // The first block of a burst, and every block when the held value has
        // been cleared at the end of the previous one.
        Assert.Equal(3500, PendantService.SnapFeed(3260, 500, previous: 0));
    }

    [Theory]
    [InlineData(250)]
    [InlineData(500)]
    [InlineData(1000)]
    public void EveryResultIsOnTheGrid(double quantum)
    {
        for (var feed = 1.0; feed < 12000; feed += 37.5)
        {
            var snapped = PendantService.SnapFeed(feed, quantum);
            Assert.Equal(0, snapped % quantum, 6);
            Assert.True(snapped >= quantum);
        }
    }
}

/// <summary>
/// Rounding up onto the grid, which is how the delivery floor survives
/// quantization.
///
/// Snapping to nearest can put the commanded feed up to half a quantum under
/// the rate movement is arriving at - 250 mm/min on a 500 grid, 1000 on a 2000
/// one. A feed under the arrival rate does not make the machine go slower; the
/// surplus queues, and comes back as travel after the hand has stopped. So the
/// coarser the grid, the more thoroughly rounding to nearest would undo the
/// floor it is meant to sit alongside.
/// </summary>
public class PendantFeedFloorSnapTests
{
    [Fact]
    public void RoundsUpRatherThanToNearest()
    {
        // 2600 would snap down to 2500 at nearest, which is 100 mm/min under
        // what is arriving - a slow leak into the planner.
        Assert.Equal(3000, PendantService.SnapFeedUp(2600, 500));
        Assert.Equal(3000, PendantService.SnapFeedUp(2501, 500));
    }

    [Fact]
    public void AValueAlreadyOnTheGridIsLeftAlone()
    {
        Assert.Equal(2500, PendantService.SnapFeedUp(2500, 500));
    }

    [Fact]
    public void NeverReturnsLessThanItWasGiven()
    {
        // The invariant the floor depends on, across both grids under test.
        foreach (var quantum in new[] { 250.0, 500.0, 2000.0 })
        for (var arriving = 1.0; arriving < 12000; arriving += 17.0)
            Assert.True(PendantService.SnapFeedUp(arriving, quantum) >= arriving);
    }

    [Fact]
    public void NoGridLeavesTheRateExactlyAsMeasured()
    {
        Assert.Equal(3273, PendantService.SnapFeedUp(3273, 0));
    }
}
