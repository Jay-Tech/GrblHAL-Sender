using GrbLHALSender.States;
using GrbLHALSender.ViewModels;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// When a jog from something other than this screen - the wireless pendant, the
/// gamepad, the movement buttons - may reach the controller, and when grblHAL's
/// jog cancel may.
///
/// The rule reads the job's latched state, not the controller's, and that is the
/// correction these tests exist to hold. Jogging changes the controller's state
/// out from under itself: grblHAL reports Jog while the wheel turns and Idle
/// when it stops, and never Tool again until the change ends. A rule keyed on
/// GrblState.Tool therefore grants permission only in the gaps between jogs and
/// withdraws it the instant one starts. Measured on the machine that was a jog
/// that stopped and restarted on its own, "planner free 128-128" across every
/// burst - the queue never once held a block - and 477 detents of wheel
/// producing 149 mm of travel at a 1 mm step.
///
/// MapGrblState holds JobState at Tool through exactly those Jog and Idle
/// reports, which is what makes the latch the right thing to read.
///
/// The cancel is deliberately wider than the jog. Motion that has started must
/// always be stoppable, and tying the two to one permission is a trap, because
/// any state change between starting a jog and cancelling it - and jogging
/// causes one - leaves an axis moving with nothing able to stop it. That was the
/// second symptom: a movement button that would not cancel on release, because
/// by then the state was Jog.
/// </summary>
public class DeviceJogStateTests
{
    // --- ordinary use, no job loaded ---------------------------------------

    [Theory]
    [InlineData(GrblState.Idle)]
    [InlineData(GrblState.Jog)]
    [InlineData(GrblState.Tool)]
    public void WithNoJobTheControllerStateDecides(GrblState grblState)
    {
        Assert.True(MainViewModel.CanJogInState(
            controlsEnabled: true, jobRunning: false, JobState.Idle, grblState));
    }

    [Theory]
    [InlineData(GrblState.Run)]
    [InlineData(GrblState.Alarm)]
    [InlineData(GrblState.Hold)]
    public void WithNoJobTheseStatesStillRefuse(GrblState grblState)
    {
        Assert.False(MainViewModel.CanJogInState(
            controlsEnabled: true, jobRunning: false, JobState.Idle, grblState));
    }

    // --- the tool change ---------------------------------------------------

    [Theory]
    [InlineData(GrblState.Tool)]
    [InlineData(GrblState.Jog)]
    [InlineData(GrblState.Idle)]
    public void AToolChangeAllowsJoggingThroughEveryStateItPassesThrough(GrblState grblState)
    {
        // The regression. Tool is what the controller reports at the M6, Jog is
        // what it reports once the wheel turns, and Idle is what it reports when
        // the wheel stops - and the operator is touching off across all three.
        Assert.True(MainViewModel.CanJogInState(
            controlsEnabled: true, jobRunning: true, JobState.Tool, grblState));
    }

    [Fact]
    public void AToolChangeAllowsTheCancelToo()
    {
        // A pendant going flat mid-touch-off has to leave the axis stopped, and
        // nothing of the job is in the receive buffer to lose: the streamer
        // stopped dead at the M6.
        Assert.True(MainViewModel.CanCancelJogInState(
            controlsEnabled: true, jobRunning: true, JobState.Tool, GrblState.Jog));
    }

    // --- the cut -----------------------------------------------------------

    [Fact]
    public void ACutRefusesJogging()
    {
        Assert.False(MainViewModel.CanJogInState(
            controlsEnabled: true, jobRunning: true, JobState.Running, GrblState.Run));
    }

    [Fact]
    public void TheMidCutIdleBlipRefusesJogging()
    {
        // Mid-cut the controller reads Idle between blocks while the streamer is
        // still counting acks for lines in flight. A hand at the wheel finds this
        // far more readily than a button does: the dispatch loop runs every 10 ms.
        Assert.False(MainViewModel.CanJogInState(
            controlsEnabled: true, jobRunning: true, JobState.Running, GrblState.Idle));
    }

    [Fact]
    public void TheMidCutIdleBlipRefusesTheCancel()
    {
        // The destructive one. 0x85 flushes the receive buffer, and mid-cut that
        // is job lines already written and counted - flushed, they answer with
        // neither "ok" nor "error:N", and the accounting stalls the stream.
        Assert.False(MainViewModel.CanCancelJogInState(
            controlsEnabled: true, jobRunning: true, JobState.Running, GrblState.Idle));
    }

    [Fact]
    public void AHoldRefusesBoth()
    {
        // Unlike a tool change, a hold leaves job lines sitting unparsed in the
        // controller's receive buffer, and a resume expects the machine where it
        // was left.
        Assert.False(MainViewModel.CanJogInState(
            controlsEnabled: true, jobRunning: true, JobState.Hold, GrblState.Hold));
        Assert.False(MainViewModel.CanCancelJogInState(
            controlsEnabled: true, jobRunning: true, JobState.Hold, GrblState.Hold));
    }

    // --- motion that has started must be stoppable -------------------------

    [Fact]
    public void AMachineThatIsJoggingCanAlwaysBeCancelled()
    {
        // Whatever the job bookkeeping says. A controller reporting Jog is not
        // one part way through a cut - jogs are rejected in Run - so the stream
        // cannot be feeding it g-code and the buffer holds nothing to lose.
        foreach (var jobRunning in new[] { true, false })
        foreach (var jobState in new[] { JobState.Idle, JobState.Running, JobState.Tool })
        {
            Assert.True(MainViewModel.CanCancelJogInState(
                controlsEnabled: true, jobRunning, jobState, GrblState.Jog));
        }
    }

    [Fact]
    public void TheCancelIsNeverNarrowerThanTheJog()
    {
        // The trap this pair exists to avoid: a jog that may be started and then
        // not stopped.
        foreach (var jobRunning in new[] { true, false })
        foreach (var jobState in new[] { JobState.Idle, JobState.Running, JobState.Hold, JobState.Tool })
        foreach (GrblState grblState in System.Enum.GetValues(typeof(GrblState)))
        {
            if (MainViewModel.CanJogInState(true, jobRunning, jobState, grblState))
                Assert.True(MainViewModel.CanCancelJogInState(true, jobRunning, jobState, grblState));
        }
    }

    // --- the controls -------------------------------------------------------

    [Theory]
    [InlineData(GrblState.Idle)]
    [InlineData(GrblState.Jog)]
    [InlineData(GrblState.Tool)]
    public void NothingPassesWithoutTheControls(GrblState grblState)
    {
        // ControlsEnabled is Connected && !MpgActive. A hardware MPG holding the
        // controller's input stream is the case worth naming: this sender is not
        // the stream in control, so two devices would be driving one axis.
        Assert.False(MainViewModel.CanJogInState(
            controlsEnabled: false, jobRunning: true, JobState.Tool, grblState));
        Assert.False(MainViewModel.CanCancelJogInState(
            controlsEnabled: false, jobRunning: true, JobState.Tool, grblState));
    }
}
