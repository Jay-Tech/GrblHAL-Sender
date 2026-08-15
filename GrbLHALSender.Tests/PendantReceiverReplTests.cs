using GrbLHALSender.Pendant;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for spotting a receiver that has fallen back to its MicroPython prompt.
///
/// This happened at the machine after minutes of working. The receiver's script
/// left its loop - it had no exception guard - and MicroPython's fallback is the
/// REPL, which sits on the same serial port the sender is writing to. Status
/// frames then went to an interpreter, which echoed each one behind its prompt
/// and evaluated it, failing on `false`: JSON spells it lower case and Python
/// does not. Thirty unreadable lines a second came back, all reported as
/// malformed JSON, and the board could not recover without being reset.
///
/// The strings below are a capture from a real board held at its prompt and fed
/// an actual status frame, not an approximation of the log.
/// </summary>
public class PendantReceiverReplTests
{
    [Theory]
    [InlineData(">>>")]
    [InlineData(">>> {\"t\":\"status\",\"state\":\"Idle\",\"wpos\":[408.195,374.096,37.02]")]
    [InlineData("Traceback (most recent call last):")]
    [InlineData("File \"<stdin>\", line 1, in <module>")]
    public void ThePromptAndItsTracebackAreRecognised(string line)
    {
        Assert.True(PendantService.LooksLikeRepl(line));
    }

    [Fact]
    public void EveryCycleCarriesAtLeastOneRecognisableLine()
    {
        // What one round trip actually puts on the wire. It only takes one of
        // these to fire for the pendant to be stood down, which is what stops
        // the sender feeding the interpreter - so the fact that the bare
        // NameError is not matched costs nothing.
        string[] cycle =
        [
            ">>> {\"t\":\"status\",\"state\":\"Idle\",\"wpos\":[408.195,374.096,37.02]}",
            "Traceback (most recent call last):",
            "File \"<stdin>\", line 1, in <module>",
            "NameError: name 'false' isn't defined",
        ];

        Assert.Equal(3, System.Array.FindAll(cycle, PendantService.LooksLikeRepl).Length);
    }

    [Theory]
    [InlineData("{\"t\":\"ping\",\"seq\":42}")]
    [InlineData("{\"t\":\"hello\",\"dev\":\"pico2w-pendant\",\"ver\":1}")]
    [InlineData("{\"t\":\"rx_note\",\"msg\":\"receiver up, this board is 68:EE:8F:50:B2:84\"}")]
    [InlineData("")]
    public void RealTrafficIsNotMistakenForIt(string line)
    {
        // A false positive here stands a working pendant down, so the markers
        // are deliberately anchored at the start of the line rather than
        // searched for anywhere in it.
        Assert.False(PendantService.LooksLikeRepl(line));
    }

    [Fact]
    public void AMessageMerelyMentioningATracebackIsNotThePrompt()
    {
        Assert.False(PendantService.LooksLikeRepl(
            "{\"t\":\"rx_note\",\"msg\":\"Traceback (most recent call last)\"}"));
    }
}
