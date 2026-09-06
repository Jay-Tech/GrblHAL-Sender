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
    /// Serial port of the ESP-NOW receiver board, e.g. "COM5" or "/dev/ttyUSB0".
    /// Empty means no receiver is fitted and only the network transport runs.
    ///
    /// The two transports are independent: naming a port here does not disable
    /// the listener, and a pendant may arrive either way.
    ///
    /// Never scanned for, deliberately. A grblHAL controller and a receiver board
    /// both enumerate as anonymous USB serial devices and cannot be told apart by
    /// name, and pendant JSON written at a controller is at best noise in its
    /// console.
    /// </summary>
    public string SerialPortName { get; set; } = string.Empty;

    /// <summary>
    /// Baud rate for the receiver board. Only meaningful over a real UART; a USB
    /// CDC device ignores it entirely.
    /// </summary>
    public int SerialBaudRate { get; set; } = 115200;

    /// <summary>
    /// Feed rate for pendant jog commands, mm/min. Falls back to the
    /// application's JogRate when zero.
    /// </summary>
    public double JogFeedRate { get; set; } = 0;

    /// <summary>
    /// Ceiling on the feed rate the pendant may request, mm/min. The pendant
    /// asks for a feed matching how fast the wheel is turned; this bounds it to
    /// what the machine can actually deliver. Zero means no ceiling.
    ///
    /// The pendant already limits itself twice, per step size and per axis, and
    /// the most it can ever ask for is 12000 - the cap on its coarsest step. So
    /// this is a guard against a corrupted request rather than the working
    /// limit, and anything below 12000 silently discards feed the operator
    /// asked for. The previous default of 5000 clamped both coarse steps, which
    /// is felt as the wheel going heavy near the top of its range rather than
    /// reported as anything.
    /// </summary>
    public double MaxJogFeedRate { get; set; } = 12000;

    /// <summary>
    /// Largest distance a single dispatch may command, in millimetres. Movement
    /// beyond this is clamped, not discarded.
    ///
    /// The guard bounds a corrupted detent count. Size it against what a
    /// dispatch interval can legitimately carry: the pendant ticks every 20 ms
    /// and at its 12000 mm/min ceiling that window is 4 mm, so 50 mm leaves an
    /// order of magnitude of headroom. It was sized when the tick was longer;
    /// tightening it would bound a bad message more closely, at the cost of
    /// clamping a legitimate one if the tick ever grows again.
    /// </summary>
    public double MaxJogDistanceMm { get; set; } = 50.0;

    /// <summary>
    /// Minimum interval between jog commands sent to the controller, in
    /// milliseconds. Pendant movement arriving faster than this is summed and
    /// sent as one longer move; movement arriving slower is sent as soon as it
    /// arrives.
    ///
    /// Keep it below the pendant's own tick, which is now 20 ms. Set equal to
    /// it, the two free-run against each other and beat - some windows carry
    /// two messages and some carry none, and an empty window is a gap the
    /// machine decelerates into. Set above it, messages are merged back into
    /// the long blocks the short tick exists to avoid: a merged block halves
    /// the count the planner can chain, and chaining is what holds a feed.
    ///
    /// This is backpressure, and it is the only place that can apply it: the
    /// pendant cannot see the controller's planner. Without it, jog blocks
    /// arrive faster than grblHAL can parse and execute, its buffer fills, and
    /// the send path blocks - which stalls status, strands the machine behind
    /// the operator's hand, and eventually drops the pendant connection.
    /// </summary>
    public int JogDispatchIntervalMs { get; set; } = 10;

    /// <summary>
    /// How often machine status is pushed to the pendant, in milliseconds.
    /// </summary>
    public int StatusIntervalMs { get; set; } = 100;

    /// <summary>
    /// Stand the pendant down if nothing arrives from it for this long. The
    /// pendant pings every few seconds, so silence means it has gone away
    /// without saying so - a case TCP alone can take minutes to notice, and one
    /// a serial receiver cannot notice at all, since its port stays open whether
    /// or not a handheld is switched on.
    ///
    /// Zero disables the watchdog.
    /// </summary>
    public int ClientTimeoutSeconds { get; set; } = 15;
    /// <summary>
    /// Modem control lines to assert when opening the receiver's port.
    /// </summary>
    /// <remarks>
    /// Configuration rather than a constant because what these do depends
    /// entirely on the board on the other end, and getting it wrong is not a
    /// subtle failure - it either resets the receiver or silences it.
    ///
    /// On a board behind a USB-UART bridge, DTR and RTS are not flow control at
    /// all: they are wired to EN and IO0 through the auto-reset circuit, and
    /// driving them on open resets the chip or drops it into its bootloader.
    /// Clearing both is the safe state there, which is what Serial.cs does for
    /// the controller port.
    ///
    /// An ESP32-S3 has native USB and no such circuit, so the same reasoning
    /// does not carry over. Reset can still be driven over these lines - that
    /// is how esptool does it - but a firmware using TinyUSB CDC may also read
    /// DTR as "a terminal is attached" and go quiet without it. The two failure
    /// modes want opposite settings, so the board decides.
    ///
    /// Defaults preserve long-standing behaviour. Change them only against an
    /// observed symptom: a receiver that wedges when the sender starts, or one
    /// that opens cleanly and then never says anything.
    /// </remarks>
    public bool SerialDtrEnable { get; set; } = true;

    /// <summary>See <see cref="SerialDtrEnable"/>.</summary>
    public bool SerialRtsEnable { get; set; } = false;

    public bool EchoJogsToConsole { get; set; } = false;

    /// <summary>
    /// Allow the pendant to zero a work axis. Off by default: zeroing rewrites
    /// the work offset, and whether that belongs on a handheld is the
    /// operator's call rather than a default.
    /// </summary>
    public bool AllowZeroAxis { get; set; } = false;
}
