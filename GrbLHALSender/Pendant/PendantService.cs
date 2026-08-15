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

    // Said once per port session, then reset when the port is reopened.
    private bool _reportedRepl;

    private readonly object _jogLock = new();
    private string? _pendingAxis;
    private double _pendingDistance;
    private double _pendingFeed;

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
    private int _plannerFreeMin;
    private int _plannerFreeMax;

    public event EventHandler<bool>? PendantConnectionChanged;
    public event EventHandler<string>? PendantStatusMessage;

    public bool IsPendantConnected { get; private set; }
    public string? PendantAxis { get; private set; }
    public double PendantStep { get; private set; }

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
        // on: cancel whatever it had in flight.
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
                    else Report($"Pendant sent malformed JSON: {Truncate(line)}");
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
                    DtrEnable = true,
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
        }
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
            if (requested > 0) _pendingFeed = requested;
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
                double distance, requested;
                lock (_jogLock)
                {
                    axis = _pendingAxis;
                    distance = _pendingDistance;
                    requested = _pendingFeed;
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
                if (_mainViewModel == null) continue;
                if (!_mainViewModel.Connected || _mainViewModel.AlarmActive) continue;

                // A hardware MPG can take the controller's input stream via the
                // MPG_MODE pin or the 0x8B toggle, and while it holds it this
                // sender is not the stream in control. Writing jogs into it
                // then is at best ignored and at worst interleaves this
                // pendant's motion with the other device's.
                //
                // Two operators driving one axis from two places is the
                // failure worth refusing outright rather than arbitrating.
                if (_machineState.MpgActive) continue;

                // And never mid-job. A jog injected into a running stream is
                // not merely rejected by the controller: this sender counts
                // characters to track what the controller is holding, and an
                // "ok" returned for an out-of-band line is credited to a job
                // line instead. The stream accounting desyncs from that point
                // on, in the middle of a cut.
                //
                // A hold is still a running job - the file is loaded, the
                // position is committed, and resuming expects the machine
                // where it was left. Jogging away from that is how a resume
                // plunges into the work.
                if (_mainViewModel.JobViewModel?.JobRunning == true) continue;

                // The pendant sends a feed matching how fast the wheel is
                // being turned, which is what makes the machine track the
                // hand rather than lag it. Fall back to configuration only
                // when it does not, and never exceed the ceiling.
                var feed = requested > 0
                    ? requested
                    : (_config.JogFeedRate > 0 ? _config.JogFeedRate : _mainViewModel.JogRate);

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
                RecordDispatch(feed);

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
            // Deliberately guarded on connection alone - not on a running job,
            // an alarm, or MPG mode, the way jogging and zeroing are.
            //
            // These are single-byte real-time commands. They bypass the line
            // buffer entirely, so they cannot desync the stream accounting that
            // makes an injected jog dangerous mid-job. And they are exactly the
            // commands wanted at the worst moment: a feed hold that stops
            // working once a job starts is worse than no button at all, and
            // cycle start is how the operator resumes from that hold.
            //
            // Do not extend the jog guards to cover this.
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
        var positions = _machineState.WorkPositions ?? Array.Empty<double>();
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

        // The controller's actual feed, so the pendant can compare what it asked
        // for against what the machine is doing. A commanded feed that holds
        // steady while this collapses to zero and back is the planner running a
        // block at a time and decelerating at the end of each - which nothing on
        // the pendant side can see.
        // "bf" is the controller's free planner slots. It answers the question
        // the pendant cannot answer for itself: whether the blocks it believes
        // it has queued ahead actually reached the planner. Zero means the
        // buffer-state bit is off in $10 and the number is unavailable.
        builder.Append("],\"fro\":").Append(_machineState.FeedOverride)
               .Append(",\"sro\":").Append(_machineState.RpmOverride)
               .Append(",\"fr\":").Append(_machineState.FeedRate)
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

    private void RecordJogArrival()
    {
        var now = Environment.TickCount64;
        lock (_arrivalLock)
        {
            if (_jogArrivals == 0)
            {
                _burstStartTicks = now;
                _dispatches = 0;
                _feedMin = double.MaxValue;
                _feedMax = 0;
                _plannerFreeMin = int.MaxValue;
                _plannerFreeMax = 0;
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
    /// Records what one dispatched block asked the controller for, and how deep
    /// the controller's planner was when it was asked.
    /// </summary>
    private void RecordDispatch(double feed)
    {
        // Sampled at dispatch rather than on a timer, so the depth is the one
        // the controller had when this block reached it.
        var free = _machineState.PlannerBlocksFree;

        lock (_arrivalLock)
        {
            _dispatches++;
            if (feed < _feedMin) _feedMin = feed;
            if (feed > _feedMax) _feedMax = feed;

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
        int arrivals, longGaps, dispatches, freeMin, freeMax;
        long worst, span;
        double feedMin, feedMax;

        lock (_arrivalLock)
        {
            if (_jogArrivals == 0) return;
            if (Environment.TickCount64 - _lastJogArrivalTicks < JogBurstQuietMs) return;

            arrivals = _jogArrivals;
            longGaps = _longGaps;
            worst = _worstGapMs;
            span = _lastJogArrivalTicks - _burstStartTicks;
            dispatches = _dispatches;
            feedMin = _feedMin;
            feedMax = _feedMax;
            freeMin = _plannerFreeMin;
            freeMax = _plannerFreeMax;

            _jogArrivals = 0;
            _longGaps = 0;
            _worstGapMs = 0;
        }

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

        Report($"Pendant jog dispatch: {dispatches} blocks, " +
               $"feed {feedMin.ToInvariantString("0")}-{feedMax.ToInvariantString("0")} mm/min, " +
               $"{planner}.");
    }

    private void ClearPendingJog()
    {
        lock (_jogLock) { _pendingDistance = 0; _pendingAxis = null; }
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
