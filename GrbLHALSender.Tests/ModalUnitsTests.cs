using GrbLHALSender.Communication;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for reading the parser's modal unit out of the <c>$G</c> report.
/// <para>
/// This is separate from <c>$13</c> and easy to conflate: <c>$13</c> decides the units the
/// controller <em>reports</em> in, while G20/G21 decides how it <em>interprets</em> the
/// numbers we send. A probe sequence sets G20/G21 to match the values in the UI, and that
/// is modal — it outlives the sequence. Left changed, a metric machine sitting in G20 reads
/// the next millimetre coordinate as inches and overshoots by 25.4, which on a G53 rapid
/// means driving into the endstops.
/// </para>
/// </summary>
public class ModalUnitsTests
{
    [Fact]
    public void ParseModalUnits_FindsMetric()
    {
        // As grblHAL reports it: [GC:G0 G54 G17 G21 G90 G94 M5 M9 T0 F0 S0]
        Assert.Equal("G21",
            CommunicationManager.ParseModalUnits("G0 G54 G17 G21 G90 G94 M5 M9 T0 F0 S0"));
    }

    [Fact]
    public void ParseModalUnits_FindsImperial()
    {
        Assert.Equal("G20",
            CommunicationManager.ParseModalUnits("G0 G54 G17 G20 G90 G94 M5 M9 T0 F0 S0"));
    }

    [Theory]
    [InlineData("G21")]
    [InlineData("G90 G21")]
    [InlineData("G21 G90")]
    public void ParseModalUnits_FindsItAnywhereInTheList(string words)
    {
        Assert.Equal("G21", CommunicationManager.ParseModalUnits(words));
    }

    [Theory]
    [InlineData("G0 G54 G17 G90 G94 M5 M9")]  // no unit word at all
    [InlineData("")]
    [InlineData("   ")]
    public void ParseModalUnits_ReturnsNullWhenAbsent(string words)
    {
        // Null means "do not guess". Guessing wrong here restores the parser to the wrong
        // unit, which is the failure this whole mechanism exists to prevent.
        Assert.Null(CommunicationManager.ParseModalUnits(words));
    }

    [Theory]
    [InlineData("G200")]
    [InlineData("G210")]
    [InlineData("G2")]
    public void ParseModalUnits_DoesNotMatchOnASubstring(string words)
    {
        Assert.Null(CommunicationManager.ParseModalUnits(words));
    }

    [Fact]
    public void ParseModalUnits_IsCaseInsensitive()
    {
        Assert.Equal("G20", CommunicationManager.ParseModalUnits("g0 g54 g20 g90"));
    }
}
