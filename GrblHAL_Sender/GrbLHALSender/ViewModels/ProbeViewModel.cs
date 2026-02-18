using System;
using System.Collections.Generic;
using System.Reactive;
using System.Windows.Input;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Probe;
using GrbLHALSender.Utility;
using ReactiveUI;

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

        private CornerDirection _selectedCorner = CornerDirection.FrontLeft;
        private CenterFinderType _selectedCenterType = CenterFinderType.Bore;
        private bool _includeZInCorner;

        private string _probeZResult = "";
        private string _probeXResult = "";
        private string _probeYResult = "";
        private string _probeStatus = "";
        private bool _isProbing;

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
            set => this.RaiseAndSetIfChanged(ref _isProbing, value);
        }

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
                ToolType = SelectedToolType
            };
        }

        // ========== Probe Z ==========
        private void StartProbeZ()
        {
            if (IsProbing) return;
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

        private void OnCommandAck(object sender, EventArgs e)
        {
            SendNextCommand();
        }

        private void OnProbeResult(object sender, ProbeState e)
        {
            _phaseResults.Add(e);
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
