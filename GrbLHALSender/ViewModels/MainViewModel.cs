using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gamepad;
using GrbLHALSender.Gcode;
using GrbLHALSender.Gpio;
using GrbLHALSender.Settings;
using GrbLHALSender.States;
using GrbLHALSender.Updates;
using GrbLHALSender.Utility;
using GrbLHALSender.WebServer;
using GrbLHALSender.Pendant;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Windows.Input;

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
    private bool _useHardwareRenderer;
    private Control? _renderControl;
    private bool _showConsole;
    private bool _isJobRunning;
    private int _spindleRpm;
    private bool _connected;
    private bool _mpgActive;
    private bool _pendantConnected;
    private bool _alarmActive;
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
    private readonly GpioOutputService _gpioOutputService;
    private readonly WebServerService _webServerService;
    private readonly PendantService _pendantService;
    private readonly FileUploadService _fileUploadService;
    private readonly GcodeEventInjector _eventInjector;

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
    private bool _canUseMdi;
    private SpindleDirection _spindleDirection = SpindleDirection.Off;

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

    // Must exceed the largest multi-line response the console can receive in
    // one command — $$ / $ES on grblHAL return 200+ lines. The console ListBox
    // is non-virtualized, so this cap also bounds its layout cost.
    private const int MaxConsoleLines = 500;

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
    public SurfacingViewModel SurfacingViewModel { get; set; }
    public CheatSheetViewModel CheatSheetViewModel { get; } = new();


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
        set
        {
            if (_connected == value) return;
            this.RaiseAndSetIfChanged(ref _connected, value);
            this.RaisePropertyChanged(nameof(ControlsEnabled));
            this.RaisePropertyChanged(nameof(SpindleOffEnabled));
        }
    }

    /// <summary>
    /// The controller has handed its input stream to a hardware MPG, via the
    /// MPG_MODE pin or the 0x8B toggle.
    /// </summary>
    public bool MpgActive
    {
        get => _mpgActive;
        private set
        {
            if (_mpgActive == value) return;
            this.RaiseAndSetIfChanged(ref _mpgActive, value);
            this.RaisePropertyChanged(nameof(ControlsEnabled));
            this.RaisePropertyChanged(nameof(SpindleOffEnabled));
        }
    }

    /// <summary>
    /// Whether this window's controls should accept input.
    ///
    /// Disconnected is the obvious case. The other is MPG mode: while a
    /// hardware pendant holds the controller's input stream, anything the
    /// sender writes is at best ignored, so leaving the buttons live invites
    /// the operator to press one and conclude the machine is broken. The same
    /// reasoning already gates PendantService, which refuses to jog while
    /// MpgActive - this puts the equivalent in front of the person.
    /// </summary>
    public bool ControlsEnabled => Connected && !MpgActive;

    /// <summary>
    /// Whether Spindle Off can reach the controller.
    ///
    /// Normally it streams M05, so it needs the stream like everything else. The
    /// exception is a hold, where it sends the real-time toggle instead and any
    /// stream will do - so it stays live through MPG mode on that one path. Hold
    /// first, then this: the two together are the whole of what the PC can still
    /// do while someone else drives the machine.
    /// </summary>
    public bool SpindleOffEnabled =>
        ControlsEnabled || (Connected && CurrentGrblState == GrblState.Hold);

    /// <summary>
    /// The wireless pendant has a live connection to this application.
    ///
    /// Nothing to do with MpgActive, despite both being "a pendant". This one
    /// talks to us, and PendantService turns what it sends into ordinary jog
    /// commands on the sender's own stream - the controller never learns it
    /// exists. So the interface stays fully usable and both can drive the
    /// machine. The indicator says the handheld is live; it takes nothing away.
    /// </summary>
    public bool PendantConnected
    {
        get => _pendantConnected;
        private set => this.RaiseAndSetIfChanged(ref _pendantConnected, value);
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

    public bool UseHardwareRenderer
    {
        get => _useHardwareRenderer;
        set
        {
            var old = _useHardwareRenderer;
            this.RaiseAndSetIfChanged(ref _useHardwareRenderer, value);
            if (old != value)
                RebuildRenderControl();
        }
    }

    public Control? RenderControl
    {
        get => _renderControl;
        private set => this.RaiseAndSetIfChanged(ref _renderControl, value);
    }

    private void RebuildRenderControl()
    {
        RenderControl = _useHardwareRenderer
            ? Views.RenderControlFactory.CreateHardwareRenderer()
            : Views.RenderControlFactory.CreateSoftwareRenderer();
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

    /// <summary>
    /// Whether the operator may type a command straight to the controller. Gated on the
    /// same machine states as jogging, and additionally closed for the whole duration of
    /// a job: an MDI command sent mid-job lands in the middle of the program and its
    /// "ok" is miscounted by the streamer's character-counting protocol.
    /// </summary>
    public bool CanUseMdi
    {
        get => _canUseMdi;
        set => this.RaiseAndSetIfChanged(ref _canUseMdi, value);
    }
    /// <summary>
    /// What the controller says the spindle is doing. The three spindle buttons all
    /// render this one value, so they cannot contradict each other or show a direction
    /// the machine never entered.
    /// </summary>
    public SpindleDirection SpindleDirection
    {
        get => _spindleDirection;
        private set => this.RaiseAndSetIfChanged(ref _spindleDirection, value);
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
    /// <summary>
    /// Where the file browser should open. Files uploaded over the web server land in the
    /// app data directory, which on Linux is <c>~/.config</c> — hidden, so a picker that
    /// starts anywhere else will not show them and offers no way to reach them on a
    /// touchscreen with no keyboard.
    /// </summary>
    public string GcodeStartFolder => _fileUploadService.UploadDirectory;

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
        SdCardViewModel sdCardViewModel, UpdateCheckService updateCheckService,
        SurfacingViewModel surfacingViewModel, GcodeEventInjector eventInjector,
        FileUploadService fileUploadService, GpioOutputService gpioOutputService,
        PendantService pendantService)
    {
        _gpioOutputService = gpioOutputService;
        CommManager = commManager;
        _fileUploadService = fileUploadService;
        _eventInjector = eventInjector;
        ProbeViewModel = probeViewModel;
        SurfacingViewModel = surfacingViewModel;
        SettingsViewModel = settingsViewModel;
        SdCardViewModel = sdCardViewModel;
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
        // Streaming diagnostics reach the console. Raised on the comms thread, so the
        // add has to be marshalled — ConsoleOutput is bound.
        JobViewModel.DiagnosticMessage += (_, msg) =>
            Dispatcher.UIThread.Post(() => ConsoleOutput.Add($"[Job] {msg}"));
        // Job start/end has to re-evaluate the controls that send a g-code line —
        // GrblState alone does not change at the moment a job begins or ends.
        JobViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(JobViewModel.JobRunning))
                UpdateButtonStates();
        };
        ConnectionViewModel.LoadFromConfig(_config);
        ProbeViewModel.LoadFromConfig(_config);
        JobViewModel.Config = _config;

        Dispatcher.UIThread.ShutdownStarted += UIThread_ShutdownStarted;
        _machineStateService = machineStateService;
        _machineStateService.PropertyChanged += OnMachineStateChanged;
        CommManager.onOptionsUpdated += _commManager_onOptionsUpdated;
        CommManager.onSettingUpdated += _commManager_onSettingUpdated;
        CommManager.onMachineDataChanged += _commManager_onMachineDataChanged;
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
            if (e.PropertyName == nameof(GHalSenderConfig.Renderer))
            {
                UseHardwareRenderer = _config.Renderer == GHalSenderConfig.RendererType.Hardware;
            }
        };
        SpindleDirection = _machineStateService.SpindleDirection;
        _machineStateService.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MachineStateService.GrblState):
                    UpdateButtonStates();
                    break;
                case nameof(MachineStateService.SpindleDirection):
                    SpindleDirection = _machineStateService.SpindleDirection;
                    break;
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
        SpindleCWCommand = ReactiveCommand.Create(SpindleCw);
        SpindleCCWCommand = ReactiveCommand.Create(SpindleCcw);
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
        SpindleSetSpeedCommand = ReactiveCommand.Create(SetSpindleSpeed);
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

        _pendantService = pendantService;
        _pendantService.SetViewModel(this);
        _pendantService.PendantStatusMessage += (_, msg) => ConsoleOutput.Add($"[Pendant] {msg}");
        // Raised from the listener task, so it has to cross to the UI thread.
        _pendantService.PendantConnectionChanged += (_, connected) =>
            Dispatcher.UIThread.Post(() => PendantConnected = connected);
        _pendantService.Initialize(_config.PendantConfig);

        updateCheckService.StatusMessage += (_, msg) =>
            Dispatcher.UIThread.Post(() => ConsoleOutput.Add($"[Update] {msg}"));
        _ = updateCheckService.CheckForUpdateAsync();

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

        SpindleSetRpm = 1000;
    }

    private void UpdateButtonStates()
    {
        // A running job owns the link. The state check alone is not enough for the
        // controls that send a g-code line: mid-job the machine reports Tool during a
        // tool change and can read Idle between blocks, both of which pass the state
        // test while the streamer is still counting acks for lines in flight.
        var jobRunning = JobViewModel?.JobRunning ?? false;

        // Tracks GrblState, because entering and leaving a hold is what changes
        // whether Spindle Off has a real-time route to the controller.
        this.RaisePropertyChanged(nameof(SpindleOffEnabled));

        // ControlsEnabled, not Connected: during MPG mode the controller reports Jog
        // while the hardware wheel drives it, which passes the state test - so the
        // arrows would look available while jogging that is not ours is underway.
        CanJog = ControlsEnabled &&
                 CurrentGrblState is GrblState.Idle or GrblState.Tool or GrblState.Jog;

        CanSetTool = ControlsEnabled && HomeState && !jobRunning &&
                     CurrentGrblState is GrblState.Idle or GrblState.Tool;

        // One rule for every control that hands a g-code line straight to the
        // controller: MDI, the macro buttons, and the probe cycles. A running job is not
        // allowed, because those lines would land in the middle of the program. Nor is
        // MPG mode, where the controller is not reading our stream at all.
        var canSendManualGcode = ControlsEnabled && !jobRunning &&
                                 CurrentGrblState is GrblState.Idle or GrblState.Tool or GrblState.Jog;

        // A mid-job tool change is the one exception for MDI. grblHAL's protocol requires
        // the operator to touch off ($TPW in modes 1 and 2) before cycle start, so that
        // has to be reachable while the job sits paused at an M6 — otherwise the tool
        // change cannot be completed at all. Safe now that commands from outside the
        // streamer are accounted for, and the controller rejects whatever it will not
        // accept at that point, which is treated as a manual failure rather than the
        // job's.
        CanUseMdi = canSendManualGcode ||
                    (ControlsEnabled && jobRunning && CurrentGrblState is GrblState.Tool);

        // Whether this machine's $341 mode expects the operator to touch the new tool off.
        // Read from the controller rather than configured here, so the control cannot
        // appear on a machine that has no such step — or be missing on one that does.
        if (JobViewModel != null)
            JobViewModel.ToolChangeNeedsTouchOff = MachineSettings?.ToolChangeNeedsTouchOff ?? false;
        if (MacroViewModel != null) MacroViewModel.CanRunMacro = canSendManualGcode;
        if (ProbeViewModel != null) ProbeViewModel.CanProbe = canSendManualGcode;
    }

    private void CloseApplication()
    {
        
        if (Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void TryLinuxShutdown()
    {
        // Try /sbin/shutdown -h now first, then systemctl poweroff as fallback.
        // Both usually require root or an explicit polkit/sudoers rule.
        string[][] attempts =
        [
            ["/sbin/shutdown", "-h", "now"],
            ["/usr/sbin/shutdown", "-h", "now"],
            ["/bin/systemctl", "poweroff"]
        ];

        foreach (var attempt in attempts)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = attempt[0],
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var arg in attempt.Skip(1))
                    startInfo.ArgumentList.Add(arg);

                using var proc = Process.Start(startInfo);
                if (proc == null) continue;

                // Wait briefly so we can capture the error if it exits fast.
                // Real shutdowns daemonize and this Wait returns after the fork.
                proc.WaitForExit(2000);

                var stderr = proc.StandardError.ReadToEnd();
                var stdout = proc.StandardOutput.ReadToEnd();

                ConsoleOutput.Add($"[Shutdown] {attempt[0]} exit={proc.ExitCode} " +
                                  $"stdout='{stdout.Trim()}' stderr='{stderr.Trim()}'");

                if (proc.ExitCode == 0)
                    return; // scheduled — we're done
            }
            catch (Exception ex)
            {
                ConsoleOutput.Add($"[Shutdown] {attempt[0]} threw: {ex.Message}");
            }
        }
    }

    private void MainViewModel_MidiTextCommitted(string command)
    {
        // Enforced here as well as by the panel's IsEnabled, so the rule holds however
        // the text was submitted and does not depend on the view's layout staying put.
        if (!CanUseMdi)
        {
            ConsoleOutput.Add(JobViewModel.JobRunning
                ? "MDI blocked: a job is running (allowed during a tool change)"
                : $"MDI blocked: machine is {GrblHalState}");
            return;
        }

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
        SendCommand($"G65{_unloadToolMacro}");
    }

    private void SetTlr()
    {
        if (!AtcEnabled || !TlrCommandEnabled) return;
        SendCommand($"G65{_tlrMacro}");
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
    /// <summary>
    /// Stops the spindle by whichever route the controller will actually accept.
    ///
    /// In a hold that is the real-time toggle, which grblHAL takes from any stream -
    /// so this still reaches the controller while a hardware MPG holds the g-code
    /// stream, which is exactly when someone standing at the PC wants it. Outside a
    /// hold there is no real-time equivalent and M05 has to be streamed.
    /// </summary>
    private void SpindleOff()
    {
        if (CurrentGrblState == GrblState.Hold)
            SendByteCommand(GrblHalConstants.SpindleStopToggle);
        else
            SendCommand(GrblHalConstants.SpindleOff);
    }
    private void SpindleCcw()
    {
        SendCommand($"{GrblHalConstants.SpindleCCw} S{SpindleSetRpm}");
    }
    private void SpindleCw()
    {
        SendCommand($"{GrblHalConstants.SpindleCw} S{SpindleSetRpm}");
    }
    private void SetSpindleSpeed()
    {
        if (string.IsNullOrEmpty(SpindleSetRpm.ToString())) return;
        SendCommand($"S{SpindleSetRpm}");
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
        UseHardwareRenderer = _config.Renderer == GHalSenderConfig.RendererType.Hardware;
        // Always rebuild — the setter only rebuilds when the value changes,
        // but on first init (or when toggling metric) we need to ensure
        // a render control exists even if the value didn't change.
        if (RenderControl == null)
            RebuildRenderControl();
        JogRateList = new ObservableCollection<double>(UseMetric ? _config.JogSpeedMetric : _config.JogSpeedImperial);
        JogStepList = new ObservableCollection<double>(UseMetric ? _config.JogDistanceMetric : _config.JogDistanceImperial);
        JogStep = JogStepList[^1];
        JogRate = JogRateList[^1];
    }
    private void Wcs(string command)
    {
        SendCommand(command);
    }
    /// <summary>
    /// Single funnel for operator-issued commands (panel buttons, MDI, gamepad, web UI).
    /// Configured G-code event rules are expanded here, so a rule fires no matter which
    /// of those raised the command.
    /// </summary>
    /// <summary>
    /// Sends a pendant jog without touching the UI thread.
    /// </summary>
    /// <remarks>
    /// Jogs arrive twenty times a second and only need the serial port, which
    /// the comm layer already serialises with its own lock. Routing them
    /// through the UI thread put every one behind rendering, DRO updates and
    /// console trimming, so a busy interface delayed the write, the pendant
    /// service coalesced the backlog, and single blocks of 30 to 50 mm went out
    /// where a stream of 7 mm blocks should have.
    ///
    /// The console echo stays optional and marshalled. It is a diagnostic, and
    /// at twenty lines a second it is also what drives the console to its cap
    /// within seconds - after which every status tick pays repeated O(n)
    /// removals with a change notification each.
    /// </remarks>
    public void SendPendantJog(string command, bool echo)
    {
        if (string.IsNullOrEmpty(command)) return;
        CommManager.SendCommand(command);
        if (echo)
            Dispatcher.UIThread.Post(() => ConsoleOutput.Add(command));
    }

    public void SendCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return;

        if (_eventInjector.IsEmpty)
        {
            ConsoleOutput.Add(command);
            CommManager.SendCommand(command);
            return;
        }

        foreach (var line in _eventInjector.ExpandBlock(command, GcodeEventScope.Manual))
        {
            ConsoleOutput.Add(line);
            CommManager.SendCommand(line);
        }
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
       // Unlock();
        AlarmActive = false;
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
        // After SaveConfig above, so the mode each output was left in is persisted, and
        // before the process goes away, so closing the sender cannot leave the dust
        // collector or the shop lights running.
        _gpioOutputService?.Stop();
        CommManager.ShutDown();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            (RuntimeInformation.OSArchitecture == Architecture.Arm ||
             RuntimeInformation.OSArchitecture == Architecture.Arm64) && _config.ShutDownOs)
        {
            TryLinuxShutdown();
        }
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
        MpgActive = svc.MpgActive;

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

            // Trim oldest lines instead of clearing: a full wipe ate multi-line
            // responses ($$, $ES) the moment they pushed the count over the cap,
            // leaving only the trailing "ok" visible in the console.
            while (ConsoleOutput.Count > MaxConsoleLines)
                ConsoleOutput.RemoveAt(0);
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
    private void _commManager_onSettingUpdated(object? sender, List<GrblHalSetting> e) =>
        RefreshMachineSettings();

    /// <summary>
    /// A single setting was written while connected. Same work as a full settings read —
    /// the values it feeds do not care which of the two they came from.
    /// </summary>
    private void _commManager_onMachineDataChanged(object? sender, MachineSettings e) =>
        RefreshMachineSettings();

    private void RefreshMachineSettings()
    {
        MachineSettings = CommManager.MachineData;
        // $341 arrives with the settings, and the touch-off control depends on it.
        UpdateButtonStates();

        MachineInMetric = CommManager.MachineData.ReportInMetric;
        if (UseMetric != MachineInMetric)
        {
            SetUpUiUnit();
        }
    }
    private void _commManager_onOptionsUpdated(object? sender, GrblHALOptions e)
    {
        // Always reconcile against the latest options — don't stop after the first event.
        // The one-shot guard caused the UI to freeze on whatever (possibly empty) state the
        // first event delivered, even when later events had complete data.
        foreach (var axis in e.AxisLabels.Where(axis => _axis.All(x => x.Name != axis.ToString())))
        {
            _axis.Add(new Axis
            {
                Name = axis.ToString(),
                Order = e.AxisLabels.IndexOf(axis),
                ZeroWcsCommand = ZeroAxis
            });
        }

        // Rebuild SignalList to match e.SignalLabels exactly: add missing, remove stale.
        // Preserves existing Signal instances (and their Triggered state) for IDs that stay.
        for (int i = SignalList.Count - 1; i >= 0; i--)
        {
            if (!e.SignalLabels.Contains(SignalList[i].Id))
                SignalList.RemoveAt(i);
        }
        foreach (var signal in e.SignalLabels)
        {
            if (SignalList.All(s => s.Id != signal))
                SignalList.Add(new Signal { Id = signal });
        }
    }
    private void JogNeg(string axis)
    {
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}-{JogStep.ToInvariantString()}F{JogRate.ToInvariantString()}";
        SendCommand(command);
    }
    private void JogPos(string axis)
    {
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}{JogStep.ToInvariantString()}F{JogRate.ToInvariantString()}";
        SendCommand(command);
    }
    public void JogContinuousNeg(string axis)
    {
        var distance = GetMachineDistance(axis);
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}-{distance}F{JogRate.ToInvariantString()}";
        SendCommand(command);
    }
    public void JogContinuousPos(string axis)
    {
        var distance = GetMachineDistance(axis);
        var command = $"$J=G91{UnitSystem}{axis.ToUpper()}{distance}F{JogRate.ToInvariantString()}";
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
        if (Connected) return;
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
        if (!Connected)
        {
            // A failed open used to be completely silent, which made a busy or
            // wrong port indistinguishable from a dead controller.
            ConsoleOutput.Add($"Connection failed: could not open {DescribeTarget()}");
        }

        CommManager.Adapter.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ICommsAdapter.IsConnected))
            {
                // IsConnected flips on the adapter's send/receive threads when
                // the link drops. Connected drives Avalonia bindings, which
                // must be touched from the UI thread — updating directly from
                // the comm thread throws there, and an unhandled exception on
                // a background thread kills the whole process.
                Dispatcher.UIThread.Post(() => Connected = CommManager.Adapter.IsConnected);
            }
        };

        // Only query when the link is actually up. GetSettings retries $I+ in an
        // unbounded loop, so running it against a port that failed to open left a
        // background task spinning forever — and another one on every retry.
        if (Connected)
            CommManager.GetSettings();
    }

    private string DescribeTarget() => _config.Connection switch
    {
        GHalSenderConfig.ConnectionType.Serial =>
            $"{_config.SerialSettings.PortName} at {_config.SerialSettings.BaudRate} baud",
        GHalSenderConfig.ConnectionType.Tcp =>
            $"{_config.TcpSettings.IpAddress}:{_config.TcpSettings.PortNumber}",
        _ => $"{_config.WebSocketSettings.IpAddress}:{_config.WebSocketSettings.PortNumber}"
    };
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

