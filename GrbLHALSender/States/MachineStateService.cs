using Avalonia.Threading;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gcode;
using GrbLHALSender.Settings;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GrbLHALSender.States;

/// <summary>
/// Centralizes machine state parsing, unit conversion, and distribution.
/// Subscribes to CommunicationManager at ~200 Hz on the data thread,
/// throttles to ~10 Hz on the UI thread via DispatcherTimer.
/// ViewModels inject this service and read typed, converted properties.
/// </summary>
public class MachineStateService : ReactiveObject, IDisposable
{
    private readonly CommunicationManager _commManager;
    private readonly ConfigManager _configManager;

    // Same throttling pattern as the previous MainViewModel implementation:
    // store latest state atomically on data thread, drain on UI timer.
    private volatile RealTImeState? _latestState;
    private DispatcherTimer? _uiUpdateTimer;

    // Unit conversion state — updated from config and machine settings
    private bool _useMetric = true;
    private bool _machineInMetric = true;

    // === Reactive Properties ===

    private bool _connected;
    public bool Connected
    {
        get => _connected;
        private set => this.RaiseAndSetIfChanged(ref _connected, value);
    }

    private GrblState _grblState = GrblState.Unknown;
    public GrblState GrblState
    {
        get => _grblState;
        private set => this.RaiseAndSetIfChanged(ref _grblState, value);
    }

    private string _grblStateString = "";
    public string GrblStateString
    {
        get => _grblStateString;
        private set => this.RaiseAndSetIfChanged(ref _grblStateString, value);
    }

    private string _subState = "";
    public string SubState
    {
        get => _subState;
        private set => this.RaiseAndSetIfChanged(ref _subState, value);
    }

    private bool _alarmActive;
    public bool AlarmActive
    {
        get => _alarmActive;
        private set => this.RaiseAndSetIfChanged(ref _alarmActive, value);
    }

    // Per-axis positions in user display units (metric/imperial converted)
    private double[] _machinePositions = [];
    public double[] MachinePositions
    {
        get => _machinePositions;
        private set => this.RaiseAndSetIfChanged(ref _machinePositions, value);
    }

    private double[] _workPositions = [];
    public double[] WorkPositions
    {
        get => _workPositions;
        private set => this.RaiseAndSetIfChanged(ref _workPositions, value);
    }

    // Spindle position in machine units (work coordinates: MPos - WCO).
    // Kept in machine units because G-code toolpath geometry is in machine coordinates.
    private Point3D? _spindlePosition;
    public Point3D? SpindlePosition
    {
        get => _spindlePosition;
        private set => this.RaiseAndSetIfChanged(ref _spindlePosition, value);
    }

    private Point3D? _workCoordinateOffset;
    public Point3D? WorkCoordinateOffset
    {
        get => _workCoordinateOffset;
        private set => this.RaiseAndSetIfChanged(ref _workCoordinateOffset, value);
    }

    // Feed & speed (parsed from strings to ints)
    private int _feedRate;
    public int FeedRate
    {
        get => _feedRate;
        private set => this.RaiseAndSetIfChanged(ref _feedRate, value);
    }

    private int _feedOverride;
    public int FeedOverride
    {
        get => _feedOverride;
        private set => this.RaiseAndSetIfChanged(ref _feedOverride, value);
    }

    private int _spindleRpm;
    public int SpindleRpm
    {
        get => _spindleRpm;
        private set => this.RaiseAndSetIfChanged(ref _spindleRpm, value);
    }

    private int _actualRpm;
    public int ActualRpm
    {
        get => _actualRpm;
        private set => this.RaiseAndSetIfChanged(ref _actualRpm, value);
    }

    public int RpmOverride
    {
        get => _rpmOverride;
        private set =>  this.RaiseAndSetIfChanged(ref _rpmOverride, value);
    }

    // Display strings
    private string _wcsDisplay = "";
    public string WcsDisplay
    {
        get => _wcsDisplay;
        private set => this.RaiseAndSetIfChanged(ref _wcsDisplay, value);
    }

    private string _toolDisplay = "";
    public string ToolDisplay
    {
        get => _toolDisplay;
        private set => this.RaiseAndSetIfChanged(ref _toolDisplay, value);
    }

    // Booleans
    private bool _homeState;
    public bool HomeState
    {
        get => _homeState;
        private set => this.RaiseAndSetIfChanged(ref _homeState, value);
    }

    private bool _tlr;
    public bool TLR
    {
        get => _tlr;
        private set => this.RaiseAndSetIfChanged(ref _tlr, value);
    }

    // Signal status chars — MainViewModel maps these to Signal UI objects
    private List<char> _signalStatus = [];
    public List<char> SignalStatus
    {
        get => _signalStatus;
        private set => this.RaiseAndSetIfChanged(ref _signalStatus, value);
    }

    // Raw state for consumers that need the full object (e.g., console RT display)
    private RealTImeState? _rawState;
    private int _rpmOverride;

    public RealTImeState? RawState
    {
        get => _rawState;
        private set => this.RaiseAndSetIfChanged(ref _rawState, value);
    }

    public int AxisCount { get; private set; }

    public MachineStateService(CommunicationManager commManager, ConfigManager configManager)
    {
        _commManager = commManager;
        _configManager = configManager;
        _configManager.OnConfigLoaded += _configManager_OnConfigLoaded;
        // Subscribe to raw state events (fires on data thread at ~200 Hz)
        _commManager.OnStateReceived += OnStateReceived;

        // Listen for setting updates to track machine's native unit system
        _commManager.onSettingUpdated += OnSettingUpdated;

        // Throttled UI update timer at 10 Hz — identical pattern to previous MainViewModel
        _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _uiUpdateTimer.Tick += UiUpdateTimerTick;
        _uiUpdateTimer.Start();
       
        
    }

    private void _configManager_OnConfigLoaded(object? sender, GHalSenderConfig e)
    {
        UpdateUseMetric(_configManager.GHalSenderConfig is { UseMetric: true }); 
        
        _configManager?.GHalSenderConfig?.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GHalSenderConfig.UseMetric))
            {
                UpdateUseMetric(_configManager.GHalSenderConfig.UseMetric);
            }
        };
    }

    /// <summary>
    /// Called by MainViewModel when the user toggles metric/imperial display.
    /// </summary>
    public void UpdateUseMetric(bool useMetric)
    {
        _useMetric = useMetric;
    }

    private void OnStateReceived(object? sender, RealTImeState e)
    {
        // Atomic store — the UI timer will pick up the latest value.
        _latestState = e;
    }

    private void OnSettingUpdated(object? sender, List<GrblHalSetting> e)
    {
        _machineInMetric = _commManager.MachineData?.ReportInMetric ?? true;
    }

    /// <summary>
    /// UI-thread timer callback. Drains the latest state, parses strings to typed values,
    /// applies unit conversion, and sets reactive properties (triggering PropertyChanged).
    /// </summary>
    private void UiUpdateTimerTick(object? sender, EventArgs e)
    {
        var state = Interlocked.Exchange(ref _latestState, null);
        if (state == null) return;

        Connected = true;
        RawState = state;

        // --- Parse positions ---
        int axisCount = state.MPos.Length;
        AxisCount = axisCount;
        Span<double> mpos = stackalloc double[axisCount];
        Span<double> wcoVals = stackalloc double[axisCount];
        bool hasWco = state.Wco.Length > 0;

        for (int i = 0; i < axisCount; i++)
        {
            if (!double.TryParse(state.MPos[i], out mpos[i])) mpos[i] = 0;
            if (hasWco && i < state.Wco.Length)
            {
                if (!double.TryParse(state.Wco[i], out wcoVals[i])) wcoVals[i] = 0;
            }
        }

        // Per-axis positions in user display units
        var machinePos = new double[axisCount];
        var workPos = new double[axisCount];

        for (int i = 0; i < axisCount; i++)
        {
            machinePos[i] = ConvertUnit(mpos[i]);
            if (hasWco)
            {
                workPos[i] = ConvertUnit(mpos[i] - wcoVals[i]);
            }
        }

        MachinePositions = machinePos;
        WorkPositions = workPos;

        // --- Spindle position for 3D visualizer (machine units, not display units) ---
        if (axisCount >= 3)
        {
            float wx = (float)mpos[0], wy = (float)mpos[1], wz = (float)mpos[2];
            if (hasWco && state.Wco.Length >= 3)
            {
                float wcoX = (float)wcoVals[0], wcoY = (float)wcoVals[1], wcoZ = (float)wcoVals[2];
                wx -= wcoX;
                wy -= wcoY;
                wz -= wcoZ;
                var newWco = new Point3D(wcoX, wcoY, wcoZ);
                if (_workCoordinateOffset == null || !_workCoordinateOffset.Value.Equals(newWco))
                    WorkCoordinateOffset = newWco;
            }
            var newSpindlePos = new Point3D(wx, wy, wz);
            if (_spindlePosition == null || !_spindlePosition.Value.Equals(newSpindlePos))
                SpindlePosition = newSpindlePos;
        }

        // --- Map GrblHalState string to typed enum ---
        GrblStateString = state.GrblHalState ?? "";
        GrblState = ParseGrblState(state.GrblHalState);
        SubState = state.SubState ?? "";
        AlarmActive = GrblState == GrblState.Alarm;

        // --- Feed & speed ---
        if (int.TryParse(state.FeedRate, out var feedRate)) FeedRate =(int) ConvertUnit(feedRate);
        if (int.TryParse(state.FeedOverRide, out var fo)) FeedOverride = fo;
        if (int.TryParse(state.ProgramRPM, out var ps)) SpindleRpm = ps;
        if (int.TryParse(state.ActualRpm, out var rpm)) ActualRpm = rpm;
        if (int.TryParse(state.RpmOverRide, out var rO)) RpmOverride = rO;

        // --- Display values ---
        WcsDisplay = state.WCS ?? "";
        ToolDisplay = state.Tool ?? "";
        HomeState = state.Home;
        TLR = state.TLR;

        // --- Signals ---
        SignalStatus = state.SignalStatus;
    }

    

    /// <summary>
    /// Centralized metric/imperial conversion. Converts a value from machine native units
    /// to the user's selected display units.
    /// </summary>
    private double ConvertUnit(double value)
    {
        return _useMetric switch
        {
            false when _machineInMetric => value / 25.4,
            true when !_machineInMetric => value * 25.4,
            _ => value
        };
    }

    private static GrblState ParseGrblState(string? state)
    {
        return state switch
        {
            "Idle" => GrblState.Idle,
            "Run" => GrblState.Run,
            "Hold" => GrblState.Hold,
            "Jog" => GrblState.Jog,
            "Alarm" => GrblState.Alarm,
            "Door" => GrblState.Door,
            "Check" => GrblState.Check,
            "Home" => GrblState.Home,
            "Sleep" => GrblState.Sleep,
            "Tool" => GrblState.Tool,
            _ => GrblState.Unknown
        };
    }

    public void Dispose()
    {
        _uiUpdateTimer?.Stop();
        _uiUpdateTimer = null;
        _commManager.OnStateReceived -= OnStateReceived;
        _commManager.onSettingUpdated -= OnSettingUpdated;
    }
}
