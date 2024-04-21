using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using GrbLHAL_Sender.Settings;
using GrbLHAL_Sender.Utility;
using static GrbLHAL_Sender.Settings.GrblHalSetting;

namespace GrbLHAL_Sender.Communication
{
    public class CommunicationManager
    {
        private const string StateString = "Idle|Run|Hold|Jog|Alarm:|Door|Check|Home|Sleep|Tool";

        private Timer _pollTimer;
        private readonly Dispatcher _dispatcher;
        private GrblHALSettings _grblHalSettings;
        private GrblHALOptions grblHalOptions = new();
        private PendingMessageSet _pendingMessageSet;

        public event EventHandler<string> OnConsoleLogReceived;
        public event EventHandler<RealTImeState> OnStateReceived;
        public event EventHandler<List<GrblHalSetting>> onSettingUpdated;
        public event EventHandler<GrblHALOptions> onOptionsUpdated;
        public event EventHandler<Probe.Probe> onOptionsResults;
        public ICommsAdapter Adapter { get; set; }

        public PendingMessageSet PendingMessage
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
            Adapter.WriteCommand(command);
        }
        public void GetSettings()
        {
            var t = Task.Factory.StartNew(async () =>
            {
                PendingMessage = PendingMessageSet.Options;
                Adapter.WriteCommand("$I+");
                await Task.Delay(500);
                Adapter.WriteCommand("$ES");
                await Task.Delay(500);
                PendingMessage = PendingMessageSet.Setting;
                Adapter.WriteCommand("$+");
                await Task.Delay(500);
                SetupPoll(250);
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

        private PendingMessageSet? _pendingMessageComplete = null;
        private void PendingJobComplete(PendingMessageSet job)
        {
            _pendingMessageComplete ??= job;
            if (job != PendingMessageSet.Options && _pendingMessageComplete == PendingMessageSet.Options)
            {
                SendOptions();
            }
            if (job != PendingMessageSet.Setting && _pendingMessageComplete == PendingMessageSet.Setting)
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
                PendingMessage = PendingMessageSet.NotPending;
                return;
            }
            if (data.StartsWith("<") || data.EndsWith(">"))
            {
                ParseRealTimeData(data);
                return;
            }
            if (data.StartsWith("[SETTING"))
            {
                PendingMessage = PendingMessageSet.Setting;
                data = data.Trim('[', ']');
                var substring = data.Split('|');
                ParseSettingsData(substring.AsSpan());
                return;
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
                Debug.Write("***Warning Data Not Parsed " + valuePair[0] + valuePair[1] + "***" + Environment.NewLine);
            }
            else 
            {
                Debug.Write("***Warning Data Not Parsed " + data + "***");
            }
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
        PendingMessage = PendingMessageSet.NotPending;
        data = data.Trim('<', '>');
        var substring = data.Split('|').AsSpan();
        var currentState = substring.Slice(0, 1);
        if (currentState[0].Contains(':'))
        {
            currentState = currentState[0].Split(':');
        }
        rtState.GrblHALState = currentState[0];
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
                        break;
                    case "Ln":
                        break;
                    case "Fs":
                        break;
                    case "Pn":
                        break;
                    case "WCO":
                        rtState.WCO = value.Split(",");
                        break;
                    case "A":
                        break;
                    case "Ov":
                        break;
                    case "MPG":
                        var mpg = value.Split(":");
                        rtState.MpgActive = mpg[0].StringToBool();
                        break;
                    case "H":
                        var h = value.Split(":");
                        rtState.Home = h[0].StringToBool();
                        break;
                    case "D":
                        break;
                    case "Sc":
                        break;
                    case "TLR":
                        break;
                    case "FW":
                        break;
                    case "In":
                        var signals = value.Split();
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