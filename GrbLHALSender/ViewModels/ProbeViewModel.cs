using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Probe;
using GrbLHALSender.Utility;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels
{
    public class ProbeViewModel : ViewModelBase, IDialogCloseable
    {
        private readonly CommunicationManager _communicationManager;
        private readonly ConfigManager _configManager;

        private ProbeToolType _selectedToolType = ProbeToolType.TouchPlate;
        private double _touchPlateThickness = 1.0;
        private double _probeDiameter = 2;
        private double _searchRate = 100;
        private double _latchRate = 20;
        private double _probeDistance = 10;
        private double _latchDistance = 1;
        private double _clearanceHeight = 5;
        private double _approxSize = 25;
        private string _unitSystem = "G21";

        private CornerDirection _selectedCorner = CornerDirection.FrontLeft;
        private CenterFinderType _selectedCenterType = CenterFinderType.Bore;
        private bool _includeZInCorner;

        private string _probeZResult = "";
        private string _probeXResult = "";
        private string _probeYResult = "";
        private string _probeStatus = "";
        private bool _isProbing;
        private bool _canProbe;
        private string _toolSetterPosition = "Not read yet";
        private string _toolSetterStatus = "";

        // Command sequencing state
        private ProbeJobBuilder _probeJob;
        private List<List<string>> _phases;
        private int _phaseIndex;
        private int _commandIndex;
        private List<string> _currentPhaseCommands;
        private Action _onAllPhasesComplete;
        private List<ProbeState> _phaseResults;

        public ProbeToolType SelectedToolType
        {
            get => _selectedToolType;
            set => this.RaiseAndSetIfChanged(ref _selectedToolType, value);
        }

        public bool IsTouchPlate => SelectedToolType == ProbeToolType.TouchPlate;

        public double TouchPlateThickness
        {
            get => _touchPlateThickness;
            set => this.RaiseAndSetIfChanged(ref _touchPlateThickness, value);
        }

        public double ProbeDiameter
        {
            get => _probeDiameter;
            set => this.RaiseAndSetIfChanged(ref _probeDiameter, value);
        }

        public double SearchRate
        {
            get => _searchRate;
            set => this.RaiseAndSetIfChanged(ref _searchRate, value);
        }

        public double LatchRate
        {
            get => _latchRate;
            set => this.RaiseAndSetIfChanged(ref _latchRate, value);
        }

        public double ProbeDistance
        {
            get => _probeDistance;
            set => this.RaiseAndSetIfChanged(ref _probeDistance, value);
        }

        public double LatchDistance
        {
            get => _latchDistance;
            set => this.RaiseAndSetIfChanged(ref _latchDistance, value);
        }

        public double ClearanceHeight
        {
            get => _clearanceHeight;
            set => this.RaiseAndSetIfChanged(ref _clearanceHeight, value);
        }

        public double ApproxSize
        {
            get => _approxSize;
            set => this.RaiseAndSetIfChanged(ref _approxSize, value);
        }

        public string UnitSystem
        {
            get => _unitSystem;
            set
            {
                this.RaiseAndSetIfChanged(ref _unitSystem, value);
                this.RaisePropertyChanged(nameof(UnitLabel));
                this.RaisePropertyChanged(nameof(RateLabel));
            }
        }

        /// <summary>Returns "mm" or "in" based on the current unit system.</summary>
        public string UnitLabel => UnitSystem == "G21" ? "mm" : "in";

        /// <summary>Returns "mm/min" or "in/min" based on the current unit system.</summary>
        public string RateLabel => UnitSystem == "G21" ? "mm/min" : "in/min";

        public CornerDirection SelectedCorner
        {
            get => _selectedCorner;
            set => this.RaiseAndSetIfChanged(ref _selectedCorner, value);
        }

        public CenterFinderType SelectedCenterType
        {
            get => _selectedCenterType;
            set => this.RaiseAndSetIfChanged(ref _selectedCenterType, value);
        }

        public bool IncludeZInCorner
        {
            get => _includeZInCorner;
            set => this.RaiseAndSetIfChanged(ref _includeZInCorner, value);
        }

        public string ProbeZResult
        {
            get => _probeZResult;
            set => this.RaiseAndSetIfChanged(ref _probeZResult, value);
        }

        public string ProbeXResult
        {
            get => _probeXResult;
            set => this.RaiseAndSetIfChanged(ref _probeXResult, value);
        }

        public string ProbeYResult
        {
            get => _probeYResult;
            set => this.RaiseAndSetIfChanged(ref _probeYResult, value);
        }

        public string ProbeStatus
        {
            get => _probeStatus;
            set => this.RaiseAndSetIfChanged(ref _probeStatus, value);
        }

        public bool IsProbing
        {
            get => _isProbing;
            set
            {
                this.RaiseAndSetIfChanged(ref _isProbing, value);
                this.RaisePropertyChanged(nameof(CanStartProbe));
            }
        }

        /// <summary>
        /// Whether the machine is in a state to accept a probe cycle. Pushed in by
        /// MainViewModel, which owns the machine state and job status: a probe cycle
        /// mid-job would interleave its moves into the running program.
        /// </summary>
        public bool CanProbe
        {
            get => _canProbe;
            set
            {
                this.RaiseAndSetIfChanged(ref _canProbe, value);
                this.RaisePropertyChanged(nameof(CanStartProbe));
            }
        }

        /// <summary>
        /// What the probe buttons bind to: the machine will accept a cycle and one is not
        /// already running. Combined here because a binding cannot express the two together.
        /// </summary>
        public bool CanStartProbe => CanProbe && !IsProbing;

        /// <summary>
        /// The stored G59.3 offset, as the controller reports it, or a note when it is not
        /// known yet. Shown so the operator can see what is there before overwriting it.
        /// </summary>
        public string ToolSetterPosition
        {
            get => _toolSetterPosition;
            set => this.RaiseAndSetIfChanged(ref _toolSetterPosition, value);
        }

        /// <summary>Outcome of the last tool setter action.</summary>
        public string ToolSetterStatus
        {
            get => _toolSetterStatus;
            set => this.RaiseAndSetIfChanged(ref _toolSetterStatus, value);
        }

        public ICommand ReadToolSetterCommand { get; }
        public ICommand SetToolSetterXyCommand { get; }
        public ICommand SetToolSetterZCommand { get; }

        public ICommand ProbeZCommand { get; }
        public ICommand ProbeCornerCommand { get; }
        public ICommand ProbeCenterCommand { get; }
        public ICommand SetToolTypeTouchPlateCommand { get; }
        public ICommand SetToolTypeProbe3DCommand { get; }
        public ICommand SetCornerCommand { get; }
        public ICommand SetCenterTypeCommand { get; }
        public Action? CloseAction { get; set; }
        public ICommand CloseCommand { get; }

        public ProbeViewModel(CommunicationManager communicationManager, ConfigManager configManager)
        {
            _communicationManager = communicationManager;
            _configManager = configManager;

            ProbeZCommand = ReactiveCommand.Create(StartProbeZ);
            ProbeCornerCommand = ReactiveCommand.Create(StartProbeCorner);
            ProbeCenterCommand = ReactiveCommand.Create(StartProbeCenter);
            SetToolTypeTouchPlateCommand = ReactiveCommand.Create(() => SelectedToolType = ProbeToolType.TouchPlate);
            SetToolTypeProbe3DCommand = ReactiveCommand.Create(() => SelectedToolType = ProbeToolType.Probe3D);
            SetCornerCommand = ReactiveCommand.Create<string>(s => SelectedCorner = Enum.Parse<CornerDirection>(s));
            SetCenterTypeCommand = ReactiveCommand.Create<string>(s => SelectedCenterType = Enum.Parse<CenterFinderType>(s));
            CloseCommand = ReactiveCommand.Create(() => CloseAction?.Invoke());
            ReadToolSetterCommand = ReactiveCommand.CreateFromTask(ReadToolSetterAsync);
            SetToolSetterXyCommand = ReactiveCommand.CreateFromTask(() => SetToolSetterAsync("X0Y0", "XY"));
            SetToolSetterZCommand = ReactiveCommand.CreateFromTask(() => SetToolSetterAsync("Z0", "Z"));


            // Update IsTouchPlate when SelectedToolType changes
            this.WhenAnyValue(x => x.SelectedToolType)
                .Subscribe(_ => this.RaisePropertyChanged(nameof(IsTouchPlate)));
        }

        public void LoadFromConfig(GHalSenderConfig config)
        {
            var pc = config.ProbeConfig;
            SelectedToolType = pc.ToolType;
            TouchPlateThickness = pc.TouchPlateThickness;
            ProbeDiameter = pc.ProbeDiameter;
            SearchRate = pc.SearchRate;
            LatchRate = pc.LatchRate;
            ProbeDistance = pc.ProbeDistance;
            LatchDistance = pc.LatchDistance;
            ClearanceHeight = pc.ClearanceHeight;
            ApproxSize = pc.ApproxSize;
            UnitSystem = config.UseMetric ? "G21" : "G20";
            config?.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GHalSenderConfig.UseMetric))
                {
                    UnitSystem = config.UseMetric ? "G21" : "G20";
                }
            };
        }

        public void SaveToConfig(GHalSenderConfig config)
        {
            var pc = config.ProbeConfig;
            pc.ToolType = SelectedToolType;
            pc.TouchPlateThickness = TouchPlateThickness;
            pc.ProbeDiameter = ProbeDiameter;
            pc.SearchRate = SearchRate;
            pc.LatchRate = LatchRate;
            pc.ProbeDistance = ProbeDistance;
            pc.LatchDistance = LatchDistance;
            pc.ClearanceHeight = ClearanceHeight;
            pc.ApproxSize = ApproxSize;
        }

        private ProbeJobBuilder CreateJobBuilder()
        {
            return new ProbeJobBuilder
            {
                ProbeSearchRate = SearchRate.ToInvariantString(),
                ProbeLatchRate = LatchRate.ToInvariantString(),
                ProbeDiameter = ProbeDiameter.ToInvariantString(),
                ProbeDistance = ProbeDistance.ToInvariantString(),
                LatchDistance = LatchDistance.ToInvariantString(),
                ClearanceHeight = ClearanceHeight.ToInvariantString(),
                TouchPlateThickness = TouchPlateThickness.ToInvariantString(),
                ToolType = SelectedToolType,
                UnitSystem = UnitSystem
            };
        }

        // ========== Probe Z ==========
        /// <summary>
        /// Reads the stored G59.3 offset for display.
        /// <para>
        /// Not run while probing: the <c>$#</c> report includes a <c>[PRB:...]</c> line,
        /// which the receive path turns into a probe result, and a phantom touch arriving
        /// mid-sequence would corrupt the offset being calculated.
        /// </para>
        /// </summary>
        private async Task ReadToolSetterAsync()
        {
            if (IsProbing) return;

            var position = await _communicationManager.GetCoordinateSystemAsync("G59.3");
            if (position == null)
            {
                ToolSetterPosition = "Unknown — controller did not report G59.3";
                return;
            }

            ToolSetterPosition = FormatAxes(position);
        }

        /// <summary>
        /// Stores the current machine position as the G59.3 origin — where tool change
        /// modes 2 and 3 go to reach the tool setter.
        /// <para>
        /// XY and Z are separate on purpose. The XY location is what the tool change moves
        /// to, and overwriting a working Z by accident is the expensive mistake here, so
        /// neither action touches an axis the operator did not name.
        /// </para>
        /// </summary>
        private async Task SetToolSetterAsync(string axisWords, string label)
        {
            if (IsProbing) return;

            // G10 L20 P9: P9 addresses G59.3, and L20 sets the offset so the current
            // position reads as the given value — so zeros park the origin right here.
            _communicationManager.SendCommand($"G10L20P9{axisWords}");
            ToolSetterStatus = $"{label} stored from current position";

            // Read it straight back rather than assume: this writes to the controller's
            // persistent storage, and the operator should see what actually took effect.
            await Task.Delay(150);
            await ReadToolSetterAsync();
        }

        /// <summary>
        /// Formats the offset as the controller reported it.
        /// <para>
        /// Deliberately unlabelled. UnitLabel follows the display preference, while $#
        /// reports in the machine's own units — so on a metric machine shown in inches the
        /// label said "in" over millimetre values. A wrong unit is worse than none, and the
        /// operator already knows what their machine reports in.
        /// </para>
        /// </summary>
        private static string FormatAxes(double[] values)
        {
            var labels = new[] { "X", "Y", "Z", "A", "B", "C" };
            var parts = new List<string>(values.Length);
            for (var i = 0; i < values.Length && i < labels.Length; i++)
                parts.Add($"{labels[i]} {values[i].ToInvariantString("F3")}");

            return string.Join("   ", parts);
        }

        private void StartProbeZ()
        {
            if (IsProbing) return;
            // Enforced here too, so the rule does not rely on the view's IsEnabled.
            if (!CanProbe) return;
            ClearResults();
            ProbeStatus = "Probing Z...";

            _probeJob = CreateJobBuilder();
            var zCommands = _probeJob.ProbeZ();

            _phases = new List<List<string>> { zCommands };
            _phaseResults = new List<ProbeState>();
            _onAllPhasesComplete = OnProbeZComplete;

            RunPhases();
        }

        private void OnProbeZComplete()
        {
            if (_phaseResults.Count < 1 || !_phaseResults[0].ProbeSuccessful)
            {
                ProbeStatus = "Z probe failed - no contact";
                return;
            }

            var zOffset = _probeJob.CalculateZOffset();
            ProbeZResult = _phaseResults[0].ZOffset;

            // Set Z WCS: G10 L20 P0 Z{offset}
            _communicationManager.SendCommand($"G90");
            _communicationManager.SendCommand($"G10L20P0Z{zOffset.ToInvariantString("F3")}");

            ProbeStatus = $"Z set. Offset: {zOffset.ToInvariantString("F3")}";
        }

        // ========== Probe Corner ==========
        private void StartProbeCorner()
        {
            if (IsProbing) return;
            // Enforced here too, so the rule does not rely on the view's IsEnabled.
            if (!CanProbe) return;
            ClearResults();
            ProbeStatus = $"Probing corner ({SelectedCorner})...";

            _probeJob = CreateJobBuilder();
            _phases = _probeJob.ProbeCorner(SelectedCorner, IncludeZInCorner);
            _phaseResults = new List<ProbeState>();
            _onAllPhasesComplete = OnProbeCornerComplete;

            RunPhases();
        }

        private void OnProbeCornerComplete()
        {
            ProbeJobBuilder.GetCornerDirections(SelectedCorner, out var xSign, out var ySign);
            var phaseOffset = IncludeZInCorner ? 1 : 0;

            // Check Z result if included
            if (IncludeZInCorner)
            {
                if (_phaseResults.Count < 1 || !_phaseResults[0].ProbeSuccessful)
                {
                    ProbeStatus = "Z probe failed - no contact";
                    return;
                }
                ProbeZResult = _phaseResults[0].ZOffset;
            }

            // Check X result
            if (_phaseResults.Count < phaseOffset + 1 || !_phaseResults[phaseOffset].ProbeSuccessful)
            {
                ProbeStatus = "X probe failed - no contact";
                return;
            }

            // Check Y result
            if (_phaseResults.Count < phaseOffset + 2 || !_phaseResults[phaseOffset + 1].ProbeSuccessful)
            {
                ProbeStatus = "Y probe failed - no contact";
                return;
            }

            var xProbeResult = _phaseResults[phaseOffset].XOffset.StringToDouble();
            var yProbeResult = _phaseResults[phaseOffset + 1].YOffset.StringToDouble();
            var xCompensation = _probeJob.CalculateXYOffset(xSign);
            var yCompensation = _probeJob.CalculateXYOffset(ySign);

            ProbeXResult = _phaseResults[phaseOffset].XOffset;
            ProbeYResult = _phaseResults[phaseOffset + 1].YOffset;

            // Build the G10 command
            _communicationManager.SendCommand("G90");
            var cmd = "G10L20P0";
            cmd += $"X{xCompensation.ToInvariantString("F3")}";
            cmd += $"Y{yCompensation.ToInvariantString("F3")}";

            if (IncludeZInCorner)
            {
                var zOffset = _probeJob.CalculateZOffset();
                cmd += $"Z{zOffset.ToInvariantString("F3")}";
            }

            _communicationManager.SendCommand(cmd);

            ProbeStatus = $"Corner set. X:{xCompensation.ToInvariantString("F3")} Y:{yCompensation.ToInvariantString("F3")}";
        }

        // ========== Probe Center ==========
        private void StartProbeCenter()
        {
            if (IsProbing) return;
            // Enforced here too, so the rule does not rely on the view's IsEnabled.
            if (!CanProbe) return;
            ClearResults();

            _probeJob = CreateJobBuilder();

            if (SelectedCenterType == CenterFinderType.Boss)
            {
                ProbeStatus = "Probing boss center...";
                _phases = _probeJob.ProbeBossCenter(ApproxSize.ToInvariantString());
            }
            else
            {
                ProbeStatus = $"Probing {SelectedCenterType} center...";
                _phases = _probeJob.ProbeInsideCenter();
            }

            _phaseResults = new List<ProbeState>();
            _onAllPhasesComplete = OnProbeCenterComplete;

            RunPhases();
        }

        private void OnProbeCenterComplete()
        {
            if (_phaseResults.Count < 4)
            {
                ProbeStatus = "Center probe incomplete";
                return;
            }

            // Phase results: 0=X+, 1=X-, 2=Y+, 3=Y-
            var xPosResult = _phaseResults[0];
            var xNegResult = _phaseResults[1];
            var yPosResult = _phaseResults[2];
            var yNegResult = _phaseResults[3];

            if (!xPosResult.ProbeSuccessful || !xNegResult.ProbeSuccessful ||
                !yPosResult.ProbeSuccessful || !yNegResult.ProbeSuccessful)
            {
                ProbeStatus = "Center probe failed - missing contact";
                return;
            }

            var xPos = xPosResult.XOffset.StringToDouble();
            var xNeg = xNegResult.XOffset.StringToDouble();
            var yPos = yPosResult.YOffset.StringToDouble();
            var yNeg = yNegResult.YOffset.StringToDouble();

            // Center = midpoint (probe radius cancels out when probing both sides)
            var centerX = (xPos + xNeg) / 2.0;
            var centerY = (yPos + yNeg) / 2.0;

            // Measured size (distance between contact points + probe diameter)
            var measuredWidth = Math.Abs(xPos - xNeg) + ProbeDiameter;
            var measuredHeight = Math.Abs(yPos - yNeg) + ProbeDiameter;

            if (SelectedCenterType == CenterFinderType.Boss)
            {
                // Boss: measured size = distance between contacts - probe diameter
                measuredWidth = Math.Abs(xPos - xNeg) - ProbeDiameter;
                measuredHeight = Math.Abs(yPos - yNeg) - ProbeDiameter;
            }

            ProbeXResult = centerX.ToInvariantString("F3");
            ProbeYResult = centerY.ToInvariantString("F3");

            // Move to computed center, then set WCS to X0 Y0
            _communicationManager.SendCommand("G90");
            _communicationManager.SendCommand($"G0X{centerX.ToInvariantString("F3")}Y{centerY.ToInvariantString("F3")}");
            _communicationManager.SendCommand("G10L20P0X0Y0");

            ProbeStatus = $"Center set. Size: {measuredWidth.ToInvariantString("F2")} x {measuredHeight.ToInvariantString("F2")}";
        }

        // ========== Phase Execution Engine ==========
        private void RunPhases()
        {
            IsProbing = true;
            _phaseIndex = 0;
            _commandIndex = 0;

            _communicationManager.OnCommandAck += OnCommandAck;
            _communicationManager.OnProbeResults += OnProbeResult;

            StartCurrentPhase();
        }

        private void StartCurrentPhase()
        {
            if (_phaseIndex >= _phases.Count)
            {
                CompleteAllPhases();
                return;
            }

            _currentPhaseCommands = _phases[_phaseIndex];
            _commandIndex = 0;
            SendNextCommand();
        }

        private void SendNextCommand()
        {
            if (_commandIndex < _currentPhaseCommands.Count)
            {
                var cmd = _currentPhaseCommands[_commandIndex];
                _commandIndex++;
                _communicationManager.SendCommand(cmd);
            }
            else
            {
                // Phase complete, move to next
                _phaseIndex++;
                StartCurrentPhase();
            }
        }

        private void OnCommandAck(object? sender, CommandAck e)
        {
            // A rejected command means the rest of the sequence is operating on state the
            // controller never reached, so stop instead of probing on regardless.
            if (e.IsError)
            {
                AbortSequence($"Probe aborted: controller rejected a command (error:{e.ErrorCode})");
                return;
            }

            SendNextCommand();
        }

        private void OnProbeResult(object sender, ProbeState e)
        {
            _phaseResults.Add(e);
        }

        /// <summary>
        /// Tears the sequence down without running the completion callback.
        /// <para>
        /// Deliberately not <see cref="CompleteAllPhases"/>: that invokes
        /// <c>_onAllPhasesComplete</c>, which sets work offsets from whatever results
        /// arrived — so finishing a half-run probe "successfully" would apply an offset
        /// derived from touches that never happened.
        /// </para>
        /// </summary>
        private void AbortSequence(string status)
        {
            _communicationManager.OnCommandAck -= OnCommandAck;
            _communicationManager.OnProbeResults -= OnProbeResult;
            IsProbing = false;
            _phases = null;
            _currentPhaseCommands = null;
            _onAllPhasesComplete = null;

            // Leave the parser in absolute mode; the sequence switches to G91 for the
            // approach moves and abandoning it there would surprise the next command.
            _communicationManager.SendCommand("G90");

            ProbeStatus = status;
        }

        private void CompleteAllPhases()
        {
            _communicationManager.OnCommandAck -= OnCommandAck;
            _communicationManager.OnProbeResults -= OnProbeResult;
            IsProbing = false;

            // Return to absolute mode
            _communicationManager.SendCommand("G90");

            _onAllPhasesComplete?.Invoke();
        }

        private void ClearResults()
        {
            ProbeXResult = "";
            ProbeYResult = "";
            ProbeZResult = "";
            ProbeStatus = "";
        }
    }
}
