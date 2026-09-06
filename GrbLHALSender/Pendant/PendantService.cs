using Avalonia.Threading;
using GrbLHALSender.Configuration;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using GrbLHALSender.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GrbLHALSender.Pendant;

/// <summary>
/// Listens for a wireless MPG pendant and translates its messages into jog and
/// real-time commands.
///
/// The pendant talks to this application rather than to the controller, for two
/// reasons. grblHAL's telnet server accepts exactly one client and this sender
/// already holds it, so a pendant could not have a session of its own. And on a
/// board wired directly to the PC over Ethernet there is no route from the
/// wireless network to reach it. Going through here also keeps this application
/// the single arbiter of the command queue, which is what GRBL requires however
/// the commands arrive.
///
/// It arrives one of two ways, and nothing below the transport knows which. Over
/// WiFi the pendant opens a TCP session here. Over ESP-NOW it talks to a receiver
/// board plugged into this machine, which presents a serial port and forwards the
/// same bytes - so the radio never becomes this application's problem.
///
/// Protocol is newline-delimited JSON; see the pendant firmware for the message
/// set. Unknown message types are ignored rather than rejected so either end can
/// add messages without breaking the other.
/// </summary>
public class PendantService : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly MachineStateService _machineState;

    private PendantConfig _config = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private Task? _serialTask;
    private Task? _jogTask;
    private Task? _statusTask;

    private readonly PendantArbiter _arbiter = new();

    // When the active pendant was last heard from, for the silence watchdog.
    private long _lastRxTicks;

    // Set after construction to avoid circular DI, matching GamepadService.
    private MainViewModel? _mainViewModel;

    // Jog movement accumulated between dispatches. Summing rather than
    // forwarding each message is what keeps the controller's planner supplied
    // without being flooded; see JogLoopAsync.
    // Poll interval for the dispatch loop. Well under the dispatch interval,
    // so motion is forwarded promptly instead of waiting on a timer edge.
    private const int JogPollMs = 10;

    // How long the status loop sleeps with no pendant on. Nothing is being sent
    // in that state, so it only has to notice a new one promptly.
    private const int IdlePollMs = 250;

    // Bounds how long a serial read blocks, which is how quickly the reader
    // notices cancellation. Silence on that port is normal - it is open whether
    // or not a pendant is switched on - so a timeout here is not an error.
    private const int SerialReadTimeoutMs = 250;

    // Gap before reopening a receiver port that failed. It is a USB device on a
    // machine that gets re-cabled, so the alternative to retrying is an
    // application restart to pick it up again.
    private const int SerialRetryMs = 2000;

    // Unparseable lines are reported no more often than this, with a count of
    // what was skipped in between.
    //
    // One bad line is worth seeing; a stuck far end produces thousands. A
    // receiver that has fallen back to its MicroPython REPL echoes every status
    // frame back as a Python error, which is thirty unparseable lines a second
    // for as long as it lasts - and past the console's line cap every status
    // tick pays repeated O(n) removals with a UI notification each, on the
    // thread that draws the DRO.
    private const int MalformedReportIntervalMs = 5000;
    private long _lastMalformedTicks;
    private int _suppressedMalformed;

    // Receiver notes get the same treatment, and for the same reason. They are
    // usually rare - one at power-up, one when a pendant pairs - but the board
    // now reports a caught fault as a note rather than dying of it, and a fault
    // that recurs every time round its loop would arrive faster than anything
    // else on the port.
    private long _lastNoteTicks;
    private int _suppressedNotes;

    // A feed limit the pendant cannot be slowed to gets the same treatment.
    private long _lastFeedFloorTicks;
    private int _suppressedFeedFloor;

    // Said once per port session, then reset when the port is reopened.
    private bool _reportedRepl;

    private readonly object _jogLock = new();
    private string? _pendingAxis;
    private double _pendingDistance;

    // What the pendant last said one detent is worth. Needed at dispatch and
    // not only for the diagnostics, because the slowest the handheld can
    // deliver is a function of it - see PendantFloorPerStepMmPerMin.
    private double _pendingStep;

    // The F word the last block went out with, which the grid holds on to while
    // the request stays near it. Only the jog loop touches this.
    private double _lastCommandedFeed;
    private double _pendingFeed;
    private double _rawFeed;
    private double _smoothedFeed;

    // Distance dispatched recently, with when it went out. The rate movement is
    // arriving at is the sum of these over the span they cover - measured on the
    // wall clock, over many blocks, which is the only form of this that survives
    // contact with the pendant.
    //
    // Per-block estimates do not: the pendant collapses several ticks into one
    // message when it falls behind, so a block is not a fixed slice of time and
    // counting messages misreads it. Distance and elapsed time are the two
    // things neither end can be wrong about.
    private const int RateWindowMs = 500;
    private const int RateMinSpanMs = 150;
    private const int RateMinSamples = 3;

    // A shorter window, taken alongside the long one, with the faster of the two
    // winning.
    //
    // A trailing average lags a hand that is speeding up: while the wheel
    // accelerates the long window still holds the slow start of the ramp, so it
    // reports a rate below the one being turned now, the ceiling comes down on
    // a request that was honest, and the machine dips before catching up. Felt
    // as a stumble part way into every acceleration.
    //
    // Taking the higher of the two is safe because this is only ever a ceiling.
    // Too high simply defers to what the pendant asked for, which is where this
    // started; too low invents a stall that nothing asked for.
    private const int RateRecentMs = 200;
    private const int RateRecentMinSpanMs = 100;

    // And enough samples in it to mean anything, which the span alone does not
    // guarantee.
    //
    // The short window was guarded on elapsed time where the long one is
    // guarded on time and sample count both, and that asymmetry was the defect.
    // The pendant emits only on ticks that carry a detent, so a hesitant turn
    // arrives at 60 ms rather than 27 - and a 200 ms window then holds three
    // messages where it normally holds seven. One extra detent moves an
    // estimate made from three by several times over, and because the two
    // windows are combined by taking the higher, that noise only ever ratchets
    // the ceiling up before the next block drops it again.
    //
    // Measured on the machine as arriving 778-3764 mm/min inside one 0.7 s
    // burst: a ceiling swinging five to one, block over block. A planner cannot
    // chain blocks that each ask for a different velocity, so it decelerates
    // into every one of them - felt as a stutter on a tentative start that
    // clears the moment the wheel is turned confidently, which is the report
    // this came from.
    //
    // Below this the long window stands on its own, which still bounds the run
    // on that the ceiling exists to prevent; it is only the fast-reacting half
    // that is withheld until there is enough to react to.
    private const int RateRecentMinSamples = 5;

    // Commanded a little above the measured rate on purpose. Exactly at it, any
    // jitter leaves the machine behind and the shortfall accumulates as queued
    // motion - which is felt as the axis running on after the wheel stops.
    private const double RateHeadroom = 1.15;

    private readonly Queue<(long Tick, double Distance)> _recentMotion = new();

    // How steadily jog messages are arriving, which is the one thing this end
    // can measure about the link without touching either board.
    //
    // The pendant emits on a 20 ms tick while the wheel turns, so arrivals
    // should be about that far apart. They are not, when the machine runs
    // rough: a gap here is a window the sender has no motion to dispatch, the
    // controller finishes what it holds and decelerates, and the operator feels
    // it as a stumble. Measuring it says whether roughness is the link
    // delivering in bursts or this end mishandling a steady stream - and the
    // same number over TCP and over the radio compares the two transports
    // directly.
    // The slowest the pendant can deliver, per millimetre of step, in mm/min.
    //
    // Its flow control converts the feed this end reports into a detent budget
    // per tick and drops the rest - but the budget is clamped at one, so on a
    // 20 ms tick it can never send fewer than fifty detents a second whatever
    // it is asked for. That is step x 50 mm/s, or step x 3000 mm/min: 1500 at a
    // 0.5 mm step and 3000 at 1 mm. See the allowed &lt; 1 clamp in jog.py.
    //
    // Commanding under it while the wheel turns is not a slower jog, it is a
    // growing queue - the machine cannot consume what the handheld cannot stop
    // sending, and the surplus comes out as travel after the hand has stopped.
    private const double PendantFloorPerStepMmPerMin = 3000.0;

    private const int JogGapThresholdMs = 60;   // three missed ticks
    private const int JogBurstQuietMs = 750;    // wheel considered stopped

    private readonly object _arrivalLock = new();
    private long _lastJogArrivalTicks;
    private long _burstStartTicks;
    private long _worstGapMs;
    private int _jogArrivals;
    private int _longGaps;

    // What was actually asked of the controller during the same burst, which is
    // a different question from how the movement arrived.
    //
    // Feed spread tests whether the blocks are asking for a steady velocity or a
    // wobbling one. The pendant sets its feed from how fast the wheel is being
    // turned, deliberately, so consecutive blocks can each carry a different F -
    // and a planner given a changing target has to accelerate and decelerate
    // between them rather than chaining them at speed.
    //
    // Planner depth is the controller's own answer to whether it ran dry. It is
    // free blocks, so high means empty: if the minimum stays high through a
    // burst, nothing was ever queued deep enough to chain and the machine is
    // decelerating at the end of every block it gets. Zero means the buffer
    // report is switched off in $10 and the number is unavailable.
    private int _dispatches;
    private double _feedMin;
    private double _feedMax;

    // How many dispatched blocks asked for a different feed than the one before
    // them, which is the number the planner actually cares about.
    //
    // The min and max above say how wide the spread was; they cannot say whether
    // it was crossed once or three hundred times. A planner chains blocks that
    // share an F word and has to ramp between blocks that do not, so a burst of
    // two hundred blocks at four distinct feeds runs smooth where the same
    // spread spent one block at a time does not.
    private int _feedChanges;
    private double _lastDispatchFeed;
    private int _plannerFreeMin;
    private int _plannerFreeMax;
    private double _rawFeedMin;
    private double _rawFeedMax;

    // What the movement itself was made of, which is the question the two lines
    // above cannot answer.
    //
    // Feed range says what was asked for and planner depth says what became of
    // it, but both are downstream of the distance - and when the same sweep of
    // the work surface takes four times as long as it used to, the distance is
    // where it went. Travel over detents is what one click of the wheel is
    // actually worth at the machine, and the step range beside it is what the
    // pendant said it should be worth. The two disagreeing localises the loss
    // to this end; the step alone changing localises it to the handheld.
    //
    // The arrival rate is recorded because the ceiling is computed from it. A
    // commanded feed well under what the pendant asked for is not evidence of
    // anything on its own: it is correct behaviour if the movement really is
    // turning up that slowly, and the only way to tell is to see the rate the
    // ceiling was taken from next to the distance it was measured over.
    private double _burstDistance;
    private double _burstDetents;
    private double _stepMin;
    private double _stepMax;
    private double _arrivalMin;
    private double _arrivalMax;

    public event EventHandler<bool>? PendantConnectionChanged;
    public event EventHandler<string>? PendantStatusMessage;
    public event EventHandler? PendantBatteryChanged;

    public bool IsPendantConnected { get; private set; }
    public string? PendantAxis { get; private set; }
    public double PendantStep { get; private set; }

    /// <summary>
    /// The handheld's own charge, or null when it has not said or cannot
    /// vouch for a reading.
    ///
    /// Null rather than zero throughout. The pendant sends the message even
    /// when it has no answer, precisely so that "cannot say" is distinguishable
    /// from "has gone quiet" - and a zero here would be shown as a flat battery
    /// on the one screen the operator is actually watching during a job.
    /// </summary>
    public int? PendantBatteryPercent { get; private set; }
    public double? PendantBatteryVolts { get; private set; }
    public bool PendantBatteryCharging { get; private set; }

    public PendantService(ConfigManager configManager, MachineStateService machineState)
    {
        _configManager = configManager;
        _machineState = machineState;
    }

    public void SetViewModel(MainViewModel vm) => _mainViewModel = vm;

    // --- lifecycle --------------------------------------------------------

    public void Initialize(PendantConfig config)
    {
        _config = config;
        if (!config.Enabled) return;
        Start();
    }

    public void Start()
    {
        if (!_config.Enabled)
        {
            Report("Pendant listener disabled in configuration.");
            return;
        }

        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        StartListener(token);

        var portName = _config.SerialPortName?.Trim();
        if (!string.IsNullOrEmpty(portName))
        {
            // A dedicated thread rather than the pool. It spends its life in a
            // blocking read - SerialPort's stream does not reliably honour a
            // cancellation token, and the usual workaround of disposing the port
            // to break a pending read races the teardown that would dispose it.
            _serialTask = Task.Factory.StartNew(
                () => SerialLoop(portName, token),
                token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        // Both loops belong to the service, not to a session. Two pendants are
        // never active at once, but a handover means one session outlives the
        // start of the next - and two jog loops draining one accumulator would
        // each dispatch half of the operator's movement.
        _jogTask = Task.Run(() => JogLoopAsync(token));
        _statusTask = Task.Run(() => StatusLoopAsync(token));
    }

    private void StartListener(CancellationToken token)
    {
        try
        {
            var address = IPAddress.Parse(_config.BindAddress);
            _listener = new TcpListener(address, _config.Port);
            _listener.Start();
            _acceptTask = Task.Run(() => AcceptLoopAsync(token));
            Report($"Pendant listener on {_config.BindAddress}:{_config.Port}");
        }
        catch (Exception ex)
        {
            // Reported and dropped rather than failing Start. The transports are
            // independent, and a port already taken by a previous instance
            // should not also cost the operator the serial receiver.
            Report($"Pendant listener failed to start: {ex.Message}");
            _listener = null;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        RetireActive("service stopping");

        try { _listener?.Stop(); } catch { /* already torn down */ }
        _listener = null;

        WaitFor(_acceptTask);
        WaitFor(_serialTask);
        WaitFor(_jogTask);
        WaitFor(_statusTask);
        _acceptTask = _serialTask = _jogTask = _statusTask = null;

        _cts?.Dispose();
        _cts = null;
    }

    private static void WaitFor(Task? task)
    {
        try { task?.Wait(TimeSpan.FromSeconds(2)); } catch { /* cancelled, or already gone */ }
    }

    public void Dispose() => Stop();

    // --- who is driving ---------------------------------------------------

    /// <summary>
    /// Hands this channel the machine, standing down whatever held it before.
    /// </summary>
    private void Adopt(IPendantChannel channel)
    {
        var previous = _arbiter.Adopt(channel);
        Volatile.Write(ref _lastRxTicks, Environment.TickCount64);

        if (previous != null)
        {
            previous.Close();
            Report($"Pendant on {previous.Describe()} superseded by {channel.Describe()}.");
        }

        // Movement the outgoing pendant accumulated is dropped rather than
        // dispatched under the new one's authority.
        ClearPendingJog();

        SetConnected(true);
        Report($"Pendant connected on {channel.Describe()}.");
    }

    /// <summary>
    /// Stands this channel down, if it is still the one driving the machine.
    /// Does nothing at all if it has already been superseded - see
    /// <see cref="PendantArbiter.Retire"/> for why that check has to be here.
    /// </summary>
    private void RetireIfActive(IPendantChannel channel, string reason)
    {
        if (!_arbiter.Retire(channel)) return;

        channel.Close();
        ClearPendingJog();

        // A pendant that vanishes mid-jog must not leave the machine running
        // on: cancel whatever it had in flight. Safe to fire unconditionally
        // because MainViewModel.JogCancel refuses it in the states where the
        // byte would do harm - a tool change not being one of them, which is
        // exactly when a handheld going flat must not leave an axis moving.
        Dispatcher.UIThread.Post(() => _mainViewModel?.JogCancel());

        SetConnected(false);
        Report($"Pendant on {channel.Describe()} stood down ({reason}).");
    }

    private void RetireActive(string reason)
    {
        var channel = _arbiter.Active;
        if (channel != null) RetireIfActive(channel, reason);
    }

    // --- accept -----------------------------------------------------------

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var incoming = await _listener!.AcceptTcpClientAsync(token);
                incoming.NoDelay = true;
                _ = Task.Run(() => TcpSessionAsync(incoming, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) break;
                Report($"Pendant accept error: {ex.Message}");
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
    }

    private async Task TcpSessionAsync(TcpClient client, CancellationToken token)
    {
        var channel = new TcpPendantChannel(client);

        // For the network transport the accept is the trigger: a socket exists
        // only because a pendant opened it. The serial receiver cannot use that
        // rule - see SerialLoop.
        Adopt(channel);

        var buffer = new byte[2048];
        var pending = new StringBuilder();

        try
        {
            var stream = channel.Stream;
            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, token);
                if (read == 0) break;                       // pendant closed

                pending.Append(Encoding.UTF8.GetString(buffer, 0, read));

                foreach (var line in TakeLines(pending))
                {
                    if (TryParse(line, out var root)) Deliver(root, channel);
                    else ReportMalformed(line);
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            // Expected once this session has been superseded: the adopting
            // channel closed this socket, and the read fails on the way out.
            if (!token.IsCancellationRequested && _arbiter.IsActive(channel))
                Report($"Pendant session on {channel.Describe()} ended: {ex.Message}");
        }
        finally
        {
            // Unconditionally: the socket is this session's whether or not the
            // channel still holds the machine.
            channel.Close();
            RetireIfActive(channel, "connection closed");
        }
    }

    // --- serial -----------------------------------------------------------

    /// <summary>
    /// Reads the ESP-NOW receiver's port and lets the pendant on the far side of
    /// it drive the machine.
    /// </summary>
    /// <remarks>
    /// The receiver forwards the same newline-delimited JSON the network carries,
    /// so everything above this reads identically either way. What differs is
    /// what counts as a connection.
    ///
    /// A socket exists only because a pendant opened it, so accepting one is
    /// proof of a pendant. The receiver's port is open from the moment it
    /// enumerates, whether or not a handheld is switched on, charged or in range
    /// - so opening it proves nothing, and the hello is the trigger instead.
    /// Treating the port as the connection would light the pendant indicator on
    /// an empty bench.
    ///
    /// The port therefore outlives the session. A pendant that goes quiet is
    /// stood down while the port stays open, waiting for the next hello, which
    /// the firmware sends whenever it acquires the link.
    /// </remarks>
    private void SerialLoop(string portName, CancellationToken token)
    {
        var buffer = new byte[512];
        var pending = new StringBuilder();
        var synced = false;
        string? lastFailure = null;
        _reportedRepl = false;

        while (!token.IsCancellationRequested)
        {
            SerialPort? port = null;
            SerialPendantChannel? channel = null;

            try
            {
                port = new SerialPort(portName, _config.SerialBaudRate, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,

                    // Set explicitly from configuration rather than left to
                    // the platform default, because what these lines do is a
                    // property of the receiver and not of this code. See
                    // PendantConfig.SerialDtrEnable - opening the port can
                    // reset the board through them, and on some firmware not
                    // asserting DTR is read as "no terminal attached" instead.
                    DtrEnable = _config.SerialDtrEnable,
                    RtsEnable = _config.SerialRtsEnable,
                    ReadTimeout = SerialReadTimeoutMs,
                    WriteTimeout = 500,
                };
                port.Open();

                // Anything buffered between the open and here is dropped. This
                // is cheap insurance rather than a fix for anything observed -
                // measured on a receiver sending steadily into a closed port,
                // the first read after opening returns one line, not the
                // backlog, so the driver does not appear to hold anything for a
                // closed handle. Kept because the cost is nothing and the
                // alternative is trusting that on every platform.
                port.DiscardInBuffer();

                pending.Clear();
                synced = false;
                lastFailure = null;
                channel = new SerialPendantChannel(port);
                Report($"Pendant receiver open on {portName}, waiting for a pendant.");

                while (!token.IsCancellationRequested)
                {
                    int read;
                    try { read = port.Read(buffer, 0, buffer.Length); }
                    catch (TimeoutException) { continue; }  // silence is the normal state
                    if (read <= 0) continue;

                    pending.Append(Encoding.UTF8.GetString(buffer, 0, read));

                    // Discard everything up to the first line break, once per
                    // port.
                    //
                    // The receiver writes continuously and this end opens
                    // whenever the application happens to start, so the first
                    // bytes read are usually the tail of a message whose start
                    // nobody saw. That tail is not harmless: it arrives
                    // terminated, so it looks like a whole line, and it reached
                    // the console as "malformed JSON" - the readable version of
                    // the failure. The unreadable version is a tail that still
                    // parses, which for a jog is a real number of detents with
                    // the sign or magnitude of whatever survived.
                    if (!synced)
                    {
                        var text = pending.ToString();
                        var firstBreak = text.IndexOf('\n');
                        pending.Clear();
                        if (firstBreak < 0) continue;      // still mid-message
                        pending.Append(text[(firstBreak + 1)..]);
                        synced = true;
                    }

                    // A session that ended left its channel closed for good.
                    // Close is one-way on purpose, so a superseded session
                    // cannot go on writing - but the port outlives the session
                    // here, and adoption hands out this instance. Left as it
                    // was, the next hello was adopted onto a dead channel, the
                    // status loop saw it closed and stood the pendant straight
                    // back down, and the hello after that did the same.
                    //
                    // Presented as the pendant indicator flashing on and off
                    // with no error anywhere, and a link that returned only when
                    // the receiver was physically reset - a read failing being
                    // the one thing that reopened the port and built a fresh
                    // channel. The class remarks promise the opposite: that a
                    // pendant which goes quiet is stood down while the port
                    // stays open, waiting for the next hello.
                    //
                    // Safe to swap. A closed channel is never the active one -
                    // Retire clears it before closing and Adopt has already
                    // replaced it - and both compare by reference, so the dead
                    // instance holds no claim on anything.
                    if (!channel.IsOpen && port.IsOpen)
                        channel = new SerialPendantChannel(port);

                    foreach (var line in TakeLines(pending))
                        ReceiveFromSerial(line, channel);
                }
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) break;

                // Once per distinct fault, not once per retry. A port name that
                // matches nothing - a receiver left unplugged, or a COM number
                // Windows has moved - would otherwise put a line in the console
                // every two seconds for the rest of the session. That is how the
                // console reaches its cap, and past the cap every status tick
                // pays repeated O(n) removals on the UI thread.
                if (ex.Message != lastFailure)
                {
                    lastFailure = ex.Message;
                    Report($"Pendant receiver on {portName}: {ex.Message}");
                }
            }
            finally
            {
                if (channel != null) RetireIfActive(channel, "receiver port closed");
                try { port?.Dispose(); } catch { /* already gone */ }
            }

            // WaitOne returns true when the token is signalled.
            if (token.WaitHandle.WaitOne(SerialRetryMs)) break;
        }
    }

    private void ReceiveFromSerial(string line, SerialPendantChannel channel)
    {
        if (!TryParse(line, out var root))
        {
            // A receiver that has stopped running its bridge is a different
            // problem from a garbled line, and saying so once beats saying
            // "malformed JSON" several thousand times.
            if (LooksLikeRepl(line))
            {
                HandleReceiverAtRepl(channel);
                return;
            }

            // Only a complaint once a pendant is actually talking. Before the
            // hello, whatever is on this port belongs to the receiver - an ESP32
            // prints a bootloader banner at every reset - and calling that a
            // protocol error would fill the console on every replug.
            if (_arbiter.IsActive(channel)) ReportMalformed(line);
            return;
        }

        var type = MessageType(root);

        // The receiver board's own diagnostics, which it writes into the same
        // stream under an "rx_" namespace rather than as bare prints that would
        // land mid-protocol.
        //
        // Taken first because they are not the pendant talking. They arrive when
        // no pendant is connected at all - "receiver up, this board is <mac>" at
        // power-on, and the pendant's MAC when one pairs, which are the two lines
        // that matter when pairing is not working - so gating them on an active
        // pendant would drop exactly the ones worth having. For the same reason
        // they must not feed the silence watchdog: the receiver being alive is no
        // evidence a pendant is.
        //
        // The relay these replaced filtered them out and printed them to its own
        // terminal. Dropping them silently here would have retired that terminal
        // and the diagnostics with it.
        if (type.StartsWith("rx_", StringComparison.Ordinal))
        {
            var note = GetString(root, "msg");
            ReportThrottled(
                note.Length > 0 ? $"Receiver: {note}" : $"Receiver sent '{type}'.",
                ref _lastNoteTicks, ref _suppressedNotes);
            return;
        }

        if (ShouldAdoptSerial(type, _arbiter.IsActive(channel), _arbiter.Active is not null))
            Adopt(channel);

        Deliver(root, channel);
    }

    /// <summary>
    /// Whether this line is the receiver board's MicroPython prompt rather than
    /// anything protocol-shaped.
    /// </summary>
    /// <remarks>
    /// When the receiver's script stops - an unguarded exception in its loop is
    /// enough - MicroPython falls back to its REPL, and that REPL is on the same
    /// serial port this end is writing to. Status frames then arrive at an
    /// interpreter, which echoes each one back with its prompt and evaluates it
    /// as Python. They die on the first `false`, since JSON spells it lower case
    /// and Python does not, and the error comes back down the wire.
    ///
    /// So the port fills with `>>>` and tracebacks at the status rate. Worth
    /// naming rather than reporting as malformed JSON several thousand times:
    /// the board cannot recover on its own, and nothing else about the symptom
    /// says the receiver stopped running.
    /// </remarks>
    internal static bool LooksLikeRepl(string line) =>
        line.StartsWith(">>>", StringComparison.Ordinal) ||
        line.StartsWith("Traceback (most recent call last)", StringComparison.Ordinal) ||
        line.StartsWith("File \"<stdin>\"", StringComparison.Ordinal);

    /// <summary>
    /// Whether a message arriving on the serial transport should hand its
    /// channel the machine.
    /// </summary>
    /// <remarks>
    /// A hello always claims it. That is a pendant announcing itself, and the
    /// newest pendant wins whether the one before it was on WiFi or the radio.
    ///
    /// Anything else claims it only when nothing is driving, and that clause is
    /// the fix for a real failure rather than a convenience. A hello is sent
    /// when the pendant acquires a link with the *receiver*, and the receiver is
    /// powered by the PC's USB and always on - so a pendant that paired before
    /// the sender started sent its hello into a port nobody was reading, and no
    /// second one is coming. It then pings and jogs forever at a sender that
    /// ignores every message, while its own screen shows the link up, because
    /// from the pendant's side it genuinely is: the receiver is answering.
    ///
    /// The network transport never had this. There the pendant connects to the
    /// sender, so restarting the sender forces a fresh connection and a fresh
    /// hello. Over the radio the sender is a third party that can come and go
    /// without either end noticing, so it has to be able to join a conversation
    /// already in progress.
    ///
    /// Adopting on any message is safe here because of what reaches this port.
    /// The receiver answers the pendant over the radio, not over the wire; the
    /// only things it writes to the PC are its own rx_ notes and packets a
    /// pendant actually sent. So a non-rx_ line is evidence of a pendant by
    /// construction, and an empty bench stays quiet - which is what the port
    /// being open must not be mistaken for.
    ///
    /// Notes are refused explicitly rather than by relying on the caller having
    /// filtered them, because "the receiver is plugged in" reading as "a pendant
    /// is connected" is exactly the confusion this whole rule exists to prevent.
    ///
    /// Only a hello or a ping may do the claiming, and that is a safety rule
    /// rather than a tidiness one. Whatever message adopts is also the first
    /// message acted on, so letting a jog adopt means the act of noticing a
    /// pendant is itself a movement of the machine - and the jog that does it is
    /// the wheel being knocked while the handheld is picked up, which is not an
    /// unlucky case but the ordinary way of lifting a thing with a wheel on it.
    /// The same argument bars btn, zero and probe: cycle start, a rewritten
    /// datum and a probing cycle are all worse ways to discover a pendant.
    ///
    /// A hello and a ping are the two that carry no instruction, and the ping is
    /// what makes this enough on its own: the firmware sends one every three
    /// seconds whenever its queue is empty. So an already-paired pendant is
    /// picked up within three seconds of going quiet, and never mid-motion -
    /// while it is being jogged the queue stays busy and the ping waits, which
    /// is the right moment to take the machine anyway.
    /// </remarks>
    internal static bool ShouldAdoptSerial(string messageType, bool alreadyActive,
                                           bool anyPendantActive)
    {
        if (alreadyActive) return false;

        // A hello is a pendant announcing itself and takes the machine from
        // whoever holds it. A ping only fills the gap this rule exists for, so
        // it claims an idle machine and never an occupied one.
        if (messageType == "hello") return true;
        if (messageType == "ping") return !anyPendantActive;

        return false;
    }

    /// <summary>
    /// Pull complete newline-delimited messages out of the buffer, leaving any
    /// partial tail behind. Neither transport gives message boundaries, so a read
    /// can land mid-object or carry several at once.
    /// </summary>
    internal static IEnumerable<string> TakeLines(StringBuilder pending)
    {
        var lines = new List<string>();
        var text = pending.ToString();
        var start = 0;

        int index;
        while ((index = text.IndexOf('\n', start)) >= 0)
        {
            var line = text[start..index].Trim();
            if (line.Length > 0) lines.Add(line);
            start = index + 1;
        }

        pending.Clear();
        if (start < text.Length) pending.Append(text[start..]);
        return lines;
    }

    // --- inbound messages -------------------------------------------------

    /// <summary>
    /// Parsed once, here, rather than in the handler. The serial path has to read
    /// the message type before deciding whether the message may be acted on at
    /// all, and parsing twice to answer that would cost every line on the port.
    /// </summary>
    private static bool TryParse(string line, out JsonElement root)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            root = default;
            return false;
        }
    }

    private static string MessageType(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("t", out var type) &&
        type.ValueKind == JsonValueKind.String
            ? type.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Acts on a message, if it came from the pendant that is driving the
    /// machine.
    /// </summary>
    /// <remarks>
    /// A superseded pendant is dropped rather than merely ignored for tidiness.
    /// It may still be transmitting - a handheld that lost WiFi and came back on
    /// the receiver is two live sources of jogs for the same hand - and acting on
    /// both is two operators driving one axis.
    /// </remarks>
    private void Deliver(JsonElement root, IPendantChannel channel)
    {
        if (!_arbiter.IsActive(channel)) return;

        // Anything at all counts against the silence watchdog, not just a ping.
        Volatile.Write(ref _lastRxTicks, Environment.TickCount64);

        switch (MessageType(root))
        {
            case "hello":
                Report($"Pendant identified: {GetString(root, "dev")} " +
                       $"v{GetDouble(root, "ver", 0):0}");
                break;

            case "jog":
                HandleJog(root);
                break;

            case "jog_cancel":
                // Refused by MainViewModel.JogCancel where the machine is in no
                // state for it. The pendant sends these constantly - on every
                // stop, reversal and axis change - so the guard is there and
                // not here.
                Dispatcher.UIThread.Post(() => _mainViewModel?.JogCancel());
                break;

            case "btn":
                HandleButton(root);
                break;

            case "zero":
                HandleZero(root);
                break;

            case "probe":
                HandleProbe(root);
                break;

            case "mode":
                PendantAxis = GetString(root, "axis");
                PendantStep = GetDouble(root, "step", 0);
                break;

            case "ping":
                channel.WriteLine($"{{\"t\":\"pong\",\"seq\":{GetDouble(root, "seq", 0):0}}}");
                break;

            case "battery":
                HandleBattery(root);
                break;
        }
    }

    // The same thresholds the pendant's own panel uses. Deliberately shared
    // rather than tuned separately: two screens disagreeing about when the
    // battery is low is worse than either number being slightly off.
    private const int BatteryWarnPercent = 20;
    private const int BatteryCriticalPercent = 10;

    // What was last said out loud. The pendant reports every ten seconds, and
    // a warning repeated at that rate for the rest of a shift is one the
    // operator stops reading - so this speaks only when the level changes.
    private int _batteryWarningLevel;

    private void HandleBattery(JsonElement root)
    {
        var charging = root.TryGetProperty("chg", out var chg) &&
                       chg.ValueKind == JsonValueKind.True;

        // Absent rather than zero when the pendant has no answer, so the
        // distinction it went to the trouble of sending survives to the UI.
        // Read through GetDouble and cast, because a value that arrives as
        // 81.0 rather than 81 would make GetInt32 throw and take the message
        // handler with it.
        int? percent = root.TryGetProperty("pct", out var pct) &&
                       pct.ValueKind == JsonValueKind.Number
            ? (int)pct.GetDouble()
            : null;
        double? volts = root.TryGetProperty("v", out var v) &&
                        v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;

        PendantBatteryPercent = percent;
        PendantBatteryVolts = volts;
        PendantBatteryCharging = charging;
        PendantBatteryChanged?.Invoke(this, EventArgs.Empty);

        // A pendant on charge is never a warning, at any level - the same rule
        // its own panel follows. Warning about a battery that is actively
        // filling teaches the operator to ignore the one that matters.
        var level = 0;
        if (!charging && percent.HasValue)
        {
            if (percent.Value <= BatteryCriticalPercent) level = 2;
            else if (percent.Value <= BatteryWarnPercent) level = 1;
        }

        if (level == _batteryWarningLevel) return;
        var previous = _batteryWarningLevel;
        _batteryWarningLevel = level;

        var reading = percent.HasValue ? $"{percent.Value}%" : "unknown";
        var detail = volts.HasValue
            ? $"{reading} ({volts.Value.ToInvariantString("0.00")} V)"
            : reading;

        Report(level switch
        {
            2 => $"Pendant battery critical: {detail}. Charge it now - a " +
                 "handheld that dies mid-jog leaves the machine where it stood.",
            1 => $"Pendant battery low: {detail}.",
            _ => previous > 0
                ? $"Pendant battery back to {detail}" +
                  (charging ? " and charging." : ".")
                : $"Pendant battery {detail}.",
        });
    }

    private void HandleJog(JsonElement root)
    {
        // Timed on arrival rather than after the guards below, so the number
        // describes the link and not what this end decided to do about it.
        RecordJogArrival();

        var axis = GetString(root, "axis");
        if (string.IsNullOrEmpty(axis) || axis.Length > 1) return;

        var detents = GetDouble(root, "det", 0);
        var step = GetDouble(root, "step", 0);
        var distance = detents * step;
        if (distance == 0) return;

        // The step is what the pendant believes one detent is worth, and until
        // now this end read it, multiplied by it and forgot it. It is the first
        // number to look at when the same movement of the wheel produces less
        // travel than it used to, because everything downstream - the block
        // distance, the arrival rate, and the ceiling measured from it - is
        // this figure multiplied out. A ceiling that has come down is not
        // evidence that the ceiling is wrong.
        lock (_arrivalLock)
        {
            if (step < _stepMin) _stepMin = step;
            if (step > _stepMax) _stepMax = step;
            _burstDetents += Math.Abs(detents);
        }

        // A limit below what the pendant can be slowed to is worth saying out
        // loud rather than leaving as mysterious run-on: nothing downstream can
        // satisfy it, because the surplus is generated at the handheld.
        if (step > 0 && _config.MaxJogFeedRate > 0 &&
            _config.MaxJogFeedRate < step * PendantFloorPerStepMmPerMin)
        {
            var floor = step * PendantFloorPerStepMmPerMin;
            ReportThrottled(
                $"MaxJogFeedRate {_config.MaxJogFeedRate.ToInvariantString("0")} is below " +
                $"the {floor.ToInvariantString("0")} mm/min the pendant delivers at a " +
                $"{step.ToInvariantString("0.###")} mm step - jogs will queue and run on.",
                ref _lastFeedFloorTicks, ref _suppressedFeedFloor);
        }

        var requested = GetDouble(root, "feed", 0);

        // Accumulate rather than dispatching. The loop decides when to send, so
        // movement arriving faster than the controller can absorb becomes one
        // longer move instead of a queue of short ones. An axis change discards
        // what was pending for the previous axis rather than carrying it over.
        lock (_jogLock)
        {
            if (_pendingAxis != axis)
            {
                _pendingAxis = axis;
                _pendingDistance = 0;
            }
            _pendingDistance += distance;
            if (step > 0) _pendingStep = step;
            if (requested > 0)
            {
                _pendingFeed = requested;
                _rawFeed = requested;

                // Tracked as the requests arrive, not as blocks go out. Sampled
                // at dispatch it missed everything asked between two of them,
                // which reported a narrower range than the pendant actually
                // asked for - and made the commanded feed look, impossibly,
                // higher than the request it was capped from.
                lock (_arrivalLock)
                {
                    if (requested < _rawFeedMin) _rawFeedMin = requested;
                    if (requested > _rawFeedMax) _rawFeedMax = requested;
                }

                // Smoothed as the figures arrive rather than at dispatch, so the
                // average is over what the wheel did and not over which of those
                // messages happened to land on a dispatch tick.
                if (_smoothedFeed <= 0) _smoothedFeed = requested;
                else
                {
                    var alpha = Math.Clamp(_config.JogFeedSmoothing, 0.01, 1.0);
                    _smoothedFeed += (requested - _smoothedFeed) * alpha;
                }
            }
        }
    }

    /// <summary>
    /// Sends accumulated pendant movement, no more often than the configured
    /// interval.
    ///
    /// This is the only place backpressure can be applied. The pendant cannot
    /// see the controller's planner, so left to itself it emits jog blocks
    /// faster than grblHAL can parse and execute; the buffer fills, the send
    /// path blocks, status stops flowing, and the machine ends up over a second
    /// behind the operator's hand.
    ///
    /// Runs for the life of the service rather than per session. Every refusal
    /// below therefore skips the dispatch and nothing more: a loop that returned
    /// on an alarm would leave the pendant dead for the rest of the run, long
    /// after the alarm was cleared.
    /// </summary>
    private async Task JogLoopAsync(CancellationToken token)
    {
        try
        {
            var sinceDispatch = Stopwatch.StartNew();

            while (!token.IsCancellationRequested)
            {
                // Poll faster than the dispatch interval and send as soon as
                // there is something to send, rather than waiting for a timer
                // edge.
                //
                // A fixed interval free-runs against the pendant's own tick, and
                // two unsynchronised periods beat: some windows carry two
                // messages, some carry none. A window with none sends nothing at
                // all, the machine finishes what little it has buffered and
                // decelerates - while the pendant's trace shows a perfectly
                // steady stream, because from its side nothing went wrong.
                await Task.Delay(JogPollMs, token);

                // Checked here because this loop already ticks faster than the
                // pendant does, so a burst is noticed ending without a timer of
                // its own.
                ReportJogBurstIfQuiet();

                lock (_jogLock)
                {
                    if (_pendingDistance == 0) continue;
                }
                if (sinceDispatch.ElapsedMilliseconds < _config.JogDispatchIntervalMs)
                    continue;

                sinceDispatch.Restart();

                string? axis;
                double distance, requested, raw, step;
                lock (_jogLock)
                {
                    axis = _pendingAxis;
                    distance = _pendingDistance;
                    step = _pendingStep;
                    raw = _rawFeed;
                    requested = _config.SmoothJogFeed && _smoothedFeed > 0
                        ? _smoothedFeed
                        : _pendingFeed;
                    _pendingDistance = 0;
                }

                if (axis == null || distance == 0) continue;

                // Clamp rather than reject. The guard exists to bound a corrupt
                // detent count, and clamping bounds it just as effectively -
                // whereas rejecting discards legitimate motion too, and every
                // discarded move is distance the machine never makes and the
                // operator never gets back. Coalescing made this reachable in
                // normal use: at full feed a 100 ms window is already 20 mm.
                if (Math.Abs(distance) > _config.MaxJogDistanceMm)
                {
                    var clamped = Math.Sign(distance) * _config.MaxJogDistanceMm;
                    Report($"Pendant jog of {distance:0.###} mm clamped to " +
                           $"{clamped:0.###} mm.");
                    distance = clamped;
                }

                // Written straight to the port rather than posted to the UI
                // thread. The write itself is already serialised by the comm
                // layer's lock, so the hop bought nothing but latency - and it
                // charged that latency to a queue shared with rendering, DRO
                // updates and console trimming.
                //
                // That was the transport stall behind every merged block. Jogs
                // arriving every 50 ms sat in the UI queue behind whatever the
                // interface was doing, the service coalesced the backlog, and
                // one 50 mm block went out where seven 7 mm blocks should have.
                // A 50 mm block at F8550 puts the machine 350 ms behind the
                // hand the instant it is queued.
                //
                // It also explained why the jerk arrived partway through a long
                // move rather than at random: the console fills to its cap
                // within seconds of a traverse starting, and every status tick
                // from then on does repeated O(n) removals with a UI
                // notification each. The load switches on mid-move and stays on.
                // Whether the controller will take a jog at all, which is the
                // controller's own state and not this sender's bookkeeping.
                // Covers the disconnected machine, the alarm, a hardware MPG
                // holding the input stream, and the cut in progress - and
                // deliberately allows a tool change, where jogging to touch off
                // is the entire point of the pause. See
                // MainViewModel.CanJogInState.
                if (_mainViewModel == null) continue;
                if (!_mainViewModel.CanJogFromDevice) continue;

                // The pendant sends a feed matching how fast the wheel is
                // being turned, which is what makes the machine track the
                // hand rather than lag it. Fall back to configuration only
                // when it does not, and never exceed the ceiling.
                //
                // The last fallback is the rate the operator picked on screen,
                // and that one is in display units while the block below states
                // G21. See FallbackJogFeedMmPerMin.
                var feed = requested > 0
                    ? requested
                    : (_config.JogFeedRate > 0
                        ? _config.JogFeedRate
                        : FallbackJogFeedMmPerMin());

                // What the pendant asked for, kept before anything here has
                // touched it. It already carries the handheld's own ceiling for
                // this step - STEP_MAX_FEED, 8000 at 0.5 mm and 10000 at 1 mm -
                // which is a limit this end has no business raising. Nothing
                // below may command above it.
                var askedFor = feed;

                if (_config.MaxJogFeedRate > 0 && feed > _config.MaxJogFeedRate)
                    feed = _config.MaxJogFeedRate;

                // And never faster than the movement is turning up.
                //
                // Commanding above this cannot make the machine cover more
                // ground - the distance in the block is already fixed - it can
                // only make it arrive early and stand still until the next one.
                // That stall is the stumble, and with the planner empty the
                // controller has to decelerate into every one of them.
                var arriving = 0.0;
                if (_config.MatchFeedToArrivalRate)
                {
                    arriving = MeasureArrivalRate(Math.Abs(distance));
                    if (arriving > 0 && feed > arriving) feed = arriving;
                }

                // Snapped to a grid last of all, so whatever the steps above
                // arrived at, consecutive blocks share an F word wherever the
                // wheel is being turned steadily. See JogFeedQuantumMmPerMin
                // for why a changing F is the roughness.
                //
                // Rounded to nearest rather than down, and never to nothing: a
                // feed under half a quantum still has to travel the distance in
                // the block, and a zero F is not a slow move but a rejected
                // line.
                feed = SnapFeed(feed, _config.JogFeedQuantumMmPerMin, _lastCommandedFeed,
                                _config.JogFeedRiseBandSteps, _config.JogFeedFallBandSteps);

                // The floor, applied after the grid so the snap cannot round
                // straight back down through it, and bounded by what the
                // pendant asked for.
                //
                // Deliberately not the measured arrival rate. Tying the command
                // to that measurement pushed the feed to 12000 at a 0.5 mm step
                // whose firmware ceiling is 8000 - the estimate reads about
                // twice the true rate, between the span bias, the headroom and
                // taking the higher of two windows - and snapping up from a
                // noisy number put the feed on a different grid value every few
                // blocks: 106 changes in 207 dispatches, against 9 in 143 with
                // the floor off. It made the roughness it sat beside worse.
                //
                // What actually makes motion pile up is narrower than
                // "commanded below arrival". The handheld throttles its own
                // detents from the feed this end reports, so a low command is
                // normally self-correcting - right up to its one-detent-per-tick
                // clamp, below which it cannot slow down however it is asked.
                // That clamp is step x 3000 mm/min and needs no measurement.
                //
                // Bounded by the request because the request already carries
                // STEP_MAX_FEED. A pendant asking for less than its own floor is
                // being turned too slowly to reach the clamp, so there is
                // nothing accumulating to prevent.
                if (_config.EnforcePendantDeliveryFloor)
                    feed = ApplyDeliveryFloor(feed, step, askedFor,
                                              _config.JogFeedQuantumMmPerMin);

                // Rounding to nearest can lift a feed past the handheld's own
                // ceiling for this step, and that ceiling is the firmware's
                // decision about what the step is for - this end does not get
                // to round through it. But coming back down has to land on the
                // grid, not on the raw request.
                //
                // Clamping to askedFor directly undid the snap entirely
                // whenever it rounded up, which is most of the time near the
                // ceiling and always below half a quantum. The commanded feed
                // came out as the pendant's raw number - 638, 7969, 3375 - and
                // hysteresis then latched that arbitrary value and held it.
                // Measured as a burst pinned at "feed 638-638" for its whole
                // length, and as a top end that could not be reached because
                // the request nearest it had become the held value.
                if (askedFor > 0 && feed > askedFor)
                    feed = SnapFeedDown(askedFor, _config.JogFeedQuantumMmPerMin);

                if (_config.MaxJogFeedRate > 0 && feed > _config.MaxJogFeedRate)
                    feed = _config.MaxJogFeedRate;

                // G21 is stated explicitly rather than inherited. The pendant
                // always works in millimetres, and a jog line carries its own
                // modal context without altering the machine's - so this stays
                // correct whether the job is running in G20 or G21.
                // Both figures are rounded rather than printed at full
                // double precision. Summing detent distances produces
                // values like 1.2000000000000002, which is 32 bytes in
                // grblHAL's receive buffer where 20 would do - and that
                // buffer is the same pipeline the pendant works to keep
                // supplied. Three decimals is exact for the finest step
                // the pendant offers.
                _lastCommandedFeed = feed;
                RecordDispatch(feed, raw, distance, arriving);

                _mainViewModel.SendPendantJog(
                    $"$J=G91G21{axis}{distance.ToInvariantString("0.###")}F{feed.ToInvariantString("0.#")}",
                    _config.EchoJogsToConsole);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    /// <summary>
    /// Runs a probe cycle the pendant asked for.
    /// </summary>
    /// <remarks>
    /// The pendant sends an operation name and nothing else. Every parameter
    /// belongs to this side, per operation, and a pendant carrying its own copy
    /// would be a second answer to a question that already has one - which is
    /// precisely what made the shared probe parameters unsafe.
    ///
    /// Refused for the same reasons a jog is, and one more: not while a cycle
    /// is already running. A probe started into a machine already probing is
    /// worth refusing outright rather than arbitrating, and the pendant greys
    /// its targets while busy so this should never be reached - "should never"
    /// being the reason it is checked here as well.
    ///
    /// Centre finding is absent by design, not oversight. It acts on a
    /// bore-or-boss selection visible only on this screen, and firing a cycle
    /// whose behaviour cannot be read at the machine is the failure this whole
    /// arrangement avoids.
    /// </remarks>
    private void HandleProbe(JsonElement root)
    {
        var operation = GetString(root, "op");
        if (string.IsNullOrEmpty(operation)) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_mainViewModel?.ProbeViewModel is not { } probe) return;

            if (!_mainViewModel.Connected || _mainViewModel.AlarmActive)
            {
                Report($"Pendant probe '{operation}' ignored: not ready.");
                return;
            }

            if (_machineState.MpgActive ||
                _mainViewModel.JobViewModel?.JobRunning == true)
            {
                Report($"Pendant probe '{operation}' ignored: a job is running.");
                return;
            }

            if (probe.IsProbing)
            {
                Report($"Pendant probe '{operation}' ignored: already probing.");
                return;
            }

            ICommand? command = operation switch
            {
                "z" => probe.ProbeZCommand,
                "corner" => probe.ProbeCornerCommand,
                "tlr" => probe.ProbeToolReferenceHereCommand,
                // Distinct from "tlr", not a variant of it. This one traverses
                // to G59.3 before probing where the other descends from wherever
                // the tool was left, and the pendant gives them separate rows
                // for the same reason they get separate names here.
                "tlr_setter" => probe.ProbeToolReferenceAtSetterCommand,
                _ => null
            };

            if (command is null)
            {
                Report($"Pendant asked for unknown probe '{operation}'.");
                return;
            }

            // Through the same command the button on this screen uses, so a
            // pendant probe and a clicked one cannot diverge - including every
            // field check that refuses to start on an unparseable value.
            if (!command.CanExecute(null))
            {
                Report($"Pendant probe '{operation}' refused by the probe page.");
                return;
            }

            Report($"Pendant started probe '{operation}'.");
            command.Execute(null);
        });
    }

    private void HandleButton(JsonElement root)
    {
        var id = GetString(root, "id");
        var down = root.TryGetProperty("down", out var d) &&
                   d.ValueKind == JsonValueKind.True;

        // Real-time commands are one-shot, so only the press edge acts. The
        // release still arrives, and is deliberately ignored rather than
        // producing a second command.
        if (!down) return;

        Dispatcher.UIThread.Post(() =>
        {
            // Feed hold and cycle start are deliberately guarded on connection
            // alone - not on a running job, an alarm, or MPG mode, the way
            // jogging and zeroing are.
            //
            // Those two are single-byte real-time commands that touch no
            // buffer. They cannot desync the stream accounting that makes an
            // injected jog dangerous mid-job, and they are exactly the commands
            // wanted at the worst moment: a feed hold that stops working once a
            // job starts is worse than no button at all, and cycle start is how
            // the operator resumes from that hold.
            //
            // Do not extend the jog guards to cover those two. Jog cancel is
            // not one of them, however much it looks like one: it flushes the
            // controller's receive buffer whatever the machine is doing, so it
            // goes through RequestJogCancel like every other cancel.
            if (_mainViewModel == null || !_mainViewModel.Connected) return;

            switch (id)
            {
                case "feed_hold":
                    _mainViewModel.SendByteCommand(GrblHalConstants.FeedHold);
                    break;
                case "cycle_start":
                    _mainViewModel.SendByteCommand(GrblHalConstants.CycleStart);
                    break;
                case "jog_cancel":
                    _mainViewModel.JogCancel();
                    break;
                default:
                    Report($"Pendant sent unmapped button '{id}'.");
                    break;
            }
        });
    }

    private void HandleZero(JsonElement root)
    {
        if (!_config.AllowZeroAxis)
        {
            Report("Pendant requested zero but AllowZeroAxis is disabled.");
            return;
        }

        var axis = GetString(root, "axis");
        if (string.IsNullOrEmpty(axis) || axis.Length > 1) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_mainViewModel == null) return;
            if (!_mainViewModel.Connected || _mainViewModel.AlarmActive) return;

            // Zeroing rewrites the work offset, so it is refused for the same
            // reasons jogging is - and more sharply. Doing it mid-job moves
            // every remaining coordinate in the file relative to the stock,
            // while the tool is in the cut.
            if (_machineState.MpgActive) return;
            if (_mainViewModel.JobViewModel?.JobRunning == true)
            {
                Report("Pendant zero ignored: a job is running.");
                return;
            }

            // G10 L20 sets the work offset so the current position reads the
            // given value on that axis alone, leaving the others untouched.
            _mainViewModel.SendCommand($"G10 L20 P0 {axis}0");
            Report($"Pendant zeroed {axis}.");
        });
    }

    // --- outbound status --------------------------------------------------

    /// <summary>
    /// Pushes machine status to whichever pendant is driving, and stands one down
    /// that has gone quiet.
    /// </summary>
    /// <remarks>
    /// The watchdog lives here because this is the one loop that ticks whether or
    /// not anything is arriving. It used to be a check inside the read loop,
    /// against a stopwatch that had just been restarted by the read that woke it
    /// - so it could only ever fire on a pendant that was still talking, which is
    /// to say never. A pendant that stops dead never wakes that loop at all.
    ///
    /// It matters more now than it did. A dropped socket is at least eventually
    /// noticed by TCP; a serial receiver holds its port open indefinitely, so
    /// without this a pendant switched off mid-session would read as connected
    /// until the application was restarted.
    /// </remarks>
    private async Task StatusLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var channel = _arbiter.Active;

                if (channel == null)
                {
                    await Task.Delay(IdlePollMs, token);
                    continue;
                }

                if (!channel.IsOpen)
                    RetireIfActive(channel, "transport closed");
                else if (SilentTooLong())
                    RetireIfActive(channel, $"silent for {_config.ClientTimeoutSeconds}s");
                else
                    channel.WriteLine(BuildStatus());

                await Task.Delay(_config.StatusIntervalMs, token);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    /// <summary>
    /// Whether the active pendant has said nothing for longer than the configured
    /// timeout. It pings every few seconds, so silence is not idleness - it is a
    /// flat battery, a lost radio link, or a half-open socket.
    /// </summary>
    private bool SilentTooLong()
    {
        var timeout = _config.ClientTimeoutSeconds;
        if (timeout <= 0) return false;

        return Environment.TickCount64 - Volatile.Read(ref _lastRxTicks) > timeout * 1000L;
    }

    private string BuildStatus()
    {
        // Millimetres, not display units. Every number in this frame is an
        // input to something on the pendant that computes in millimetres - its
        // step ladder, its per-step feed ceilings, its lag tracker's jump
        // threshold - and the jog blocks that come back state G21 for the same
        // reason. Sending what the interface happens to be showing put the one
        // unit conversion in this application that nobody is looking at
        // straight into the handheld's flow control.
        //
        // The failure was invisible from either end. With the sender in
        // imperial a machine running at 9000 mm/min was announced as 354, the
        // pendant read that as its drain rate and allowed one detent per 20 ms
        // tick where the hand was producing five, and dropped the rest. Nothing
        // reported an error: the pendant asked for its full feed, the sender
        // measured motion genuinely arriving slowly and lowered the commanded
        // feed to match, and both were behaving correctly on a premise that was
        // false. It reads on the machine as a pendant that will not keep up in
        // inches and is fine in millimetres, and it gets worse as it goes -
        // a throttled machine reports a lower feed, which throttles it further.
        var positions = _machineState.WorkPositionsMm ?? Array.Empty<double>();
        var builder = new StringBuilder(128);
        builder.Append("{\"t\":\"status\",\"state\":\"")
               .Append(_machineState.GrblStateString ?? "?")
               .Append("\",\"wpos\":[");

        for (var i = 0; i < 3; i++)
        {
            if (i > 0) builder.Append(',');
            var value = i < positions.Length ? positions[i] : 0;
            builder.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        // The controller's actual feed in mm/min, so the pendant can compare what
        // it asked for against what the machine is doing - and, since it caps
        // its own detent output at this, so it can tell how fast the controller
        // is draining what it has already been sent. A commanded feed that holds
        // steady while this collapses to zero and back is the planner running a
        // block at a time and decelerating at the end of each - which nothing on
        // the pendant side can see.
        // "bf" is the controller's free planner slots. It answers the question
        // the pendant cannot answer for itself: whether the blocks it believes
        // it has queued ahead actually reached the planner. Zero means the
        // buffer-state bit is off in $10 and the number is unavailable.
        //
        // "fr" is formatted invariantly rather than appended. It is a double,
        // and StringBuilder.Append(double) uses the current culture - so on a
        // comma-decimal machine the frame would carry {"fr":118,5}, which is
        // not the number the pendant would read even if the JSON survived it.
        // The overrides and the planner count are integers and cannot say it.
        builder.Append("],\"fro\":").Append(_machineState.FeedOverride)
               .Append(",\"sro\":").Append(_machineState.RpmOverride)
               .Append(",\"fr\":").Append(_machineState.FeedRateMmPerMin.ToInvariantString("0.###"))
               .Append(",\"bf\":").Append(_machineState.PlannerBlocksFree);

        // The corner a probe would use, and whether one is running. Sent so
        // the pendant can name the target on its own screen: firing a cycle
        // whose corner is only visible here, behind the operator, is the same
        // failure as the probe parameters that used to be shared.
        var probeVm = _mainViewModel?.ProbeViewModel;
        if (probeVm != null)
        {
            builder.Append(",\"probe\":{\"corner\":\"")
                   .Append(probeVm.SelectedCorner)
                   .Append("\",\"busy\":")
                   .Append(probeVm.IsProbing ? "true" : "false")
                   .Append('}');
        }

        builder.Append('}');
        return builder.ToString();
    }


    // --- helpers ----------------------------------------------------------

    /// <summary>
    /// The receiver has fallen back to its MicroPython prompt. Said once per
    /// port, and the pendant stood down so this end stops talking into it.
    /// </summary>
    /// <remarks>
    /// Standing down is the point, not the reporting. While a pendant is
    /// adopted the status loop writes ten frames a second, and with the bridge
    /// gone those frames are going into an interpreter that echoes and
    /// evaluates every one of them. Retiring stops that immediately rather than
    /// waiting out the silence watchdog, and costs nothing: the far end is not
    /// a pendant, so there is nothing to keep.
    ///
    /// The port stays open, so the moment the board is reset and its bridge
    /// runs again, the pendant's next ping is adopted and this heals itself.
    /// </remarks>
    private void HandleReceiverAtRepl(SerialPendantChannel channel)
    {
        if (!_reportedRepl)
        {
            _reportedRepl = true;
            Report("Receiver is sitting at its MicroPython prompt - main.py has " +
                   "stopped, so nothing is bridging the radio. Reset the board.");
        }

        RetireIfActive(channel, "receiver stopped running its bridge");
    }

    private void ReportMalformed(string line) =>
        ReportThrottled($"Pendant sent malformed JSON: {Truncate(line)}",
                        ref _lastMalformedTicks, ref _suppressedMalformed);

    /// <summary>
    /// Reports at most once per interval, carrying a count of what was dropped
    /// in between. The first of anything always gets through.
    /// </summary>
    /// <remarks>
    /// Every caller of this is something the far end can emit without limit, and
    /// the console is not a free place to put things: it has a line cap, and
    /// past that cap each addition costs repeated O(n) removals with a UI
    /// notification each, on the thread that draws the DRO. That load has been
    /// felt as jerk in the middle of a move twice in this project already.
    /// </remarks>
    private void ReportThrottled(string message, ref long lastTicks, ref int suppressed)
    {
        var now = Environment.TickCount64;
        if (now - lastTicks < MalformedReportIntervalMs)
        {
            suppressed++;
            return;
        }

        var skipped = suppressed;
        suppressed = 0;
        lastTicks = now;

        Report(skipped > 0 ? $"{message} (and {skipped} more)" : message);
    }

    /// <summary>
    /// Millimetres per minute per inch per minute. The pendant protocol is
    /// millimetres throughout, so anything arriving in the operator's units has
    /// to be converted before it reaches a jog block.
    /// </summary>
    private const double MmPerInch = 25.4;

    /// <summary>
    /// The feed to command when the pendant asks for none, in mm/min.
    /// </summary>
    /// <remarks>
    /// The pendant normally sends a feed of its own and this is not reached. It
    /// is reached whenever that word is missing or unreadable, though - a feed
    /// serialised as a JSON string rather than a number reads as zero here -
    /// and until now what it fell back to was wrong in imperial.
    ///
    /// The number comes from the jog rate list the operator picked from, which
    /// is built from JogSpeedMetric or JogSpeedImperial according to the Metric
    /// UI setting, so it is in display units. The jog block states G21 and its
    /// F word is therefore read as mm/min. In metric the two agree by accident
    /// and nothing looks wrong; in imperial the list offers 10-300 in/min and
    /// passing 300 straight through commands 300 mm/min, which is 11.8 in/min -
    /// a twenty-fivefold shortfall that reads on the machine as a pendant that
    /// crawls and stumbles in imperial while behaving in metric.
    ///
    /// The shortfall is felt as roughness and not just slowness, because the
    /// distance in each block is untouched. Movement keeps arriving at the
    /// speed of the hand while the blocks carrying it are commanded to take
    /// twenty-five times as long, so the machine falls progressively further
    /// behind the wheel and keeps running after it stops.
    ///
    /// Converted here rather than at the jog line so the millimetre contract
    /// stays in one place, and against the configured UI units rather than the
    /// machine's $13, since the UI setting is what chose the list.
    /// </remarks>
    /// <summary>
    /// The commanded feed snapped to the configured grid, holding the previous
    /// value while the request stays within one grid step of it.
    /// </summary>
    /// <remarks>
    /// Rounded to nearest rather than down, and never to nothing. A feed under
    /// half a quantum still has to carry the distance already in the block, and
    /// an F of zero is not a slow move - it is a line the controller rejects.
    ///
    /// The hysteresis is what makes the grid worth having below the pendant's
    /// own ceiling. A bare snap changes the F word every time the request
    /// crosses a boundary, so a hand drifting around 3250 on a 500 grid flips
    /// between 3000 and 3500 over and over - and each flip is a velocity change
    /// the planner has to ramp through. It converts small wobble into whole
    /// grid steps rather than removing it.
    ///
    /// Measured across twelve bursts: every one near the pendant's step ceiling
    /// changed feed on 3-7% of blocks, because a saturated request cannot
    /// wobble at all. The single burst at roughly half speed changed on 30%,
    /// which is what boundary-flapping looks like.
    ///
    /// The slack is deliberately asymmetric: a full grid step to come down, half
    /// a step to go up.
    ///
    /// Symmetric slack made the top end unreachable at a coarse grid, and the
    /// coarser the grid the worse it got. Held at 6000 on a 2000 grid, nothing
    /// below a request of 8000 could move it - and 8000 is the pendant's own
    /// ceiling for that step, so the operator had to hit the absolute maximum
    /// exactly to leave 75% of it. Measured as "smoother at half speed but the
    /// top end is almost unreachable", with feed changes down at 1-4% and the
    /// machine stuck below the speed being asked for.
    ///
    /// Asking for more speed is answered promptly; a hand wobbling downward is
    /// not. That is the same shape as the pendant's own FEED_DEADBAND, which
    /// lets the feed climb on demand and decay lazily, and it is the right way
    /// round: being slower than the hand is felt immediately, being briefly
    /// faster is absorbed by the arrival ceiling above.
    ///
    /// Nothing runs away upward. The request is bounded by the handheld's step
    /// ceiling before this sees it, and the arrival ceiling has already capped
    /// it to what is actually turning up.
    /// </remarks>
    internal static double SnapFeed(double feed, double quantum, double previous = 0,
                                    double riseSteps = 0.5, double fallSteps = 1.0)
    {
        if (quantum <= 0 || feed <= 0) return feed;

        if (previous > 0)
        {
            var steps = feed > previous ? riseSteps : fallSteps;
            if (steps > 0 && Math.Abs(feed - previous) < quantum * steps) return previous;
        }

        var snapped = Math.Round(feed / quantum) * quantum;
        return snapped < quantum ? quantum : snapped;
    }

    /// <summary>
    /// Raises the commanded feed to the slowest rate the pendant can actually
    /// deliver at this step, but never past what the pendant asked for.
    /// </summary>
    /// <remarks>
    /// The handheld throttles its own detents from the feed this end reports,
    /// so commanding low is normally self-correcting - right up to its
    /// one-detent-per-tick clamp, below which it cannot slow down however it is
    /// asked. Command under that while the wheel turns and the surplus has
    /// nowhere to go but the planner, arriving later as travel after the hand
    /// has stopped.
    ///
    /// The request is the upper bound because it already carries the firmware's
    /// own ceiling for the step. A pendant asking for less than its floor is
    /// being turned too slowly to reach the clamp, so there is nothing piling
    /// up to prevent, and raising it there would command motion nobody asked
    /// for.
    /// </remarks>
    internal static double ApplyDeliveryFloor(double feed, double step,
                                              double askedFor, double quantum)
    {
        if (step <= 0) return feed;

        var floor = step * PendantFloorPerStepMmPerMin;
        if (askedFor > 0 && floor > askedFor) floor = askedFor;
        if (feed >= floor) return feed;

        // Snapped up so the floor lands on the grid rather than reintroducing
        // the off-grid values the quantum exists to remove - then held to the
        // request again, because rounding up is itself a way through it. A
        // floor of 600 on a 500 grid rounds to 1000, which is the same mistake
        // this method exists to prevent, arriving by the back door.
        var raised = SnapFeedUp(floor, quantum);
        return askedFor > 0 && raised > askedFor ? askedFor : raised;
    }

    /// <summary>
    /// The feed rounded down onto the grid, for holding it under a ceiling
    /// without leaving the grid to do it.
    /// </summary>
    /// <remarks>
    /// A request below one whole quantum has no grid value beneath it except
    /// zero, so there it stands unchanged. That is a real gap rather than a
    /// tidy edge case: the coarser the grid, the more of the range sits under
    /// the first step and escapes quantization entirely, which is part of why
    /// a 2000 grid behaved worse than a 500 one rather than merely coarser.
    /// </remarks>
    internal static double SnapFeedDown(double feed, double quantum)
    {
        if (quantum <= 0 || feed <= 0) return feed;
        var snapped = Math.Floor(feed / quantum) * quantum;
        return snapped < quantum ? feed : snapped;
    }

    /// <summary>
    /// The feed rounded up onto the grid, for the delivery floor. Never below
    /// what was asked of it, which is the whole point of a floor.
    /// </summary>
    internal static double SnapFeedUp(double feed, double quantum)
    {
        if (quantum <= 0 || feed <= 0) return feed;
        return Math.Ceiling(feed / quantum) * quantum;
    }

    private double FallbackJogFeedMmPerMin() =>
        ToMmPerMin(_mainViewModel?.JogRate ?? 0,
                   _configManager.GHalSenderConfig?.UseMetric ?? true);

    /// <summary>
    /// A feed in the operator's display units, in mm/min.
    /// </summary>
    internal static double ToMmPerMin(double feed, bool useMetric) =>
        useMetric || feed <= 0 ? feed : feed * MmPerInch;

    private void RecordJogArrival()
    {
        var now = Environment.TickCount64;
        lock (_arrivalLock)
        {
            if (_jogArrivals == 0)
            {
                _burstStartTicks = now;
                _dispatches = 0;
                _feedChanges = 0;
                _lastDispatchFeed = 0;
                _feedMin = double.MaxValue;
                _feedMax = 0;
                _rawFeedMin = double.MaxValue;
                _rawFeedMax = 0;
                _plannerFreeMin = int.MaxValue;
                _plannerFreeMax = 0;
                _burstDistance = 0;
                _burstDetents = 0;
                _stepMin = double.MaxValue;
                _stepMax = 0;
                _arrivalMin = double.MaxValue;
                _arrivalMax = 0;
            }
            else
            {
                var gap = now - _lastJogArrivalTicks;
                if (gap > _worstGapMs) _worstGapMs = gap;
                if (gap > JogGapThresholdMs) _longGaps++;
            }

            _lastJogArrivalTicks = now;
            _jogArrivals++;
        }
    }

    /// <summary>
    /// Millimetres per minute that movement is actually turning up at, or zero
    /// while there is too little history to say.
    /// </summary>
    /// <remarks>
    /// Returning zero matters as much as the number does. After a pause there is
    /// nothing to measure, and inventing a rate from one block is what commanded
    /// single-digit feeds and left the machine crawling through a full planner.
    /// With no measurement the pendant's own figure stands, which is the
    /// behaviour this had before any of it.
    /// </remarks>
    private double MeasureArrivalRate(double distance)
    {
        var now = Environment.TickCount64;
        _recentMotion.Enqueue((now, distance));

        while (_recentMotion.Count > 0 && now - _recentMotion.Peek().Tick > RateWindowMs)
            _recentMotion.Dequeue();

        return ArrivalRate(_recentMotion, now);
    }

    /// <summary>
    /// The rate movement is arriving at, in mm/min, from the window of recently
    /// dispatched blocks - or zero while there is too little history to say.
    /// </summary>
    /// <remarks>
    /// Separated from the window it reads so the thresholds can be exercised
    /// against a stream of a chosen cadence. They are tuning constants found at
    /// the machine, which is exactly the kind of thing that drifts unnoticed.
    /// </remarks>
    internal static double ArrivalRate(IEnumerable<(long Tick, double Distance)> window, long now)
    {
        var samples = 0;
        var total = 0.0;
        var recent = 0.0;
        var recentCount = 0;

        long span = 0;
        long recentSpan = 0;
        long previous = 0;
        var havePrevious = false;

        // One ordered pass. The window is a queue, so it enumerates oldest
        // first, and the span is built from the intervals between arrivals
        // rather than from the wall clock across them.
        foreach (var (tick, moved) in window)
        {
            samples++;
            total += moved;

            if (havePrevious)
            {
                // A stall contributes one ordinary interval and no more.
                //
                // Taken as raw elapsed time, a gap adds its whole duration to
                // the denominator and no distance to the numerator, so the
                // measured rate collapses in proportion - and with the ceiling
                // taken from it, a hiccup on the radio arrives at the machine
                // as a speed change. Seen as "arriving 640-12827 mm/min" in a
                // burst carrying one 406 ms gap: a twenty to one spread in a
                // number describing how fast a hand was moving.
                //
                // Capping rather than discarding, because dropping the
                // interval entirely leaves the distance either side of it
                // divided by a span that no longer covers it, which reads as a
                // burst of speed that never happened. Capped, a stall costs
                // the distance that did not arrive during it and nothing more.
                var step = Math.Min(tick - previous, JogGapThresholdMs);
                span += step;
                if (now - tick <= RateRecentMs) recentSpan += step;
            }

            previous = tick;
            havePrevious = true;

            if (now - tick > RateRecentMs) continue;
            recent += moved;
            recentCount++;
        }

        if (samples < RateMinSamples) return 0;

        // And the silence since the newest sample, bounded the same way, so a
        // rate measured a moment after the last block still reflects it.
        var tail = Math.Min(now - previous, JogGapThresholdMs);
        span += tail;
        recentSpan += tail;

        if (span < RateMinSpanMs) return 0;
        if (total <= 0) return 0;

        var rate = total / (double)span;

        // Only once the short window covers enough time, and holds enough
        // messages, to mean anything. One block spanning a few milliseconds
        // would read as an enormous rate and lift the ceiling out of the way
        // entirely; three spread over a sparse stream do the same thing more
        // slowly. See RateRecentMinSamples.
        if (recent > 0 && recentCount >= RateRecentMinSamples &&
            recentSpan >= RateRecentMinSpanMs)
        {
            var recentRate = recent / (double)recentSpan;
            if (recentRate > rate) rate = recentRate;
        }

        return rate * 60000.0 * RateHeadroom;
    }

    /// <summary>
    /// Records what one dispatched block asked the controller for, and how deep
    /// the controller's planner was when it was asked.
    /// </summary>
    private void RecordDispatch(double feed, double raw, double distance, double arriving)
    {
        // Sampled at dispatch rather than on a timer, so the depth is the one
        // the controller had when this block reached it.
        var free = _machineState.PlannerBlocksFree;

        lock (_arrivalLock)
        {
            _dispatches++;
            if (_dispatches > 1 && feed != _lastDispatchFeed) _feedChanges++;
            _lastDispatchFeed = feed;
            if (feed < _feedMin) _feedMin = feed;
            if (feed > _feedMax) _feedMax = feed;

            // Distance as it goes out, so this is what the machine was actually
            // told to travel and not what arrived and was later dropped.
            _burstDistance += Math.Abs(distance);

            // Zero means there was too little history to measure - the start of
            // a burst - and folding that in as a rate would read as a stall
            // that never happened.
            if (arriving > 0)
            {
                if (arriving < _arrivalMin) _arrivalMin = arriving;
                if (arriving > _arrivalMax) _arrivalMax = arriving;
            }

            if (free <= 0) return;              // buffer report off in $10
            if (free < _plannerFreeMin) _plannerFreeMin = free;
            if (free > _plannerFreeMax) _plannerFreeMax = free;
        }
    }

    /// <summary>
    /// Summarises a burst of pendant movement once the wheel has stopped: how it
    /// arrived, and what was made of it. One pair of lines per burst, not per
    /// message - the per-message form already exists as EchoJogsToConsole and is
    /// documented there as filling the console within seconds of a traverse.
    /// </summary>
    private void ReportJogBurstIfQuiet()
    {
        int arrivals, longGaps, dispatches, feedChanges, freeMin, freeMax;
        long worst, span;
        double feedMin, feedMax, rawMin, rawMax;
        double travel, detents, stepMin, stepMax, arriveMin, arriveMax;

        lock (_arrivalLock)
        {
            if (_jogArrivals == 0) return;
            if (Environment.TickCount64 - _lastJogArrivalTicks < JogBurstQuietMs) return;

            arrivals = _jogArrivals;
            longGaps = _longGaps;
            worst = _worstGapMs;
            span = _lastJogArrivalTicks - _burstStartTicks;
            dispatches = _dispatches;
            feedChanges = _feedChanges;
            feedMin = _feedMin;
            feedMax = _feedMax;
            rawMin = _rawFeedMin;
            rawMax = _rawFeedMax;
            freeMin = _plannerFreeMin;
            freeMax = _plannerFreeMax;
            travel = _burstDistance;
            detents = _burstDetents;
            stepMin = _stepMin;
            stepMax = _stepMax;
            arriveMin = _arrivalMin;
            arriveMax = _arrivalMax;

            _jogArrivals = 0;
            _longGaps = 0;
            _worstGapMs = 0;
        }

        // The wheel has stopped, so the next burst starts from whatever it then
        // asks for rather than from where this one left off.
        //
        // Carrying the average across a pause commanded a feed nobody asked
        // for: stop at 8000, start again gently at 900, and the first blocks go
        // out near 8000 while the average decays. The console showed it as a
        // commanded range above the requested one, which cannot otherwise
        // happen - the ceiling only ever lowers. On the machine it is a burst
        // that sprints and then settles, which is the wrong way round.
        //
        // It also lands in the one window the arrival-rate ceiling cannot
        // cover, since three samples of history do not exist yet.
        lock (_jogLock)
        {
            _smoothedFeed = 0;
            _rawFeed = 0;
        }

        // The held feed goes with them. Carrying it across a pause would hold
        // the next burst at a speed the previous one was turning at, which is
        // the same mistake the smoothed feed made here before it.
        _lastCommandedFeed = 0;

        // A nudge of the wheel says nothing about steadiness.
        if (arrivals < 10) return;

        var average = span / (double)(arrivals - 1);
        Report($"Pendant jog stream: {arrivals} messages over {span / 1000.0:0.0} s, " +
               $"average {average:0} ms apart, worst gap {worst} ms, " +
               $"{longGaps} over {JogGapThresholdMs} ms.");

        if (dispatches == 0) return;

        var planner = freeMax > 0
            ? $"planner free {freeMin}-{freeMax}"
            : "planner depth unavailable ($10 buffer report off)";

        var asked = _config.SmoothJogFeed && rawMax > 0
            ? $" (pendant asked {rawMin.ToInvariantString("0")}-{rawMax.ToInvariantString("0")})"
            : string.Empty;

        Report($"Pendant jog dispatch: {dispatches} blocks, " +
               $"feed {feedMin.ToInvariantString("0")}-{feedMax.ToInvariantString("0")} mm/min" +
               $"{asked}, feed changed {feedChanges}/{dispatches}, {planner}.");

        // The distance the feed lines above are a rate for. Reported separately
        // because it answers a different question: those say how fast the
        // machine was told to go, this says how much there was to go at.
        var step = stepMax > 0
            ? (stepMin == stepMax
                ? $"step {stepMin.ToInvariantString("0.####")} mm"
                : $"step {stepMin.ToInvariantString("0.####")}-{stepMax.ToInvariantString("0.####")} mm")
            : "step not sent";

        var perDetent = detents > 0
            ? $"{(travel / detents).ToInvariantString("0.####")} mm per detent over " +
              $"{detents.ToInvariantString("0")} detents"
            : "no detents counted";

        var arriving = arriveMax > 0
            ? $"arriving {arriveMin.ToInvariantString("0")}-{arriveMax.ToInvariantString("0")} mm/min"
            : "arrival rate never measured";

        Report($"Pendant jog travel: {travel.ToInvariantString("0.##")} mm, " +
               $"{perDetent}, {step}, {arriving}.");
    }

    private void ClearPendingJog()
    {
        lock (_jogLock) { _pendingDistance = 0; _pendingAxis = null; _pendingStep = 0; }
    }

    private void SetConnected(bool connected)
    {
        if (IsPendantConnected == connected) return;
        IsPendantConnected = connected;
        PendantConnectionChanged?.Invoke(this, connected);
    }

    private void Report(string message)
    {
        Debug.WriteLine($"[Pendant] {message}");
        PendantStatusMessage?.Invoke(this, message);
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static double GetDouble(JsonElement root, string name, double fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : fallback;

    private static string Truncate(string text) =>
        text.Length <= 80 ? text : text[..80] + "...";
}
