using GrbLHALSender.Pendant;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for when a message on the serial transport takes the machine.
///
/// The case that motivates these was found at the machine, not here. Adopting on
/// the hello alone looked obviously right: the receiver's port is open whether or
/// not a pendant is switched on, so the hello is what proves one is there. What
/// that missed is who the hello is addressed to. The pendant sends it when it
/// acquires a link with the receiver, and the receiver is always on - so a
/// pendant already paired when the sender started had sent its hello into a port
/// nobody was reading, and there was no second one coming. It jogged at a sender
/// that ignored it, showing a healthy link on its own screen the whole time.
/// </summary>
public class PendantSerialAdoptionTests
{
    [Fact]
    public void Hello_ClaimsTheMachine()
    {
        Assert.True(PendantService.ShouldAdoptSerial("hello", false, false));
    }

    [Fact]
    public void Hello_ClaimsTheMachineFromANetworkPendant()
    {
        // Newest wins across transports: switching the handheld on takes the
        // machine from a pendant on WiFi.
        Assert.True(PendantService.ShouldAdoptSerial("hello", false, true));
    }

    [Theory]
    [InlineData("ping")]
    [InlineData("jog")]
    [InlineData("btn")]
    [InlineData("mode")]
    public void AnyPendantMessage_ClaimsAnIdleMachine(string type)
    {
        // The regression. A pendant paired before the sender started sends no
        // further hello, so its ping - every three seconds - has to be enough to
        // be noticed. Without this the pendant is invisible until someone
        // restarts it, and nothing on either screen says why.
        Assert.True(PendantService.ShouldAdoptSerial(type, false, false));
    }

    [Theory]
    [InlineData("ping")]
    [InlineData("jog")]
    public void AnyPendantMessage_LeavesABusyMachineAlone(string type)
    {
        // Only a hello may take the machine from whoever holds it. A stray jog
        // from a pendant that was superseded must not claim it back, or two
        // handhelds trade an axis between them.
        Assert.False(PendantService.ShouldAdoptSerial(type, false, true));
    }

    [Theory]
    [InlineData("rx_note")]
    [InlineData("rx_hello")]
    [InlineData("rx_anything")]
    public void ReceiverNotes_NeverClaimTheMachine(string type)
    {
        // The receiver announcing itself at power-on is the single message most
        // likely to arrive with no pendant anywhere near the shop. Treating it
        // as a pendant would light the indicator on an empty bench, which is the
        // exact confusion the hello rule was introduced to prevent - so it is
        // refused here too rather than only by the caller filtering first.
        Assert.False(PendantService.ShouldAdoptSerial(type, false, false));
        Assert.False(PendantService.ShouldAdoptSerial(type, false, true));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("ping")]
    [InlineData("jog")]
    public void TheChannelAlreadyDriving_DoesNotClaimAgain(string type)
    {
        // A pendant re-announcing itself is a no-op, not a re-adoption. Adopting
        // again would close the channel it is adopting, and the second hello of
        // a pair is ordinary: the pendant broadcasts one while unpaired and
        // queues another the moment it pairs.
        Assert.False(PendantService.ShouldAdoptSerial(type, true, true));
    }
}
