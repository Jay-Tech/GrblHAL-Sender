using GrbLHALSender.ViewModels;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for mapping grblHAL's reported state onto the job's state.
/// <para>
/// Reproduced on hardware: during a tool change the operator jogged 12 times and, with no
/// error anywhere, the TOOL banner disappeared and the file index ran to the end while the
/// machine sat still waiting for cycle start. grblHAL reports Jog during the jog and Idle
/// once it finishes; Idle was mapped straight to JobState.Idle, which unlatched the pause
/// and let the streamer empty the rest of the file into the controller, which acknowledged
/// every line as it buffered it.
/// </para>
/// </summary>
public class JobStateMappingTests
{
    [Fact]
    public void IdleDuringAToolChange_KeepsTheJobPaused()
    {
        // The jog finishes and grblHAL reports Idle. The program has not resumed.
        var mapped = JobViewModel.MapGrblState("Idle", JobState.Tool, jobRunning: true);

        Assert.Equal(JobState.Tool, mapped);
    }

    [Fact]
    public void IdleDuringAHold_KeepsTheJobHeld()
    {
        var mapped = JobViewModel.MapGrblState("Idle", JobState.Hold, jobRunning: true);

        Assert.Equal(JobState.Hold, mapped);
    }

    [Fact]
    public void JogDuringAToolChange_KeepsTheJobPaused()
    {
        var mapped = JobViewModel.MapGrblState("Jog", JobState.Tool, jobRunning: true);

        Assert.Equal(JobState.Tool, mapped);
    }

    [Fact]
    public void OnlyRunResumesFromAToolChange()
    {
        // Cycle start is the one thing that continues the program, and it shows up as Run.
        var mapped = JobViewModel.MapGrblState("Run", JobState.Tool, jobRunning: true);

        Assert.Equal(JobState.Running, mapped);
    }

    [Fact]
    public void IdleWithNoJobRunning_IsIdle()
    {
        // The latch must not leave the UI stuck showing a tool change after a job ends.
        var mapped = JobViewModel.MapGrblState("Idle", JobState.Tool, jobRunning: false);

        Assert.Equal(JobState.Idle, mapped);
    }

    [Fact]
    public void IdleWhileRunning_StillGoesIdle()
    {
        // Only Tool and Hold latch; a running job reaching Idle really has stopped.
        var mapped = JobViewModel.MapGrblState("Idle", JobState.Running, jobRunning: true);

        Assert.Equal(JobState.Idle, mapped);
    }

    [Theory]
    [InlineData("Hold", JobState.Hold)]
    [InlineData("Tool", JobState.Tool)]
    [InlineData("Run", JobState.Running)]
    [InlineData("Alarm", JobState.Alarm)]
    [InlineData("Home", JobState.Running)]
    [InlineData("Door", JobState.Hold)]
    public void KnownStates_MapDirectly(string grblState, JobState expected)
    {
        Assert.Equal(expected, JobViewModel.MapGrblState(grblState, JobState.Running, jobRunning: true));
    }

    [Theory]
    [InlineData("Jog")]
    [InlineData("Check")]
    [InlineData("Sleep")]
    [InlineData("")]
    public void UnhandledStates_LeaveTheJobStateAlone(string grblState)
    {
        Assert.Equal(
            JobState.Running,
            JobViewModel.MapGrblState(grblState, JobState.Running, jobRunning: true));
    }

    [Fact]
    public void AToolChangeSurvivesAWholeJogCycle()
    {
        // The full sequence from the hardware repro, twelve times over.
        var state = JobState.Tool;
        for (var i = 0; i < 12; i++)
        {
            state = JobViewModel.MapGrblState("Jog", state, jobRunning: true);
            state = JobViewModel.MapGrblState("Idle", state, jobRunning: true);
            state = JobViewModel.MapGrblState("Tool", state, jobRunning: true);
        }

        Assert.Equal(JobState.Tool, state);
    }
}
