using System.Collections.Generic;
using GrbLHALSender.Pendant;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for the rule deciding which pendant drives the machine: one at a time,
/// newest wins, across both transports.
///
/// Two failures motivate these, and neither is visible without a machine in front
/// of you. A superseded session tearing down unconditionally kills the connection
/// that replaced it, which reads on the shop floor as a pendant that reconnects
/// and immediately dies again. And a channel re-adopting itself - which the serial
/// path can attempt, since a pendant may send a second hello - must not close the
/// connection it is in the middle of adopting.
/// </summary>
public class PendantArbiterTests
{
    private sealed class FakeChannel(string name) : IPendantChannel
    {
        public int CloseCount { get; private set; }
        public bool IsOpen { get; private set; } = true;

        public string Describe() => name;
        public void WriteLine(string message) { }
        public void Close() { CloseCount++; IsOpen = false; }
    }

    [Fact]
    public void Adopt_OnAnEmptyArbiter_DisplacesNothing()
    {
        var arbiter = new PendantArbiter();
        var pendant = new FakeChannel("COM5");

        Assert.Null(arbiter.Adopt(pendant));
        Assert.True(arbiter.IsActive(pendant));
        Assert.Same(pendant, arbiter.Active);
    }

    [Fact]
    public void Adopt_ReturnsThePendantItDisplaced()
    {
        var arbiter = new PendantArbiter();
        var wifi = new FakeChannel("192.168.1.9:51000");
        var serial = new FakeChannel("COM5");

        arbiter.Adopt(wifi);
        var displaced = arbiter.Adopt(serial);

        // Returned rather than closed here: the caller closes it and says so on
        // the console, which the arbiter has no business doing.
        Assert.Same(wifi, displaced);
        Assert.Equal(0, wifi.CloseCount);

        Assert.True(arbiter.IsActive(serial));
        Assert.False(arbiter.IsActive(wifi));
    }

    [Fact]
    public void Adopt_TheChannelAlreadyActive_DoesNotDisplaceItself()
    {
        // A second hello on a serial channel that already holds the machine. If
        // this returned the channel, the caller would close the very connection
        // it had just adopted and the pendant would go dead on a message that
        // should have been a no-op.
        var arbiter = new PendantArbiter();
        var pendant = new FakeChannel("COM5");

        arbiter.Adopt(pendant);

        Assert.Null(arbiter.Adopt(pendant));
        Assert.True(arbiter.IsActive(pendant));
    }

    [Fact]
    public void Retire_TheActivePendant_StandsItDown()
    {
        var arbiter = new PendantArbiter();
        var pendant = new FakeChannel("COM5");
        arbiter.Adopt(pendant);

        Assert.True(arbiter.Retire(pendant));
        Assert.Null(arbiter.Active);
        Assert.False(arbiter.IsActive(pendant));
    }

    [Fact]
    public void Retire_APendantAlreadySuperseded_LeavesItsReplacementAlone()
    {
        // The regression this class exists for. The old session's read fails only
        // after the new one is established, so its teardown arrives last and used
        // to close whatever was active by then - the replacement.
        var arbiter = new PendantArbiter();
        var stale = new FakeChannel("192.168.1.9:51000");
        var fresh = new FakeChannel("192.168.1.9:51001");

        arbiter.Adopt(stale);
        arbiter.Adopt(fresh);

        Assert.False(arbiter.Retire(stale));
        Assert.True(arbiter.IsActive(fresh));
        Assert.Same(fresh, arbiter.Active);
    }

    [Fact]
    public void Retire_Twice_OnlyReportsTheFirst()
    {
        // Both the read loop ending and the watchdog can retire the same channel.
        // The second must not report a disconnection that already happened.
        var arbiter = new PendantArbiter();
        var pendant = new FakeChannel("COM5");
        arbiter.Adopt(pendant);

        Assert.True(arbiter.Retire(pendant));
        Assert.False(arbiter.Retire(pendant));
    }

    [Fact]
    public void RetireActive_ReturnsWhatItStoodDown()
    {
        var arbiter = new PendantArbiter();
        var pendant = new FakeChannel("COM5");
        arbiter.Adopt(pendant);

        Assert.Same(pendant, arbiter.RetireActive());
        Assert.Null(arbiter.Active);
        Assert.Null(arbiter.RetireActive());
    }

    [Fact]
    public void IsActive_IsIdentity_NotDescription()
    {
        // Two sessions on one receiver port describe themselves identically. The
        // pendant that reconnected is not the one that went away, and only
        // reference identity separates them.
        var arbiter = new PendantArbiter();
        var first = new FakeChannel("COM5");
        var second = new FakeChannel("COM5");

        arbiter.Adopt(first);

        Assert.True(arbiter.IsActive(first));
        Assert.False(arbiter.IsActive(second));
    }

    [Fact]
    public void Handover_BetweenTransports_KeepsExactlyOnePendantActive()
    {
        var arbiter = new PendantArbiter();
        var wifi = new FakeChannel("192.168.1.9:51000");
        var serial = new FakeChannel("COM5");
        var closed = new List<IPendantChannel>();

        arbiter.Adopt(wifi);

        // The ESP-NOW pendant is switched on and says hello.
        var displaced = arbiter.Adopt(serial);
        if (displaced != null) { displaced.Close(); closed.Add(displaced); }

        // The WiFi session only now discovers its socket is gone.
        Assert.False(arbiter.Retire(wifi));

        Assert.Same(serial, arbiter.Active);
        Assert.Equal([wifi], closed);
        Assert.True(serial.IsOpen);
    }
}
