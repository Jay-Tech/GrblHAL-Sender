﻿using System;
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
using System.Linq;
using Avalonia.Threading;
using DynamicData;
using GrbLHAL_Sender.Settings;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GrbLHAL_Sender.Configuration;
using Microsoft.CodeAnalysis;

namespace GrbLHAL_Sender.ViewModels;

public class MainViewModel : ViewModelBase
{
    public string jogDataDistance = "10";
    public string FeedRate = "1000";

    private readonly SettingsViewModel _settingsViewModel;
    private readonly CommunicationManager _commManager;
    private readonly ConfigManager _configManager;
    private ObservableCollection<Axis> _axis;
    private ObservableCollection<string> _consoleOutput = new();
    private ObservableCollection<int> _toolList = new();
    private ObservableCollection<Signal> _signalList = [];
    private RealTImeState _state;
    private bool _showConsole;
    private bool _isJobRunning;
    private bool _hasAtc;
    private bool _hasSdCard;
    private bool _hasProbing;
    private bool _isFileLoaded;
    private ICommand _connectCommand;
    private ICommand _zeroAxis;
    private ICommand _jogXDownCommand;
    private ICommand _jogXUpCommand;
    private ICommand _jogYUpCommand;
    private ICommand _jogYDownCommand;
    private ICommand _jogZDownCommand;
    private ICommand _jogZUpCommand;
    private ICommand _zeroAllCommand;
    private ICommand _unLockCommand;
    private ICommand _homeCommand;
    private ICommand _clearAlarmCommand;
    private ReactiveCommand<object, Unit> _doubleTapCommand;
    private ReactiveCommand<object, Unit> _hideBoxCommand;
    private string _unitSystem = "G21";
    private bool _connected;
    private Color _homeStateColor;
    private SettingsViewModel _settingsViewModel1;
    private bool _alarmActive;
    private bool _needsSetup;
    private readonly GHalSenderConfig _config;
    private int _selectedTool;
    private bool _hideToolChangeList;
    public bool ShowRTCommands { get; set; }
    public bool AutoConnect { get; set; }
    public bool IsJobRunning
    {
        get => _isJobRunning;
        set => _isJobRunning = value;
    }
    public bool HasATC
    {
        get => _hasAtc;
        set => _hasAtc = value;
    }
    public bool HasSdCard
    {
        get => _hasSdCard;
        set => _hasSdCard = value;
    }
    public bool HasProbing
    {
        get => _hasProbing;
        set => _hasProbing = value;
    }
    public bool IsFileLoaded
    {
        get => _isFileLoaded;
        set => _isFileLoaded = value;
    }
    public string UnitSystem
    {
        get => _unitSystem;
        set => _unitSystem = value;
    }
    public Color HomeStateColor
    {
        get => _homeStateColor;
        set => _homeStateColor = value;
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
    public int SelectedTool
    {
        get => _selectedTool;
        set => this.RaiseAndSetIfChanged(ref _selectedTool, value);
    }

    public RealTImeState State
    {
        get => _state;
        set => this.RaiseAndSetIfChanged(ref _state, value);
    }
    public SettingsViewModel SettingsViewModel
    {
        get => _settingsViewModel1;
        set => _settingsViewModel1 = value;
    }
    public ICommand ConnectCommand
    {
        get => _connectCommand;
        set => _connectCommand = value;
    }
    public ICommand ZeroAxis
    {
        get => _zeroAxis;
        set => _zeroAxis = value;
    }
    public ICommand JogXDownCommand
    {
        get => _jogXDownCommand;
        set => _jogXDownCommand = value;
    }
    public ICommand JogXUpCommand
    {
        get => _jogXUpCommand;
        set => _jogXUpCommand = value;
    }
    public ICommand JogYUpCommand
    {
        get => _jogYUpCommand;
        set => _jogYUpCommand = value;
    }
    public ICommand JogYDownCommand
    {
        get => _jogYDownCommand;
        set => _jogYDownCommand = value;
    }
    public ICommand JogZDownCommand
    {
        get => _jogZDownCommand;
        set => _jogZDownCommand = value;
    }
    public ICommand JogZUpCommand
    {
        get => _jogZUpCommand;
        set => _jogZUpCommand = value;
    }
    public ICommand ZeroAllCommand
    {
        get => _zeroAllCommand;
        set => _zeroAllCommand = value;
    }
    public ICommand UnLockCommand
    {
        get => _unLockCommand;
        set => _unLockCommand = value;
    }
    public ICommand HomeCommand
    {
        get => _homeCommand;
        set => _homeCommand = value;
    }
    public ICommand ClearAlarmCommand
    {
        get => _clearAlarmCommand;
        set => _clearAlarmCommand = value;
    }
    public ICommand ClearConsoleCommand { get; }
    public ICommand ToggleRTCommand { get; }
    public ICommand MdiTextCommand { get; }
    public ICommand WcsCommand { get; }
    public ICommand ToolSelectedCommand { get; }
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

    public MainViewModel(CommunicationManager commManager, SettingsViewModel settingsViewModel, ConfigManager configManager)
    {
        SettingsViewModel = settingsViewModel;
        _needsSetup = true;
        _commManager = commManager;
        _configManager = configManager;
        _config = _configManager.LoadConfig();

        Dispatcher.UIThread.ShutdownStarted += UIThread_ShutdownStarted;
        _commManager.OnStateReceived += _commManager_OnStateReceived;
        _commManager.onOptionsUpdated += _commManager_onOptionsUpdated;
        _commManager.OnConsoleLogReceived += _commManager_OnConsoleLogReceived;

        ConnectCommand = ReactiveCommand.Create(Connect);
        ZeroAxis = ReactiveCommand.Create<string>(Zero);
        HomeCommand = ReactiveCommand.Create(Home);
        UnLockCommand = ReactiveCommand.Create(Unlock);
        JogXDownCommand = ReactiveCommand.Create<string>(JogXDown);
        JogXUpCommand = ReactiveCommand.Create<string>(JogXUp);
        JogZUpCommand = ReactiveCommand.Create<string>(JogZUp);
        JogZDownCommand = ReactiveCommand.Create<string>(JogZDown);
        JogYUpCommand = ReactiveCommand.Create<string>(JogYUp);
        JogYDownCommand = ReactiveCommand.Create<string>(JogYDown);
        ZeroAllCommand = ReactiveCommand.Create(ZeroAll);
        ClearAlarmCommand = ReactiveCommand.Create(ClearAlarm);

        ClearConsoleCommand = ReactiveCommand.Create(ClearConsole);
        ToggleRTCommand = ReactiveCommand.Create(ToggleConsoleRt);
        MdiTextCommand = ReactiveCommand.Create<string>(MDIText);
        WcsCommand = ReactiveCommand.Create<string>(Wcs);
        ToolSelectedCommand = ReactiveCommand.Create<int>(ToolSelected);
        DoubleTapCommand = ReactiveCommand.Create<object>(DoubleTap);
        HideBoxCommand = ReactiveCommand.Create<object>(HideToolList);
        //TODO just temp will use the setting grblhal returns from $I and $I+ to build the axis count values 

        _axis = new ObservableCollection<Axis>
        {
            new()
            {
                Name = "X",
                ZeroWcsCommand  =  ZeroAxis,
                Order = 0
            },
            new()
            {
                Name = "Y",
                ZeroWcsCommand  =  ZeroAxis,
                Order = 1

            },
            new()
            {
                Name = "Z",
                ZeroWcsCommand  =  ZeroAxis,
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
        var toolNumber = _config.ToolList.Tools.Select(tool => tool.ToolNumber).ToList();
        ToolList.AddRange<int>(toolNumber);
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

    private void ProcessSignals(List<string> signals)
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
            foreach (var axis in e.AxisLabels)
            {
                if (_axis.All(x => x.Name != axis.ToString()))
                {
                    _axis.Add(new Axis
                    {
                        Name = axis.ToString(),
                        Order = e.AxisLabels.IndexOf(axis),
                        ZeroWcsCommand = ZeroAxis
                    });
                }
            }
            foreach (var signal in e.SignalLabels)
            {
                SignalList.Add(new Signal
                {
                    Id = signal.ToString()
                });
            }
            _needsSetup = false;
        }
    }
    private void JogYDown(string axis)
    {
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}-{jogDataDistance}F{FeedRate}";
        SendCommand(command);
    }
    private void JogYUp(string axis)
    {
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}{jogDataDistance}F{FeedRate}";
        SendCommand(command);
    }
    private void JogZUp(string axis)
    {
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}{jogDataDistance}F{FeedRate}";
        SendCommand(command);
    }
    private void JogZDown(string axis)
    {
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}-{jogDataDistance}F{FeedRate}";
        SendCommand(command);
    }
    private void JogXUp(string axis)
    {
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}{jogDataDistance}F{FeedRate}";
        SendCommand(command);
    }
    private void JogXDown(string axis)
    {
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}-{jogDataDistance}F{FeedRate}";
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
    //private double _mPosition;
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
    public string Id { get; set; }

    [ObservableProperty]
    private bool _triggered;
}
