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

    [Fact]
    public void Ping_ClaimsAnIdleMachine()
    {
        // The regression this rule was widened for. A pendant paired before the
        // sender started sends no further hello, so its ping - every three
        // seconds while its queue is empty - has to be enough to be noticed.
        // Without this the pendant is invisible until someone restarts it, and
        // nothing on either screen says why.
        Assert.True(PendantService.ShouldAdoptSerial("ping", false, false));
    }

    [Fact]
    public void Ping_LeavesABusyMachineAlone()
    {
        // Only a hello may take the machine from whoever holds it. A ping from
        // a pendant that was superseded must not claim it back, or two
        // handhelds trade an axis between them.
        Assert.False(PendantService.ShouldAdoptSerial("ping", false, true));
    }

    [Theory]
    [InlineData("jog")]
    [InlineData("btn")]
    [InlineData("zero")]
    [InlineData("probe")]
    [InlineData("mode")]
    [InlineData("jog_cancel")]
    public void AnInstruction_NeverClaimsTheMachine(string type)
    {
        // The message that adopts is also the first message acted on. Letting a
        // jog adopt makes the act of noticing a pendant into a movement of the
        // machine - and the jog that does it is the wheel being knocked while
        // the handheld is picked up, which is how anyone lifts a thing with a
        // wheel on it. This was found the hard way: an encoder bumped on pickup
        // jumped the machine the moment the sender started.
        //
        // btn, zero and probe are barred by the same argument. Cycle start, a
        // rewritten datum and a probing cycle are all worse ways to discover
        // that a pendant exists.
        Assert.False(PendantService.ShouldAdoptSerial(type, false, false));
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

    [Fact]
    public void AJogIsStillActedOnOnceThePendantIsDriving()
    {
        // The bar is on jogs *claiming* the machine, not on jogs working. Once a
        // hello or a ping has adopted the channel, everything it sends is live -
        // which is the whole point of the pendant.
        Assert.False(PendantService.ShouldAdoptSerial("jog", true, true));
        Assert.True(PendantService.ShouldAdoptSerial("ping", false, false));
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
