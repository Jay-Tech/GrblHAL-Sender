using GrbLHALSender.Configuration;
using GrbLHALSender.Toolpaths;
using ReactiveUI;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels
{
    public class SurfacingViewModel : ViewModelBase, IDialogCloseable
    {
        private readonly ConfigManager _configManager;

        private double _stockWidth = 10;
        private double _stockHeight = 10;
        private double _toolDiameter = 1;
        private double _stepoverPercent = 40;
        private double _cutDepthPerPass = 0.1;
        private int _numberOfPasses = 2;
        private int _spindleRpm = 16000;
        private double _feedRate = 160;
        private double _safeZ = 1;
        private bool _spindleCw = true;
        private int _toolNumber = 1;
        private bool _useMetric = true;

        private string _statusMessage = "";
        private string _previewText = "";

        public double StockWidth
        {
            get => _stockWidth;
            set => this.RaiseAndSetIfChanged(ref _stockWidth, value);
        }

        public double StockHeight
        {
            get => _stockHeight;
            set => this.RaiseAndSetIfChanged(ref _stockHeight, value);
        }

        public double ToolDiameter
        {
            get => _toolDiameter;
            set => this.RaiseAndSetIfChanged(ref _toolDiameter, value);
        }

        public double StepoverPercent
        {
            get => _stepoverPercent;
            set => this.RaiseAndSetIfChanged(ref _stepoverPercent, value);
        }

        public double CutDepthPerPass
        {
            get => _cutDepthPerPass;
            set => this.RaiseAndSetIfChanged(ref _cutDepthPerPass, value);
        }

        public int NumberOfPasses
        {
            get => _numberOfPasses;
            set => this.RaiseAndSetIfChanged(ref _numberOfPasses, value);
        }

        public int SpindleRpm
        {
            get => _spindleRpm;
            set => this.RaiseAndSetIfChanged(ref _spindleRpm, value);
        }

        public double FeedRate
        {
            get => _feedRate;
            set => this.RaiseAndSetIfChanged(ref _feedRate, value);
        }

        public double SafeZ
        {
            get => _safeZ;
            set => this.RaiseAndSetIfChanged(ref _safeZ, value);
        }

        public bool SpindleCw
        {
            get => _spindleCw;
            set => this.RaiseAndSetIfChanged(ref _spindleCw, value);
        }

        public int ToolNumber
        {
            get => _toolNumber;
            set => this.RaiseAndSetIfChanged(ref _toolNumber, value);
        }

        public bool UseMetric
        {
            get => _useMetric;
            set
            {
                this.RaiseAndSetIfChanged(ref _useMetric, value);
                this.RaisePropertyChanged(nameof(UnitLabel));
                this.RaisePropertyChanged(nameof(RateLabel));
            }
        }

        public string UnitLabel => UseMetric ? "mm" : "in";
        public string RateLabel => UseMetric ? "mm/min" : "in/min";

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public string PreviewText
        {
            get => _previewText;
            set => this.RaiseAndSetIfChanged(ref _previewText, value);
        }

        /// <summary>
        /// Set by the View. Prompts the user for a save location and writes the given
        /// content. Returns the chosen path on success, null if canceled.
        /// </summary>
        public Func<string, string, Task<string?>>? SaveFileAsync { get; set; }

        public ICommand GenerateCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CloseCommand { get; }
        public Action? CloseAction { get; set; }

        public SurfacingViewModel(ConfigManager configManager)
        {
            _configManager = configManager;

            GenerateCommand = ReactiveCommand.Create(Generate);
            SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
            CloseCommand = ReactiveCommand.Create(() => CloseAction?.Invoke());

            var config = _configManager?.LoadConfig();
            if (config != null)
                UseMetric = config.UseMetric;
        }

        private SurfacingOptions BuildOptions() => new()
        {
            StockWidth = StockWidth,
            StockHeight = StockHeight,
            ToolDiameter = ToolDiameter,
            StepoverPercent = StepoverPercent,
            CutDepthPerPass = CutDepthPerPass,
            NumberOfPasses = NumberOfPasses,
            SpindleRpm = SpindleRpm,
            FeedRate = FeedRate,
            SafeZ = SafeZ,
            UseMetric = UseMetric,
            SpindleCw = SpindleCw,
            ToolNumber = ToolNumber
        };

        private bool ValidateInputs(out string error)
        {
            if (StockWidth <= 0) { error = "Stock width must be > 0"; return false; }
            if (StockHeight <= 0) { error = "Stock height must be > 0"; return false; }
            if (ToolDiameter <= 0) { error = "Tool diameter must be > 0"; return false; }
            if (StepoverPercent <= 0 || StepoverPercent > 100) { error = "Stepover must be 1-100%"; return false; }
            if (CutDepthPerPass <= 0) { error = "Cut depth must be > 0"; return false; }
            if (NumberOfPasses <= 0) { error = "Number of passes must be > 0"; return false; }
            if (SpindleRpm <= 0) { error = "Spindle RPM must be > 0"; return false; }
            if (FeedRate <= 0) { error = "Feed rate must be > 0"; return false; }
            if (ToolNumber < 0) { error = "Tool number must be >= 0"; return false; }
            error = "";
            return true;
        }

        private void Generate()
        {
            if (!ValidateInputs(out var error))
            {
                StatusMessage = error;
                PreviewText = "";
                return;
            }

            PreviewText = SurfacingGenerator.Generate(BuildOptions());
            var totalDepth = CutDepthPerPass * NumberOfPasses;
            StatusMessage = $"Generated. Total depth: {totalDepth:F3} {UnitLabel}";
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrEmpty(PreviewText))
            {
                Generate();
                if (string.IsNullOrEmpty(PreviewText)) return;
            }

            if (SaveFileAsync == null)
            {
                StatusMessage = "Save not available";
                return;
            }

            var defaultName = $"surface_{(int)StockWidth}x{(int)StockHeight}.nc";
            var path = await SaveFileAsync(defaultName, PreviewText);
            if (!string.IsNullOrEmpty(path))
                StatusMessage = $"Saved: {path}";
        }
    }
}
