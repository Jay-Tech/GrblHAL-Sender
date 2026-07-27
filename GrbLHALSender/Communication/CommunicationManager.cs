using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using GrbLHALSender.Configuration;
using GrbLHALSender.Probe;
using GrbLHALSender.Settings;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;


namespace GrbLHALSender.Communication
{
    public record AuxPinInfo(string Description, int PortNumber);

    /// <summary>
    /// A command that has just been written to the controller.
    /// <paramref name="IsStreamLine"/> distinguishes a line of a streamed job file from
    /// anything else — a jog, a macro, an aux output button, an injected event command.
    /// </summary>
    public record CommandSentEventArgs(string Command, bool IsStreamLine);

    /// <summary>
    /// The controller's response to one command. grblHAL answers every line with either
    /// "ok" or "error:N", and either way that command is finished and its bytes have left
    /// the RX buffer — so both must be reported, or anything counting outstanding
    /// commands loses its place at the first rejected line.
    /// </summary>
    public record CommandAck(bool IsError, int ErrorCode)
    {
        public static readonly CommandAck Ok = new(false, 0);
    }

    public class CommunicationManager
    {
        private readonly ConfigManager _configManager;
        private const string StateString = "Idle|Run|Hold|Jog|Alarm:|Door|Check|Home|Sleep|Tool";

        // ~15s of retries at 500ms before the connect query sequence gives up on $I+.
        private const int MaxInfoAttempts = 30;

        public event EventHandler<string> OnConsoleLogReceived;
        public event EventHandler<RealTImeState> OnStateReceived;
        public event EventHandler<List<GrblHalSetting>> onSettingUpdated;
        public event EventHandler<GrblHALOptions> onOptionsUpdated;
        public event EventHandler<ProbeState> OnProbeResults;
        /// <summary>
        /// One response per command sent: "ok", or "error:N" carrying the code. Both end
        /// the command, so subscribers that track outstanding commands must treat them
        /// alike; only the caller decides whether the failure matters.
        /// </summary>
        public event EventHandler<CommandAck>? OnCommandAck;
        public event EventHandler<List<AuxPinInfo>> OnAuxPinsDiscovered;

        /// <summary>
        /// Every command written to the controller, whatever raised it — a panel button,
        /// MDI, a macro, a streamed job line, or a G-code event rule's injected command.
        /// Lets UI that mirrors controller state notice changes it did not initiate, and
        /// lets the job streamer account for commands it did not send.
        /// <para>
        /// Raised while holding the write lock, so handlers observe commands in the exact
        /// order they reached the wire. Handlers run on the caller's thread, which during
        /// streaming is the comms thread, so they must marshal any UI work themselves and
        /// must not block.
        /// </para>
        /// </summary>
        public event EventHandler<CommandSentEventArgs>? OnCommandSent;


        private Dictionary<int, string> _errorCodes = new Dictionary<int, string>();
        private Dictionary<int, string> _alarmCodes = new Dictionary<int, string>();
        private bool _messageCompleted;
        private MachineSettings _machineData;
        private Timer _pollTimer;
        private readonly Dispatcher _dispatcher;
        private GrblHALSettings _grblHalSettings;
        private GrblHALOptions grblHalOptions = new();
        private readonly ProbeState _probe;
        private double _pollInterval;

        // Serializes command writes with their OnCommandSent notification.
        private readonly object _writeLock = new();


        public ICommsAdapter Adapter { get; set; }

        /// <summary>
        /// True while a job is being streamed to the controller.
        /// <para>
        /// The streamer uses grblHAL's character-counting protocol: it tracks the bytes
        /// of every line it has sent and frees them as each "ok" arrives. A command sent
        /// from anywhere else during that window breaks both halves of that bookkeeping —
        /// its bytes occupy the controller's RX buffer unaccounted (risking an overflow
        /// that corrupts the g-code mid-cut), and its "ok" is credited to a streamed
        /// line that has not actually completed. So the request/response helpers below
        /// refuse to send while this is set.
        /// </para>
        /// </summary>
        public bool IsStreaming { get; private set; }

        /// <summary>Called by the job streamer when it takes exclusive use of the link.</summary>
        public void BeginStreaming() => IsStreaming = true;

        /// <summary>Called by the job streamer when the job ends, however it ended.</summary>
        public void EndStreaming() => IsStreaming = false;

        public MachineSettings MachineData => _machineData;
        public GrblHALOptions Options => grblHalOptions;
        public Type? ActiveAdapterType => Adapter?.GetType();
        public CommunicationManager(ConfigManager configManager)
        {
            _configManager = configManager;
            _dispatcher = Dispatcher.UIThread;
            _grblHalSettings = new GrblHALSettings();
            _pollTimer = new Timer();
            _pollTimer.Elapsed += _pollTimer_Elapsed;
            _probe = new ProbeState();
            _configManager.OnConfigLoaded += _configManger_OnConfigLoaded;
        }

        private void _configManger_OnConfigLoaded(object? sender, GHalSenderConfig e)
        {
            if (_configManager.GHalSenderConfig == null) return;
            _pollInterval = _configManager.GHalSenderConfig.PollRate;
            _configManager?.GHalSenderConfig?.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GHalSenderConfig.PollRate))
                {
                    UpdateRealTimePoll();
                }
            };

        }

        private void UpdateRealTimePoll()
        {
            StopPoll();
            SetupPoll();
        }

        public void ShutDown()
        {
            _pollTimer?.Stop();
            if (Adapter != null)
            {
                try
                {
                    Adapter.WriteByte(0x18); // grblHAL soft-reset — flushes planner
                    Thread.Sleep(50);
                }
                catch
                {
                    // A dead/disconnected adapter must not abort the shutdown
                    // sequence — the caller still has cleanup (and possibly an
                    // OS power-off) to run after this returns.
                }
                Adapter.OnDataReceived -= Adapter_OnDataReceived;
                Adapter.Close();
            }
        }

        /// <summary>Sends a command that is not part of a streamed job.</summary>
        public void SendCommand(string command) => Write(command, isStreamLine: false);

        /// <summary>
        /// Sends one line of a streamed job. Separate from <see cref="SendCommand"/> only
        /// so the streamer can tell its own lines from everything else in OnCommandSent.
        /// </summary>
        public void SendStreamLine(string command) => Write(command, isStreamLine: true);

        private void Write(string command, bool isStreamLine)
        {
            // The write and the notification are one step. The job streamer records what
            // the controller is holding from this event, and that record is only correct
            // if it observes commands in the same order the bytes went out — otherwise a
            // command sent from the UI thread mid-stream can be recorded out of sequence
            // and its "ok" credited to the wrong entry.
            lock (_writeLock)
            {
                Adapter?.WriteCommand(command);
                OnCommandSent?.Invoke(this, new CommandSentEventArgs(command, isStreamLine));
            }
        }
        public void GetSettings()
        {
            var t = Task.Factory.StartNew(async () =>
            {
                // Reset per-connection option state so stale data from a prior session
                // doesn't leak into the new one, and so retries start from a clean slate.
                grblHalOptions.AxisLabels.Clear();
                grblHalOptions.SignalLabels.Clear();

                // Bounded retry. This used to loop until $I+ answered, with no exit — a
                // controller that never replies (or a query refused because a job is
                // streaming) left this task spinning for the life of the process, and a
                // second connect attempt added another one. Give up after a few tries and
                // continue: the code below is written to cope with missing option data.
                var infoResults = await SendAsyncCommand(GrblHalConstants.GetinfoExtended, timeOutMs: 1000);
                for (var attempt = 0; !infoResults && attempt < MaxInfoAttempts; attempt++)
                {
                    await Task.Delay(500);
                    infoResults = await SendAsyncCommand(GrblHalConstants.GetinfoExtended, timeOutMs: 1000);
                }
                // Always fire onOptionsUpdated after $I+ completes, regardless of which
                // attempt succeeded. Previously this only ran on the fast path, which
                // meant a timed-out first attempt left the UI with no signal/axis data.
                SendOptions();

                var settings = await SendAsyncCommand(GrblHalConstants.Getsettingsdetails, timeOutMs: 2000);

                // Group names. Collected rather than awaited-for-ok so firmware without
                // $EG simply yields an empty list instead of stalling the sequence;
                // the [SETTINGGROUP:...] lines are parsed by the normal receive path.
                _grblHalSettings.Groups.Clear();
                await SendCommandCollectResponsesAsync(GrblHalConstants.Getsettingsgroups, timeOutMs: 2000);

                var settingResults = await SendAsyncCommand(GrblHalConstants.GetsettingsAll, timeOutMs: 2000);
                if (settingResults)
                {
                    SendSettings();
                }
                var pinLines = await SendCommandCollectResponsesAsync(GrblHalConstants.GetPins, timeOutMs: 2000);
                ParseAllPins(pinLines);
                await SendAsyncCommand(GrblHalConstants.Alarmcodes, timeOutMs: 1000);
                await SendAsyncCommand(GrblHalConstants.Errorcodes, timeOutMs: 1000);

                // Re-broadcast options now that the whole query sequence is done.
                // The first SendOptions can fire with EMPTY signal/axis labels:
                // SendAsyncCommand matches any line containing "ok", so the "ok"
                // from a command sent concurrently at connect (e.g. the $X unlock)
                // can complete the $I+ await before the $I+ response has actually
                // arrived and been parsed. By this point every earlier response is
                // in, so this call carries the complete label set. The UI-side
                // rebuild is add-missing/remove-stale, so firing twice is safe.
                SendOptions();

                SetupPoll();
            });
        }
        public void SetupPoll()
        {
            _pollTimer?.Stop();
            _pollTimer?.Interval = _pollTimer.Interval == 0 ? 200 : _pollInterval;
            _pollTimer?.Start();
        }
        public void StopPoll()
        {
            _pollTimer?.Stop();
        }

        private void _pollTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            Poll();
        }

        public void Poll()
        {
            Adapter.WriteByte(0x87);
        }
        private void SendOptions()
        {
            // Prepend axes to the signal list once, here, so the result is independent
            // of whether AXS or SIGNALS arrived first in the firmware $I response.
            for (int i = grblHalOptions.AxisLabels.Count - 1; i >= 0; i--)
            {
                var axis = grblHalOptions.AxisLabels[i];
                grblHalOptions.SignalLabels.Remove(axis);
                grblHalOptions.SignalLabels.Insert(0, axis);
            }
            onOptionsUpdated?.Invoke(this, grblHalOptions);
        }

        private void SendSettings()
        {
            _grblHalSettings.SettingCollection.Sort(
                SortExpressionComparer<GrblHalSetting>
                    .Ascending(s => s.GroupId)
                    .ThenByAscending(s => s.Id));

            // Resolve group names once here, so the view model and search never have to
            // reach back into the group dictionary per row.
            foreach (var setting in _grblHalSettings.SettingCollection)
                setting.GroupName = _grblHalSettings.GroupNameFor(setting.GroupId);

            _machineData = new MachineSettings();

            _machineData.SetXBoundaries(_grblHalSettings.SettingCollection.FirstOrDefault(x => x.Id == GrblHalConstants.XAxisLength)?.SettingValue ?? "");
            _machineData.SetYBoundaries(_grblHalSettings.SettingCollection.FirstOrDefault(x => x.Id == GrblHalConstants.YAxisLength)?.SettingValue ?? "");
            _machineData.SetZBoundaries(_grblHalSettings.SettingCollection.FirstOrDefault(x => x.Id == GrblHalConstants.ZAxisLength)?.SettingValue ?? "");
            _machineData.SetXRapid(_grblHalSettings.SettingCollection.FirstOrDefault(x => x.Id == GrblHalConstants.XRapid)?.SettingValue ?? "");
            _machineData.SetYRapid(_grblHalSettings.SettingCollection.FirstOrDefault(x => x.Id == GrblHalConstants.YRapid)?.SettingValue ?? "");
            _machineData.SetZRapid(_grblHalSettings.SettingCollection.FirstOrDefault(x => x.Id == GrblHalConstants.ZRapid)?.SettingValue ?? "");
            _machineData.SetIsMetric(_grblHalSettings.SettingCollection.FirstOrDefault(x => x.Id == GrblHalConstants.ReportUnits)?.SettingValue ?? "");
            onSettingUpdated?.Invoke(this, _grblHalSettings.SettingCollection);
        }

        private void SendState(RealTImeState rtState)
        {
            // Fire directly on the data thread — subscribers are responsible for
            // their own UI marshalling (e.g., storing state in a volatile field
            // and applying it on a timer). This avoids flooding the dispatcher
            // queue with InvokeAsync work items during high-frequency streaming.
            OnStateReceived?.Invoke(this, rtState);
        }

        public void NewTcpConnection(TcpSettings tcpSettings)
        {
            if (Adapter != null)
            {
                Adapter.OnDataReceived -= Adapter_OnDataReceived;
                Adapter.Close();
            }

            Adapter = new Tcp(tcpSettings);
            Adapter.OnDataReceived += Adapter_OnDataReceived;
        }

        public void NewSerialConnection(SerialSettings connection)
        {
            // Tear the old adapter down first, as the TCP and WebSocket paths do.
            // Without this, a second connect attempt built a new Serial while the old
            // one still held the port open: the new open failed with "access denied",
            // and the old adapter kept its auto-reconnect loop running and its
            // OnDataReceived hooked up — so status kept arriving from the zombie while
            // every command went out through an adapter that was never opened.
            if (Adapter != null)
            {
                Adapter.OnDataReceived -= Adapter_OnDataReceived;
                Adapter.Close();
            }

            Adapter = new Serial(connection);
            Adapter.OnDataReceived += Adapter_OnDataReceived;
        }
        public void WebSocketConnection(WebSocketSettings settings)
        {
            if (Adapter != null)
            {
                Adapter.OnDataReceived -= Adapter_OnDataReceived;
                Adapter.Close();
            }

            Adapter = new WebSocket(settings);
            Adapter.OnDataReceived += Adapter_OnDataReceived;
        }
        /// <summary>
        /// Stops the poll timer so binary transfers (YModem) are not disrupted
        /// by the 0x87 status query byte.
        /// </summary>
        public void SuspendForTransfer()
        {
            StopPoll();
        }

        /// <summary>
        /// Restarts the poll timer after a binary transfer completes or fails.
        /// </summary>
        public void ResumeAfterTransfer()
        {
            SetupPoll();
        }

        /// <summary>
        /// Sends a command and collects all response lines until the end marker
        /// (default "ok") is received or the timeout expires.
        /// Used for commands like $F+ that return multiple lines before "ok".
        /// Automatically filters out real-time status messages (&lt;...&gt;) that arrive
        /// from the poll timer during collection.
        /// </summary>
        public async Task<List<string>> SendCommandCollectResponsesAsync(
            string command, string endMarker = "ok", int timeOutMs = 5000)
        {
            // Never inject a command into a running job's stream — see IsStreaming.
            // Callers get an empty result; those that would misread that as "the
            // controller answered nothing" check IsStreaming before asking.
            if (IsStreaming) return [];

            var lines = new List<string>();
            var tcs = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? sender, string data)
            {
                var trimmed = data.Trim();

                // Skip real-time status messages (<Idle|MPos:...>) that arrive from
                // the poll timer (0x87) — these are not responses to our command.
                if (trimmed.Length > 0 && trimmed[0] == '<' && trimmed[^1] == '>')
                    return;

                // Terminate on "ok" or "error:XX" — both signal command completion
                if (trimmed.Equals(endMarker, StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                {
                    Adapter.OnDataReceived -= Handler;
                    tcs.TrySetResult(lines);
                }
                else
                {
                    lines.Add(data);
                }
            }

            Adapter.OnDataReceived += Handler;
            SendCommand(command);

            try
            {
                return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeOutMs));
            }
            catch (TimeoutException)
            {
                Adapter.OnDataReceived -= Handler;
                return lines; // return whatever we collected
            }
        }

        private void Adapter_OnDataReceived(object? sender, string e)
        {
            var data = e.Trim();
            if (data.Length == 0) return;

            // Real-time status messages: <...>
            if (data[0] == '<' && data[^1] == '>')
            {
                ParseRealTimeData(data);
                return;
            }

            // Log non-realtime data to console
            OnConsoleLogReceived?.Invoke(this, data);

            // "ok" acknowledgement
            if (data.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                OnCommandAck?.Invoke(this, CommandAck.Ok);
                return;
            }

            // Bracketed messages: [...]
            if (data[0] == '[')
            {
                var inner = data.AsSpan(1, data.Length - 2); // strip [ and ]

                // Match the full prefix, not just "SETTING": $EG replies with
                // SETTINGGROUP: and $SED with SETTINGDESCR:, and both would
                // otherwise be parsed as settings and pollute the collection.
                if (inner.StartsWith("SETTING:"))
                {
                    var trimmed = data.Trim('[', ']');
                    var substring = trimmed.Split('|');
                    ParseSettingsData(substring.AsSpan());
                }
                else if (inner.StartsWith("SETTINGGROUP:"))
                {
                    var trimmed = data.Trim('[', ']');
                    var substring = trimmed.Split('|');
                    ParseSettingGroup(substring.AsSpan());
                }
                else if (inner.StartsWith("SETTINGDESCR:"))
                {
                    // Consumed by the awaiting GetSettingDescriptionAsync caller;
                    // nothing to accumulate here.
                }
                else if (inner.StartsWith("ALARMCODE:"))
                {
                    var trimmed = data.Trim('[', ']');
                    var substring = trimmed.Split('|');
                    ParseAlarm(substring.AsSpan());
                }
                else if (inner.StartsWith("ERRORCODE:"))
                {
                    var trimmed = data.Trim('[', ']');
                    var substring = trimmed.Split('|');
                    ParseError(substring.AsSpan());
                }
                else if (inner.StartsWith("MSG:Pgm End"))
                {
                    // Program end notification — can be handled in the future
                }
                else if (inner.StartsWith("PRB"))
                {
                    var trimmed = data.Trim('[', ']');
                    var substring = trimmed.Split(':');
                    ParseProbe(substring.AsSpan());
                }
                else if (inner.StartsWith("PIN"))
                {
                    var trimmed = data.Trim('[', ']');
                    var substring = trimmed.Split(':');
                }
                else
                {
                    // Generic bracketed data (OPT, NEWOPT, AXS, SIGNALS, etc.)
                    var trimmed = data.Trim('[', ']');
                    var substring = trimmed.Split(':');
                    ParseOptionsData(substring.AsSpan());
                }
                return;
            }

            // Dollar-sign settings: $...
            if (data[0] == '$')
            {
                var inner = data.AsSpan(1); // strip leading $
                if (inner.Contains('\t'))
                {
                    // Tab-separated settings format
                    //var valuePair = data[1..].Split('\t');
                    //ParseTabSettings(valuePair.AsSpan());
                }
                else
                {
                    var valuePair = data[1..].Split('=');
                    ParseSettingsValueData(valuePair.AsSpan());
                }
                return;
            }

            // Error responses: error:N
            if (data.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
            {
                var errorCode = 0;
                var valuePair = data.Split(':');
                if (valuePair.Length >= 2)
                {
                    errorCode = valuePair[1].StringToInt();
                    Debug.WriteLine(_errorCodes.TryGetValue(errorCode, out var error)
                        ? $"***Error Code {errorCode}: {error}***"
                        : $"***Unknown Error Code {errorCode}***");
                }

                // A rejected command is still a finished command. Reporting it here is
                // what keeps the job streamer's outstanding-command queue aligned; when
                // errors were silent, one rejected line left the queue head stuck and
                // every later "ok" was credited to the wrong command.
                OnCommandAck?.Invoke(this, new CommandAck(true, errorCode));
                return;
            }

            // Alarm responses: ALARM:N
            if (data.StartsWith("ALARM:", StringComparison.OrdinalIgnoreCase))
            {
                var valuePair = data.Split(':');
                if (valuePair.Length >= 2)
                {
                    var code = valuePair[1].StringToInt();
                    Debug.WriteLine(_alarmCodes.TryGetValue(code, out var alarm)
                        ? $"***Alarm Code {code}: {alarm}***"
                        : $"***Unknown Alarm Code {code}***");
                }
                return;
            }

            Debug.WriteLine($"***Warning Data Not Parsed: {data}***");
        }

        private void ParseAllPins(List<string> pinLines)
        {
            var auxPins = new List<AuxPinInfo>();
            foreach (var line in pinLines)
            {
                var trimmed = line.Trim().Trim('[', ']');
                if (!trimmed.StartsWith("PIN:", StringComparison.OrdinalIgnoreCase)) continue;

                // Format: PIN:<port>,<description>,<pinNumber>
                var afterPrefix = trimmed.Substring(4); // skip "PIN:"
                var parts = afterPrefix.Split(',');
                if (parts.Length < 3) continue;

                var description = parts[1].Trim();
                if (!description.StartsWith("Aux out", StringComparison.OrdinalIgnoreCase)) continue;

                if (int.TryParse(parts[2].Trim().TrimStart('P'), out var portNumber))
                {
                    auxPins.Add(new AuxPinInfo(description, portNumber));
                }
            }

            if (auxPins.Count > 0)
            {
                OnAuxPinsDiscovered?.Invoke(this, auxPins);
            }
        }

        /// <summary>
        /// Queries the controller for current aux pin states via $pinstate.
        /// Returns a dictionary mapping port number to on/off state.
        /// </summary>
        public async Task<Dictionary<int, bool>> QueryPinStatesAsync()
        {
            var result = new Dictionary<int, bool>();
            var lines = await SendCommandCollectResponsesAsync(GrblHalConstants.GetPinState, timeOutMs: 2000);
            foreach (var line in lines)
            {
                var trimmed = line.Trim().Trim('[', ']');
                if (!trimmed.StartsWith("PINSTATE:", StringComparison.OrdinalIgnoreCase)) continue;

                // Format: PINSTATE:DOUT|P<n>|<physPin>|<mode>|<caps>|<state>
                // Example: PINSTATE:DOUT|P0|13|N|I|1
                var parts = trimmed.Substring(9).Split('|');
                if (parts.Length < 6) continue;
                if (!parts[0].Equals("DOUT", StringComparison.OrdinalIgnoreCase)) continue;

                // Port number is in parts[1] as "P0", "P1", etc.
                var portStr = parts[1].Trim().TrimStart('P');
                if (int.TryParse(portStr, out var portId))
                {
                    var stateStr = parts[5].Trim();
                    result[portId] = stateStr == "1" || stateStr.Equals("on", StringComparison.OrdinalIgnoreCase);
                }
            }
            return result;
        }


        private void ParseProbe(Span<string> span)
        {
            var probe = new ProbeState();
            if (span.Length >= 2)
            {
                probe.ProbeSuccessful = span[2].StringToBool();
                var cords = span[1].Split(',');
                probe.XOffset = cords[0];
                probe.YOffset = cords[1];
                probe.ZOffset = cords[2];
            }

            OnProbeResults?.Invoke(this, probe);
        }

        private void ParseError(Span<string> asSpan)
        {
            var code = asSpan[0].Split(':')[1].StringToInt();
            var errorData = asSpan[2];
            _errorCodes.TryAdd(code, errorData);

        }
        private void ParseAlarm(Span<string> asSpan)
        {
            var code = asSpan[0].Split(':')[1].StringToInt();
            var alarmData = asSpan[2];
            _alarmCodes.TryAdd(code, alarmData);
        }

        private void ParseOptionsData(Span<string> asSpan)
        {
            if (asSpan[0].StartsWith("OPT"))
            {
                // OPT:<flags>,<block_buf>,<rx_buf>{,<axes>{,<tools>}}
                var op = asSpan[1].Split(',');
                if (op.Length >= 3)
                {
                    if (int.TryParse(op[1], out var blockBuf))
                        grblHalOptions.BlockBufferSize = blockBuf;
                    if (int.TryParse(op[2], out var rxBuf))
                        grblHalOptions.RxBufferSize = rxBuf;
                }
                if (op.Length >= 5)
                {
                    if (int.TryParse(op[4], out var toolCount))
                        grblHalOptions.ToolTableCount = toolCount;
                }
            }

            if (asSpan[0].StartsWith("NEWOPT"))
            {
                grblHalOptions.Options = asSpan[1].Split(',').ToList();
            }

            if (asSpan[0].StartsWith("AXS"))
            {
                // Two firmware formats seen in the wild:
                //   [AXS:<count>:<labels>]  e.g. AXS:3:XYZ   -> asSpan = ["AXS","3","XYZ"]
                //   [AXS:<labels>]          e.g. AXS:XYZ     -> asSpan = ["AXS","XYZ"]
                string labels;
                if (asSpan.Length >= 3 && int.TryParse(asSpan[1], out var declaredCount))
                {
                    labels = asSpan[2];
                    GrblHalSettingsConst.AxisCount = grblHalOptions.AxesCount = declaredCount;
                }
                else
                {
                    labels = asSpan[1];
                    GrblHalSettingsConst.AxisCount = grblHalOptions.AxesCount = labels.Length;
                }

                GrblHalSettingsConst.Axis = labels.ToCharArray();
                grblHalOptions.AxisLabels = labels.ToCharArray().ToList();

                // Seed the signal list: axes first, then defaults + probe. SIGNALS will union additional ones.
                // Done inline (not just in SendOptions) so the result is correct even if SendOptions doesn't
                // fire on a retry path.
                for (int i = grblHalOptions.AxisLabels.Count - 1; i >= 0; i--)
                {
                    var axis = grblHalOptions.AxisLabels[i];
                    grblHalOptions.SignalLabels.Remove(axis);
                    grblHalOptions.SignalLabels.Insert(0, axis);
                }
                foreach (var ch in GrblHalSettingsConst.DefaultSignals)
                    if (!grblHalOptions.SignalLabels.Contains(ch))
                        grblHalOptions.SignalLabels.Add(ch);
                if (!grblHalOptions.SignalLabels.Contains('P'))
                    grblHalOptions.SignalLabels.Add('P');
            }

            if (asSpan[0].StartsWith("SIGNALS"))
            {
                // Union firmware-reported signals with whatever AXS already seeded — never drop entries.
                foreach (var ch in asSpan[1])
                    if (!grblHalOptions.SignalLabels.Contains(ch))
                        grblHalOptions.SignalLabels.Add(ch);
            }
        }

        private void ParseTabSettings(Span<string> asSpan)
        {
            _grblHalSettings.SettingCollection.Add(new GrblHalSetting(asSpan));
        }

        private void ParseSettingsValueData(Span<string> data)
        {
            _grblHalSettings.AddSettingValue(data);
        }

        private void ParseSettingsData(Span<string> asSpan)
        {
            _grblHalSettings.SettingCollection.Add(new GrblHalSetting(asSpan));
        }

        private void ParseSettingGroup(Span<string> asSpan)
        {
            var group = SettingGroup.Parse(asSpan);
            if (group != null)
                _grblHalSettings.AddGroup(group);
        }

        /// <summary>
        /// Fetches a single setting's description via <c>$SED=&lt;id&gt;</c>. Returns null
        /// when the firmware was built without descriptions, which callers should treat
        /// as "stop asking" rather than retrying per setting.
        /// </summary>
        public async Task<string?> GetSettingDescriptionAsync(int id, int timeOutMs = 1500)
        {
            var lines = await SendCommandCollectResponsesAsync(
                $"{GrblHalConstants.GetsettingDescription}{id}", timeOutMs: timeOutMs);

            foreach (var line in lines)
            {
                var trimmed = line.Trim().Trim('[', ']');
                if (!trimmed.StartsWith("SETTINGDESCR:", StringComparison.Ordinal)) continue;

                // SETTINGDESCR:<id>|<description>
                var split = trimmed.IndexOf('|');
                if (split < 0) continue;

                // Verify the id echoed back matches what we asked for. Responses are
                // collected off the shared receive stream, so a late or interleaved
                // reply would otherwise be pinned onto the wrong setting.
                var idPart = trimmed["SETTINGDESCR:".Length..split];
                if (!int.TryParse(idPart, out var replyId) || replyId != id) continue;

                // Firmware escapes line breaks as a literal backslash-n.
                var text = trimmed[(split + 1)..].Replace("\\n", "\n").Trim();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            return null;
        }

        private void ParseRealTimeData(string data)
        {
            var rtState = new RealTImeState
            {
                RawRt = data
            };
            data = data.Trim('<', '>');
            var substring = data.Split('|').AsSpan();
            var currentState = substring.Slice(0, 1);
            if (currentState[0].Contains(':'))
            {
                currentState = currentState[0].Split(':');
            }
            rtState.GrblHalState = currentState[0];
            if (currentState.Length > 1)
            {
                rtState.SubState = currentState[1];
            }
            foreach (var state in substring)
            {
                var pair = state.Split(':');
                if (pair.Length > 1)
                {
                    var topic = pair[0];
                    var value = pair[1];
                    switch (topic)
                    {
                        case "WPos":
                            var wpos = value.Split(',');
                            break;
                        case "MPos":
                            rtState.MPos = value.Split(',');
                            break;
                        case "Bf":
                            var y = value.Split(",");
                            break;
                        case "Ln":
                            var ln = value.Split(",");
                            break;
                        case "F":
                            var feed = value.Split(",");
                            rtState.FeedRate = feed[0];
                            break;
                        case "FS":
                            var speed = value.Split(",");
                            rtState.FeedRate = speed[0];
                            rtState.ProgramRPM = speed[1];
                            if (speed.Length > 2)
                                rtState.ActualRpm = speed[2];
                            break;
                        case "WCS":
                            rtState.WCS = value;
                            break;
                        case "Pn":
                            rtState.SignalStatus = value.ToCharArray().ToList();
                            break;
                        case "WCO":
                            rtState.Wco = value.Split(",");
                            break;
                        case "A":
                            rtState.AccessoryState = value;
                            break;
                        case "Ov":
                            var overRides = value.Split(",");
                            if (overRides.Length > 0)
                                rtState.FeedOverRide = overRides[0];
                            if (overRides.Length > 1)
                                rtState.RapidOverRide = overRides[1];
                            if (overRides.Length > 2)
                                rtState.RpmOverRide = overRides[2];
                            break;
                        case "MPG":
                            rtState.MpgActive = value.StringToBool();

                            break;
                        case "H":
                            var homeBlock = value.Split(":");
                            var h = homeBlock[0].Split(",");
                            rtState.Home = h[0].StringToBool();
                            break;
                        case "D":
                            break;
                        case "Sc":
                            var scale = value.Split(":");
                            break;
                        case "T":
                            rtState.Tool = value;
                            break;
                        case "TLR":
                            rtState.TLR = value.StringToBool();
                            break;
                        case "FW":
                            break;
                        case "In":
                            var signals = int.Parse(value);
                            break;

                    }
                }
            }
            SendState(rtState);
        }
        public async Task<bool> SendAsyncCommand(string command, string resultMatch = "ok", int timeOutMs = 300)
        {
            // Never inject a command into a running job's stream — see IsStreaming.
            if (IsStreaming) return false;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? sender, string data)
            {
                if (data.Contains(resultMatch, StringComparison.OrdinalIgnoreCase))
                {
                    Adapter.OnDataReceived -= Handler;
                    tcs.TrySetResult(true);
                }
            }

            Adapter.OnDataReceived += Handler;
            SendCommand(command);

            try
            {
                return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeOutMs));
            }
            catch (TimeoutException)
            {
                // Always clean up the handler on timeout to prevent leaking subscriptions
                Adapter.OnDataReceived -= Handler;
                return false;
            }
        }

    }
}
