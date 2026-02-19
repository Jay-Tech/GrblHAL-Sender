using Avalonia.Threading;
using GrbLHALSender.Configuration;
using GrbLHALSender.Utility;
using GrbLHALSender.ViewModels;
using Hexa.NET.SDL3;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace GrbLHALSender.Gamepad;

public class GamepadService : IDisposable
{
    private readonly ConfigManager _configManager;
    private GamepadConfig _config;

    // SDL state
    private SDLGamepadPtr _gamepad;
    private int _gamepadId;
    private bool _sdlInitialized;
    private bool _gamepadConnected;

    // Threading
    private Thread? _pollThread;
    private volatile bool _running;
    private readonly object _lock = new();

    // Cached parsed mappings
    private Dictionary<SDLGamepadButton, GamepadAction> _buttonActionMap = new();
    private Dictionary<string, string> _axisCncMap = new();
    private Dictionary<string, bool> _axisInvertMap = new();
    private Dictionary<SDLGamepadAxis, GamepadAction> _triggerActionMap = new();
    private readonly Dictionary<SDLGamepadAxis, bool> _triggerActive = new();

    // Proportional jog state
    private readonly Dictionary<string, double> _axisDeflections = new();
    private readonly Dictionary<string, double> _axisDirections = new();
    private readonly Stopwatch _jogTimer = new();
    private bool _wasJogging;

    // ViewModel reference (set after construction to avoid circular DI)
    private MainViewModel? _mainViewModel;

    // Events
    public event EventHandler<bool>? GamepadConnectionChanged;
    public event EventHandler<string>? GamepadStatusMessage;

    public bool IsGamepadConnected => _gamepadConnected;

    public GamepadService(ConfigManager configManager)
    {
        _configManager = configManager;
        _config = new GamepadConfig();
    }

    public void SetViewModel(MainViewModel vm)
    {
        _mainViewModel = vm;
    }

    public void Initialize(GamepadConfig config)
    {
        _config = config;
        if (!_config.Enabled) return;

        ParseMappings();
        InitializeSdl();

        if (_sdlInitialized)
        {
            StartPolling();
        }
    }

    /// <summary>
    /// Xbox/common aliases → SDL3 positional button names.
    /// Allows users to write "A" instead of "South" in config JSON.
    /// </summary>
    private static readonly Dictionary<string, string> ButtonAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "A", "South" },
        { "B", "East" },
        { "X", "West" },
        { "Y", "North" },
        { "LB", "LeftShoulder" },
        { "RB", "RightShoulder" },
        { "L3", "LeftStick" },
        { "R3", "RightStick" },
        { "Select", "Back" },
    };

    private void ParseMappings()
    {
        // Parse button mappings with Xbox alias support
        _buttonActionMap.Clear();
        foreach (var kvp in _config.ButtonMapping)
        {
            // Resolve alias first (e.g. "A" → "South"), then parse enum
            string buttonName = ButtonAliases.TryGetValue(kvp.Key, out var resolved)
                ? resolved
                : kvp.Key;

            if (Enum.TryParse<SDLGamepadButton>(buttonName, true, out var button) &&
                Enum.TryParse<GamepadAction>(kvp.Value, true, out var action))
            {
                _buttonActionMap[button] = action;
            }
        }

        // Parse trigger-to-action mappings
        _triggerActionMap.Clear();
        _triggerActive.Clear();
        foreach (var kvp in _config.TriggerMapping)
        {
            SDLGamepadAxis? triggerAxis = kvp.Key switch
            {
                "LeftTrigger" or "LT" => SDLGamepadAxis.LeftTrigger,
                "RightTrigger" or "RT" => SDLGamepadAxis.RightTrigger,
                _ => null
            };

            if (triggerAxis.HasValue && Enum.TryParse<GamepadAction>(kvp.Value, true, out var trigAction))
            {
                _triggerActionMap[triggerAxis.Value] = trigAction;
                _triggerActive[triggerAxis.Value] = false;
            }
        }

        _axisCncMap = new Dictionary<string, string>(_config.AxisMapping);
        _axisInvertMap = new Dictionary<string, bool>(_config.AxisInverted);
    }

    private void InitializeSdl()
    {
        try
        {
            if (!SDL.Init((uint)SDLInitFlags.Gamepad))
            {
                GamepadStatusMessage?.Invoke(this, "SDL3 init failed");
                return;
            }

            _sdlInitialized = true;
            GamepadStatusMessage?.Invoke(this, "SDL3 initialized for gamepad input");
        }
        catch (Exception ex)
        {
            GamepadStatusMessage?.Invoke(this, $"SDL3 init exception: {ex.Message}");
        }
    }

    private void StartPolling()
    {
        if (_running) return;
        _running = true;
        _jogTimer.Start();

        _pollThread = new Thread(PollLoop)
        {
            Name = "GamepadPollThread",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        _pollThread.Start();
    }

    private void PollLoop()
    {
        while (_running)
        {
            try
            {
                SDLEvent evt = default;
                while (SDL.PollEvent(ref evt))
                {
                    ProcessSdlEvent(ref evt);
                }

                if (_jogTimer.ElapsedMilliseconds >= _config.JogCommandIntervalMs)
                {
                    _jogTimer.Restart();
                    ProcessProportionalJog();
                }

                Thread.Sleep(_config.PollIntervalMs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Gamepad poll error: {ex.Message}");
            }
        }
    }

    private void ProcessSdlEvent(ref SDLEvent evt)
    {
        switch ((SDLEventType)evt.Type)
        {
            case SDLEventType.GamepadAdded:
                OnGamepadAdded(evt.Gdevice.Which);
                break;

            case SDLEventType.GamepadRemoved:
                OnGamepadRemoved(evt.Gdevice.Which);
                break;

            case SDLEventType.GamepadButtonDown:
                OnButtonDown((SDLGamepadButton)evt.Gbutton.Button);
                break;

            case SDLEventType.GamepadButtonUp:
                OnButtonUp((SDLGamepadButton)evt.Gbutton.Button);
                break;

            case SDLEventType.GamepadAxisMotion:
                OnAxisMotion((SDLGamepadAxis)evt.Gaxis.Axis, evt.Gaxis.Value);
                break;
        }
    }

    private void OnGamepadAdded(int which)
    {
        if (!_gamepad.IsNull) return;

        _gamepad = SDL.OpenGamepad(which);
        if (!_gamepad.IsNull)
        {
            _gamepadId = which;
            _gamepadConnected = true;

            string name;
            unsafe
            {
                var namePtr = SDL.GetGamepadName(_gamepad);
                name = namePtr != null
                    ? System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)namePtr) ?? "Unknown"
                    : "Unknown";
            }

            Dispatcher.UIThread.Post(() =>
            {
                GamepadConnectionChanged?.Invoke(this, true);
                GamepadStatusMessage?.Invoke(this, $"Gamepad connected: {name}");
            });
        }
    }

    private void OnGamepadRemoved(int which)
    {
        if (which != _gamepadId) return;

        SDL.CloseGamepad(_gamepad);
        _gamepad = default;
        _gamepadConnected = false;

        lock (_lock)
        {
            _axisDeflections.Clear();
            _axisDirections.Clear();
        }

        if (_wasJogging)
        {
            DispatchJogCancel();
            _wasJogging = false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            GamepadConnectionChanged?.Invoke(this, false);
            GamepadStatusMessage?.Invoke(this, "Gamepad disconnected");
        });
    }

    private void OnButtonDown(SDLGamepadButton button)
    {
        if (!_buttonActionMap.TryGetValue(button, out var action)) return;
        if (action == GamepadAction.None) return;

        Dispatcher.UIThread.Post(() => ExecuteAction(action));
    }

    private void OnButtonUp(SDLGamepadButton button)
    {
        // Reserved for future press-and-hold behavior
    }

    private void OnAxisMotion(SDLGamepadAxis axis, short rawValue)
    {
        // Handle triggers as button-like actions (fire once when crossing threshold)
        if (axis == SDLGamepadAxis.LeftTrigger || axis == SDLGamepadAxis.RightTrigger)
        {
            ProcessTriggerAxis(axis, rawValue);
            return;
        }

        string? axisName = axis switch
        {
            SDLGamepadAxis.Leftx => "LeftX",
            SDLGamepadAxis.Lefty => "LeftY",
            SDLGamepadAxis.Rightx => "RightX",
            SDLGamepadAxis.Righty => "RightY",
            _ => null
        };

        if (axisName == null) return;
        if (!_axisCncMap.ContainsKey(axisName)) return;

        double absValue = Math.Abs((double)rawValue);
        double direction = rawValue >= 0 ? 1.0 : -1.0;

        if (_axisInvertMap.TryGetValue(axisName, out var invert) && invert)
        {
            direction = -direction;
        }

        if (absValue < _config.DeadZone)
        {
            lock (_lock)
            {
                _axisDeflections[axisName] = 0.0;
                _axisDirections[axisName] = 0.0;
            }
            return;
        }

        double maxRange = 32767.0 - _config.DeadZone;
        double normalized = (absValue - _config.DeadZone) / maxRange;
        normalized = Math.Clamp(normalized, 0.0, 1.0);
        normalized = Math.Pow(normalized, _config.ResponseCurveExponent);

        lock (_lock)
        {
            _axisDeflections[axisName] = normalized;
            _axisDirections[axisName] = direction;
        }
    }

    /// <summary>
    /// Treats a trigger axis (0-32767) as a digital button.
    /// Fires the mapped action once when crossing above TriggerThreshold,
    /// resets when falling below (so you can pull and release to repeat).
    /// </summary>
    private void ProcessTriggerAxis(SDLGamepadAxis axis, short rawValue)
    {
        if (!_triggerActionMap.TryGetValue(axis, out var action)) return;

        bool wasActive = _triggerActive.GetValueOrDefault(axis, false);
        bool isActive = rawValue >= _config.TriggerThreshold;

        if (isActive && !wasActive)
        {
            // Rising edge — trigger just crossed threshold, fire action
            _triggerActive[axis] = true;
            Dispatcher.UIThread.Post(() => ExecuteAction(action));
        }
        else if (!isActive && wasActive)
        {
            // Falling edge — trigger released below threshold
            _triggerActive[axis] = false;
        }
    }

    /// <summary>
    /// Sends proportional jog commands based on current stick deflection.
    /// Combines all active axes into a single multi-axis jog command for smooth diagonal motion.
    /// </summary>
    private void ProcessProportionalJog()
    {
        if (_mainViewModel == null) return;

        Dictionary<string, double> deflections;
        Dictionary<string, double> directions;
        lock (_lock)
        {
            deflections = new Dictionary<string, double>(_axisDeflections);
            directions = new Dictionary<string, double>(_axisDirections);
        }

        bool anyActive = false;
        double maxDeflection = 0;
        var jogParts = new Dictionary<string, double>();

        foreach (var kvp in _axisCncMap)
        {
            string sdlAxis = kvp.Key;
            string cncAxis = kvp.Value;

            if (!deflections.TryGetValue(sdlAxis, out double defl)) continue;
            if (!directions.TryGetValue(sdlAxis, out double dir)) continue;
            if (defl < 0.001) continue;

            anyActive = true;
            if (defl > maxDeflection) maxDeflection = defl;

            double baseIncrement = _mainViewModel.UnitSystem == "G21"
                ? _config.JogIncrementMm
                : _config.JogIncrementInch;

            double distance = baseIncrement * defl * dir;
            jogParts[cncAxis] = distance;
        }

        if (anyActive)
        {
            double jogRate = _mainViewModel.JogRate * maxDeflection;
            jogRate = Math.Max(jogRate, 1.0);

            string unitSystem = _mainViewModel.UnitSystem;
            var axisParts = string.Join("",
                jogParts.Select(kvp =>
                    $"{kvp.Key}{kvp.Value.ToString("F3", CultureInfo.InvariantCulture)}"));

            string command = $"$J=G91{unitSystem}{axisParts}F{jogRate.ToString("F0", CultureInfo.InvariantCulture)}";

            Dispatcher.UIThread.Post(() =>
            {
                if (_mainViewModel.Connected && !_mainViewModel.AlarmActive)
                {
                    _mainViewModel.SendCommand(command);
                }
            });

            _wasJogging = true;
        }
        else if (_wasJogging)
        {
            DispatchJogCancel();
            _wasJogging = false;
        }
    }

    private void ExecuteAction(GamepadAction action)
    {
        if (_mainViewModel == null) return;
        if (!_mainViewModel.Connected) return;

        switch (action)
        {
            case GamepadAction.JogStepUp:
                CycleJogStep(1);
                break;
            case GamepadAction.JogStepDown:
                CycleJogStep(-1);
                break;
            case GamepadAction.JogRateUp:
                CycleJogRate(1);
                break;
            case GamepadAction.JogRateDown:
                CycleJogRate(-1);
                break;

            case GamepadAction.FeedHold:
                _mainViewModel.SendByteCommand(GrblHalConstants.FeedHold);
                break;
            case GamepadAction.CycleStart:
                _mainViewModel.SendByteCommand(GrblHalConstants.CycleStart);
                break;
            case GamepadAction.JogCancel:
                _mainViewModel.JogCancel();
                break;
            case GamepadAction.SafetyDoor:
                _mainViewModel.SendByteCommand(GrblHalConstants.SafetyDoor);
                break;

            case GamepadAction.Home:
                _mainViewModel.SendCommand("$H");
                break;
            case GamepadAction.ClearAlarm:
                _mainViewModel.SendCommand("$X");
                break;
            case GamepadAction.Unlock:
                _mainViewModel.SendByteCommand(GrblHalConstants.GrblReset);
                break;
            case GamepadAction.EStop:
                _mainViewModel.SendByteCommand(GrblHalConstants.GrblReset);
                break;

            case GamepadAction.FeedOverridePlus:
                _mainViewModel.SendByteCommand(GrblHalConstants.FeedOrCoarsePlus);
                break;
            case GamepadAction.FeedOverrideMinus:
                _mainViewModel.SendByteCommand(GrblHalConstants.FeedOrCoarseMinus);
                break;
            case GamepadAction.FeedOverrideReset:
                _mainViewModel.SendByteCommand(GrblHalConstants.FeedOrReset);
                break;

            case GamepadAction.SpindleOverridePlus:
                _mainViewModel.SendByteCommand(GrblHalConstants.SpindleFinePlus);
                break;
            case GamepadAction.SpindleOverrideMinus:
                _mainViewModel.SendByteCommand(GrblHalConstants.SpindleFineMinus);
                break;
            case GamepadAction.SpindleOverrideReset:
                _mainViewModel.SendByteCommand(GrblHalConstants.SpindleReset);
                break;

            case GamepadAction.JogXPos:
                _mainViewModel.SendCommand(
                    $"$J=G91{_mainViewModel.UnitSystem}X{_mainViewModel.JogStep}F{_mainViewModel.JogRate}");
                break;
            case GamepadAction.JogXNeg:
                _mainViewModel.SendCommand(
                    $"$J=G91{_mainViewModel.UnitSystem}X-{_mainViewModel.JogStep}F{_mainViewModel.JogRate}");
                break;
            case GamepadAction.JogYPos:
                _mainViewModel.SendCommand(
                    $"$J=G91{_mainViewModel.UnitSystem}Y{_mainViewModel.JogStep}F{_mainViewModel.JogRate}");
                break;
            case GamepadAction.JogYNeg:
                _mainViewModel.SendCommand(
                    $"$J=G91{_mainViewModel.UnitSystem}Y-{_mainViewModel.JogStep}F{_mainViewModel.JogRate}");
                break;
            case GamepadAction.JogZPos:
                _mainViewModel.SendCommand(
                    $"$J=G91{_mainViewModel.UnitSystem}Z{_mainViewModel.JogStep}F{_mainViewModel.JogRate}");
                break;
            case GamepadAction.JogZNeg:
                _mainViewModel.SendCommand(
                    $"$J=G91{_mainViewModel.UnitSystem}Z-{_mainViewModel.JogStep}F{_mainViewModel.JogRate}");
                break;
        }
    }

    private void CycleJogStep(int direction)
    {
        if (_mainViewModel?.JogStepList == null || _mainViewModel.JogStepList.Count == 0) return;

        var list = _mainViewModel.JogStepList;
        int currentIndex = list.IndexOf(_mainViewModel.JogStep);
        if (currentIndex < 0) currentIndex = 0;

        int newIndex = Math.Clamp(currentIndex + direction, 0, list.Count - 1);
        _mainViewModel.JogStep = list[newIndex];
    }

    private void CycleJogRate(int direction)
    {
        if (_mainViewModel?.JogRateList == null || _mainViewModel.JogRateList.Count == 0) return;

        var list = _mainViewModel.JogRateList;
        int currentIndex = list.IndexOf(_mainViewModel.JogRate);
        if (currentIndex < 0) currentIndex = 0;

        int newIndex = Math.Clamp(currentIndex + direction, 0, list.Count - 1);
        _mainViewModel.JogRate = list[newIndex];
    }

    private void DispatchJogCancel()
    {
        Dispatcher.UIThread.Post(() => _mainViewModel?.JogCancel());
    }

    public void ReloadConfig(GamepadConfig config)
    {
        _config = config;
        ParseMappings();
    }

    public void Stop()
    {
        _running = false;
        _pollThread?.Join(1000);
        _pollThread = null;

        if (!_gamepad.IsNull)
        {
            SDL.CloseGamepad(_gamepad);
            _gamepad = default;
        }

        if (_sdlInitialized)
        {
            SDL.Quit();
            _sdlInitialized = false;
        }

        _gamepadConnected = false;
    }

    public void Dispose()
    {
        Stop();
    }
}
