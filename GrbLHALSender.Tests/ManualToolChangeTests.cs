using GrbLHALSender.ViewModels;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for pressing Start with no job of our own running.
/// <para>
/// Reported on hardware: an <c>M6T2</c> typed into the MDI with no file loaded put the
/// machine into Tool, and then neither touch off nor Start would complete the change.
/// StartJob's file-loaded check returned early before any cycle start could go out, so the
/// button did nothing at all. Hold was already handled here; Tool was not.
/// </para>
/// </summary>
public class ManualToolChangeTests
{
    [Fact]
    public void AToolChangeOutsideAJob_GetsACycleStart()
    {
        // The M6-from-MDI case. Cycle start is what runs the controller's restore move.
        Assert.True(JobViewModel.NeedsBareCycleStart(JobState.Tool, jobRunning: false));
    }

    [Fact]
    public void AHoldOutsideAJob_GetsACycleStart()
    {
        Assert.True(JobViewModel.NeedsBareCycleStart(JobState.Hold, jobRunning: false));
    }

    [Theory]
    [InlineData(JobState.Tool)]
    [InlineData(JobState.Hold)]
    public void APausedJob_ResumesInsteadOfABareCycleStart(JobState state)
    {
        // A running job has a buffer to refill on the way out, so it takes the ResumeJob
        // path rather than a raw cycle start byte.
        Assert.False(JobViewModel.NeedsBareCycleStart(state, jobRunning: true));
    }

    [Theory]
    [InlineData(JobState.Idle)]
    [InlineData(JobState.Alarm)]
    [InlineData(JobState.Running)]
    [InlineData(JobState.ProgramComplete)]
    public void NothingToResume_FallsThroughToStartingTheFile(JobState state)
    {
        // Start must still mean "start the loaded file" everywhere else.
        Assert.False(JobViewModel.NeedsBareCycleStart(state, jobRunning: false));
    }
}
