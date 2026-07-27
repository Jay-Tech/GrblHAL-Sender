using GrbLHALSender.Gcode;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for recognising a tool change in a streamed line. The streamer has to stop dead
/// at M6: grblHAL suspends the program there and rejects anything further with error:40,
/// so lines already sitting in its receive buffer past the M6 are discarded rather than
/// queued. Misreading a line either way is costly — a missed M6 loses the rest of the
/// file, and a false positive stalls the stream on an ordinary line.
/// </summary>
public class GcodeWordsTests
{
    [Theory]
    [InlineData("M6")]
    [InlineData("M06")]
    [InlineData("T2M6")]
    [InlineData("T2 M6")]
    [InlineData("m6")]
    [InlineData("M6 T2")]
    [InlineData("M 6")]           // spaced, as some posts emit
    [InlineData("T2M6 (change)")] // trailing comment
    [InlineData("N40 T2 M6")]     // line number
    public void IsToolChange_RecognisesATooolChange(string line)
    {
        Assert.True(GcodeWords.IsToolChange(line));
    }

    [Theory]
    [InlineData("M61Q3")]   // set current tool, not a change
    [InlineData("M60")]
    [InlineData("M6.1")]
    [InlineData("M65P0")]   // aux output off
    [InlineData("M64P0")]
    [InlineData("G4P0.2")]
    [InlineData("X-6")]     // 6 as a coordinate
    [InlineData("G0X6Y6")]
    [InlineData("(T2 M6)")] // commented out
    [InlineData("")]
    [InlineData(null)]
    public void IsToolChange_IgnoresEverythingElse(string? line)
    {
        Assert.False(GcodeWords.IsToolChange(line));
    }

    [Theory]
    [InlineData("M65P0", 'M', 65, true)]
    [InlineData("M65P0", 'P', 0, true)]
    [InlineData("M65P0", 'M', 6, false)]
    [InlineData("G4P0.2", 'G', 4, true)]
    [InlineData("G4P0.2", 'P', 0.2, true)]
    [InlineData("g4p0.2", 'G', 4, true)]
    public void HasWord_ComparesLetterAndValue(string line, char letter, double value, bool expected)
    {
        Assert.Equal(expected, GcodeWords.HasWord(line, letter, value));
    }

    [Fact]
    public void IsToolChange_MatchesTheRealTestFilesToolChangeLine()
    {
        // The line from TestSimGcode.nc that the barrier has to catch.
        Assert.True(GcodeWords.IsToolChange("T2 M6"));
        // ...and the injected neighbours it must not catch.
        Assert.False(GcodeWords.IsToolChange("G4P0.1"));
        Assert.False(GcodeWords.IsToolChange("M65P0"));
        Assert.False(GcodeWords.IsToolChange("M64P0"));
        Assert.False(GcodeWords.IsToolChange("G4P0.2"));
    }
}
