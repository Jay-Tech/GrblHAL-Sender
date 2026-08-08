using Avalonia.Threading;
using GrbLHALSender.Configuration;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using GrbLHALSender.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

    private TcpClient? _client;
    private readonly object _clientLock = new();

    // Set after construction to avoid circular DI, matching GamepadService.
    private MainViewModel? _mainViewModel;

    // Jog movement accumulated between dispatches. Summing rather than
    // forwarding each message is what keeps the controller's planner supplied
    // without being flooded; see JogLoopAsync.
    private readonly object _jogLock = new();
    private string? _pendingAxis;
    private double _pendingDistance;
    private double _pendingFeed;

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

        if (_listener != null) return;

        try
        {
            var address = IPAddress.Parse(_config.BindAddress);
            _listener = new TcpListener(address, _config.Port);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
            Report($"Pendant listener on {_config.BindAddress}:{_config.Port}");
        }
        catch (Exception ex)
        {
            Report($"Pendant listener failed to start: {ex.Message}");
            _listener = null;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        CloseClient("service stopping");

        try { _listener?.Stop(); } catch { /* already torn down */ }
        _listener = null;

        try { _acceptTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _acceptTask = null;

        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();

    // --- accept -----------------------------------------------------------

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var incoming = await _listener!.AcceptTcpClientAsync(token);

                // One pendant at a time, newest wins. The usual reason a second
                // connection arrives is that the first is a stale half-open
                // socket from a pendant that lost WiFi; refusing would lock the
                // operator out until that socket finally timed out.
                CloseClient("superseded by a new pendant");

                incoming.NoDelay = true;
                lock (_clientLock) _client = incoming;

                SetConnected(true);
                Report($"Pendant connected from {incoming.Client.RemoteEndPoint}");

                _ = Task.Run(() => SessionAsync(incoming, token), token);
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

    private async Task SessionAsync(TcpClient client, CancellationToken token)
    {
        var buffer = new byte[2048];
        var pending = new StringBuilder();
        var lastRx = Stopwatch.StartNew();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        var statusTask = Task.Run(() => StatusLoopAsync(client, linked.Token), linked.Token);
        var jogTask = Task.Run(() => JogLoopAsync(linked.Token), linked.Token);

        try
        {
            var stream = client.GetStream();
            while (!linked.Token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, linked.Token);
                if (read == 0) break;                       // pendant closed

                lastRx.Restart();
                pending.Append(Encoding.UTF8.GetString(buffer, 0, read));

                foreach (var line in TakeLines(pending))
                    HandleMessage(line, stream);

                if (lastRx.Elapsed.TotalSeconds > _config.ClientTimeoutSeconds)
                    break;
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            Report($"Pendant session ended: {ex.Message}");
        }
        finally
        {
            linked.Cancel();
            try { await statusTask; } catch { /* ignore */ }
            try { await jogTask; } catch { /* ignore */ }
            lock (_jogLock) { _pendingDistance = 0; _pendingAxis = null; }

            // A pendant that vanishes mid-jog must not leave the machine
            // running on: cancel whatever it had in flight.
            Dispatcher.UIThread.Post(() => _mainViewModel?.JogCancel());

            CloseClient("pendant disconnected");
        }
    }

    /// <summary>
    /// Pull complete newline-delimited messages out of the buffer, leaving any
    /// partial tail behind. TCP gives no message boundaries, so a read can land
    /// mid-object or carry several at once.
    /// </summary>
    private static IEnumerable<string> TakeLines(StringBuilder pending)
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

    private void HandleMessage(string line, NetworkStream stream)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(line);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            Report($"Pendant sent malformed JSON: {Truncate(line)}");
            return;
        }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("t", out var typeElement))
            return;

        switch (typeElement.GetString())
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

            case "mode":
                PendantAxis = GetString(root, "axis");
                PendantStep = GetDouble(root, "step", 0);
                break;

            case "ping":
                Send(stream, $"{{\"t\":\"pong\",\"seq\":{GetDouble(root, "seq", 0):0}}}");
                break;
        }
    }

    private void HandleJog(JsonElement root)
    {
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
    /// </summary>
    private async Task JogLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(_config.JogDispatchIntervalMs, token);

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

                Dispatcher.UIThread.Post(() =>
                {
                    if (_mainViewModel == null) return;
                    if (!_mainViewModel.Connected || _mainViewModel.AlarmActive) return;

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
                    _mainViewModel.SendCommand(
                        $"$J=G91G21{axis}{distance.ToInvariantString()}F{feed.ToInvariantString()}");
                });
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
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

            // G10 L20 sets the work offset so the current position reads the
            // given value on that axis alone, leaving the others untouched.
            _mainViewModel.SendCommand($"G10 L20 P0 {axis}0");
            Report($"Pendant zeroed {axis}.");
        });
    }

    // --- outbound status --------------------------------------------------

    private async Task StatusLoopAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            var stream = client.GetStream();
            while (!token.IsCancellationRequested && client.Connected)
            {
                Send(stream, BuildStatus());
                await Task.Delay(_config.StatusIntervalMs, token);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception)
        {
            // The session loop owns teardown; a write failure here just ends
            // this task.
        }
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

        builder.Append("],\"fro\":").Append(_machineState.FeedOverride)
               .Append(",\"sro\":").Append(_machineState.RpmOverride)
               .Append('}');
        return builder.ToString();
    }

    private void Send(NetworkStream stream, string message)
    {
        try
        {
            var payload = Encoding.UTF8.GetBytes(message + "\n");
            stream.Write(payload, 0, payload.Length);
        }
        catch (Exception)
        {
            // Peer has gone; the session loop notices and tears down.
        }
    }

    // --- helpers ----------------------------------------------------------

    private void CloseClient(string reason)
    {
        TcpClient? existing;
        lock (_clientLock)
        {
            existing = _client;
            _client = null;
        }

        if (existing == null) return;

        try { existing.Close(); } catch { /* already gone */ }
        SetConnected(false);
        Report($"Pendant connection closed ({reason}).");
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
