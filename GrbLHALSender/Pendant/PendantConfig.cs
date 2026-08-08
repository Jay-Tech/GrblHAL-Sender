namespace GrbLHALSender.Pendant;

public class PendantConfig
{
    /// <summary>
    /// Enable the pendant listener. Requires app restart to take effect.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// TCP port the pendant connects to (default: 8422).
    /// </summary>
    public int Port { get; set; } = 8422;

    /// <summary>
    /// Bind address. Use "0.0.0.0" for LAN access, "127.0.0.1" for localhost only.
    /// </summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>
    /// Feed rate for pendant jog commands, mm/min. Falls back to the
    /// application's JogRate when zero.
    /// </summary>
    public double JogFeedRate { get; set; } = 0;

    /// <summary>
    /// Ceiling on the feed rate the pendant may request, mm/min. The pendant
    /// asks for a feed matching how fast the wheel is turned; this bounds it to
    /// what the machine can actually deliver. Zero means no ceiling.
    /// </summary>
    public double MaxJogFeedRate { get; set; } = 5000;

    /// <summary>
    /// Largest distance a single pendant message may command, in millimetres.
    /// A jog message carries a detent count and a step size, so a corrupted or
    /// malformed count would otherwise command an arbitrarily long move. At the
    /// coarsest step a fast spin produces roughly 10 mm per message, so this
    /// leaves headroom while still bounding the worst case.
    /// </summary>
    public double MaxJogDistanceMm { get; set; } = 25.0;

    /// <summary>
    /// Minimum interval between jog commands sent to the controller, in
    /// milliseconds. Pendant movement arriving faster than this is summed and
    /// sent as one longer move.
    ///
    /// This is backpressure, and it is the only place that can apply it: the
    /// pendant cannot see the controller's planner. Without it, jog blocks
    /// arrive faster than grblHAL can parse and execute, its buffer fills, and
    /// the send path blocks - which stalls status, strands the machine behind
    /// the operator's hand, and eventually drops the pendant connection.
    /// </summary>
    public int JogDispatchIntervalMs { get; set; } = 100;

    /// <summary>
    /// How often machine status is pushed to the pendant, in milliseconds.
    /// </summary>
    public int StatusIntervalMs { get; set; } = 100;

    /// <summary>
    /// Drop the connection if nothing arrives from the pendant for this long.
    /// The pendant pings every few seconds, so silence means it has gone away
    /// without closing - a case TCP alone can take minutes to notice.
    /// </summary>
    public int ClientTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Allow the pendant to zero a work axis. Off by default: zeroing rewrites
    /// the work offset, and whether that belongs on a handheld is the
    /// operator's call rather than a default.
    /// </summary>
    public bool AllowZeroAxis { get; set; } = false;
}
