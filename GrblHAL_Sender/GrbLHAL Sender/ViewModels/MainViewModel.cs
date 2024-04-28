using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Transactions;
using ReactiveUI;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using GrbLHAL_Sender.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using GrbLHAL_Sender.Communication;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia.Threading;
using DynamicData;
using GrbLHAL_Sender.Settings;
using System.Threading.Tasks;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using GrbLHAL_Sender.Configuration;
using Microsoft.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using GrbLHAL_Sender.Gcode;
using Avalonia.Platform.Storage;
using GrbLHAL_Sender.Behaviors;

namespace GrbLHAL_Sender.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly CommunicationManager _commManager;
    private readonly ConfigManager _configManager;
    private JobViewModel _jobViewModel;
    private ObservableCollection<Axis> _axis;
    private ObservableCollection<string> _consoleOutput = new();
    private ObservableCollection<int> _toolList = new();
    private ObservableCollection<Signal> _signalList = [];

    private ObservableCollection<double> _jogStepList;
    private ObservableCollection<double> _jogRateList;
    private readonly GHalSenderConfig _config;
    private RealTImeState _state;
    private bool _showConsole;
    private bool _isJobRunning;

    //private bool _hasAtc;
    //private bool _hasSdCard;
    //private bool _hasProbing;
    //private bool _isFileLoaded;
    private ReactiveCommand<object, Unit> _doubleTapCommand;
    private ReactiveCommand<object, Unit> _hideBoxCommand;
    private bool _connected;
    private Color _homeStateColor;
    private bool _alarmActive;
    private bool _needsSetup;
    private int _selectedTool;
    private bool _hideToolChangeList;
    private double _jogStep;
    private double _jogRate;
    private string _mdiText;
    private JobViewModel _jobViewModel1;
    private ReactiveCommand<object, Unit> _focusedCommand;
    public bool ShowRTCommands { get; set; }
    public bool AutoConnect { get; set; }

    //public bool IsJobRunning
    //{
    //    get => _isJobRunning;
    //    set => _isJobRunning = value;
    //}
    //public bool HasATC
    //{
    //    get => _hasAtc;
    //    set => _hasAtc = value;
    //}
    //public bool HasSdCard
    //{
    //    get => _hasSdCard;
    //    set => _hasSdCard = value;
    //}
    //public bool HasProbing
    //{
    //    get => _hasProbing;
    //    set => _hasProbing = value;
    //}
    //public bool IsFileLoaded
    //{
    //    get => _isFileLoaded;
    //    set => _isFileLoaded = value;
    //}
    public string UnitSystem { get; set; } = "G20";

    public Color HomeStateColor
    {
        get => _homeStateColor;
        set => _homeStateColor = value;
    }
    public double JogStep
    {
        get => _jogStep;
        set => this.RaiseAndSetIfChanged(ref _jogStep, value);
    }
    public double JogRate
    {
        get => _jogRate;
        set => this.RaiseAndSetIfChanged(ref _jogRate, value);
    }
    public bool Connected
    {
        get => _connected;
        set => this.RaiseAndSetIfChanged(ref _connected, value);
    }
    public bool HideToolChangeList
    {
        get => _hideToolChangeList;
        set => this.RaiseAndSetIfChanged(ref _hideToolChangeList, value);
    }
    public bool ShowConsole
    {
        get => _showConsole;
        set => this.RaiseAndSetIfChanged(ref _showConsole, value);
    }
    public bool AlarmActive
    {
        get => _alarmActive;
        set => this.RaiseAndSetIfChanged(ref _alarmActive, value);
    }

    public string MdiText
    {
        get => _mdiText;
        set => this.RaiseAndSetIfChanged(ref _mdiText, value);
    }

    public int SelectedTool
    {
        get => _selectedTool;
        set => this.RaiseAndSetIfChanged(ref _selectedTool, value);
    }
    public JobViewModel JobViewModel { get; set; }

    public RealTImeState State
    {
        get => _state;
        set => this.RaiseAndSetIfChanged(ref _state, value);
    }
    public SettingsViewModel SettingsViewModel { get; set; }

    public ICommand ConnectCommand { get; set; }

    public ICommand ZeroAxis { get; set; }

    public ICommand ZeroAllCommand { get; set; }

    public ICommand UnLockCommand { get; set; }

    public ICommand HomeCommand { get; set; }

    public ICommand ClearAlarmCommand { get; set; }

    public ICommand JogNegCommand { get; }

    public ICommand JogPosCommand { get; }

    public ICommand ClearConsoleCommand { get; }

    public ICommand ToggleRtCommand { get; }

    public ICommand MdiTextCommand { get; }

    public ICommand WcsCommand { get; }

    public ICommand ToolSelectedCommand { get; }

    public ICommand FeedRateChangeCommand { get; }

    public ICommand StepRateChangeCommand { get; }

    public ICommand KeyPressCommand { get; }

    public ReactiveCommand<object, Unit> DoubleTapCommand
    {
        get => _doubleTapCommand;
        set => _doubleTapCommand = value;
    }
    public ReactiveCommand<object, Unit> HideBoxCommand
    {
        get => _hideBoxCommand;
        set => _hideBoxCommand = value;
    }
    public ObservableCollection<Signal> SignalList
    {
        get => _signalList;
        set => this.RaiseAndSetIfChanged(ref _signalList, value);
    }
    public ObservableCollection<Axis> AxisCollection
    {
        get => _axis;
        set => this.RaiseAndSetIfChanged(ref _axis, value);
    }
    public ObservableCollection<int> ToolList
    {
        get => _toolList;
        set => this.RaiseAndSetIfChanged(ref _toolList, value);
    }
    public ObservableCollection<string> ConsoleOutput
    {
        get => _consoleOutput;
        set => this.RaiseAndSetIfChanged(ref _consoleOutput, value);
    }

    public ObservableCollection<double> JogRateList
    {
        get => _jogRateList;
        set => this.RaiseAndSetIfChanged(ref _jogRateList, value);
    }
    public ObservableCollection<double> JogStepList
    {
        get => _jogStepList;
        set => this.RaiseAndSetIfChanged(ref _jogStepList, value);
    }

    public ReactiveCommand<object, Unit> FocusedCommand
    {
        get => _focusedCommand;
        set => _focusedCommand = value;
    }

    public MainViewModel(CommunicationManager commManager, SettingsViewModel settingsViewModel,
        ConfigManager configManager, JobViewModel jobViewModel)
    {
        SettingsViewModel = settingsViewModel;
        _needsSetup = true;
        _commManager = commManager;
        _configManager = configManager;
        JobViewModel = jobViewModel;
        _config = _configManager.LoadConfig();

        Dispatcher.UIThread.ShutdownStarted += UIThread_ShutdownStarted;
        _commManager.OnStateReceived += _commManager_OnStateReceived;
        _commManager.onOptionsUpdated += _commManager_onOptionsUpdated;
        _commManager.OnConsoleLogReceived += _commManager_OnConsoleLogReceived;

        ConnectCommand = ReactiveCommand.Create(Connect);
        ZeroAxis = ReactiveCommand.Create<string>(Zero);
        HomeCommand = ReactiveCommand.Create(Home);
        UnLockCommand = ReactiveCommand.Create(Unlock);
        JogNegCommand = ReactiveCommand.Create<string>(JogNeg);
        JogPosCommand = ReactiveCommand.Create<string>(JogPos);
        ZeroAllCommand = ReactiveCommand.Create(ZeroAll);
        ClearAlarmCommand = ReactiveCommand.Create(ClearAlarm);
        ClearConsoleCommand = ReactiveCommand.Create(ClearConsole);
        ToggleRtCommand = ReactiveCommand.Create(ToggleConsoleRt);
        MdiTextCommand = ReactiveCommand.Create<string>(MDIText);
        WcsCommand = ReactiveCommand.Create<string>(Wcs);
        ToolSelectedCommand = ReactiveCommand.Create<int>(ToolSelected);
        DoubleTapCommand = ReactiveCommand.Create<object>(DoubleTap);
        HideBoxCommand = ReactiveCommand.Create<object>(HideToolList);
        KeyPressCommand = ReactiveCommand.Create<string>(KeyPressed);
        FeedRateChangeCommand = ReactiveCommand.Create<double>(ChangeFeedRate);
        StepRateChangeCommand = ReactiveCommand.Create<double>(ChangeStepRate);
        FocusedCommand = ReactiveCommand.Create<object>(FocusTextInput);

        //TODO just temp will use the setting grblhal returns from $I and $I+ to build the axis count values 

        _axis = new ObservableCollection<Axis>
        {
            new()
            {
                Name = "X",
                ZeroWcsCommand = ZeroAxis,
                Order = 0
            },
            new()
            {
                Name = "Y",
                ZeroWcsCommand = ZeroAxis,
                Order = 1
            },
            new()
            {
                Name = "Z",
                ZeroWcsCommand = ZeroAxis,
                Order = 2
            },
        };

        SetUpUiSettings();

        if (!_config.AutoConnect) return;
        try
        {
            Connect();
        }
        catch (Exception e)
        {

        }
    }

    bool _routedKeyStroke = false;
    public string Tnputs { get; set; }
    private Action<string> MessageTarget;
    private void FocusTextInput(object obj)
    {
        if (obj is not AutoCompleteBox tb) return;
        tb.LostFocus += TbLostFocus;
        _routedKeyStroke = true;
        return;

        void TbLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            tb.LostFocus -= TbLostFocus;
            Tnputs = string.Empty;
        }
    }
    private void KeyPressed(string key)
    {
        string text;
        switch (key)
        {
            case "Ent":
                text = "\r\n";
                break;
            case "Spc":
                text = " ";
                break;
            case "Del":
                {
                    if (MdiText.EndsWith("\r\n"))
                    {
                        MdiText = MdiText.TrimEnd();
                        return;
                    }

                    if (MdiText.Length >= 1)
                        MdiText = MdiText.Remove(MdiText.Length - 1);
                    return;

                }
            default:
                text = key;
                break;
        }

        if (_routedKeyStroke)
        {

            Tnputs += text;
            MessageTarget = s => FocusTextInput(s);
            return;
        }

        MdiText += text;
    }
  

    private void ChangeStepRate(double step)
    {
        JogStep = step;
    }
    private void ChangeFeedRate(double feed)
    {
        JogRate = feed;
    }
    private void HideToolList(object obj)
    {
        HideToolChangeList = !Convert.ToBoolean(obj);
    }
    private void ToolSelected(int tool)
    {
        var command = _isJobRunning ? $"T{tool}M6" : $"M61Q{tool}";
        SendCommand(command);
    }
    private void SetUpUiSettings()
    {
        UnitSystem = _config.UseMetric ? "G21" : "G20";
        AutoConnect = _config.AutoConnect;
        JogRateList = new ObservableCollection<double>(_config.JogSpeed);
        JogStepList = new ObservableCollection<double>(_config.JogDistance);
        JogStep = JogStepList[^1];
        JogRate = JogRateList[^1];
        ToolList.AddRange(_config.ToolList.Tools);
    }
    private void Wcs(string command)
    {
        SendCommand(command);
    }
    private void SendCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return;
        ConsoleOutput.Add(command);
        _commManager.SendCommand(command);
    }
    private void MDIText(string command)
    {
        SendCommand(command);
    }
    private void DoubleTap(object p)
    {
        ShowConsole = !Convert.ToBoolean(p);
    }
    private void ToggleConsoleRt()
    {
        ShowRTCommands = !ShowRTCommands;
    }
    private void ClearConsole()
    {
        ConsoleOutput.Clear();
    }
    private void ZeroAll()
    {
        var command = "G90G10L20P0X0.000Y0.000Z0.000";
        SendCommand(command);
    }
    private void Zero(string axis)
    {
        var command = $"G10L20P0{axis}0.000";
        SendCommand(command);
    }
    private void Home()
    {
        var command = "$H";
        SendCommand(command);
    }
    private void ClearAlarm()
    {
        SendCommand("$X");
    }

    public const byte Cmd_Reset = 0x18;
    private void Unlock()
    {
        _commManager.Adapter.WriteByte(Cmd_Reset);
    }
    private void UIThread_ShutdownStarted(object? sender, EventArgs e)
    {
        _commManager.ShutDown();
    }
    private void _commManager_OnConsoleLogReceived(object? sender, string e)
    {
        if (ShowConsole)
            ConsoleOutput.Add(e);
    }
    private void _commManager_OnStateReceived(object? sender, RealTImeState e)
    {
        Connected = true;
        for (int i = 0; i < e.MPos.Length; i++)
        {
            var pos = new Position
            {
                MPos = double.Parse(e.MPos[i])
            };
            if (e.Wco.Length > 0)
            {
                pos.Wco = double.Parse(e.MPos[i]) - double.Parse(e?.Wco[i] ?? "0.0");
            }

            AxisCollection[i].Position = pos;
        }
        State = e;
        AlarmActive = e.GrblHalState == "Alarm";
        if (ConsoleOutput.Count > 200)
        {
            ConsoleOutput.Clear();
        }
        if (ShowConsole && ShowRTCommands)
        {
            ConsoleOutput.Add(e.RawRt);
        }
        ProcessSignals(e.SignalStatus);
    }
    private void ProcessSignals(List<char> signals)
    {
        if (signals.Count == 0)
        {
            if (!SignalList.Any(x => x.Triggered)) return;

            foreach (var signal in from signal in SignalList where signal.Triggered select signal)
            {
                signal.Triggered = false;
            }
            return;
        }
        foreach (var signal in from signal in signals from sig in SignalList where sig.Id == signal select sig)
        {
            signal.Triggered = true;
        }
    }
    private void _commManager_onOptionsUpdated(object? sender, GrblHALOptions e)
    {
        if (_needsSetup)
        {
            foreach (var axis in e.AxisLabels.Where(axis => _axis.All(x => x.Name != axis.ToString())))
            {
                _axis.Add(new Axis
                {
                    Name = axis.ToString(),
                    Order = e.AxisLabels.IndexOf(axis),
                    ZeroWcsCommand = ZeroAxis
                });
            }
            foreach (var signal in e.SignalLabels)
            {
                SignalList.Add(new Signal
                {
                    Id = signal
                });
            }
            _needsSetup = false;
        }
    }
    private void JogNeg(string axis)
    {
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}-{JogStep}F{JogRate}";
        SendCommand(command);
    }
    private void JogPos(string axis)
    {
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}{JogStep}F{JogRate}";
        SendCommand(command);
    }
    public void Connect()
    {
        _commManager.NewSerialConnection(_config.SerialSettings.PortName);
        _commManager.GetSettings();
    }
}
public class Axis : ViewModelBase
{
    private Position _position;
    public int Order { get; set; }
    public string Name { get; set; }
    public ICommand? ZeroWcsCommand { get; set; }
    public Position Position
    {
        get => _position;
        set
        {
            if (_position == value) return;
            this.RaiseAndSetIfChanged(ref _position, value);
        }
    }
    public Axis()
    {
        Position = new Position();
    }
}

public partial class Signal : ObservableObject
{
    public char Id { get; set; }
    [ObservableProperty]
    private bool _triggered;
}

