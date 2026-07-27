namespace GrbLHALSender.Communication;

/// <summary>
/// Decides when streaming may continue after a tool change.
/// <para>
/// No acknowledgement marks the end of a tool change, which is what makes this subtle.
/// grblHAL's sequence (tool_change.c) is:
/// </para>
/// <list type="number">
/// <item>M6 sets a pending flag and returns — so the M6 line is answered "ok" while the
/// change is only just beginning.</item>
/// <item>0xA3 installs a trap for the next cycle start; it clears nothing itself.</item>
/// <item>Cycle start runs a restore move.</item>
/// <item>The pending flag is cleared when that move finishes.</item>
/// </list>
/// <para>
/// Anything sent before step 4 is refused with error:40, "command not allowed while a
/// tool change is pending". So the earliest safe moment is the machine reporting Idle
/// after a cycle start — not the M6's own acknowledgement, and not the first Run report,
/// which is the restore move still in progress.
/// </para>
/// <para>
/// $341=4 (ignore M6) never raises Tool state. There the M6's acknowledgement really is
/// the whole story, so that case lifts on the ack instead and needs no configuration.
/// </para>
/// </summary>
internal sealed class ToolChangeBarrier
{
    /// <summary>True while nothing may be streamed.</summary>
    public bool IsUp { get; private set; }

    private bool _sawToolState;
    private bool _cycleStarted;

    /// <summary>Called when a line containing M6 has been sent.</summary>
    public void ToolChangeSent()
    {
        IsUp = true;
        _sawToolState = false;
        _cycleStarted = false;
    }

    /// <summary>
    /// Called when the machine entered Tool state with nothing streaming — an M6 typed
    /// into the MDI. There is no sent line to hang the barrier on, so entering the state
    /// is what raises it; from there the change ends exactly as a job's does.
    /// </summary>
    public void ManualToolChangeSeen()
    {
        if (IsUp) return;

        IsUp = true;
        _sawToolState = true;
        _cycleStarted = false;
    }

    /// <summary>
    /// Called when a cycle start has been issued while the barrier is up. That is what
    /// triggers the controller's restore move, so the change cannot end before it.
    /// </summary>
    public void CycleStartIssued()
    {
        if (IsUp) _cycleStarted = true;
    }

    public void Reset()
    {
        IsUp = false;
        _sawToolState = false;
        _cycleStarted = false;
    }

    /// <summary>
    /// Feeds in a status report. Returns true on the transition that lifts the barrier,
    /// so the caller can resume streaming exactly once.
    /// </summary>
    /// <param name="machineState">State as grblHAL reported it: Tool, Run, Idle, Jog, ...</param>
    /// <param name="toolChangeLineAcked">Whether the M6 line itself has been answered.</param>
    public bool Update(string machineState, bool toolChangeLineAcked)
    {
        if (!IsUp) return false;

        if (machineState == "Tool")
        {
            _sawToolState = true;
            return false;
        }

        if (_sawToolState)
        {
            // Jog and Idle both appear while the operator works at the tool change, so
            // neither means anything on its own — only Idle *after* a cycle start does,
            // by which point the restore move has finished and the flag is cleared.
            if (_cycleStarted && machineState == "Idle")
            {
                Reset();
                return true;
            }

            return false;
        }

        // Tool state never appeared: M6 is being ignored, so nothing is pending once it
        // has been answered. Without this the job would sit here forever on $341=4.
        if (toolChangeLineAcked && machineState is "Idle" or "Run")
        {
            Reset();
            return true;
        }

        return false;
    }
}
