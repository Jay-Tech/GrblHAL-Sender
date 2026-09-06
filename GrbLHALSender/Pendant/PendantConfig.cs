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
    /// <summary>
    /// Snap the commanded feed to a multiple of this, in mm/min. Zero disables it.
    /// </summary>
    /// <remarks>
    /// The pendant sets its feed from how fast the wheel is being turned, so below
    /// the step's own ceiling every block carries a slightly different F word. A
    /// planner cannot chain blocks that ask for different velocities: it ramps down
    /// and back up at each junction, hundreds of times a second, and that is the
    /// roughness felt at any wheel speed other than flat out.
    ///
    /// Flat out is smooth for exactly one reason - the feed saturates the pendant's
    /// STEP_MAX_FEED and stops varying. Measured at the machine, a burst held at a
    /// constant feed ran the planner down to 103 blocks free where the same wheel
    /// speed with a varying feed never got below 125, which is a queue that never
    /// held more than three blocks to look ahead through.
    ///
    /// Snapping to a grid gives a steady turn a steady F without pinning it to one
    /// speed, so consecutive blocks chain. Coarse enough to span the wobble, fine
    /// enough that the operator still feels the wheel: a few hundred mm/min.
    ///
    /// <para>
    /// A coarse grid locks against MatchFeedToArrivalRate, and the arithmetic is
    /// worth keeping because the symptom looks nothing like the cause. Climbing
    /// from a held value V to the next step needs the ceiling to permit V + Q/2,
    /// and the ceiling is the measured arrival rate - which, once delivery has
    /// settled to match the command, is about V x RateHeadroom. So climbing needs
    /// V x 1.15 >= V + Q/2, or V >= Q x 3.33.
    /// </para>
    /// <para>
    /// At Q = 2000 that is V >= 6667: a 0.5 mm step whose ceiling is 8000 sticks
    /// at 6000 for good, because the arrival rate tops out near 6900 and the next
    /// grid step needs 7000. Measured exactly that way - "feed 300-6000" in every
    /// burst at 0.5 mm while 1 mm, sitting at 8000, cleared the threshold and
    /// reached 10000. It is a feedback trap: the ceiling measures what arrives,
    /// what arrives is bounded by what was commanded, and what was commanded was
    /// bounded by the ceiling. At Q = 500 the threshold is 1667 and it never
    /// appears.
    /// </para>
    /// <para>
    /// So a grid much above a few hundred wants the arrival ceiling switched off,
    /// and hysteresis cannot rescue it - the constraint is between the grid and
    /// the ceiling, not in the hold.
    /// </para>
    /// </remarks>
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

    public double JogFeedQuantumMmPerMin { get; set; } = 0;

    /// <summary>
    /// Scale the grid with the step size, taking JogFeedQuantumMmPerMin as the
    /// value for a 1 mm step. Zero keeps the flat grid.
    /// </summary>
    /// <remarks>
    /// A flat grid is the wrong width at every step but one, because the feed
    /// is turn_rate x step x 60 - so the same wobble of the hand moves the
    /// requested feed twice as far at a 1 mm step as at 0.5 mm. Ten detents a
    /// second either way is 600 mm/min at 1 mm and 300 at 0.5.
    ///
    /// Tuned for the finer step, the grid is then half as wide as it should be
    /// at the coarser one. Measured that way: with 500 flat, a 0.5 mm burst at
    /// half speed changed feed on 3-7% of blocks while a 1 mm burst at the same
    /// fraction of its own top speed changed on 10-11%, and it was the only
    /// outlier left in the run.
    ///
    /// Scaling by the step makes the band a fixed amount of *wheel* rather than
    /// a fixed amount of feed, which is what the hand actually holds steady. A
    /// grid of 500 at 1 mm becomes 250 at 0.5 mm and 50 at 0.1 mm, and each is
    /// the same number of detents per second wide.
    ///
    /// <para>
    /// Turning this on wants JogFeedQuantumMmPerMin doubled, because the value
    /// is now read against a 1 mm step rather than applied flat. A setting of
    /// 500 that was tuned at 0.5 mm becomes 1000: the finer step keeps the 500
    /// it was tuned with, and the coarser one gets the wider band it always
    /// needed. Left at 500 this narrows the fine step instead of widening the
    /// coarse one, which is the wrong direction and would undo the very
    /// behaviour it was tuned for.
    /// </para>
    /// </remarks>
    public bool ScaleJogFeedQuantumByStep { get; set; } = false;

    /// <summary>
    /// How far the request must rise above the held feed before the commanded
    /// one follows, as a multiple of the grid step.
    /// </summary>
    /// <remarks>
    /// Small, because being slower than the hand is felt at once. It also has
    /// to stay small enough that the top of a step's range is reachable: the
    /// last climb to the pendant's ceiling costs this much wheel speed on top
    /// of what the ceiling itself needs, and at a 0.5 mm step every 500 mm/min
    /// is another 17 detents a second.
    /// </remarks>
    public double JogFeedRiseBandSteps { get; set; } = 0.5;

    /// <summary>
    /// How far the request must fall below the held feed before the commanded
    /// one follows, as a multiple of the grid step.
    /// </summary>
    /// <remarks>
    /// Larger than the rise, and this is the number that decides how much the
    /// wheel can ease off while still holding top speed. At one step, holding
    /// 8000 on a 0.5 mm grid of 500 needs the request to stay above 7500, which
    /// is 250 detents a second sustained - reported as "got to keep spinning
    /// fast, not much room to reduce". The same band at a 1 mm step needs only
    /// 125, which is why the two steps feel so different at the top.
    ///
    /// Widening it buys that room, and costs the other way: commanded above
    /// what the hand is actually delivering, each block finishes early and the
    /// machine waits, which is felt as a stumble. With MatchFeedToArrivalRate
    /// off there is nothing else bounding that, so this is the whole of the
    /// trade.
    /// </remarks>
    public double JogFeedFallBandSteps { get; set; } = 1.0;

    /// <summary>
    /// Never command a feed below the slowest the pendant can deliver at the
    /// current step.
    /// </summary>
    /// <remarks>
    /// The pendant's flow control floors at one detent per 20 ms tick - see the
    /// allowed &lt; 1 clamp in jog.py - so it can never deliver slower than
    /// step x 3000 mm/min: 1500 at a 0.5 mm step, 3000 at 1 mm. Command below
    /// that while the wheel turns and the excess has nowhere to go but the
    /// queue, arriving later as travel after the hand has stopped.
    ///
    /// Measured: with the feed capped at 2000 and a 1 mm step, 431 mm arrived
    /// in 7.9 s (3273 mm/min) against 2000 commanded, and the planner filled to
    /// four blocks free - about 124 mm still to run when the hand stopped. The
    /// same session at a 0.5 mm step, whose floor of 1500 sits under the cap,
    /// stayed well behaved throughout.
    ///
    /// Bounded by what the pendant asked for, which is not a detail. Written
    /// first against the measured arrival rate instead, it pushed the feed to
    /// 12000 at a 0.5 mm step whose firmware ceiling is 8000 - that estimate
    /// reads about twice the true rate - and snapping up from a noisy number
    /// changed the commanded feed every few blocks: 106 changes in 207
    /// dispatches against 9 in 143 with the floor off. It made the roughness it
    /// was meant to sit beside measurably worse. The request already carries
    /// STEP_MAX_FEED, and a pendant asking for less than its own floor is being
    /// turned too slowly to reach the clamp at all.
    /// </remarks>
    public bool EnforcePendantDeliveryFloor { get; set; } = false;

    public bool EchoJogsToConsole { get; set; } = false;

    /// <summary>
    /// Allow the pendant to zero a work axis. Off by default: zeroing rewrites
    /// the work offset, and whether that belongs on a handheld is the
    /// operator's call rather than a default.
    /// </summary>
    public bool AllowZeroAxis { get; set; } = false;
}
