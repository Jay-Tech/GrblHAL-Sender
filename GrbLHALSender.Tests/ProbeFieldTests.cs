using GrbLHALSender.Utility;
using GrbLHALSender.ViewModels;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for the state probe entry fields pass through while being edited.
/// <para>
/// Field-confirmed during 3D probe testing: the Diameter box showed an "invalidCast" error
/// and stopped accepting values. The fields were bound straight to doubles, so every
/// keystroke had to convert — and clearing the box to retype it leaves it empty, which does
/// not. The source kept the previous number while the box showed something else, so the
/// value being probed with was not the value on screen.
/// </para>
/// <para>
/// They are bound as text now and parsed at the point of use, which moves the problem
/// rather than removing it: an unparseable field reads as zero instead of failing. Zero is
/// not always harmless — a zero probe diameter shifts a centre result by the width of the
/// probe and writes that as the work offset — so a cycle is refused while any field is not
/// a number.
/// </para>
/// </summary>
public class ProbeFieldTests
{
    [Theory]
    [InlineData("0.25")]
    [InlineData("2")]
    [InlineData("3.175")]
    [InlineData(".5")]      // no leading zero — a real way to type it
    [InlineData("-1")]      // clearance below machine zero
    [InlineData("0")]
    [InlineData("0.")]      // trailing point: .NET parses this, so typing a decimal is safe
    public void AUsableValue_IsAccepted(string text)
    {
        Assert.True(ProbeViewModel.IsNumber(text));
    }

    [Theory]
    [InlineData("")]        // cleared, about to be retyped — the confirmed trigger
    [InlineData("   ")]
    [InlineData("-")]       // first character of a negative
    [InlineData(".")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    public void AnEmptyOrJunkValue_IsRefused(string text)
    {
        Assert.False(ProbeViewModel.IsNumber(text));
    }

    [Fact]
    public void TypingADecimalNeverPassesThroughAnInvalidState()
    {
        // Worth pinning because it is the intuitive explanation and it is wrong: "0." is a
        // legal double in .NET. Reaching for UpdateSourceTrigger to fix "typing a decimal"
        // would have been solving a problem that does not exist.
        foreach (var partial in new[] { "0", "0.", "0.2", "0.25" })
            Assert.True(ProbeViewModel.IsNumber(partial), $"'{partial}' should parse");
    }

    [Fact]
    public void ClearingAFieldDoesPassThroughAnInvalidState()
    {
        // The actual fault. Replacing a value on a touchscreen means emptying the box first.
        Assert.True(ProbeViewModel.IsNumber("2"));
        Assert.False(ProbeViewModel.IsNumber(""));
        Assert.True(ProbeViewModel.IsNumber("0.25"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("abc")]
    public void AnUnparseableFieldReadsAsZero(string text)
    {
        // The reason the guard exists rather than relying on the parse: this is silent.
        Assert.Equal(0, text.StringToDouble());
    }

    [Fact]
    public void ParsingIsInvariant()
    {
        // Dot-decimal regardless of the OS region — the same rule as everywhere else that
        // touches a number in this app.
        Assert.True(ProbeViewModel.IsNumber("1.5"));
        Assert.Equal(1.5, "1.5".StringToDouble());
    }
}
