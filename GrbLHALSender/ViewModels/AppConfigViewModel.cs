using CommunityToolkit.Mvvm.ComponentModel;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gamepad;
using GrbLHALSender.Utility;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels
{
    public class AppConfigViewModel : ViewModelBase, IDialogCloseable
    {
        public AuxOutputViewModel AuxOutputViewModel { get; }
        private readonly ConfigManager _configManager;
        private readonly CommunicationManager _commManager;
        private GHalSenderConfig _appConfig;
        private bool _enableCutLines;
        private bool _useMetric;
        private double[] _metricJogDistance = [];
        private double[] _metricJogSpeed = [];
        private double[] _imperialJogDistance = [];
        private double[] _imperialJogSpeed = [];
        private bool _isAtcEnabled;
        private int _atcToolCount;
        private string _tlrMacro;
        private string _unloadMacro;
        private bool _isGamePadEnabled;
        private int _gamePadDeadZone = 8000;
        private int _gamePadPollIntervalMs = 20;
        private int _gamePadJogCommandIntervalMs = 75;
        private double _gamePadJogIncrementMm = 2.0;
        private double _gamePadJogIncrementInch = 0.08;
        private double _gamePadResponseCurveExponent = 1.5;
        private int _gamePadTriggerThreshold = 16000;
        private bool _isWebServerEnabled;
        private int _webServerPort;
        private bool _useAntiAlias = true;
        private string _spindleImagePath = "spindle.png";
        private double _pollRate;
        private bool _borderlessWindow;


        public bool EnableCutLines
        {
            get => _enableCutLines;
            set => this.RaiseAndSetIfChanged(ref _enableCutLines, value);
        }

        public bool UseMetric
        {
            get => _useMetric;
            set => this.RaiseAndSetIfChanged(ref _useMetric, value);
        }

        public double[] MetricJogDistance
        {
            get => _metricJogDistance;
            set => this.RaiseAndSetIfChanged(ref _metricJogDistance, value);
        }

        public double[] MetricJogSpeed
        {
            get => _metricJogSpeed;
            set => this.RaiseAndSetIfChanged(ref _metricJogSpeed, value);
        }

        public double[] ImperialJogDistance
        {
            get => _imperialJogDistance;
            set => this.RaiseAndSetIfChanged(ref _imperialJogDistance, value);
        }

        public double[] ImperialJogSpeed
        {
            get => _imperialJogSpeed;
            set => this.RaiseAndSetIfChanged(ref _imperialJogSpeed, value);
        }

        public bool IsAtcEnabled
        {
            get => _isAtcEnabled;
            set => this.RaiseAndSetIfChanged(ref _isAtcEnabled, value);
        }

        public int AtcToolCount
        {
            get => _atcToolCount;
            set => this.RaiseAndSetIfChanged(ref _atcToolCount, value);
        }
        public string? TlrMacro
        {
            get => _tlrMacro;
            set => this.RaiseAndSetIfChanged(ref _tlrMacro, value);
        }

        public string? UnloadMacro
        {
            get => _unloadMacro;
            set => this.RaiseAndSetIfChanged(ref _unloadMacro, value);
        }

        public bool IsGamePadEnabled
        {
            get => _isGamePadEnabled;
            set => this.RaiseAndSetIfChanged(ref _isGamePadEnabled, value);
        }

        public int GamePadDeadZone
        {
            get => _gamePadDeadZone;
            set => this.RaiseAndSetIfChanged(ref _gamePadDeadZone, value);
        }

        public int GamePadPollIntervalMs
        {
            get => _gamePadPollIntervalMs;
            set => this.RaiseAndSetIfChanged(ref _gamePadPollIntervalMs, value);
        }

        public int GamePadJogCommandIntervalMs
        {
            get => _gamePadJogCommandIntervalMs;
            set => this.RaiseAndSetIfChanged(ref _gamePadJogCommandIntervalMs, value);
        }

        public double GamePadJogIncrementMm
        {
            get => _gamePadJogIncrementMm;
            set => this.RaiseAndSetIfChanged(ref _gamePadJogIncrementMm, value);
        }

        public double GamePadJogIncrementInch
        {
            get => _gamePadJogIncrementInch;
            set => this.RaiseAndSetIfChanged(ref _gamePadJogIncrementInch, value);
        }

        public double GamePadResponseCurveExponent
        {
            get => _gamePadResponseCurveExponent;
            set => this.RaiseAndSetIfChanged(ref _gamePadResponseCurveExponent, value);
        }

        public int GamePadTriggerThreshold
        {
            get => _gamePadTriggerThreshold;
            set => this.RaiseAndSetIfChanged(ref _gamePadTriggerThreshold, value);
        }

        public string[] GamepadActionNames { get; } =
            Enum.GetNames(typeof(GamepadAction));

        public string[] CncAxisOptions { get; } =
            ["None", "X", "Y", "Z", "A", "B", "C"];

        public ObservableCollection<ButtonMappingItem> GamePadButtonMappings { get; } = [];

        public ObservableCollection<AxisMappingItem> GamePadAxisMappings { get; } = [];

        public ObservableCollection<TriggerMappingItem> GamePadTriggerMappings { get; } = [];

        // Aux Outputs
        public ObservableCollection<AuxOutputItem> AuxOutputItems { get; } = [];

        /// <summary>
        /// Available presets: static Mist/Flood + dynamically discovered aux pins from $PINS.
        /// </summary>
        public ObservableCollection<AuxOutputConfig> AuxOutputPresets { get; } =
            new(AuxOutputConfig.Presets);

        private AuxOutputConfig? _selectedAuxPreset;
        public AuxOutputConfig? SelectedAuxPreset
        {
            get => _selectedAuxPreset;
            set => this.RaiseAndSetIfChanged(ref _selectedAuxPreset, value);
        }

        public ICommand AddPresetCommand { get; }
        public ICommand RemoveAuxOutputCommand { get; }

        public bool IsWebServerEnabled
        {
            get => _isWebServerEnabled;
            set => this.RaiseAndSetIfChanged(ref _isWebServerEnabled, value);
        }

        public int WebServerPort
        {
            get => _webServerPort;
            set => this.RaiseAndSetIfChanged(ref _webServerPort, value);
        }

        public bool UseAntiAlias
        {
            get => _useAntiAlias;
            set => this.RaiseAndSetIfChanged(ref _useAntiAlias, value);
        }

        public string SpindleImagePath
        {
            get => _spindleImagePath;
            set => this.RaiseAndSetIfChanged(ref _spindleImagePath, value);
        }

        public double PollRate
        {
            get => _pollRate;   
            set => this.RaiseAndSetIfChanged(ref _pollRate, value);
        }

        public bool BorderlessWindow
        {
            get => _borderlessWindow;
            set => this.RaiseAndSetIfChanged(ref _borderlessWindow, value);
        }

        public Action? CloseAction { get; set; }

        public ICommand SaveConfigCommand { get; }

        public ICommand CloseCommand { get; }

        public AppConfigViewModel(ConfigManager configManager, CommunicationManager commManager, AuxOutputViewModel  auxOutputViewModel)
        {
            AuxOutputViewModel = auxOutputViewModel;
            _configManager = configManager;
            _commManager = commManager;
            SaveConfigCommand = ReactiveCommand.Create(SaveConfig);
            CloseCommand = ReactiveCommand.Create(() => CloseAction?.Invoke());
            AddPresetCommand = ReactiveCommand.Create(AddSelectedPreset);
            RemoveAuxOutputCommand = ReactiveCommand.Create<AuxOutputItem>(RemoveAuxOutput);
            _configManager.OnConfigLoaded += _configManager_OnConfigLoaded;
            _commManager.OnAuxPinsDiscovered += OnAuxPinsDiscovered;
        }

        private void _configManager_OnConfigLoaded(object? sender, GHalSenderConfig e)
        {
            _appConfig = e;
            EnableCutLines = _appConfig.ShowToolpathProgress;
            UseMetric = _appConfig.UseMetric;
            MetricJogDistance = _appConfig.JogDistanceMetric;
            MetricJogSpeed = _appConfig.JogSpeedMetric;
            ImperialJogDistance = _appConfig.JogDistanceImperial;
            ImperialJogSpeed = _appConfig.JogSpeedImperial;
            IsAtcEnabled = _appConfig.AtcConfig.EnableAtc;
            AtcToolCount = _appConfig.ToolList.Tools.Count;
            TlrMacro = _appConfig.AtcConfig.TlrMacroName;
            UnloadMacro = _appConfig.AtcConfig.UnloadToolMacroName;
            IsGamePadEnabled = _appConfig.GamepadConfig.Enabled;
            GamePadDeadZone = _appConfig.GamepadConfig.DeadZone;
            GamePadPollIntervalMs = _appConfig.GamepadConfig.PollIntervalMs;
            GamePadJogCommandIntervalMs = _appConfig.GamepadConfig.JogCommandIntervalMs;
            GamePadJogIncrementMm = _appConfig.GamepadConfig.JogIncrementMm;
            GamePadJogIncrementInch = _appConfig.GamepadConfig.JogIncrementInch;
            GamePadResponseCurveExponent = _appConfig.GamepadConfig.ResponseCurveExponent;
            GamePadTriggerThreshold = _appConfig.GamepadConfig.TriggerThreshold;
            LoadGamepadMappings(_appConfig.GamepadConfig);
            IsWebServerEnabled = _appConfig.WebServerConfig.Enabled;
            WebServerPort = _appConfig.WebServerConfig.Port;
            LoadAuxOutputs(_appConfig.AuxOutputs);
            UseAntiAlias = _appConfig.UseAntiAlias;
            SpindleImagePath = _appConfig.SpindleImagePath;
            PollRate = _appConfig.PollRate;
            BorderlessWindow = _appConfig.Borderless;
        }

        public void SaveConfig()
        {
            _appConfig.ShowToolpathProgress = EnableCutLines;
            _appConfig.UseMetric = UseMetric;
            _appConfig.JogDistanceMetric = MetricJogDistance;
            _appConfig.JogSpeedMetric = MetricJogSpeed;
            _appConfig.JogDistanceImperial = ImperialJogDistance;
            _appConfig.JogSpeedImperial = ImperialJogSpeed;
            _appConfig.AtcConfig.EnableAtc = IsAtcEnabled;
            _appConfig.ToolList.Tools = Enumerable.Range(1, AtcToolCount).ToList();
            _appConfig.AtcConfig.TlrMacroName = TlrMacro ?? "";
            _appConfig.AtcConfig.UnloadToolMacroName = UnloadMacro ?? "";
            _appConfig.GamepadConfig.Enabled = IsGamePadEnabled;
            _appConfig.GamepadConfig.DeadZone = GamePadDeadZone;
            _appConfig.GamepadConfig.PollIntervalMs = GamePadPollIntervalMs;
            _appConfig.GamepadConfig.JogCommandIntervalMs = GamePadJogCommandIntervalMs;
            _appConfig.GamepadConfig.JogIncrementMm = GamePadJogIncrementMm;
            _appConfig.GamepadConfig.JogIncrementInch = GamePadJogIncrementInch;
            _appConfig.GamepadConfig.ResponseCurveExponent = GamePadResponseCurveExponent;
            _appConfig.GamepadConfig.TriggerThreshold = GamePadTriggerThreshold;
            SaveGamepadMappings(_appConfig.GamepadConfig);
            _appConfig.WebServerConfig.Enabled = IsWebServerEnabled;
            _appConfig.WebServerConfig.Port = WebServerPort;
            SaveAuxOutputs();
            _appConfig.UseAntiAlias = UseAntiAlias;
            _appConfig.SpindleImagePath = SpindleImagePath;
            _appConfig.PollRate = PollRate;
            _appConfig.Borderless = BorderlessWindow;
            _configManager.SaveConfig();
        }
        private static readonly (string Name, string Display)[] AllButtons =
        [
            ("South", "South (A)"),
            ("East", "East (B)"),
            ("West", "West (X)"),
            ("North", "North (Y)"),
            ("LeftShoulder", "LB"),
            ("RightShoulder", "RB"),
            ("DpadUp", "D-Pad Up"),
            ("DpadDown", "D-Pad Down"),
            ("DpadLeft", "D-Pad Left"),
            ("DpadRight", "D-Pad Right"),
            ("LeftStick", "Left Stick (L3)"),
            ("RightStick", "Right Stick (R3)"),
            ("Start", "Start"),
            ("Back", "Back (Select)"),
        ];

        private static readonly (string Name, string Display)[] AllAxes =
        [
            ("LeftX", "Left Stick X"),
            ("LeftY", "Left Stick Y"),
            ("RightX", "Right Stick X"),
            ("RightY", "Right Stick Y"),
        ];

        private static readonly (string Name, string Display)[] AllTriggers =
        [
            ("LeftTrigger", "Left Trigger (LT)"),
            ("RightTrigger", "Right Trigger (RT)"),
        ];

        private void LoadGamepadMappings(GamepadConfig cfg)
        {
            GamePadButtonMappings.Clear();
            foreach (var (name, display) in AllButtons)
            {
                cfg.ButtonMapping.TryGetValue(name, out var action);
                GamePadButtonMappings.Add(new ButtonMappingItem
                {
                    ButtonName = name,
                    DisplayName = display,
                    SelectedAction = action ?? "None",
                });
            }

            GamePadAxisMappings.Clear();
            foreach (var (name, display) in AllAxes)
            {
                cfg.AxisMapping.TryGetValue(name, out var axis);
                cfg.AxisInverted.TryGetValue(name, out var inverted);
                GamePadAxisMappings.Add(new AxisMappingItem
                {
                    AxisName = name,
                    DisplayName = display,
                    SelectedCncAxis = axis ?? "None",
                    IsInverted = inverted,
                });
            }

            GamePadTriggerMappings.Clear();
            foreach (var (name, display) in AllTriggers)
            {
                cfg.TriggerMapping.TryGetValue(name, out var action);
                GamePadTriggerMappings.Add(new TriggerMappingItem
                {
                    TriggerName = name,
                    DisplayName = display,
                    SelectedAction = action ?? "None",
                });
            }
        }

        private void SaveGamepadMappings(GamepadConfig cfg)
        {
            cfg.ButtonMapping = new Dictionary<string, string>();
            foreach (var item in GamePadButtonMappings)
            {
                if (item.SelectedAction is not (null or "None"))
                    cfg.ButtonMapping[item.ButtonName] = item.SelectedAction;
            }

            cfg.AxisMapping = new Dictionary<string, string>();
            cfg.AxisInverted = new Dictionary<string, bool>();
            foreach (var item in GamePadAxisMappings)
            {
                if (item.SelectedCncAxis is not (null or "None"))
                {
                    cfg.AxisMapping[item.AxisName] = item.SelectedCncAxis;
                    cfg.AxisInverted[item.AxisName] = item.IsInverted;
                }
            }

            cfg.TriggerMapping = new Dictionary<string, string>();
            foreach (var item in GamePadTriggerMappings)
            {
                if (item.SelectedAction is not (null or "None"))
                    cfg.TriggerMapping[item.TriggerName] = item.SelectedAction;
            }
        }

        private void LoadAuxOutputs(List<AuxOutputConfig> configs)
        {
            AuxOutputItems.Clear();
            foreach (var cfg in configs)
            {
                AuxOutputItems.Add(new AuxOutputItem
                {
                    Name = cfg.Name,
                    OnCommand = cfg.OnCommand,
                    OffCommand = cfg.OffCommand,
                    StateKey = cfg.StateKey
                });
            }
        }

        private void SaveAuxOutputs()
        {
            _appConfig.AuxOutputs = AuxOutputItems.Select(item => new AuxOutputConfig
            {
                Name = item.Name,
                OnCommand = item.OnCommand,
                OffCommand = item.OffCommand,
                StateKey = item.StateKey
            }).ToList();
        }

        private void AddSelectedPreset()
        {
            if (SelectedAuxPreset == null) return;
            // Don't add duplicates (check by StateKey since Name is editable)
            if (AuxOutputItems.Any(i => i.StateKey == SelectedAuxPreset.StateKey)) return;
            AuxOutputItems.Add(new AuxOutputItem
            {
                Name = SelectedAuxPreset.Name,
                OnCommand = SelectedAuxPreset.OnCommand,
                OffCommand = SelectedAuxPreset.OffCommand,
                StateKey = SelectedAuxPreset.StateKey
            });
        }

        private void RemoveAuxOutput(AuxOutputItem item)
        {
            AuxOutputItems.Remove(item);
        }

        private void OnAuxPinsDiscovered(object? sender, List<AuxPinInfo> pins)
        {
            foreach (var pin in pins)
            {
                var stateKey = $"DOUT:{pin.PortNumber}";
                // Skip if this pin is already in the preset list
                if (AuxOutputPresets.Any(p => p.StateKey == stateKey)) continue;
                AuxOutputPresets.Add(new AuxOutputConfig
                {
                    Name = $"Aux {pin.PortNumber}",
                    OnCommand = $"{GrblHalConstants.AuxOutOn} P{pin.PortNumber}",
                    OffCommand = $"{GrblHalConstants.AuxOutOff} P{pin.PortNumber}",
                    StateKey = stateKey
                });
            }
        }
    }

    public partial class AuxOutputItem : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _onCommand = "";
        [ObservableProperty] private string _offCommand = "";
        [ObservableProperty] private string _stateKey = "";
    }
}
