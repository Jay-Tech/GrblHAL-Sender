namespace GrbLHALSender.Pendant;

/// <summary>
/// Decides which pendant is driving the machine. One at a time, newest wins,
/// across every transport.
/// </summary>
/// <remarks>
/// The rule is not about arbitrating between two operators - it is about one
/// operator whose pendant has appeared twice. Over WiFi the first connection is
/// usually a stale half-open socket left by a lost link, which TCP can take
/// minutes to give up on; over ESP-NOW it is a handheld that has just been
/// switched on while the WiFi one is still nominally connected. Refusing the
/// newcomer in either case locks the operator out of the machine while a dead
/// connection times out.
///
/// This lives apart from the service because it is the one piece of the pendant
/// path with no hardware in it, and because both of the mistakes it exists to
/// prevent are invisible until they happen on a real machine.
/// </remarks>
internal sealed class PendantArbiter
{
    private readonly object _lock = new();
    private IPendantChannel? _active;

    /// <summary>The pendant currently driving the machine, if any.</summary>
    public IPendantChannel? Active
    {
        get { lock (_lock) return _active; }
    }

    /// <summary>Whether this channel is the one driving the machine.</summary>
    public bool IsActive(IPendantChannel channel)
    {
        lock (_lock) return ReferenceEquals(_active, channel);
    }

    /// <summary>
    /// Makes this channel the active one and returns whatever it displaced, for
    /// the caller to close and report. Null if nothing was active.
    /// </summary>
    public IPendantChannel? Adopt(IPendantChannel channel)
    {
        lock (_lock)
        {
            var previous = ReferenceEquals(_active, channel) ? null : _active;
            _active = channel;
            return previous;
        }
    }

    /// <summary>
    /// Stands this channel down, but only if it is still the active one. False
    /// means it had already been superseded and the caller must do nothing.
    /// </summary>
    /// <remarks>
    /// The identity check is the entire point of this method. A superseded
    /// session finds out it is over only when its own read fails, which happens
    /// after the replacement is established - so tearing down unconditionally
    /// here drops the connection that had just replaced it. From the shop floor
    /// that looks like a pendant reconnecting and immediately dying again,
    /// repeatedly, with no error anywhere.
    /// </remarks>
    public bool Retire(IPendantChannel channel)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_active, channel)) return false;
            _active = null;
            return true;
        }
    }

    /// <summary>
    /// Stands down whatever is active and returns it, for shutdown.
    /// </summary>
    public IPendantChannel? RetireActive()
    {
        lock (_lock)
        {
            var previous = _active;
            _active = null;
            return previous;
        }
    }
}
