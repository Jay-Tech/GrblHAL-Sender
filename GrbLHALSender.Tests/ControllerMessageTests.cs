using GrbLHALSender.Communication;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for pulling the text out of grblHAL's <c>[MSG:...]</c> lines.
/// <para>
/// This is how a g-code macro talks to the operator — grblHAL turns <c>(debug, ...)</c>
/// into one of these. On a machine running a tool change macro, five failure branches end
/// in a message and an M0, so this text is the only explanation for why the machine has
/// stopped. Losing or mangling it leaves the operator staring at a halted machine.
/// </para>
/// </summary>
public class ControllerMessageTests
{
    [Theory]
    [InlineData("[MSG:Info: RGBonToolSelected called]", "Info: RGBonToolSelected called")]
    [InlineData("[MSG:Pgm End]", "Pgm End")]
    [InlineData("[MSG:Caution: Unlocked]", "Caution: Unlocked")]
    [InlineData("[MSG: leading and trailing spaces ]", "leading and trailing spaces")]
    public void ExtractMessage_ReturnsTheText(string line, string expected)
    {
        Assert.Equal(expected, CommunicationManager.ExtractMessage(line));
    }

    [Fact]
    public void ExtractMessage_KeepsTheWholeMacroFailureText()
    {
        // Verbatim from the tool change macro's failure branch.
        const string line =
            "[MSG:Tool 3 failed zone 1. Manually unload tool 3 and unlock to continue.]";

        Assert.Equal(
            "Tool 3 failed zone 1. Manually unload tool 3 and unlock to continue.",
            CommunicationManager.ExtractMessage(line));
    }

    [Fact]
    public void ExtractMessage_KeepsABracketInsideTheText()
    {
        // Trimming every bracket rather than exactly one from each end would eat this.
        Assert.Equal("offset [0] applied",
            CommunicationManager.ExtractMessage("[MSG:offset [0] applied]"));
    }

    [Theory]
    [InlineData("[PRB:0.000,0.000,0.000:1]")]   // a different bracketed report
    [InlineData("[SETTING:...]")]
    [InlineData("[MSG:]")]                      // no text
    [InlineData("[MSG:   ]")]                   // whitespace only
    [InlineData("[MSG:unterminated")]           // no closing bracket
    [InlineData("MSG:not bracketed]")]
    [InlineData("ok")]
    [InlineData("")]
    public void ExtractMessage_ReturnsNullWhenThereIsNoMessage(string line)
    {
        Assert.Null(CommunicationManager.ExtractMessage(line));
    }
}
