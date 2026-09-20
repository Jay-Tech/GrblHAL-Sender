using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gamepad;
using GrbLHALSender.Theming;
using GrbLHALSender.WebServer;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels
{
    public class AppConfigViewModel : ViewModelBase, IDialogCloseable, ISavableViewModel
    {
       
        private readonly ConfigManager _configManager;
        private readonly FileUploadService _fileUploadService;
       // private readonly CommunicationManager _commManager;
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
        private bool _isPendantEnabled;
        private int _pendantPort;
        private double _pendantMaxJogMm;
        private double _pendantMaxFeed;
        private int _pendantDispatchMs;
        private bool _pendantAllowZeroAxis;
        private bool _pendantEchoJogs;
        private string _pendantSerialPort = NoSerialReceiver;
        private int _pendantSerialBaud;
        private int _webServerPort;
        private bool _useAntiAlias = true;
        private string _spindleImagePath = "spindle.png";
        private int _rendererIndex;
        private double _pollRate;
        private bool _borderlessWindow;
        private bool _streamBufferAhead = true;
        private bool _shutDownOs;
        private readonly ThemeService _themeService;
        private int _themePresetIndex;
        private string _accentColor = string.Empty;
        private bool _suppressThemePreview;

        public AuxOutputViewModel AuxOutputViewModel { get; }
        public GpioOutputViewModel GpioOutputViewModel { get; }
        public GcodeEventViewModel GcodeEventViewModel { get; }
        public bool EnableCutLines
        {
            get => _enableCutLines;
            set => this.RaiseAndSetIfChanged(ref _enableCutLines, value);
        }

        public bool StreamBufferAhead
        {
            get => _streamBufferAhead;
            set => this.RaiseAndSetIfChanged(ref _streamBufferAhead, value);
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

        public bool IsPendantEnabled
        {
            get => _isPendantEnabled;
            set => this.RaiseAndSetIfChanged(ref _isPendantEnabled, value);
        }

        public int PendantPort
        {
            get => _pendantPort;
            set => this.RaiseAndSetIfChanged(ref _pendantPort, value);
        }

        /// <summary>
        /// Ceiling on the distance one pendant message may command. A jog message
        /// carries a detent count times a step size, so a corrupted count would
        /// otherwise command an arbitrarily long move.
        /// </summary>
        public double PendantMaxJogMm
        {
            get => _pendantMaxJogMm;
            set => this.RaiseAndSetIfChanged(ref _pendantMaxJogMm, value);
        }

        /// <summary>
        /// Ceiling on the feed the pendant may request, mm/min. Set it to the
        /// machine's own maximum jog rate: the pendant already limits itself per
        /// axis and per step size, so clamping lower here silently discards feed
        /// it deliberately asked for.
        /// </summary>
        public double PendantMaxFeed
        {
            get => _pendantMaxFeed;
            set => this.RaiseAndSetIfChanged(ref _pendantMaxFeed, value);
        }

        /// <summary>
        /// Minimum gap between jog commands sent to the controller. Must stay
        /// below the pendant's own tick, or movement from several pendant
        /// messages is merged into one longer block - which halves the number of
        /// blocks the controller's planner has to look ahead at, and the planner
        /// decelerates to a stop at the end of the last block it holds.
        /// </summary>
        public int PendantDispatchMs
        {
            get => _pendantDispatchMs;
            set => this.RaiseAndSetIfChanged(ref _pendantDispatchMs, value);
        }

        /// <summary>
        /// Whether the pendant may rewrite a work offset. Off by default: losing a
        /// datum to a mis-hit button on a handheld is expensive.
        /// </summary>
        public bool PendantAllowZeroAxis
        {
            get => _pendantAllowZeroAxis;
            set => this.RaiseAndSetIfChanged(ref _pendantAllowZeroAxis, value);
        }

        public bool PendantEchoJogs
        {
            get => _pendantEchoJogs;
            set => this.RaiseAndSetIfChanged(ref _pendantEchoJogs, value);
        }

        /// <summary>
        /// Shown instead of a blank row when no receiver is fitted. A ComboBox
        /// selected on an empty string renders as nothing at all, which on a
        /// touchscreen is a target the operator cannot see or hit.
        /// </summary>
        public const string NoSerialReceiver = "(none)";

        /// <summary>
        /// Serial port of the ESP-NOW receiver board, or <see cref="NoSerialReceiver"/>
        /// when none is fitted and only the network transport runs.
        ///
        /// Chosen explicitly and never scanned for: the controller is on one of
        /// these ports too, and the two cannot be told apart by name.
        /// </summary>
        public string PendantSerialPort
        {
            get => _pendantSerialPort;
            set => this.RaiseAndSetIfChanged(ref _pendantSerialPort, value ?? NoSerialReceiver);
        }

        /// <summary>
        /// Baud rate for the receiver board. Only meaningful over a real UART; a
        /// USB CDC device ignores it.
        /// </summary>
        public int PendantSerialBaud
        {
            get => _pendantSerialBaud;
            set => this.RaiseAndSetIfChanged(ref _pendantSerialBaud, value);
        }

        /// <summary>
        /// Ports offered for the receiver. Kept beside the pendant settings rather
        /// than shared with the connection page, whose list is bound to the
        /// controller's own selection.
        /// </summary>
        public ObservableCollection<string> PendantSerialPorts { get; } = [];

        public ICommand RefreshPendantPortsCommand { get; }

        private void RefreshPendantPorts()
        {
            List<string> ports;
            try
            {
                ports = ConnectionViewModel.OrderPorts(System.IO.Ports.SerialPort.GetPortNames());
            }
            catch (Exception)
            {
                // Not every platform has serial ports at all.
                ports = [];
            }

            // A receiver unplugged while its port is still the configured one is
            // kept in the list. Without it the ComboBox would find no matching
            // item, select nothing, and write that back over a setting the
            // operator never touched - the same way refreshing the connection
            // page's list used to lose the controller's port.
            if (PendantSerialPort != NoSerialReceiver &&
                !string.IsNullOrWhiteSpace(PendantSerialPort) &&
                !ports.Contains(PendantSerialPort, StringComparer.OrdinalIgnoreCase))
                ports.Add(PendantSerialPort);

            ports.Insert(0, NoSerialReceiver);
            ConnectionViewModel.ReconcilePorts(PendantSerialPorts, ports);
        }

        /// <summary>
        /// Where uploads actually land, read live from the service rather than rebuilt here
        /// so the two can never disagree. Shown because the path is otherwise unguessable:
        /// on Linux it resolves inside <c>~/.config</c>, which is hidden from file browsers.
        /// </summary>
        public string UploadDirectory => _fileUploadService.UploadDirectory;

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

        public string[] RendererOptions { get; } = ["Software (Skia)", "Hardware (OpenGL)"];

        public int RendererIndex
        {
            get => _rendererIndex;
            set => this.RaiseAndSetIfChanged(ref _rendererIndex, value);
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

        public bool ShutDownOs
        {
            get => _shutDownOs;
            set => this.RaiseAndSetIfChanged(ref _shutDownOs, value);
        }

        // ---------------- Theme ----------------

        /// <summary>Built-in presets, shown in the Theme tab's dropdown.</summary>
        public IReadOnlyList<ThemePalette> ThemePresetList { get; } = ThemePresets.All;

        public int ThemePresetIndex
        {
            get => _themePresetIndex;
            set
            {
                this.RaiseAndSetIfChanged(ref _themePresetIndex, value);
                PreviewTheme();
            }
        }

        /// <summary>
        /// Accent override as "#RRGGBB", or empty for the preset default. Bound to the
        /// hex box and set by the swatch buttons; every change previews immediately.
        /// </summary>
        public string AccentColor
        {
            get => _accentColor;
            set
            {
                this.RaiseAndSetIfChanged(ref _accentColor, value);
                this.RaisePropertyChanged(nameof(IsAccentValid));
                PreviewTheme();
            }
        }

        /// <summary>False while the hex box holds something unparseable, so the UI can say so.</summary>
        public bool IsAccentValid =>
            string.IsNullOrWhiteSpace(AccentColor) || ThemeService.TryParseColor(AccentColor, out _);

        public IReadOnlyList<AccentSwatch> AccentSwatches { get; } = AccentSwatch.Defaults;

        public ICommand SelectAccentCommand { get; }

        public ICommand ResetAccentCommand { get; }

        public Action? CloseAction { get; set; }

        public ICommand SaveConfigCommand { get; }

        public ICommand CloseCommand { get; }


        public AppConfigViewModel(ConfigManager configManager,
            AuxOutputViewModel  auxOutputViewModel,
            GpioOutputViewModel gpioOutputViewModel,
            GcodeEventViewModel gcodeEventViewModel,
            ThemeService themeService,
            FileUploadService fileUploadService)
        {
            AuxOutputViewModel = auxOutputViewModel;
            GpioOutputViewModel = gpioOutputViewModel;
            GcodeEventViewModel = gcodeEventViewModel;
            _configManager = configManager;
            _themeService = themeService;
            _fileUploadService = fileUploadService;
            SaveConfigCommand = ReactiveCommand.Create(Save);
            CloseCommand = ReactiveCommand.Create(() => CloseAction?.Invoke());
            SelectAccentCommand = ReactiveCommand.Create<string>(hex => AccentColor = hex);
            ResetAccentCommand = ReactiveCommand.Create(() => AccentColor = string.Empty);
            RefreshPendantPortsCommand = ReactiveCommand.Create(RefreshPendantPorts);
            _configManager.OnConfigLoaded += _configManager_OnConfigLoaded;
         
        }

        private void _configManager_OnConfigLoaded(object? sender, GHalSenderConfig e)
        {
            _appConfig = e;
            EnableCutLines = _appConfig.ShowToolpathProgress;
            StreamBufferAhead = _appConfig.StreamBufferAhead;
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
            IsPendantEnabled = _appConfig.PendantConfig.Enabled;
            PendantPort = _appConfig.PendantConfig.Port;
            PendantMaxJogMm = _appConfig.PendantConfig.MaxJogDistanceMm;
            PendantMaxFeed = _appConfig.PendantConfig.MaxJogFeedRate;
            PendantDispatchMs = _appConfig.PendantConfig.JogDispatchIntervalMs;
            PendantAllowZeroAxis = _appConfig.PendantConfig.AllowZeroAxis;
            PendantEchoJogs = _appConfig.PendantConfig.EchoJogsToConsole;
            PendantSerialPort = string.IsNullOrWhiteSpace(_appConfig.PendantConfig.SerialPortName)
                ? NoSerialReceiver
                : _appConfig.PendantConfig.SerialPortName;
            PendantSerialBaud = _appConfig.PendantConfig.SerialBaudRate;
            // After the configured port is known, so an unplugged receiver still
            // appears in the list rather than the binding blanking the setting.
            RefreshPendantPorts();
            UseAntiAlias = _appConfig.UseAntiAlias;
            SpindleImagePath = _appConfig.SpindleImagePath;
            RendererIndex = (int)_appConfig.Renderer;
            PollRate = _appConfig.PollRate;
            BorderlessWindow = _appConfig.Borderless;
            ShutDownOs = _appConfig.ShutDownOs;
            LoadTheme(_appConfig.Theme);
        }

        private void LoadTheme(ThemeConfig theme)
        {
            // Seeding the bound properties would otherwise fire a preview per setter
            // and re-apply the theme that was just loaded.
            _suppressThemePreview = true;
            try
            {
                var index = ThemePresetList
                    .Select((p, i) => (p, i))
                    .FirstOrDefault(x => string.Equals(x.p.Id, theme.Preset, StringComparison.OrdinalIgnoreCase)).i;
                ThemePresetIndex = index;
                AccentColor = theme.AccentColor ?? string.Empty;
            }
            finally
            {
                _suppressThemePreview = false;
            }
        }

        /// <summary>Applies the in-progress selection so the user sees it before saving.</summary>
        private void PreviewTheme()
        {
            if (_suppressThemePreview) return;
            _themeService.Apply(BuildThemeConfig());
        }

        private ThemeConfig BuildThemeConfig() => new()
        {
            Preset = ThemePresetList[Math.Clamp(ThemePresetIndex, 0, ThemePresetList.Count - 1)].Id,
            // Blank or malformed input means "use the preset accent" rather than a broken color.
            AccentColor = ThemeService.TryParseColor(AccentColor, out _) ? AccentColor : null,
        };

        /// <summary>
        /// Drops an unsaved live preview and restores the persisted theme. Called when the
        /// config dialog is dismissed without saving, so closing can't leave the app
        /// wearing colors that aren't on disk.
        /// </summary>
        public void RevertUnsavedTheme()
        {
            if (_appConfig == null) return;
            _themeService.Apply(_appConfig.Theme);
            LoadTheme(_appConfig.Theme);
        }

        public void Save()
        {
            _appConfig.ShowToolpathProgress = EnableCutLines;
            _appConfig.StreamBufferAhead = StreamBufferAhead;
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
            _appConfig.PendantConfig.Enabled = IsPendantEnabled;
            _appConfig.PendantConfig.Port = PendantPort;
            _appConfig.PendantConfig.MaxJogDistanceMm = PendantMaxJogMm;
            _appConfig.PendantConfig.MaxJogFeedRate = PendantMaxFeed;
            _appConfig.PendantConfig.JogDispatchIntervalMs = PendantDispatchMs;
            _appConfig.PendantConfig.AllowZeroAxis = PendantAllowZeroAxis;
            _appConfig.PendantConfig.EchoJogsToConsole = PendantEchoJogs;
            _appConfig.PendantConfig.SerialPortName =
                PendantSerialPort == NoSerialReceiver ? string.Empty : PendantSerialPort;
            _appConfig.PendantConfig.SerialBaudRate = PendantSerialBaud;
             AuxOutputViewModel.Save();
            GpioOutputViewModel.Save();
            GcodeEventViewModel.Save();
            _appConfig.UseAntiAlias = UseAntiAlias;
            _appConfig.SpindleImagePath = SpindleImagePath;
            _appConfig.Renderer = (GHalSenderConfig.RendererType)RendererIndex;
            _appConfig.PollRate = PollRate;
            _appConfig.Borderless = BorderlessWindow;
            _appConfig.ShutDownOs = ShutDownOs;
            _appConfig.Theme = BuildThemeConfig();
            // SaveConfig raises OnConfigSaved, which ThemeService listens to — the
            // saved palette is applied from there, no explicit Apply needed.
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
        
    }
}
