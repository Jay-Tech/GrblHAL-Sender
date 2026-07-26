using System.Collections.Generic;

namespace GrbLHALSender.Communication;

/// <summary>
/// Tracks what the controller is still holding while a job streams, for grblHAL's
/// character-counting protocol.
/// <para>
/// Every command written occupies room in the controller's serial RX buffer until it
/// answers "ok", and it answers in the order the commands arrived. So this keeps one
/// FIFO entry per command written — <em>including commands the streamer did not
/// send</em>, such as a jog during a tool change or an aux output button — and credits
/// each "ok" to the entry at the head.
/// </para>
/// <para>
/// Recording foreign commands is the whole point. When only job lines were recorded,
/// their "ok" was credited to a job line that had not finished: the file index walked
/// forward on its own (visibly climbing during a tool change, and reaching end of file
/// while jogging), and the freed bytes made the streamer believe the controller had
/// room it did not have, overflowing the RX buffer after a resume.
/// </para>
/// </summary>
internal sealed class StreamAccounting
{
    internal enum AckKind
    {
        /// <summary>An "ok" with nothing recorded to credit it to; ignore it.</summary>
        Unrecorded,

        /// <summary>A line of the job file completed.</summary>
        JobLine,

        /// <summary>A command from elsewhere completed; it only frees buffer room.</summary>
        Foreign
    }

    private readonly record struct InFlight(int Bytes, bool IsJobLine);

    private readonly Queue<InFlight> _inFlight = new();
    private readonly object _lock = new();

    private int _bufferUsed;
    private int _ackPending;
    private int _ackedJobLines;

    /// <summary>Bytes the streamer is allowed to have outstanding.</summary>
    public int Capacity { get; set; }

    /// <summary>Bytes written and not yet acknowledged, job lines and others alike.</summary>
    public int BufferUsed
    {
        get { lock (_lock) return _bufferUsed; }
    }

    /// <summary>Job lines written and not yet acknowledged.</summary>
    public int AckPending
    {
        get { lock (_lock) return _ackPending; }
    }

    /// <summary>Job lines acknowledged so far — the file's true progress.</summary>
    public int AckedJobLines
    {
        get { lock (_lock) return _ackedJobLines; }
    }

    /// <summary>Byte cost of a line: its text plus the \r that WriteCommand appends.</summary>
    public static int CostOf(string command) => command.Length + 1;

    public void Reset()
    {
        lock (_lock)
        {
            _inFlight.Clear();
            _bufferUsed = 0;
            _ackPending = 0;
            _ackedJobLines = 0;
        }
    }

    /// <summary>
    /// Whether another line of this size fits. Foreign commands consume the same
    /// budget, so a burst of them correctly stalls the streamer until they are acked.
    /// </summary>
    public bool HasRoomFor(int bytes)
    {
        lock (_lock) return _bufferUsed + bytes <= Capacity;
    }

    /// <summary>
    /// Records a command that has just been written. Call order must match the order
    /// the commands reached the wire — <see cref="CommunicationManager"/> serializes
    /// its write and its notification to guarantee that.
    /// </summary>
    public void RecordSent(int bytes, bool isJobLine)
    {
        lock (_lock)
        {
            _inFlight.Enqueue(new InFlight(bytes, isJobLine));
            _bufferUsed += bytes;
            if (isJobLine) _ackPending++;
        }
    }

    /// <summary>
    /// Credits one "ok" to the oldest outstanding command and reports what it was, so
    /// the caller only advances file progress for the job's own lines.
    /// </summary>
    public AckKind Ack()
    {
        lock (_lock)
        {
            if (_inFlight.Count == 0) return AckKind.Unrecorded;

            var entry = _inFlight.Dequeue();
            _bufferUsed -= entry.Bytes;
            if (_bufferUsed < 0) _bufferUsed = 0;

            if (!entry.IsJobLine) return AckKind.Foreign;

            if (_ackPending > 0) _ackPending--;
            _ackedJobLines++;
            return AckKind.JobLine;
        }
    }
}
