using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using GrbLHAL_Sender.Probe;
using GrbLHAL_Sender.Settings;
using GrbLHAL_Sender.Utility;


namespace GrbLHAL_Sender.Communication
{
    public class CommunicationManager
    {
        private const string StateString = "Idle|Run|Hold|Jog|Alarm:|Door|Check|Home|Sleep|Tool";

        private Timer _pollTimer;
        private readonly Dispatcher _dispatcher;
        private GrblHALSettings _grblHalSettings;
        private GrblHALOptions grblHalOptions = new();
        private GrblHalSetting.PendingMessageSet _pendingMessageSet;

        public event EventHandler<string> OnConsoleLogReceived;
        public event EventHandler<RealTImeState> OnStateReceived;
        public event EventHandler<List<GrblHalSetting>> onSettingUpdated;
        public event EventHandler<GrblHALOptions> onOptionsUpdated;
        public event EventHandler<ProbeState> OnProbeResults;
        public ICommsAdapter Adapter { get; set; }

        public GrblHalSetting.PendingMessageSet PendingMessage
        {
            get => _pendingMessageSet;
            set
            {
                if (_pendingMessageSet != value)
                {
                    _pendingMessageSet = value;
                    PendingJobComplete(_pendingMessageSet);
                }
            }
        }
        public CommunicationManager()
        {
            _dispatcher = Dispatcher.UIThread;
            _grblHalSettings = new GrblHALSettings();
            _pollTimer = new Timer();
            _pollTimer.Elapsed += _pollTimer_Elapsed;
            _probe = new ProbeState();
        }

        //  set a call back directly 
        public void StartJob(Action<string>? callBack)
        {
            _callBack = callBack;
        }
        public void EndJob()
        {
            _callBack = null;
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
                PendingMessage = GrblHalSetting.PendingMessageSet.Options;
                Adapter.WriteCommand("$I+");
                await Task.Delay(300);
                Adapter.WriteCommand("$ES");
                await Task.Delay(300);
                PendingMessage = GrblHalSetting.PendingMessageSet.Setting;
                Adapter.WriteCommand("$+");
                await Task.Delay(300);
                Adapter.WriteCommand(GrblHalConstants.Alarmcodes);
                await Task.Delay(300);
                Adapter.WriteCommand(GrblHalConstants.Errorcodes);
                SetupPoll(300);
            });
        }
        public void SetupPoll(int rate)
        {
            _pollTimer.Interval = rate;
            _pollTimer.Start();
        }

        private void _pollTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            Poll();
        }

        public void Poll()
        {
            Adapter.WriteByte(0x87);
        }

        private GrblHalSetting.PendingMessageSet? _pendingMessageComplete = null;
        private readonly ProbeState _probe;
        private Action<string>? _callBack;

        private void PendingJobComplete(GrblHalSetting.PendingMessageSet job)
        {
            _pendingMessageComplete ??= job;
            if (job != GrblHalSetting.PendingMessageSet.Options && _pendingMessageComplete == GrblHalSetting.PendingMessageSet.Options)
            {
                SendOptions();
            }
            if (job != GrblHalSetting.PendingMessageSet.Setting && _pendingMessageComplete == GrblHalSetting.PendingMessageSet.Setting)
            {
                SendSettings();
            }
            _pendingMessageComplete = job;
        }

        private void SendOptions()
        {
            onOptionsUpdated?.Invoke(this, grblHalOptions);
        }

        private void SendSettings()
        {
            _grblHalSettings.SettingCollection.Sort(SortExpressionComparer<GrblHalSetting>.Ascending(s => s.GroupId));
            onSettingUpdated?.Invoke(this, _grblHalSettings.SettingCollection);
        }

        private void SendState(RealTImeState rtState)
        {
            _dispatcher.InvokeAsync(() => OnStateReceived(this, rtState), DispatcherPriority.Background);
        }

        public void NewWebSocketConnection()
        {

        }

        public void NewSerialConnection(string connection)
        {
            Adapter = new Serial(connection);
            Adapter.OnDataReceived += Adapter_OnDataReceived;
        }

        private void Adapter_OnDataReceived(object? sender, string e)
        {
            var data = e;
            if (!data.StartsWith("<") && !data.EndsWith(">"))
            {
                OnConsoleLogReceived?.Invoke(this, new string(data));
            }
            if (string.Equals(data, "ok", StringComparison.OrdinalIgnoreCase))
            {
                if (_callBack != null)
                {
                    _callBack(data);
                }

                PendingMessage = GrblHalSetting.PendingMessageSet.NotPending;

            }
            if (data.StartsWith("<") || data.EndsWith(">"))
            {
                ParseRealTimeData(data);
                return;
            }
            if (data.StartsWith("[SETTING"))
            {
                PendingMessage = GrblHalSetting.PendingMessageSet.Setting;
                data = data.Trim('[', ']');
                var substring = data.Split('|');
                ParseSettingsData(substring.AsSpan());
                return;
            }
            if (data.StartsWith("[ALARMCODE:"))
            {
                PendingMessage = GrblHalSetting.PendingMessageSet.Setting;
                data = data.Trim('[', ']');
                var substring = data.Split('|');
                ParseAlarm(substring.AsSpan());
                return;
            }
            if (data.StartsWith("[ERRORCODE:"))
            {
                PendingMessage = GrblHalSetting.PendingMessageSet.Setting;
                data = data.Trim('[', ']');
                var substring = data.Split('|');
                ParseError(substring.AsSpan());
                return;
            }

            if (data.StartsWith("[MSG:Pgm End"))
            {

            }

            if (data.StartsWith("[PRB"))
            {
                data = data.Trim('[', ']');
                var substring = data.Split(':');
                ParseProbe(substring.AsSpan());
            }
            if (data.StartsWith("["))
            {
                data = data.Trim('[', ']');
                var substring = data.Split(':');
                ParseOptionsData(substring.AsSpan());
                return;
            }
            if (data.StartsWith("$"))
            {
                data = data.Trim('$');
                if (data.Contains('\t'))
                {
                    var valuePair = data.Split('\t');
                    //ParseTabSettings(valuePair.AsSpan());
                }
                else
                {
                    var valuePair = data.Split('=');
                    ParseSettingsValueData(valuePair.AsSpan());
                }
                return;
            }

            if (data.StartsWith("error"))
            {

                var valuePair = data.Split(':');
                var error = _errorCodes[valuePair[1].StringToInt()];
                Debug.Write("***Warning Error Code " + error + valuePair[1] + error + "***" + Environment.NewLine);
            }
            if (data.StartsWith("ALARM"))
            {

                var valuePair = data.Split(':');
                var alarm = _alarmCodes[valuePair[1].StringToInt()];
                Debug.Write("***Alarm Code " + alarm + valuePair[1] + alarm + "***" + Environment.NewLine);
            }
            else
            {
                if (data == "ok")
                {
                    Debug.Write("***" + data + "***" + Environment.NewLine);
                }
                else
                    Debug.Write("***Warning Data Not Parsed " + data + "***");
            }
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

            OnProbeResults?.Invoke(this,probe);
        }

        private Dictionary<int, string> _errorCodes = new Dictionary<int, string>();
        private void ParseError(Span<string> asSpan)
        {
            var code = asSpan[0].Split(':')[1].StringToInt();
            var errorData = asSpan[2];
            _errorCodes.Add(code, errorData);

        }

        private Dictionary<int, string> _alarmCodes = new Dictionary<int, string>();
        private void ParseAlarm(Span<string> asSpan)
        {
            var code = asSpan[0].Split(':')[1].StringToInt();
            var alarmData = asSpan[2];
            _alarmCodes.Add(code, alarmData);
        }


        private void ParseOptionsData(Span<string> asSpan)
        {
            if (asSpan[0].StartsWith("OPT"))
            {
                var op = asSpan[1].Split(',');
                if (op.Length >= 4)
                {
                    grblHalOptions.ToolTableCount = int.Parse(op[4]);
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
                grblHalOptions.SignalLabels.Add("P");
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
            PendingMessage = GrblHalSetting.PendingMessageSet.NotPending;
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
    }


}

// settings being constructed in ref grblHAL  Report extensions
//https://github.com/grblHAL/core/wiki/Report-extensions#controller-information-extensions