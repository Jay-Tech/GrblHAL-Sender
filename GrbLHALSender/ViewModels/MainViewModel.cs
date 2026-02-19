using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gamepad;
using GrbLHALSender.Gcode;
using GrbLHALSender.Settings;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels;

public class MainViewModel : ViewModelBase
{
    private bool _fine;
    private readonly CommunicationManager _commManager;
    private readonly ConfigManager _configManager;
    private JobViewModel _jobViewModel;
    private ProbeViewModel _probeViewModel;
    private ObservableCollection<Axis> _axis;
    private ObservableCollection<string> _consoleOutput = new();
    private ObservableCollection<int> _toolList = new();
    private ObservableCollection<Signal> _signalList = [];
    private ObservableCollection<double> _jogStepList;
    private ObservableCollection<double> _jogRateList;
    private readonly GHalSenderConfig _config;
    private RealTImeState _state;
    private Point3D? _spindlePosition;
    private Point3D? _workCoordinateOffset;
    private MachineSettings? _machineSettings;
    private bool _showConsole;
    private bool _isJobRunning;
    private int _spindleRpm;
    private bool _connected;
    private bool _alarmActive;
    private bool _needsSetup;
    private int _selectedTool;
    private bool _hideToolChangeList;
    private double _jogStep;
    private double _jogRate;
    private string _mdiText;
    private int _actualRpm;
    private int _feedRate;
    private int _feedOverRide;
    private int _spindleSetRpm;
    private bool _tlr = false;
    private string _callBackText;
    private string _tool;
    private bool _homeState;
    private ReactiveCommand<object, Unit> _doubleTapCommand;
    private ReactiveCommand<object, Unit> _hideBoxCommand;
    private bool _tlrCommandEnabled;
    private bool _unloadToolCommandEnabled;
    private bool _atcEnabled;
    private string _unloadToolMacro;
    private string _tlrMacro;
    private readonly GamepadService _gamepadService;

    // UI throttling: store latest state off-thread, push to UI on a timer
    private volatile RealTImeState? _latestState;
    private DispatcherTimer? _uiUpdateTimer;

    // Buffered console log messages — accumulated on the data thread, drained on UI timer
    private readonly Queue<string> _consoleLogBuffer = new();
    private readonly object _consoleLogLock = new();
    private bool _useMetric;
    private bool _machineInMetric;
    private string _unitText;

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

    public bool ShowConsole { get; set; }
    public bool ShowRTCommands { get; set; }
    public bool AutoConnect { get; set; }
    public JobViewModel JobViewModel { get; set; }
    public MacroViewModel MacroViewModel { get; set; }
    public SettingsViewModel SettingsViewModel { get; set; }
    public ConnectionViewModel ConnectionViewModel { get; set; }
    public DialogViewModel DialogViewModel { get; set; }
    public MdiViewModel MdiViewModel { get; set; }
    public AppConfigViewModel AppConfigViewModel { get; set; }
    public string UnitSystem { get; set; } = "G21";

    public bool UseMetric
    {
        get => _useMetric;
        set => this.RaiseAndSetIfChanged(ref _useMetric, value);
    }

    public bool MachineInMetric
    {
        get => _machineInMetric;
        set => this.RaiseAndSetIfChanged(ref _machineInMetric, value);
    }

    public string UnitText
    {
        get => _unitText;
        set => this.RaiseAndSetIfChanged(ref _unitText, value);
    }

    public ProbeViewModel ProbeViewModel
    {
        get => _probeViewModel;
        set => _probeViewModel = value;
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
    public int SpindleRpm
    {
        get => _spindleRpm;
        set => this.RaiseAndSetIfChanged(ref _spindleRpm, value);
    }
    public int ActualRpm
    {
        get => _actualRpm;
        set => this.RaiseAndSetIfChanged(ref _actualRpm, value);
    }
    public RealTImeState State
    {
        get => _state;
        set => this.RaiseAndSetIfChanged(ref _state, value);
    }
    public Point3D? SpindlePosition
    {
        get => _spindlePosition;
        set => this.RaiseAndSetIfChanged(ref _spindlePosition, value);
    }
    public Point3D? WorkCoordinateOffset
    {
        get => _workCoordinateOffset;
        set => this.RaiseAndSetIfChanged(ref _workCoordinateOffset, value);
    }
    public MachineSettings? MachineSettings
    {
        get => _machineSettings;
        set => this.RaiseAndSetIfChanged(ref _machineSettings, value);
    }
    public int FeedRate
    {
        get => _feedRate;
        set => this.RaiseAndSetIfChanged(ref _feedRate, value);
    }
    public int FeedOverRide
    {
        get => _feedOverRide;
        set => this.RaiseAndSetIfChanged(ref _feedOverRide, value);
    }
    public int SpindleSetRpm
    {
        get => _spindleSetRpm;
        set => this.RaiseAndSetIfChanged(ref _spindleSetRpm, value);
    }
    public string CallBackText
    {
        get => _callBackText;
        set => this.RaiseAndSetIfChanged(ref _callBackText, value);
    }
    public bool HomeState
    {
        get => _homeState;
        set => this.RaiseAndSetIfChanged(ref _homeState, value);
    }
    public bool TLR
    {
        get => _tlr;
        set => this.RaiseAndSetIfChanged(ref _tlr, value);
    }
    public bool AtcEnabled
    {
        get => _atcEnabled;
        set => this.RaiseAndSetIfChanged(ref _atcEnabled, value);
    }

    public bool TlrCommandEnabled
    {
        get => _tlrCommandEnabled;
        set => this.RaiseAndSetIfChanged(ref _tlrCommandEnabled, value);
    }

    public bool UnloadToolCommandEnabled
    {
        get => _unloadToolCommandEnabled;
        set => this.RaiseAndSetIfChanged(ref _unloadToolCommandEnabled, value);
    }

    public ICommand ConnectCommand { get; set; }
    public ICommand ZeroAxis { get; set; }
    public ICommand ZeroAllCommand { get; set; }
    public ICommand UnLockCommand { get; set; }
    public ICommand HomeCommand { get; set; }
    public ICommand ClearAlarmCommand { get; set; }
    public ICommand JogNegCommand { get; }
    public ICommand JogPosCommand { get; }
    public ICommand JogCancelCommand { get; }
    public ICommand ClearConsoleCommand { get; }
    public ICommand ToggleRtCommand { get; }
    public ICommand WcsCommand { get; }
    public ICommand ToolSelectedCommand { get; }
    public ICommand FeedRateChangeCommand { get; }
    public ICommand StepRateChangeCommand { get; }
    public ICommand SpindleCWCommand { get; }
    public ICommand SpindleCCWCommand { get; }
    public ICommand SpindleOffCommand { get; }
    public ICommand SpindleResetCommand { get; }
    public ICommand SpindleIncreaseCommand { get; }
    public ICommand SpindleDecreaseCommand { get; }
    public ICommand FeedOrPlus { get; }
    public ICommand FeedOrMinus { get; }
    public ICommand FeedOrReset { get; }
    public ICommand RapidOrMediumCommand { get; }
    public ICommand RapidOrFineCommand { get; }
    public ICommand ResetRapidCommand { get; }
    public ICommand SpindleSetSpeedCommand { get; }
    public ICommand SetToolSelectCommand { get; }
    public ICommand SetTlrCommand { get; }
    public ICommand UnloadToolCommand { get; }
    public ICommand OpenProbeCommand { get; }
    public Action? CloseConsoleAction { get; set; }
    public ICommand CloseConsoleCommand { get; }

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

    public MainViewModel(CommunicationManager commManager, SettingsViewModel settingsViewModel,
        ConfigManager configManager, JobViewModel jobViewModel, MacroViewModel macroViewModel,
        ProbeViewModel probeViewModel, ConnectionViewModel connectionViewModel, DialogViewModel dialogViewModel,
        MdiViewModel mdiViewModel, GamepadService gamepadService, AppConfigViewModel appConfigViewModel)
    {
        ProbeViewModel = probeViewModel;
        SettingsViewModel = settingsViewModel;
        _needsSetup = true;
        _commManager = commManager;
        _configManager = configManager;
        JobViewModel = jobViewModel;
        MacroViewModel = macroViewModel;
        _config = _configManager.LoadConfig();
        ConnectionViewModel = connectionViewModel;
        DialogViewModel = dialogViewModel;
        MdiViewModel = mdiViewModel;
        AppConfigViewModel = appConfigViewModel;
        MdiViewModel.MidiTextCommitted += MainViewModel_MidiTextCommitted;
        ConnectionViewModel.LoadFromConfig(_config);
        ProbeViewModel.LoadFromConfig(_config);
        JobViewModel.Config = _config;

        Dispatcher.UIThread.ShutdownStarted += UIThread_ShutdownStarted;
        _commManager.OnStateReceived += _commManager_OnStateReceived;
        _commManager.onOptionsUpdated += _commManager_onOptionsUpdated;
        _commManager.onSettingUpdated += _commManager_onSettingUpdated;
        _commManager.OnConsoleLogReceived += _commManager_OnConsoleLogReceived;


        _configManager?.GHalSenderConfig?.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GHalSenderConfig.UseMetric))
            {
                SetUpUiUnit();
            }
        };

        // Throttled UI update timer — coalesces status updates to ~10 Hz
        _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _uiUpdateTimer.Tick += UiUpdateTimerTick;
        _uiUpdateTimer.Start();

        ConnectCommand = ReactiveCommand.Create(Connect);
        ZeroAxis = ReactiveCommand.Create<string>(Zero);
        HomeCommand = ReactiveCommand.Create(Home);
        UnLockCommand = ReactiveCommand.Create(Unlock);
        JogNegCommand = ReactiveCommand.Create<string>(JogNeg);
        JogPosCommand = ReactiveCommand.Create<string>(JogPos);
        JogCancelCommand = ReactiveCommand.Create(JogCancel);
        ZeroAllCommand = ReactiveCommand.Create(ZeroAll);
        ClearAlarmCommand = ReactiveCommand.Create(ClearAlarm);
        ClearConsoleCommand = ReactiveCommand.Create(ClearConsole);
        ToggleRtCommand = ReactiveCommand.Create(ToggleConsoleRt);
        WcsCommand = ReactiveCommand.Create<string>(Wcs);
        HideBoxCommand = ReactiveCommand.Create<object>(HideToolList);
        FeedRateChangeCommand = ReactiveCommand.Create<double>(ChangeFeedRate);
        StepRateChangeCommand = ReactiveCommand.Create<double>(ChangeStepRate);
        SpindleCWCommand = ReactiveCommand.Create<string>(SpindleCw);
        SpindleCCWCommand = ReactiveCommand.Create<string>(SpindleCcw);
        SetToolSelectCommand = ReactiveCommand.Create<int>(SetSelectedTool);
        SpindleOffCommand = ReactiveCommand.Create(SpindleOff);
        SpindleResetCommand = ReactiveCommand.Create(SpindleReset);
        SpindleIncreaseCommand = ReactiveCommand.Create(SpindleIncrease);
        SpindleDecreaseCommand = ReactiveCommand.Create(SpindleDecrease);
        FeedOrPlus = ReactiveCommand.Create(FeedPlus);
        FeedOrMinus = ReactiveCommand.Create(FeedMinus);
        FeedOrReset = ReactiveCommand.Create(FeedReset);
        RapidOrMediumCommand = ReactiveCommand.Create(RapidMedium);
        RapidOrFineCommand = ReactiveCommand.Create(RapidFine);
        ResetRapidCommand = ReactiveCommand.Create(RapidReset);
        SpindleSetSpeedCommand = ReactiveCommand.Create<string>(SetSpindleSpeed);
        ToolSelectedCommand = ReactiveCommand.Create<int>(ToolSelected);
        SetTlrCommand = ReactiveCommand.Create(SetTlr);
        UnloadToolCommand = ReactiveCommand.Create(UnloadTool);
        OpenProbeCommand = ReactiveCommand.Create(OpenProbe);
        CloseConsoleCommand = ReactiveCommand.Create(() => CloseConsoleAction?.Invoke());

        //TODO just temp will use the setting grblhal returns from $I and $I+ to build the axis count values
        _axis =
        [
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
            }

        ];

        SetUpUiSettings();

        _gamepadService = gamepadService;
        _gamepadService.SetViewModel(this);
        _gamepadService.GamepadStatusMessage += (_, msg) => ConsoleOutput.Add($"[Gamepad] {msg}");
        _gamepadService.Initialize(_config.GamepadConfig);

        if (!_config.AutoConnect) return;
        try
        {
            Connect();
        }
        catch (Exception e)
        {
            // Handle connection exceptions (e.g., show a message to the user)
            ConsoleOutput.Add($"Connection failed: {e.Message}");
        }
    }

    private void MainViewModel_MidiTextCommitted(string command)
    {
        SendCommand(command);
    }

    private void SetSelectedTool(int tool)
    {
        SelectedTool = tool;
    }
    public string Tool
    {
        get => _tool;

        set
        {
            if (_tool == value) return;
            _tool = value;
            SetTool(value);
        }
    }

    private void SetTool(string tool)
    {
        if (tool == SelectedTool.ToString()) return;
        if (int.TryParse(tool, out var t))
        {
            SelectedTool = t;
        }
    }
    private void ToolSelected(int tool)
    {
        var command = _isJobRunning ? $"T{tool}M6" : $"M61Q{tool}";
        SendCommand(command);
    }
    private void UnloadTool()
    {
        if (!AtcEnabled || !UnloadToolCommandEnabled) return;
        _commManager.SendCommand($"G65{_tlrMacro}");
    }

    private void SetTlr()
    {
        if (!AtcEnabled || !TlrCommandEnabled) return;
        _commManager.SendCommand($"G65{_unloadToolMacro}");
    }

    private void RapidReset()
    {
        SendByteCommand(GrblHalConstants.RapidOrReset);
    }

    private void RapidFine()
    {
        SendByteCommand(GrblHalConstants.RapidOrLow);
    }

    private void RapidMedium()
    {
        SendByteCommand(GrblHalConstants.RapidOrMedium);
    }

    private void FeedReset()
    {
        SendByteCommand(GrblHalConstants.FeedOrReset);
    }

    private void FeedMinus()
    {
        var command = _fine ? GrblHalConstants.FeedOrFineMinus : GrblHalConstants.FeedOrCoarseMinus;
        SendByteCommand(command);
    }
    public void SendByteCommand(byte command)
    {
        _commManager.Adapter.WriteByte(command);
    }
    private void FeedPlus()
    {
        var command = _fine ? GrblHalConstants.FeedOrFinePlus : GrblHalConstants.FeedOrCoarsePlus;
        SendByteCommand(command);
    }
    private void SpindleDecrease()
    {
        SendByteCommand(GrblHalConstants.SpindleFineMinus);
    }
    private void SpindleIncrease()
    {
        SendByteCommand(GrblHalConstants.SpindleFinePlus);
    }
    private void SpindleReset()
    {
        SendByteCommand(GrblHalConstants.SpindleReset);
    }
    private void SpindleOff()
    {
        SendCommand(GrblHalConstants.SpindleOff);
    }
    private void SpindleCcw(string rpm)
    {
        SendCommand($"{GrblHalConstants.SpindleCCw}{rpm}");
    }
    private void SpindleCw(string rpm)
    {
        SendCommand($"{GrblHalConstants.SpindleCw}{rpm}");
    }
    private void SetSpindleSpeed(string speed)
    {
        if (string.IsNullOrEmpty(speed)) return;
        SendCommand($"S{speed}");
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

    private void SetUpUiSettings()
    {
        SetUpUiUnit();
        ToolList.AddRange(_config.ToolList.Tools);
        AtcEnabled = _config.AtcConfig.EnableAtc;
        TlrCommandEnabled = !string.IsNullOrEmpty(_config.AtcConfig.TlrMacroName) && AtcEnabled;
        if (TlrCommandEnabled)
        {
            _tlrMacro = _config.AtcConfig.TlrMacroName ?? " ";
        }
        UnloadToolCommandEnabled = !string.IsNullOrEmpty(_config.AtcConfig.UnloadToolMacroName) && AtcEnabled;
        if (UnloadToolCommandEnabled)
        {
            _unloadToolMacro = _config.AtcConfig.UnloadToolMacroName ?? "";
        }
    }

    private void SetUpUiUnit()
    {
        UseMetric = _config.UseMetric;
        UnitText = UseMetric ? "mm" : "in";
        UnitSystem = UseMetric ? "G21" : "G20";
        AutoConnect = _config.AutoConnect;
        JogRateList = new ObservableCollection<double>(UseMetric ? _config.JogSpeedMetric : _config.JogSpeedImperial);
        JogStepList = new ObservableCollection<double>(UseMetric ? _config.JogDistanceMetric : _config.JogDistanceImperial);
        JogStep = JogStepList[^1];
        JogRate = JogRateList[^1];
    }
    private void Wcs(string command)
    {
        SendCommand(command);
    }
    public void SendCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return;
        ConsoleOutput.Add(command);
        _commManager.SendCommand(command);
    }

    private void ToggleConsoleRt()
    {
        ShowRTCommands = !ShowRTCommands;
    }
    private void ClearConsole()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            ConsoleOutput.Clear();
        });
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
    private void Unlock()
    {
        _commManager?.Adapter?.WriteByte(GrblHalConstants.GrblReset);
    }
    private void OpenProbe()
    {
        ProbeViewModel.SaveToConfig(_config);
        _configManager.SaveConfig();
    }

    private void UIThread_ShutdownStarted(object? sender, EventArgs e)
    {
        ProbeViewModel.SaveToConfig(_config);
        _configManager.SaveConfig();
        _gamepadService?.Stop();
        _commManager.ShutDown();
    }
    private void _commManager_OnConsoleLogReceived(object? sender, string e)
    {
        // Buffer console messages — they arrive on the data thread and cannot
        // safely modify the ObservableCollection directly. The UI timer drains them.
        if (!ShowConsole) return;
        lock (_consoleLogLock)
        {
            _consoleLogBuffer.Enqueue(e);
        }
    }
    private void _commManager_OnStateReceived(object? sender, RealTImeState e)
    {
        // Store the latest state — the UI timer will pick it up and apply it.
        // This avoids flooding the UI thread with property notifications on every poll.
        _latestState = e;
    }

    /// <summary>
    /// Timer callback that applies the latest machine state to UI-bound properties.
    /// Runs on the UI thread at ~10 Hz, coalescing multiple status updates into one pass.
    /// </summary>
    private void UiUpdateTimerTick(object? sender, EventArgs e)
    {
        var state = Interlocked.Exchange(ref _latestState, null);
        if (state == null) return;

        Connected = true;
        for (int i = 0; i < state.MPos.Length; i++)
        {
            //var pos = new Position
            //{
            //    MPos = double.Parse(state.MPos[i])


            //};
            var pos = new Position();
            var machinePos = double.Parse(state.MPos[i]);
            pos.MPos = UseMetric switch
            {
                false when MachineInMetric => machinePos / 25.4,
                true when !MachineInMetric => machinePos * 25.4,
                _ => machinePos
            };
            if (state.Wco.Length > 0)
            {
                var wco = double.Parse(state.MPos[i]) - double.Parse(state?.Wco[i] ?? "0.0");

                pos.Wco = UseMetric switch
                {
                    false when MachineInMetric => wco / 25.4,
                    true when !MachineInMetric => wco * 25.4,
                    _ => wco
                };
            }

            AxisCollection[i].Position = pos;
        }
        HomeState = state.Home;
        State = state;
        TLR = state.TLR;
        SetFeedAndSpeeds(State);
        Tool = state.Tool;

        // Update spindle position for 3D visualizer
        // G-code toolpath is in work coordinates, so we must convert MPos to WPos
        // WPos = MPos - WCO (Work Coordinate Offset)
        if (state.MPos.Length >= 3 &&
            float.TryParse(state.MPos[0], out float mx) &&
            float.TryParse(state.MPos[1], out float my) &&
            float.TryParse(state.MPos[2], out float mz))
        {
            float wx = mx, wy = my, wz = mz;
            if (state.Wco.Length >= 3 &&
                float.TryParse(state.Wco[0], out float wcoX) &&
                float.TryParse(state.Wco[1], out float wcoY) &&
                float.TryParse(state.Wco[2], out float wcoZ))
            {
                wx = mx - wcoX;
                wy = my - wcoY;
                wz = mz - wcoZ;
                var newWco = new Point3D(wcoX, wcoY, wcoZ);
                if (_workCoordinateOffset == null || !_workCoordinateOffset.Value.Equals(newWco))
                    WorkCoordinateOffset = newWco;
            }
            var newSpindlePos = new Point3D(wx, wy, wz);
            if (_spindlePosition == null || !_spindlePosition.Value.Equals(newSpindlePos))
                SpindlePosition = newSpindlePos;

            // Push spindle position to JobViewModel for progress tracking
            JobViewModel.CurrentSpindlePosition = newSpindlePos;
        }

        AlarmActive = state.GrblHalState == "Alarm";

        // Drain buffered console log messages (from data thread) to UI
        lock (_consoleLogLock)
        {
            while (_consoleLogBuffer.Count > 0)
            {
                ConsoleOutput.Add(_consoleLogBuffer.Dequeue());
            }
        }
        if (ShowConsole && ShowRTCommands)
        {
            ConsoleOutput.Add(state.RawRt);
        }
        if (ConsoleOutput.Count > 200)
        {
            ConsoleOutput.Clear();
        }

        ProcessSignals(state.SignalStatus);
    }

    private void SetFeedAndSpeeds(RealTImeState rt)
    {
        if (int.TryParse(rt.FeedRate, out var feedRate))
        {
            FeedRate = feedRate;
        }
        if (int.TryParse(rt.FeedOverRide, out var fo))
        {
            FeedOverRide = fo;
        }
        if (int.TryParse(rt.ProgramRPM, out var ps))
        {
            SpindleRpm = ps;
        }
        if (int.TryParse(rt.ActualRpm, out var rpm))
        {
            this.ActualRpm = rpm;
        }
    }
    private void ProcessSignals(List<char> signals)
    {
        // Single pass: set each signal's Triggered state only if it actually changed.
        // This avoids redundant property change notifications that would trigger
        // unnecessary UI binding updates and layout passes.
        foreach (var sig in SignalList)
        {
            bool shouldBeTriggered = signals.Count > 0 && signals.Contains(sig.Id);
            if (sig.Triggered != shouldBeTriggered)
                sig.Triggered = shouldBeTriggered;
        }
    }
    private void _commManager_onSettingUpdated(object? sender, List<GrblHalSetting> e)
    {
        MachineSettings = _commManager.MachineData;

        MachineInMetric = _commManager.MachineData.ReportInMetric;
        if (UseMetric != MachineInMetric)
        {
            SetUpUiUnit();
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
    public void JogContinuousNeg(string axis)
    {
        var distance = GetMachineDistance(axis);
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}-{distance}F{JogRate}";
        SendCommand(command);
    }
    public void JogContinuousPos(string axis)
    {
        var distance = GetMachineDistance(axis);
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}{distance}F{JogRate}";
        SendCommand(command);
    }

    private string GetMachineDistance(string axis)
    {
        var distance = axis switch
        {
            "X" => $"{_machineSettings?.XSize.ToInvariantString()}",
            "Y" => $"{_machineSettings?.YSize.ToInvariantString()}",
            "Z" => $"{_machineSettings?.ZSize.ToInvariantString()}",
            _ => "1000"
        };
        return distance;
    }


    public void JogCancel()
    {
        SendByteCommand(GrblHalConstants.JogCancel);
    }
    public void Connect()
    {

        if (_config.Connection == GHalSenderConfig.ConnectionType.Tcp)
        {
            _commManager.NewTcpConnection(_config.TcpSettings);
        }
        else if (_config.Connection == GHalSenderConfig.ConnectionType.Serial)
        {
            _commManager.NewSerialConnection(_config.SerialSettings);
        }
        else
        {
            _commManager.WebSocketConnection(_config.WebSocketSettings);
        }

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

