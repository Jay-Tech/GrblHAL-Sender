using GrbLHALSender.Communication;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for a tool change with no job behind it — an M6 typed into the MDI.
/// <para>
/// Reported on hardware: the change itself worked, but the Touch Off button was not
/// there to complete it. Nothing is streaming, so there is no M6 line to raise the
/// barrier on; entering Tool state has to do it instead. What follows is the same
/// problem a job has — the operator must jog to the setter, and grblHAL reports Idle the
/// moment that jog finishes, which read as "the change is over" and cleared the button.
/// </para>
/// </summary>
public class ManualToolChangeBarrierTests
{
    private static ToolChangeBarrier Manual()
    {
        var barrier = new ToolChangeBarrier();
        barrier.ManualToolChangeSeen();
        return barrier;
    }

    [Fact]
    public void EnteringToolState_RaisesTheBarrier()
    {
        Assert.True(Manual().IsUp);
    }

    [Fact]
    public void JoggingToTheSetter_DoesNotLiftIt()
    {
        var barrier = Manual();

        Assert.False(barrier.Update("Jog", toolChangeLineAcked: true));
        Assert.True(barrier.IsUp);
    }

    [Fact]
    public void IdleAfterThatJog_DoesNotLiftIt()
    {
        // The reported symptom: this is where the Touch Off button used to disappear.
        var barrier = Manual();
        barrier.Update("Jog", toolChangeLineAcked: true);

        Assert.False(barrier.Update("Idle", toolChangeLineAcked: true));
        Assert.True(barrier.IsUp);
    }

    [Fact]
    public void ItSurvivesJoggingBackAndForth()
    {
        var barrier = Manual();
        for (var i = 0; i < 12; i++)
        {
            barrier.Update("Jog", toolChangeLineAcked: true);
            barrier.Update("Idle", toolChangeLineAcked: true);
        }

        Assert.True(barrier.IsUp);
    }

    [Fact]
    public void OnlyIdleAfterACycleStartLiftsIt()
    {
        var barrier = Manual();
        barrier.Update("Idle", toolChangeLineAcked: true);
        Assert.True(barrier.IsUp);

        barrier.CycleStartIssued();

        // Still the restore move in progress, not the end of it.
        Assert.False(barrier.Update("Run", toolChangeLineAcked: true));
        Assert.True(barrier.IsUp);

        Assert.True(barrier.Update("Idle", toolChangeLineAcked: true));
        Assert.False(barrier.IsUp);
    }

    [Fact]
    public void SeeingToolStateAgainWhileUp_ChangesNothing()
    {
        // Tool is reported every poll for the length of the pause; only the first matters.
        var barrier = Manual();
        barrier.CycleStartIssued();
        barrier.ManualToolChangeSeen();

        // The cycle start must not have been forgotten, or the change could never end.
        Assert.True(barrier.Update("Idle", toolChangeLineAcked: true));
    }

    [Fact]
    public void AResetOutOfAHalfFinishedChange_ClearsIt()
    {
        // Alarm is how a soft reset comes back. JobViewModel resets the barrier there, so
        // the next Idle cannot latch the UI into a change that no longer exists.
        var barrier = Manual();
        barrier.Reset();

        Assert.False(barrier.IsUp);
        Assert.False(barrier.Update("Idle", toolChangeLineAcked: true));
    }
}
