using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gamepad;
using GrbLHALSender.Gcode;
using GrbLHALSender.WebServer;
using GrbLHALSender.Settings;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace GrbLHALSender.ViewModels;

public class MainViewModel : ViewModelBase
{
    private bool _fine;
    
    private readonly ConfigManager _configManager;
    private readonly MachineStateService _machineStateService;
    private JobViewModel _jobViewModel;
    private ObservableCollection<Axis> _axis;
    private ObservableCollection<string> _consoleOutput = new();
    private ObservableCollection<int> _toolList = new();
    private ObservableCollection<Signal> _signalList = [];
    private ObservableCollection<double> _jogStepList;
    private ObservableCollection<double> _jogRateList;
    private readonly GHalSenderConfig _config;
    private RealTImeState _state;
    private string _grblHalState = "";
    private string _wcs = "";
    private string _toolDisplay = "";
    private string _subState = "";
    private MachineSettings? _machineSettings;
    private string _spindleImagePath = "spindle.png";
    private bool _useAntiAlias = true;
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
    private ReactiveCommand<object, Unit> _hideBoxCommand;
    private bool _tlrCommandEnabled;
    private bool _unloadToolCommandEnabled;
    private bool _atcEnabled;
    private string _unloadToolMacro;
    private string _tlrMacro;
    private readonly GamepadService _gamepadService;
    private readonly WebServerService _webServerService;

    // Buffered console log messages — accumulated on the data thread, drained on UI timer
    private readonly Queue<string> _consoleLogBuffer = new();
    private readonly object _consoleLogLock = new();
    private bool _useMetric;
    private bool _machineInMetric;
    private string _unitText;
    private int _spindleOverRide;
    private int _consoleOutputIndex;
    private WindowState _fullScreenMode;
    private bool _uiActiveState;
    private bool _canSetTool;
    private bool _canJog;

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
    public bool ShowRtCommands { get; set; }
    public bool AutoConnect { get; set; }
    public JobViewModel JobViewModel { get; set; }
    public MacroViewModel MacroViewModel { get; set; }
    public SettingsViewModel SettingsViewModel { get; set; }
    public ConnectionViewModel ConnectionViewModel { get; set; }
    public DialogViewModel DialogViewModel { get; set; }
    public MdiViewModel MdiViewModel { get; set; }
    public AppConfigViewModel AppConfigViewModel { get; set; }
    public SdCardViewModel SdCardViewModel { get; set; }
    public CommunicationManager CommManager { get; set; }
    public ProbeViewModel ProbeViewModel { get; set; }
  

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

    // Dedicated properties for commonly-bound state fields.
    // These use RaiseAndSetIfChanged with string equality, so labels only
    // re-render when the displayed value actually changes — unlike binding
    // to State.X which re-evaluates every tick because State is a new reference.
    public string GrblHalState
    {
        get => _grblHalState;
        set => this.RaiseAndSetIfChanged(ref _grblHalState, value);
    }
    public string WcsDisplay
    {
        get => _wcs;
        set => this.RaiseAndSetIfChanged(ref _wcs, value);
    }
    public string ToolDisplay
    {
        get => _toolDisplay;
        set => this.RaiseAndSetIfChanged(ref _toolDisplay, value);
    }
    public string SubState
    {
        get => _subState;
        set => this.RaiseAndSetIfChanged(ref _subState, value);
    }

    public MachineSettings? MachineSettings
    {
        get => _machineSettings;
        set => this.RaiseAndSetIfChanged(ref _machineSettings, value);
    }
    public string SpindleImagePath
    {
        get => _spindleImagePath;
        set => this.RaiseAndSetIfChanged(ref _spindleImagePath, value);
    }

    public bool UseAntiAlias
    {
        get => _useAntiAlias;
        set => this.RaiseAndSetIfChanged(ref _useAntiAlias, value);
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
        set
        {
            this.RaiseAndSetIfChanged(ref _homeState, value);
            UpdateButtonStates();
        } 
        
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
    public int SpindleOverRide
    {
        get => _spindleOverRide;
        set => this.RaiseAndSetIfChanged(ref _spindleOverRide, value);
    }

    public int ConsoleOutputIndex
    {
        get => _consoleOutputIndex;
        set => this.RaiseAndSetIfChanged(ref _consoleOutputIndex, value);
    }

    public WindowState FullScreenMode
    {
        get => _fullScreenMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _fullScreenMode, value);
            this.RaisePropertyChanged(nameof(IsFullScreen));
        }
        
    }
    public bool IsFullScreen => FullScreenMode == WindowState.FullScreen;
    public GrblState CurrentGrblState => _machineStateService.GrblState;

    public bool CanJog
    {
        get => _canJog;
        set => this.RaiseAndSetIfChanged(ref _canJog, value);
    }

    public bool CanSetTool
    {
        get => _canSetTool;
        set => this.RaiseAndSetIfChanged(ref _canSetTool, value);
    }
   

    public ICommand CloseCommand { get; }
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

    public ReactiveCommand<object, Unit> HideBoxCommand
    {
        get => _hideBoxCommand;
        set => _hideBoxCommand = value;
    }

    public MainViewModel(CommunicationManager commManager, SettingsViewModel settingsViewModel,
        ConfigManager configManager, JobViewModel jobViewModel, MacroViewModel macroViewModel,
        ProbeViewModel probeViewModel, ConnectionViewModel connectionViewModel, DialogViewModel dialogViewModel,
        MdiViewModel mdiViewModel, GamepadService gamepadService, AppConfigViewModel appConfigViewModel,
        WebServerService webServerService, MachineStateService machineStateService,
        SdCardViewModel sdCardViewModel)
    {
        CommManager = commManager;
        ProbeViewModel = probeViewModel;
        SettingsViewModel = settingsViewModel;
        SdCardViewModel = sdCardViewModel;
        _needsSetup = true;
        _configManager = configManager;
        JobViewModel = jobViewModel;
        MacroViewModel = macroViewModel;
        _config = _configManager.LoadConfig();
        FullScreenMode = _config.Borderless ? WindowState.FullScreen : WindowState.Maximized;
        ConnectionViewModel = connectionViewModel;
        DialogViewModel = dialogViewModel;
        MdiViewModel = mdiViewModel;
        AppConfigViewModel = appConfigViewModel;
        MdiViewModel.MidiTextCommitted += MainViewModel_MidiTextCommitted;
        ConnectionViewModel.LoadFromConfig(_config);
        ProbeViewModel.LoadFromConfig(_config);
        JobViewModel.Config = _config;

        Dispatcher.UIThread.ShutdownStarted += UIThread_ShutdownStarted;
        _machineStateService = machineStateService;
        _machineStateService.PropertyChanged += OnMachineStateChanged;
        CommManager.onOptionsUpdated += _commManager_onOptionsUpdated;
        CommManager.onSettingUpdated += _commManager_onSettingUpdated;
        CommManager.OnConsoleLogReceived += _commManager_OnConsoleLogReceived;
        // Discovered aux pins are added as presets in AppConfigViewModel only —
        // they don't auto-appear as buttons until the user adds them through config.


        _configManager?.GHalSenderConfig?.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GHalSenderConfig.UseMetric))
            {
                SetUpUiUnit();
            }
            if (e.PropertyName == nameof(GHalSenderConfig.Borderless))
            {
                FullScreenMode = _config.Borderless ? WindowState.FullScreen : WindowState.Maximized;
            }
        };
        _machineStateService.PropertyChanged+= (_, e) =>
        {
            if (e.PropertyName == nameof(MachineStateService.GrblState))
            {
                UpdateButtonStates();
            }
        };

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
        CloseCommand = ReactiveCommand.Create(CloseApplication);

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

        _webServerService = webServerService;
        _webServerService.SetViewModel(this);
        _webServerService.StatusMessage += (_, msg) => ConsoleOutput.Add($"[WebServer] {msg}");
        _webServerService.Initialize(_config.WebServerConfig);

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
   

    private void UpdateButtonStates()
    {
        
        CanJog = Connected &&
                 CurrentGrblState is GrblState.Idle or GrblState.Tool or GrblState.Jog;

        CanSetTool = Connected && HomeState && CurrentGrblState is  GrblState.Idle or GrblState.Tool;
    }
    private void CloseApplication()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
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
        string command;
        if (_atcEnabled)
        {
            command = $"T{tool}M6";
        }
        else
        {
            command = _isJobRunning ? $"T{tool}M6" : $"M61Q{tool}";
        }

        SendCommand(command);
    }
    private void UnloadTool()
    {
        if (!AtcEnabled || !UnloadToolCommandEnabled) return;
        CommManager.SendCommand($"G65{_unloadToolMacro}");
    }

    private void SetTlr()
    {
        if (!AtcEnabled || !TlrCommandEnabled) return;
        CommManager.SendCommand($"G65{_tlrMacro}");
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
        CommManager.Adapter.WriteByte(command);
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
        SendCommand($"{GrblHalConstants.SpindleCCw}S{rpm}");
    }
    private void SpindleCw(string rpm)
    {
        SendCommand($"{GrblHalConstants.SpindleCw}S{rpm}");
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
        SpindleImagePath = _config.SpindleImagePath;
        UseAntiAlias = _config.UseAntiAlias;
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
        CommManager.SendCommand(command);
    }

    private void ToggleConsoleRt()
    {
        ShowRtCommands = !ShowRtCommands;
    }
    private void ClearConsole()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            ConsoleOutputIndex = -1;
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
        CommManager?.Adapter?.WriteByte(GrblHalConstants.GrblReset);
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
        _webServerService?.Stop();
        _gamepadService?.Stop();
        CommManager.ShutDown();
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
    /// <summary>
    /// Reacts to MachineStateService property changes (fires on UI thread at ~10 Hz).
    /// Maps service typed values to UI-bound properties and drains console log buffer.
    /// </summary>
    private void OnMachineStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var svc = _machineStateService;
        if (!svc.Connected) return;

       // Connected = true;

        // Map service positions to AxisCollection (UI concern: Axis objects have commands)
        int axisCount = Math.Min(svc.MachinePositions.Length, AxisCollection.Count);
        for (int i = 0; i < axisCount; i++)
        {
            var pos = new Position
            {
                MPos = svc.MachinePositions[i],
                Wco = svc.WorkPositions.Length > i ? svc.WorkPositions[i] : 0
            };
            AxisCollection[i].Position = pos;
        }

        // Copy typed values from service
        HomeState = svc.HomeState;
        State = svc.RawState;
        TLR = svc.TLR;
        FeedRate = svc.FeedRate;
        FeedOverRide = svc.FeedOverride;
        SpindleRpm = svc.SpindleRpm;
        ActualRpm = svc.ActualRpm;
        Tool = svc.ToolDisplay;
        SpindleOverRide = svc.RpmOverride;

        GrblHalState = svc.GrblStateString;
        WcsDisplay = svc.WcsDisplay;
        ToolDisplay = svc.ToolDisplay;
        SubState = svc.SubState;
        //SpindlePosition = svc.SpindlePosition;
        //WorkCoordinateOffset = svc.WorkCoordinateOffset;
        AlarmActive = svc.AlarmActive;

        // Drain buffered console log messages (from data thread) to UI.
        // Only process when the console panel is visible to avoid unnecessary
        // CollectionChanged events and ListBox layout work.
        if (ShowConsole)
        {
            lock (_consoleLogLock)
            {
                while (_consoleLogBuffer.Count > 0)
                    ConsoleOutput.Add(_consoleLogBuffer.Dequeue());
            }
            var rawState = svc.RawState;
            if (ShowRtCommands && rawState != null)
                ConsoleOutput.Add(rawState.RawRt);

            if (ConsoleOutput.Count > 200)
                ConsoleOutput.Clear();
            ConsoleOutputIndex = ConsoleOutput.Count - 1;
        }
        else
        {
            // Console hidden — discard buffered messages to prevent unbounded growth
            lock (_consoleLogLock)
            {
                _consoleLogBuffer.Clear();
            }
        }

        ProcessSignals(svc.SignalStatus);
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
        MachineSettings = CommManager.MachineData;

        MachineInMetric = CommManager.MachineData.ReportInMetric;
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
        if(Connected) return;
        switch (_config.Connection)
        {
            case GHalSenderConfig.ConnectionType.Tcp:
                CommManager.NewTcpConnection(_config.TcpSettings);
                break;
            case GHalSenderConfig.ConnectionType.Serial:
                CommManager.NewSerialConnection(_config.SerialSettings);
                break;
            default:
                CommManager.WebSocketConnection(_config.WebSocketSettings);
                break;
        }
        
        Connected = CommManager.Adapter.IsConnected;
        CommManager.Adapter.PropertyChanged+= (s, e) =>
        {
            if (e.PropertyName == nameof(ICommsAdapter.IsConnected))
            {
                Connected = CommManager.Adapter.IsConnected;
            }
        };
        
        CommManager.GetSettings();
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

