using GrbLHALSender.Communication;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for when streaming may continue after a tool change.
/// <para>
/// Reproduced on hardware as error:40, "command not allowed while a tool change is
/// pending", on resume. grblHAL answers the M6 line as soon as the change *starts*
/// (tool_change.c sets a pending flag and returns), 0xA3 only installs a trap for the
/// next cycle start, and the flag is cleared at the end of the restore move that cycle
/// start triggers. So neither the M6 acknowledgement nor the first Run report means the
/// change is over — and anything sent before it is refused.
/// </para>
/// </summary>
public class ToolChangeBarrierTests
{
    private static ToolChangeBarrier Sent()
    {
        var barrier = new ToolChangeBarrier();
        barrier.ToolChangeSent();
        return barrier;
    }

    [Fact]
    public void SendingAToolChange_RaisesTheBarrier()
    {
        Assert.True(Sent().IsUp);
    }

    [Fact]
    public void TheM6AcknowledgementAloneDoesNotLiftIt()
    {
        // The ack arrives when the change begins. Lifting on it was the bug.
        var barrier = Sent();

        Assert.False(barrier.Update("Tool", toolChangeLineAcked: true));
        Assert.True(barrier.IsUp);
    }

    [Fact]
    public void JoggingAtTheToolChange_DoesNotLiftIt()
    {
        var barrier = Sent();
        barrier.Update("Tool", toolChangeLineAcked: true);

        for (var i = 0; i < 12; i++)
        {
            Assert.False(barrier.Update("Jog", toolChangeLineAcked: true));
            Assert.False(barrier.Update("Idle", toolChangeLineAcked: true));
        }

        Assert.True(barrier.IsUp);
    }

    [Fact]
    public void IdleWithoutACycleStart_DoesNotLiftIt()
    {
        // The operator has not resumed yet, so the change cannot be over.
        var barrier = Sent();
        barrier.Update("Tool", toolChangeLineAcked: true);

        Assert.False(barrier.Update("Idle", toolChangeLineAcked: true));
        Assert.True(barrier.IsUp);
    }

    [Fact]
    public void RunAfterCycleStart_DoesNotLiftIt()
    {
        // Run is the restore move in progress; the pending flag clears at its end.
        // Resuming here is what produced error:40.
        var barrier = Sent();
        barrier.Update("Tool", toolChangeLineAcked: true);
        barrier.CycleStartIssued();

        Assert.False(barrier.Update("Run", toolChangeLineAcked: true));
        Assert.True(barrier.IsUp);
    }

    [Fact]
    public void IdleAfterCycleStartAndRestore_LiftsItExactlyOnce()
    {
        var barrier = Sent();
        barrier.Update("Tool", toolChangeLineAcked: true);
        barrier.CycleStartIssued();
        barrier.Update("Run", toolChangeLineAcked: true);   // restore move

        Assert.True(barrier.Update("Idle", toolChangeLineAcked: true));
        Assert.False(barrier.IsUp);

        // Later reports must not re-fire the resume.
        Assert.False(barrier.Update("Idle", toolChangeLineAcked: true));
        Assert.False(barrier.Update("Run", toolChangeLineAcked: true));
    }

    [Fact]
    public void CycleStartBeforeToolStateIsStillHonoured()
    {
        // The status report showing Tool can arrive after the operator has resumed.
        var barrier = Sent();
        barrier.CycleStartIssued();
        barrier.Update("Tool", toolChangeLineAcked: true);

        Assert.True(barrier.Update("Idle", toolChangeLineAcked: true));
    }

    [Fact]
    public void IgnoreM6_LiftsOnTheAcknowledgement()
    {
        // $341=4 never raises Tool state. Without this the job would sit here forever.
        var barrier = Sent();

        Assert.False(barrier.Update("Run", toolChangeLineAcked: false));
        Assert.True(barrier.IsUp);

        Assert.True(barrier.Update("Run", toolChangeLineAcked: true));
        Assert.False(barrier.IsUp);
    }

    [Fact]
    public void IgnoreM6_AlsoLiftsWhenTheMachineIsIdle()
    {
        var barrier = Sent();

        Assert.True(barrier.Update("Idle", toolChangeLineAcked: true));
    }

    [Fact]
    public void OnceToolStateIsSeen_TheAcknowledgementPathIsClosed()
    {
        // A real tool change must not fall through the $341=4 shortcut just because the
        // M6 was answered — that answer arrives at the start of every tool change.
        var barrier = Sent();
        barrier.Update("Tool", toolChangeLineAcked: true);

        Assert.False(barrier.Update("Run", toolChangeLineAcked: true));
        Assert.False(barrier.Update("Idle", toolChangeLineAcked: true));
        Assert.True(barrier.IsUp);
    }

    [Fact]
    public void Reset_ClearsEverythingForTheNextJob()
    {
        var barrier = Sent();
        barrier.Update("Tool", toolChangeLineAcked: true);
        barrier.CycleStartIssued();

        barrier.Reset();

        Assert.False(barrier.IsUp);
        Assert.False(barrier.Update("Idle", toolChangeLineAcked: true));
    }

    [Fact]
    public void ASecondToolChangeInTheSameJobBehavesTheSame()
    {
        var barrier = Sent();
        barrier.Update("Tool", true);
        barrier.CycleStartIssued();
        barrier.Update("Run", true);
        Assert.True(barrier.Update("Idle", true));

        barrier.ToolChangeSent();
        Assert.True(barrier.IsUp);
        barrier.Update("Tool", true);
        Assert.False(barrier.Update("Idle", true));   // no cycle start yet
        barrier.CycleStartIssued();
        Assert.True(barrier.Update("Idle", true));
    }
}
