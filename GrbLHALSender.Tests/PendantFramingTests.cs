using System.Linq;
using System.Text;
using GrbLHALSender.Pendant;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for pulling whole pendant messages out of a byte stream.
///
/// Neither transport gives message boundaries, so a read can land mid-object or
/// carry several at once. It matters more on the serial receiver than it did on
/// the socket: a USB CDC device delivers whatever happened to be in its buffer
/// when the host polled, so a split mid-object is the normal case rather than the
/// unlucky one.
/// </summary>
public class PendantFramingTests
{
    private static string[] Take(StringBuilder pending) =>
        PendantService.TakeLines(pending).ToArray();

    [Fact]
    public void TakeLines_ReturnsSeveralMessagesFromOneRead()
    {
        var pending = new StringBuilder("{\"t\":\"ping\"}\n{\"t\":\"jog\"}\n");

        Assert.Equal(["{\"t\":\"ping\"}", "{\"t\":\"jog\"}"], Take(pending));
        Assert.Equal(0, pending.Length);
    }

    [Fact]
    public void TakeLines_HoldsAPartialMessageBack()
    {
        var pending = new StringBuilder("{\"t\":\"ping\"}\n{\"t\":\"jo");

        Assert.Equal(["{\"t\":\"ping\"}"], Take(pending));

        // The tail is kept, not delivered as malformed JSON and not dropped.
        Assert.Equal("{\"t\":\"jo", pending.ToString());
    }

    [Fact]
    public void TakeLines_JoinsAMessageSplitAcrossReads()
    {
        var pending = new StringBuilder();

        pending.Append("{\"t\":\"jog\",\"ax");
        Assert.Empty(Take(pending));

        pending.Append("is\":\"X\"}\n");
        Assert.Equal(["{\"t\":\"jog\",\"axis\":\"X\"}"], Take(pending));
    }

    [Fact]
    public void TakeLines_SurvivesAByteAtATime()
    {
        // The degenerate case a slow UART actually produces.
        var pending = new StringBuilder();
        var message = "{\"t\":\"hello\",\"dev\":\"mpg\"}";
        string[] delivered = [];

        foreach (var character in message + "\n")
        {
            pending.Append(character);
            var lines = Take(pending);
            if (lines.Length > 0) delivered = lines;
        }

        Assert.Equal([message], delivered);
    }

    [Fact]
    public void TakeLines_AcceptsCarriageReturns()
    {
        // A receiver forwarding through a UART bridge may terminate with CRLF.
        var pending = new StringBuilder("{\"t\":\"ping\"}\r\n{\"t\":\"pong\"}\r\n");

        Assert.Equal(["{\"t\":\"ping\"}", "{\"t\":\"pong\"}"], Take(pending));
    }

    [Fact]
    public void TakeLines_DropsBlankLines()
    {
        // Keepalive newlines and the blank line after a receiver's boot banner.
        var pending = new StringBuilder("\n\n{\"t\":\"ping\"}\n\n");

        Assert.Equal(["{\"t\":\"ping\"}"], Take(pending));
    }

    [Fact]
    public void TakeLines_OnNothingUseful_ReturnsNothingAndKeepsNothing()
    {
        var pending = new StringBuilder("\n");

        Assert.Empty(Take(pending));
        Assert.Equal(0, pending.Length);
    }
}
