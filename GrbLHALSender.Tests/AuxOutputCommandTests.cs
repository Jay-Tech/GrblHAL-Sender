using GrbLHALSender.ViewModels;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for reading aux-output state off an outgoing command. An aux button used to
/// track its pin only when tapped, so a G-code event rule toggling the same pin
/// (M65P0 before homing, M64P0 after) left the button stuck showing the old state.
/// </summary>
public class AuxOutputCommandTests
{
    [Theory]
    [InlineData("M64P0", 0, true)]    // immediate on
    [InlineData("M65P0", 0, false)]   // immediate off
    [InlineData("M62P3", 3, true)]    // synchronized on
    [InlineData("M63P3", 3, false)]   // synchronized off
    [InlineData("M64 P2", 2, true)]   // spaced
    [InlineData("m65 p11", 11, false)] // lower case, two-digit port
    [InlineData("M65P0 (shoe up)", 0, false)] // trailing comment
    public void TryParse_ReadsPortAndState(string command, int expectedPort, bool expectedOn)
    {
        Assert.True(AuxOutputViewModel.TryParseAuxOutputCommand(command, out var port, out var isOn));
        Assert.Equal(expectedPort, port);
        Assert.Equal(expectedOn, isOn);
    }

    [Fact]
    public void TryParse_TakesThePortBelongingToTheMCode()
    {
        // One block, two P words: P1 is the port, P0.2 is the dwell time. Reading the
        // last P would report port 0 and switch the wrong button.
        Assert.True(AuxOutputViewModel.TryParseAuxOutputCommand("M65P1 G4P0.2", out var port, out var isOn));
        Assert.Equal(1, port);
        Assert.False(isOn);
    }

    [Theory]
    [InlineData("M64")]        // no port given
    [InlineData("G4P0.2")]     // dwell only
    [InlineData("M3S1000")]    // spindle
    [InlineData("M8")]         // flood, tracked via the A: state field instead
    [InlineData("M61Q3")]      // set current tool
    [InlineData("G0X64Y65")]   // 64/65 as coordinates
    [InlineData("(M64P0)")]    // commented out
    [InlineData("")]
    public void TryParse_RejectsNonAuxCommands(string command)
    {
        Assert.False(AuxOutputViewModel.TryParseAuxOutputCommand(command, out _, out _));
    }

    [Fact]
    public void TryParse_IgnoresDwellPortAfterAnUnrelatedMCode()
    {
        // M3 is not an aux code, so the following P must not be taken as a port.
        Assert.False(AuxOutputViewModel.TryParseAuxOutputCommand("M3 G4P0.2", out _, out _));
    }
}
