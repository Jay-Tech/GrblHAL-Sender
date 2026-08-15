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
    /// Smooth the feed rate commanded to the controller instead of passing the
    /// pendant's instantaneous figure straight through.
    ///
    /// The pendant derives feed from detents per tick, and at a slow hand that
    /// estimate has almost no resolution: at 0.1 mm per detent and a 20 ms tick,
    /// one detent is about 300 mm/min and two is about 600, so the number
    /// quantises into large steps. Passed through, consecutive blocks then carry
    /// feeds differing several-fold, and grblHAL plans the junction between two
    /// blocks from the lower of them - so it decelerates hard at every one.
    /// Measured on a slow jog: feed 60-551 mm/min across a burst, with the
    /// planner never holding more than 11 blocks of 128.
    ///
    /// Smoothing changes only the rate, never the distance. Each block still
    /// carries exactly the movement the wheel produced, so the axis finishes in
    /// the same place either way - what changes is that neighbouring blocks
    /// share a feed the planner can chain instead of stepping between them.
    ///
    /// Turn it off to compare: the jog dispatch line reports the commanded feed
    /// range beside the range the pendant actually asked for, so the effect is
    /// visible in the console rather than only in the hand.
    /// </summary>
    public bool SmoothJogFeed { get; set; } = true;

    /// <summary>
    /// How strongly each new feed figure pulls the smoothed value, 0 to 1.
    /// Around a quarter settles in roughly four messages - near a tenth of a
    /// second at the pendant's tick, which is short enough to follow a real
    /// change of hand speed and long enough to lose the per-tick quantisation.
    /// </summary>
    public double JogFeedSmoothing { get; set; } = 0.25;

    /// <summary>
    /// Never command a feed faster than movement is actually arriving.
    ///
    /// The pendant derives feed from how fast the wheel is turning, but it does
    /// not deliver everything the wheel produces - a third of the detents were
    /// being dropped in its own queue on a fast jog. So the controller is told
    /// to travel at a rate for distance that never comes: it runs through what
    /// did arrive, empties the planner and waits, thirty times a second. Seen as
    /// F600/act0 with the planner reporting every one of its 128 blocks free.
    ///
    /// This end can measure the honest figure without asking anyone: the
    /// distance accumulated since the last dispatch, over the time it took to
    /// accumulate. That is the rate motion is genuinely arriving at, and
    /// commanding above it cannot make the machine go faster - the distance is
    /// what it is - it can only make it finish early and stop.
    ///
    /// So the commanded feed is capped there. Average speed is unchanged, since
    /// the same distance is covered in the same time; what changes is that it is
    /// covered continuously instead of in sprints separated by stalls.
    ///
    /// Only a ceiling. A slower feed than the pendant asked for is never raised,
    /// and the distance in every block is untouched, so the axis still finishes
    /// exactly where the wheel sent it.
    /// </summary>
    public bool MatchFeedToArrivalRate { get; set; } = true;

    /// <summary>
    /// Echo every pendant jog command to the console.
    ///
    /// Off by default. It is useful for diagnosing pendant motion, but the
    /// pendant ticks every 20 ms, so jogs arrive up to fifty times a second:
    /// the console reaches its line cap within
    /// seconds of a traverse, and from then on each status tick pays repeated
    /// O(n) removals with a UI notification each. That load lands on the same
    /// thread the interface renders from, which is felt as jerk partway
    /// through a long move rather than at the start.
    /// </summary>
    public bool EchoJogsToConsole { get; set; } = false;

    /// <summary>
    /// Allow the pendant to zero a work axis. Off by default: zeroing rewrites
    /// the work offset, and whether that belongs on a handheld is the
    /// operator's call rather than a default.
    /// </summary>
    public bool AllowZeroAxis { get; set; } = false;
}
