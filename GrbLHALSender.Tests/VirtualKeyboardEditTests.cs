using GrbLHALSender.Utility;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests the text arithmetic behind the virtual keyboard's keys.
/// <para>
/// Reported from the Pi: after a double tap opened the keyboard the field stayed partly
/// highlighted, and typing left the highlight alone and put the character somewhere else. The
/// keys only ever read the caret, so a live selection was ignored rather than replaced — which
/// is not how any other keyboard behaves, and made the insertion point look random.
/// </para>
/// <para>
/// These cover the string maths in isolation; the selection handling itself lives on the
/// TextBox and needs the UI to exercise.
/// </para>
/// </summary>
public class VirtualKeyboardEditTests
{
    // What the view model does to replace a selection: cut the range, then insert at its start.
    private static string TypeOverSelection(string text, int from, int to, string key) =>
        text.Remove(from, to - from).Insert(from, key);

    [Fact]
    public void TypingOverASelectionReplacesIt()
    {
        // "0.25" fully selected, then 5 pressed.
        Assert.Equal("5", TypeOverSelection("0.25", 0, 4, "5"));
    }

    [Fact]
    public void TypingOverAPartialSelectionReplacesOnlyThat()
    {
        // The double tap word-selects, so a partial range is the usual case.
        Assert.Equal("0.5", TypeOverSelection("0.25", 2, 4, "5"));
    }

    [Fact]
    public void AMultiCharacterKeyReplacesASelectionToo()
    {
        // The CNC shortcut keys send more than one character.
        Assert.Equal("G90", TypeOverSelection("G1", 0, 2, "G90"));
    }

    [Fact]
    public void DeletingASelectionLeavesTheRest()
    {
        Assert.Equal("0.", "0.25".Remove(2, 2));
    }

    [Theory]
    [InlineData("0.25", 4, "0.2")]   // caret at the end
    [InlineData("0.25", 2, "025")]   // caret mid-string
    [InlineData("0.25", 1, ".25")]
    public void BackspaceRemovesTheCharacterBeforeTheCaret(string text, int caret, string expected)
    {
        Assert.Equal(expected, text.Remove(caret - 1, 1));
    }

    [Theory]
    [InlineData("0.25", 0, "5", "50.25")]
    [InlineData("0.25", 4, "5", "0.255")]
    [InlineData("0.25", 2, "5", "0.525")]
    public void WithNoSelectionAKeyGoesInAtTheCaret(
        string text, int caret, string key, string expected)
    {
        Assert.Equal(expected, text.Insert(caret, key));
    }

    [Fact]
    public void AProbeFieldSurvivesBeingRetypedFromScratch()
    {
        // The whole point on this dialog: select all, type a new value, and have the field hold
        // exactly that. Empty is a legal intermediate — the fields parse at the point of use.
        var text = TypeOverSelection("0.25", 0, 4, ".");
        text = text.Insert(text.Length, "5");

        Assert.Equal(".5", text);
        Assert.Equal(0.5, text.StringToDouble(), 3);
    }
}
