using GrbLHALSender.Configuration;
using ReactiveUI;
using System;
using System.Linq;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels
{
    public class AppConfigViewModel : ViewModelBase, IDialogCloseable
    {
        private readonly ConfigManager _configManager;
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
        private bool _isWebServerEnabled;
        private int _webServerPort;
        private string _spindleImagePath = "spindle.png";
        private double _pollRate;


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

        public Action? CloseAction { get; set; }
        public ICommand SaveConfigCommand { get; }
        public ICommand CloseCommand { get; }

        public AppConfigViewModel(ConfigManager configManager)
        {
            _configManager = configManager;
            SaveConfigCommand = ReactiveCommand.Create(SaveConfig);
            CloseCommand = ReactiveCommand.Create(() => CloseAction?.Invoke());
            _configManager.OnConfigLoaded += _configManager_OnConfigLoaded;
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
            IsWebServerEnabled = _appConfig.WebServerConfig.Enabled;
            WebServerPort = _appConfig.WebServerConfig.Port;
            SpindleImagePath = _appConfig.SpindleImagePath;
            PollRate = _appConfig.PollRate;
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
            _appConfig.WebServerConfig.Enabled = IsWebServerEnabled;
            _appConfig.WebServerConfig.Port = WebServerPort;
            _appConfig.SpindleImagePath = SpindleImagePath;
            _appConfig.PollRate = PollRate;
            _configManager.SaveConfig();
        }
    }
}
