using GrbLHALSender.Communication;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for the character-counting bookkeeping behind job streaming.
/// <para>
/// The bug these pin down was confirmed on hardware: during a mid-job tool change the
/// operator jogs the machine, each jog gets its own "ok", and every one of those was
/// credited to a job line that had not finished. The file index climbed on its own in
/// the g-code viewer and reached end of file while jogging, ending the job early — and
/// the phantom freed bytes let the streamer overflow the controller's RX buffer on
/// resume.
/// </para>
/// </summary>
public class StreamAccountingTests
{
    private static StreamAccounting NewAccounting(int capacity = 128) =>
        new() { Capacity = capacity };

    [Fact]
    public void CostOf_CountsTheLineTerminator()
    {
        // WriteCommand appends \r, so the controller holds one more byte than the text.
        Assert.Equal(6, StreamAccounting.CostOf("G0X10"));
    }

    [Fact]
    public void JobLines_AreCreditedAndFreed()
    {
        var acc = NewAccounting();
        acc.RecordSent(10, isJobLine: true);
        acc.RecordSent(10, isJobLine: true);

        Assert.Equal(20, acc.BufferUsed);
        Assert.Equal(2, acc.AckPending);
        Assert.Equal(0, acc.AckedJobLines);

        Assert.Equal(StreamAccounting.AckKind.JobLine, acc.Ack());
        Assert.Equal(10, acc.BufferUsed);
        Assert.Equal(1, acc.AckPending);
        Assert.Equal(1, acc.AckedJobLines);

        Assert.Equal(StreamAccounting.AckKind.JobLine, acc.Ack());
        Assert.Equal(0, acc.BufferUsed);
        Assert.Equal(0, acc.AckPending);
        Assert.Equal(2, acc.AckedJobLines);
    }

    [Fact]
    public void ForeignCommand_FreesBufferButNeverAdvancesFileProgress()
    {
        var acc = NewAccounting();
        acc.RecordSent(10, isJobLine: false);

        Assert.Equal(10, acc.BufferUsed);
        Assert.Equal(0, acc.AckPending); // not a job line, nothing of ours is waiting

        Assert.Equal(StreamAccounting.AckKind.Foreign, acc.Ack());
        Assert.Equal(0, acc.BufferUsed);
        Assert.Equal(0, acc.AckedJobLines); // the whole point
    }

    [Fact]
    public void JoggingDuringAToolChange_DoesNotWalkTheFileIndexForward()
    {
        // The reproduced scenario: 5 job lines are in flight when the machine enters
        // Tool state, then the operator sends 20 commands (jogs, aux button) while the
        // streamer is paused. All 25 acks come back.
        var acc = NewAccounting(capacity: 4096);
        for (var i = 0; i < 5; i++) acc.RecordSent(10, isJobLine: true);
        for (var i = 0; i < 20; i++) acc.RecordSent(25, isJobLine: false);

        var jobAcks = 0;
        var foreignAcks = 0;
        for (var i = 0; i < 25; i++)
        {
            switch (acc.Ack())
            {
                case StreamAccounting.AckKind.JobLine: jobAcks++; break;
                case StreamAccounting.AckKind.Foreign: foreignAcks++; break;
            }
        }

        Assert.Equal(5, jobAcks);
        Assert.Equal(20, foreignAcks);
        // Before the fix this was 25 — which is how a short file reached its end while
        // the operator was only jogging.
        Assert.Equal(5, acc.AckedJobLines);
        Assert.Equal(0, acc.AckPending);
        Assert.Equal(0, acc.BufferUsed);
    }

    [Fact]
    public void Ack_WithNothingOutstanding_IsIgnored()
    {
        var acc = NewAccounting();

        Assert.Equal(StreamAccounting.AckKind.Unrecorded, acc.Ack());
        Assert.Equal(0, acc.AckedJobLines);
        Assert.Equal(0, acc.BufferUsed);
    }

    [Fact]
    public void Ack_CreditsInTheOrderCommandsWereSent()
    {
        // grblHAL answers in arrival order, so the head of the queue is always what the
        // next "ok" belongs to.
        var acc = NewAccounting();
        acc.RecordSent(5, isJobLine: true);
        acc.RecordSent(7, isJobLine: false);
        acc.RecordSent(5, isJobLine: true);

        Assert.Equal(StreamAccounting.AckKind.JobLine, acc.Ack());
        Assert.Equal(StreamAccounting.AckKind.Foreign, acc.Ack());
        Assert.Equal(StreamAccounting.AckKind.JobLine, acc.Ack());
        Assert.Equal(2, acc.AckedJobLines);
    }

    [Fact]
    public void ForeignCommands_ConsumeTheSameBufferBudget()
    {
        // This is what stops the overflow after a resume: the streamer sees the room a
        // burst of manual commands is really taking up, and waits.
        var acc = NewAccounting(capacity: 50);
        Assert.True(acc.HasRoomFor(20));

        acc.RecordSent(40, isJobLine: false);
        Assert.False(acc.HasRoomFor(20));

        Assert.Equal(StreamAccounting.AckKind.Foreign, acc.Ack());
        Assert.True(acc.HasRoomFor(20));
    }

    [Fact]
    public void HasRoomFor_AllowsALineThatExactlyFills()
    {
        var acc = NewAccounting(capacity: 50);
        acc.RecordSent(30, isJobLine: true);

        Assert.True(acc.HasRoomFor(20));
        Assert.False(acc.HasRoomFor(21));
    }

    [Fact]
    public void BufferUsed_NeverGoesNegative()
    {
        var acc = NewAccounting();
        acc.RecordSent(10, isJobLine: true);
        acc.Ack();
        acc.Ack(); // stray extra "ok"

        Assert.Equal(0, acc.BufferUsed);
    }

    [Fact]
    public void Reset_ClearsEverythingForTheNextJob()
    {
        var acc = NewAccounting();
        acc.RecordSent(10, isJobLine: true);
        acc.RecordSent(10, isJobLine: false);

        acc.Reset();

        Assert.Equal(0, acc.BufferUsed);
        Assert.Equal(0, acc.AckPending);
        Assert.Equal(0, acc.AckedJobLines);
        Assert.Equal(StreamAccounting.AckKind.Unrecorded, acc.Ack());
    }

    [Fact]
    public void AckPending_GatesLockStepMode()
    {
        // With StreamBufferAhead off the streamer sends one line at a time and waits on
        // AckPending, which only job lines may raise — a manual command must not look
        // like the outstanding line.
        var acc = NewAccounting();
        acc.RecordSent(10, isJobLine: true);
        Assert.Equal(1, acc.AckPending);

        acc.RecordSent(10, isJobLine: false);
        Assert.Equal(1, acc.AckPending);
    }
}
