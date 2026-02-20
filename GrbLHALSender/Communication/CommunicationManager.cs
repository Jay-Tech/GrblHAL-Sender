using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using GrbLHALSender.Probe;
using GrbLHALSender.Settings;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using GrbLHALSender.Configuration;
using Hexa.NET.SDL3;
using Timer = System.Timers.Timer;


namespace GrbLHALSender.Communication
{
    public class CommunicationManager
    {
        private readonly ConfigManager _configManager;
        private const string StateString = "Idle|Run|Hold|Jog|Alarm:|Door|Check|Home|Sleep|Tool";

        public event EventHandler<string> OnConsoleLogReceived;
        public event EventHandler<RealTImeState> OnStateReceived;
        public event EventHandler<List<GrblHalSetting>> onSettingUpdated;
        public event EventHandler<GrblHALOptions> onOptionsUpdated;
        public event EventHandler<ProbeState> OnProbeResults;
        public event EventHandler OnCommandAck;


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


        public ICommsAdapter Adapter { get; set; }
        public MachineSettings MachineData => _machineData;
        public GrblHALOptions Options => grblHalOptions;
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
                Adapter.OnDataReceived -= Adapter_OnDataReceived;
                Adapter.Close();
            }
            Environment.Exit(0);
        }

        public void SendCommand(string command)
        {
            Adapter?.WriteCommand(command);
        }
        public void GetSettings()
        {
            var t = Task.Factory.StartNew(async () =>
            {
                var infoResults = await SendAsyncCommand(GrblHalConstants.GetinfoExtended, timeOutMs: 1000);
                if (!infoResults)
                {
                    while (!await SendAsyncCommand(GrblHalConstants.GetinfoExtended, timeOutMs: 1000))
                    {
                        Thread.Sleep(500);
                    }
                }
                else
                {
                    SendOptions();
                }

                await SendAsyncCommand(GrblHalConstants.Getsettingsdetails, timeOutMs: 2000);
                var settingResults = await SendAsyncCommand(GrblHalConstants.GetsettingsAll, timeOutMs: 1000);
                if (settingResults)
                {
                    SendSettings();
                }
                await SendAsyncCommand(GrblHalConstants.Alarmcodes, timeOutMs: 1000);
                await SendAsyncCommand(GrblHalConstants.Errorcodes, timeOutMs: 1000);
                SetupPoll();
            });
        }
        public void SetupPoll()
        {
            _pollTimer?.Stop();
            _pollTimer?.Interval = _pollTimer.Interval == 0 ? 200: _pollInterval;
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
            onOptionsUpdated?.Invoke(this, grblHalOptions);
        }

        private void SendSettings()
        {
            _grblHalSettings.SettingCollection.Sort(
                SortExpressionComparer<GrblHalSetting>
                    .Ascending(s => s.GroupId)
                    .ThenByAscending(s => s.Id));
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
                OnCommandAck?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Bracketed messages: [...]
            if (data[0] == '[')
            {
                var inner = data.AsSpan(1, data.Length - 2); // strip [ and ]

                if (inner.StartsWith("SETTING"))
                {
                    var trimmed = data.Trim('[', ']');
                    var substring = trimmed.Split('|');
                    ParseSettingsData(substring.AsSpan());
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
                var valuePair = data.Split(':');
                if (valuePair.Length >= 2)
                {
                    var code = valuePair[1].StringToInt();
                    Debug.WriteLine(_errorCodes.TryGetValue(code, out var error)
                        ? $"***Error Code {code}: {error}***"
                        : $"***Unknown Error Code {code}***");
                }
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
                GrblHalSettingsConst.AxisCount = grblHalOptions.AxesCount = int.Parse(asSpan[1]);
                GrblHalSettingsConst.Axis = asSpan[2].ToCharArray();
                grblHalOptions.AxisLabels = asSpan[2].ToCharArray().ToList();
                grblHalOptions.SignalLabels = asSpan[2].ToCharArray().ToList();
                grblHalOptions.SignalLabels.AddRange(GrblHalSettingsConst.DefaultSignals);
                grblHalOptions.SignalLabels.Add("P");
            }

            if (asSpan[0].StartsWith("SIGNALS"))
            {
                grblHalOptions.SignalLabels = asSpan[1].ToCharArray().ToList();
                if (grblHalOptions.AxisLabels.Count > 0)
                {
                    grblHalOptions.SignalLabels.AddOrInsertRange(grblHalOptions.AxisLabels, 0);
                }
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
                            var a = value;
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
                            var h = value.Split(":");
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
            try
            {
                var result = await FetchDataAsync(command, resultMatch)
                    .WaitAsync(TimeSpan.FromMilliseconds(timeOutMs));
                return result;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        private Task<bool> FetchDataAsync(string command, string resultMatch)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? sender, string data)
            {
                if (data.Contains(resultMatch, StringComparison.OrdinalIgnoreCase))
                {
                    Adapter.OnDataReceived -= Handler;
                    tcs.TrySetResult(true);
                }
            }
            Adapter.OnDataReceived -= Handler;
            Adapter.OnDataReceived += Handler;
            SendCommand(command);

            return tcs.Task;
        }

    }
}
