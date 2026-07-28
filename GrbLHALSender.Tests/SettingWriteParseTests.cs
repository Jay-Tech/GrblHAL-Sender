using GrbLHALSender.Communication;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for spotting a setting write in an outgoing command.
/// <para>
/// Settings are read once, at connect. A value changed while running never reached the
/// app's model, so the settings grid showed the new number while everything derived from
/// it still held the connect-time one — the two only reconciled on reconnect. That is how
/// a $341 change mid-session silently removed the Touch Off button and read as a
/// regression. Watching the write is what keeps the model honest.
/// </para>
/// <para>
/// The risk runs the other way too: a false positive here rewrites a setting off the back
/// of a command that was never one. <c>$J=</c> is the dangerous shape — a jog, sent
/// constantly, that looks exactly like a setting write.
/// </para>
/// </summary>
public class SettingWriteParseTests
{
    [Theory]
    [InlineData("$341=2", 341, "2")]
    [InlineData("$13=1", 13, "1")]
    [InlineData("$110=5000.000", 110, "5000.000")]
    [InlineData("$30=1000", 30, "1000")]
    public void ASettingWrite_IsRecognised(string command, int expectedId, string expectedValue)
    {
        Assert.True(CommunicationManager.TryParseSettingWrite(command, out var id, out var value));
        Assert.Equal(expectedId, id);
        Assert.Equal(expectedValue, value);
    }

    [Fact]
    public void AJogIsNotASettingWrite()
    {
        // $J=G91X10F500 has the exact shape of a setting write and is sent constantly.
        // Treating it as one would invent a setting and re-derive on every jog.
        Assert.False(CommunicationManager.TryParseSettingWrite("$J=G91X10F500", out _, out _));
    }

    [Theory]
    [InlineData("$$")]          // read all settings
    [InlineData("$G")]          // parser state
    [InlineData("$#")]          // offsets
    [InlineData("$I")]
    [InlineData("$TPW")]        // tool probe workpiece
    [InlineData("$TLR")]
    [InlineData("$RST=*")]      // restore defaults — not a numbered setting
    [InlineData("$SED=341")]    // setting description request, not a write
    [InlineData("G0X10Y10")]
    [InlineData("M6T2")]
    [InlineData("")]
    [InlineData("   ")]
    public void EverythingElseIsNot(string command)
    {
        Assert.False(CommunicationManager.TryParseSettingWrite(command, out _, out _));
    }

    [Fact]
    public void ANullCommandIsHandled()
    {
        Assert.False(CommunicationManager.TryParseSettingWrite(null!, out _, out _));
    }

    [Fact]
    public void SurroundingWhitespaceIsIgnored()
    {
        Assert.True(CommunicationManager.TryParseSettingWrite("  $341=2  ", out var id, out var value));
        Assert.Equal(341, id);
        Assert.Equal("2", value);
    }

    [Fact]
    public void AValueIsRequired()
    {
        // "$341=" clears nothing meaningful here; without a value there is nothing to record.
        Assert.False(CommunicationManager.TryParseSettingWrite("$341=", out _, out _));
    }

    [Fact]
    public void TheIdMustBeNumeric()
    {
        Assert.False(CommunicationManager.TryParseSettingWrite("$ABC=1", out _, out _));
    }

    [Fact]
    public void AValueContainingEqualsKeepsTheRest()
    {
        // Split on the first '=' only, so a value is never truncated at a second one.
        Assert.True(CommunicationManager.TryParseSettingWrite("$70=a=b", out var id, out var value));
        Assert.Equal(70, id);
        Assert.Equal("a=b", value);
    }
}
