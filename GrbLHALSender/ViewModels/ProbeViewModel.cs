using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Probe;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels
{
    public class ProbeViewModel : ViewModelBase, IDialogCloseable
    {
        private readonly CommunicationManager _communicationManager;
        private readonly ConfigManager _configManager;
        private readonly MachineStateService _machineStateService;

        private ProbeToolType _selectedToolType = ProbeToolType.TouchPlate;
        private string _touchPlateThicknessText = "1";
        private string _probeDiameterText = "2";
        private string _searchRateText = "100";
        private string _latchRateText = "20";
        private string _probeDistanceText = "10";
        private string _latchDistanceText = "1";
        private string _clearanceHeightText = "5";
        private string _probeDepthText = "5";
        private string _approxSizeText = "25";
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
        private bool _toolReferenceSet;
        // Parser unit in force before a probe, so the sequence can hand it back.
        private string? _modalUnitsBeforeProbe;
        // Moves that put the machine back where it started, run on a good probe only.
        private List<string>? _toolReferenceReturn;
        // Where a corner cycle began, so its approach heights and its finishing move can all
        // be planned from one fixed reference rather than from wherever the last probe stopped.
        private double[]? _cornerStart;
        // The same for a centre cycle: the inside phases return to it, and both cycles climb
        // back to its height before crossing to the measured centre.
        private double[]? _centerStart;

        // What to say when a probe in this cycle fails to make contact. Set per cycle because
        // the consequence differs, and on a tool reference it is the whole point: $TLR sent
        // after a failed probe clears whatever reference the controller was holding, so the
        // operator needs telling that it was not sent and their reference is intact. Since the
        // abort happens on the probe report, it has no idea which cycle it interrupted.
        private string _probeFailureMessage = "Probe failed — no contact, sequence stopped";

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

        // Every numeric field is bound as text and parsed at the point of use. Deliberately
        // not bound as a double.
        //
        // A TextBox bound straight to a double converts on every keystroke, and not every
        // state you pass through while editing converts. Clearing the box to retype it leaves
        // it empty, which fails and puts an "invalidCast" over the field while the source
        // quietly keeps the old number — so the box and the value being used disagree.
        // Field-confirmed on the Diameter box during 3D probe testing.
        //
        // Note a trailing "0." parses fine in .NET, so typing a decimal is not the trigger;
        // emptying the field is, along with a lone "-" or ".".
        //
        // UpdateSourceTrigger=LostFocus would hide the message but is the wrong fix here: the
        // virtual keyboard assigns Text directly, and focus need not leave the box before
        // Probe is pressed, so the cycle would run on the value the operator just replaced.
        // Parsing at the point of use keeps what is on screen and what gets sent identical.
        //
        // The doubles stay as read-only projections so every existing caller is untouched.

        public string TouchPlateThicknessText
        {
            get => _touchPlateThicknessText;
            set => SetNumericField(ref _touchPlateThicknessText, value, nameof(TouchPlateThickness));
        }

        public string ProbeDiameterText
        {
            get => _probeDiameterText;
            set => SetNumericField(ref _probeDiameterText, value, nameof(ProbeDiameter));
        }

        public string SearchRateText
        {
            get => _searchRateText;
            set => SetNumericField(ref _searchRateText, value, nameof(SearchRate));
        }

        public string LatchRateText
        {
            get => _latchRateText;
            set => SetNumericField(ref _latchRateText, value, nameof(LatchRate));
        }

        public string ProbeDistanceText
        {
            get => _probeDistanceText;
            set => SetNumericField(ref _probeDistanceText, value, nameof(ProbeDistance));
        }

        public string LatchDistanceText
        {
            get => _latchDistanceText;
            set => SetNumericField(ref _latchDistanceText, value, nameof(LatchDistance));
        }

        public string ClearanceHeightText
        {
            get => _clearanceHeightText;
            set => SetNumericField(ref _clearanceHeightText, value, nameof(ClearanceHeight));
        }

        public string ProbeDepthText
        {
            get => _probeDepthText;
            set => SetNumericField(ref _probeDepthText, value, nameof(ProbeDepth));
        }

        public string ApproxSizeText
        {
            get => _approxSizeText;
            set => SetNumericField(ref _approxSizeText, value, nameof(ApproxSize));
        }

        public double TouchPlateThickness => _touchPlateThicknessText.StringToDouble();
        public double ProbeDiameter => _probeDiameterText.StringToDouble();
        public double SearchRate => _searchRateText.StringToDouble();
        public double LatchRate => _latchRateText.StringToDouble();
        public double ProbeDistance => _probeDistanceText.StringToDouble();
        public double LatchDistance => _latchDistanceText.StringToDouble();
        public double ClearanceHeight => _clearanceHeightText.StringToDouble();
        public double ProbeDepth => _probeDepthText.StringToDouble();
        public double ApproxSize => _approxSizeText.StringToDouble();

        private void SetNumericField(ref string field, string value, string numericName,
            [CallerMemberName] string? textName = null)
        {
            if (field == value) return;

            field = value;
            this.RaisePropertyChanged(textName);
            this.RaisePropertyChanged(numericName);
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

        /// <summary>
        /// Whether the selected centre feature is an outside one. Approx Size and Clearance
        /// Height only do anything for a boss: the inside cycle probes outward until it touches
        /// and never moves Z, so leaving those enabled implied they were being used.
        /// </summary>
        public bool IsBossCenter => SelectedCenterType == CenterFinderType.Boss;

        public CenterFinderType SelectedCenterType
        {
            get => _selectedCenterType;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedCenterType, value);
                this.RaisePropertyChanged(nameof(IsBossCenter));
            }
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

        /// <summary>
        /// Whether the controller holds a tool length reference, from the TLR field of the
        /// status report.
        /// </summary>
        public bool ToolReferenceSet
        {
            get => _toolReferenceSet;
            set => this.RaiseAndSetIfChanged(ref _toolReferenceSet, value);
        }

        public ICommand ProbeToolReferenceHereCommand { get; }
        public ICommand ProbeToolReferenceAtSetterCommand { get; }
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

        public ProbeViewModel(CommunicationManager communicationManager, ConfigManager configManager,
            MachineStateService machineStateService)
        {
            _communicationManager = communicationManager;
            _configManager = configManager;
            _machineStateService = machineStateService;
            _machineStateService.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MachineStateService.TLR))
                    ToolReferenceSet = _machineStateService.TLR;
            };

            ProbeZCommand = ReactiveCommand.CreateFromTask(async () => { await CaptureModalUnitsAsync(); StartProbeZ(); });
            ProbeCornerCommand = ReactiveCommand.CreateFromTask(async () => { await CaptureModalUnitsAsync(); StartProbeCorner(); });
            ProbeCenterCommand = ReactiveCommand.CreateFromTask(async () => { await CaptureModalUnitsAsync(); StartProbeCenter(); });
            SetToolTypeTouchPlateCommand = ReactiveCommand.Create(() => SelectedToolType = ProbeToolType.TouchPlate);
            SetToolTypeProbe3DCommand = ReactiveCommand.Create(() => SelectedToolType = ProbeToolType.Probe3D);
            SetCornerCommand = ReactiveCommand.Create<string>(s => SelectedCorner = Enum.Parse<CornerDirection>(s));
            SetCenterTypeCommand = ReactiveCommand.Create<string>(s => SelectedCenterType = Enum.Parse<CenterFinderType>(s));
            CloseCommand = ReactiveCommand.Create(() => CloseAction?.Invoke());
            ReadToolSetterCommand = ReactiveCommand.CreateFromTask(ReadToolSetterAsync);
            ProbeToolReferenceHereCommand = ReactiveCommand.CreateFromTask(ProbeToolReferenceHereAsync);
            ProbeToolReferenceAtSetterCommand = ReactiveCommand.CreateFromTask(() => StartToolReferenceProbeAtSetterAsync());
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
            TouchPlateThicknessText = pc.TouchPlateThickness.ToInvariantString();
            ProbeDiameterText = pc.ProbeDiameter.ToInvariantString();
            SearchRateText = pc.SearchRate.ToInvariantString();
            LatchRateText = pc.LatchRate.ToInvariantString();
            ProbeDistanceText = pc.ProbeDistance.ToInvariantString();
            LatchDistanceText = pc.LatchDistance.ToInvariantString();
            ClearanceHeightText = pc.ClearanceHeight.ToInvariantString();
            ProbeDepthText = pc.ProbeDepth.ToInvariantString();
            ApproxSizeText = pc.ApproxSize.ToInvariantString();
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
            pc.ProbeDepth = ProbeDepth;
            pc.ApproxSize = ApproxSize;
        }

        /// <summary>
        /// Refuses to start a cycle while any field is not a number, naming the offender.
        /// <para>
        /// Necessary because an unparseable field reads as zero rather than failing. Zero is
        /// harmless in some places — a zero distance just fails to make contact — but a zero
        /// diameter silently shifts a centre result by the width of the probe and then writes
        /// that as the work offset, and a zero clearance drags the tool across the job. A
        /// field left mid-edit is the normal way to arrive at either, so it is worth one
        /// check rather than a wrong datum nobody notices.
        /// </para>
        /// </summary>
        private bool FieldsValid(List<(string Label, string Text)> fields)
        {
            foreach (var (label, text) in fields)
            {
                if (IsNumber(text)) continue;

                ProbeStatus = $"{label} is not a number — fix it before probing";
                return false;
            }

            return true;
        }

        /// <summary>
        /// The four every cycle reads. Deliberately the only ones checked in common: validating
        /// the whole dialog meant a tool reference probe could be refused over Diameter, which
        /// it never reads and which its own tab does not even display.
        /// </summary>
        private List<(string Label, string Text)> CommonFields() =>
        [
            ("Search Rate", SearchRateText),
            ("Latch Rate", LatchRateText),
            ("Distance", ProbeDistanceText),
            ("Latch Dist", LatchDistanceText)
        ];

        /// <summary>A Z touch reads the plate thickness, and on a 3D probe nothing else.</summary>
        private List<(string, string)> ZProbeFields()
        {
            var fields = CommonFields();
            if (IsTouchPlate) fields.Add(("Plate Thickness", TouchPlateThicknessText));
            return fields;
        }

        /// <summary>
        /// A corner reads the approach heights, and the stylus radius for edge compensation.
        /// </summary>
        private List<(string, string)> CornerFields()
        {
            var fields = CommonFields();
            fields.Add(("Clearance Height", ClearanceHeightText));
            fields.Add(("Probe Depth", ProbeDepthText));
            if (!IsTouchPlate) fields.Add(("Diameter", ProbeDiameterText));
            if (IsTouchPlate && IncludeZInCorner)
                fields.Add(("Plate Thickness", TouchPlateThicknessText));
            return fields;
        }

        /// <summary>
        /// A centre reads the diameter for the size it reports, and on a boss the approximate
        /// size and clearance it needs to stand off and drop. The inside cycle needs neither.
        /// </summary>
        private List<(string, string)> CenterFields()
        {
            var fields = CommonFields();
            if (!IsTouchPlate) fields.Add(("Diameter", ProbeDiameterText));
            if (SelectedCenterType == CenterFinderType.Boss)
            {
                fields.Add(("Approx Size", ApproxSizeText));
                fields.Add(("Clearance Height", ClearanceHeightText));
            }
            return fields;
        }

        /// <summary>
        /// Whether a field holds something a probe can be run on. Invariant, because the
        /// controller only ever talks dot-decimal and so do these fields.
        /// </summary>
        internal static bool IsNumber(string text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        /// <summary>
        /// Notes the parser unit in force before a probe so it can be handed back.
        /// <para>
        /// A probe sequence starts with G20 or G21 to match the numbers in the UI, and that
        /// is modal — it outlives the sequence. Left changed, the next thing to send an
        /// unqualified coordinate has it read in the wrong unit: a metric machine left in
        /// G20 treats millimetre values as inches and overshoots by 25.4.
        /// </para>
        /// </summary>
        private async Task CaptureModalUnitsAsync()
        {
            if (IsProbing) return;
            _modalUnitsBeforeProbe = await _communicationManager.GetModalUnitsAsync();
        }

        private void RestoreModalUnits()
        {
            // Nothing to restore if $G could not be read, or if it already matches what the
            // sequence set — sending it anyway would just be noise.
            if (_modalUnitsBeforeProbe == null || _modalUnitsBeforeProbe == UnitSystem) return;

            _communicationManager.SendCommand(_modalUnitsBeforeProbe);
            _modalUnitsBeforeProbe = null;
        }

        /// <summary>
        /// Converts a value the controller reported into the unit the probe sequence is
        /// sending in.
        /// <para>
        /// Needed because the two are set independently: $# follows $13, while the numbers
        /// we send are read in whatever G20/G21 the sequence established. On a metric
        /// machine driven from an imperial display those differ, and a raw millimetre
        /// coordinate sent as inches is a 25.4x overshoot.
        /// </para>
        /// <para>
        /// For raw controller output only. MachineStateService has already converted its
        /// positions to display units — see <see cref="MachinePosition"/> — so putting
        /// those through here converts them a second time.
        /// </para>
        /// </summary>
        private double ToSequenceUnits(double reportedValue) =>
            ToSequenceUnits(reportedValue,
                _communicationManager.MachineData?.ReportInMetric ?? true,
                UnitSystem == "G21");

        /// <inheritdoc cref="ToSequenceUnits(double)"/>
        internal static double ToSequenceUnits(double reportedValue, bool machineIsMetric,
            bool sequenceIsMetric)
        {
            if (machineIsMetric == sequenceIsMetric) return reportedValue;

            return machineIsMetric
                ? reportedValue / 25.4   // mm reported, inches being sent
                : reportedValue * 25.4;  // inches reported, mm being sent
        }

        private ProbeJobBuilder CreateJobBuilder()
        {
            return new ProbeJobBuilder
            {
                ProbeSearchRate = SearchRate.ToInvariantString(),
                ProbeLatchRate = LatchRate.ToInvariantString(),
                ProbeDistance = ProbeDistance.ToInvariantString(),
                LatchDistance = LatchDistance.ToInvariantString(),
                ClearanceHeight = ClearanceHeight.ToInvariantString(),
                ProbeDepth = ProbeDepth.ToInvariantString(),
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

        /// <summary>
        /// Probes from where the machine is standing, then backs the tool off the trigger to
        /// the height it started from. Without the retract the tool is left resting on the
        /// setter, which is no place to leave it.
        /// </summary>
        private async Task ProbeToolReferenceHereAsync()
        {
            if (IsProbing || !CanProbe) return;

            var start = MachinePosition();
            await CaptureModalUnitsAsync();

            var back = start == null
                ? null
                : new List<string> { "G90", $"G53G0Z{start[2].ToInvariantString("F3")}" };

            StartToolReferenceProbe(moveToSetter: false, returnMoves: back);
        }

        /// <summary>
        /// Machine position as the controller last reported it, or null when fewer than
        /// three axes have been seen — a caller must not build a move out of a partial one.
        /// <para>
        /// These are already in display units: MachineStateService converts on the way in,
        /// and the sequence's G20/G21 comes from the same display preference. So they go
        /// straight into a move — do <b>not</b> put them through ToSequenceUnits, which is
        /// for raw controller output following $13. Doing so converts a second time and,
        /// on a metric machine shown in inches, drives to machine zero instead.
        /// </para>
        /// </summary>
        private double[]? MachinePosition()
        {
            var pos = _machineStateService.MachinePositions;
            return pos is { Length: >= 3 } ? pos : null;
        }

        /// <summary>
        /// Probes the tool currently in the spindle and captures the result as the tool
        /// length reference. Run this once, before starting a job, with the tool you set
        /// work Z zero from — after which every tool change only needs a touch off.
        /// <para>
        /// Deliberately a pre-job action rather than something done mid-stream. It ends in
        /// $TLR, and $TLR sent after a failed probe clears the reference rather than
        /// setting it, so this only issues it when the probe actually made contact.
        /// </para>
        /// </summary>
        private void StartToolReferenceProbe(bool moveToSetter, List<string>? approach = null,
            List<string>? returnMoves = null)
        {
            if (IsProbing || !CanProbe) return;
            // Rates and distances only. A tool length reference is a straight vertical touch:
            // no stylus radius, and no plate thickness either, since the offset $TPW applies is
            // the difference between two probes of the same surface and thickness cancels.
            // Before ClearResults, which would wipe the message explaining the refusal.
            if (!FieldsValid(CommonFields())) return;
            _probeFailureMessage = "Probe failed — no contact, TLR not set";

            _toolReferenceReturn = returnMoves;
            ClearResults();
            ProbeStatus = moveToSetter
                ? "Moving to tool setter and probing reference..."
                : "Probing tool length reference...";

            _probeJob = CreateJobBuilder();
            _phases = new List<List<string>>();
            if (moveToSetter && approach != null)
                _phases.Add(approach);
            _phases.Add(_probeJob.ProbeZ());

            _phaseResults = new List<ProbeState>();
            _onAllPhasesComplete = OnToolReferenceComplete;

            RunPhases();
        }

        /// <summary>
        /// Reads the stored tool setter position and drives to it before probing.
        /// <para>
        /// Retracts Z to machine zero before travelling and only descends once over the
        /// setter, so the approach cannot drag a long tool across the work — the same order
        /// a tool change macro uses.
        /// </para>
        /// </summary>
        private async Task StartToolReferenceProbeAtSetterAsync()
        {
            if (IsProbing || !CanProbe) return;

            // Captured before the approach moves, so a good probe can put the machine back
            // where the operator left it.
            var start = MachinePosition();

            var setter = await _communicationManager.GetCoordinateSystemAsync("G59.3");
            if (setter == null || setter.Length < 3)
            {
                ProbeStatus = "No G59.3 tool setter position stored — set it first";
                return;
            }

            var (approach, back) = BuildSetterProbeMoves(setter, start, UnitSystem,
                _communicationManager.MachineData?.ReportInMetric ?? true);

            await CaptureModalUnitsAsync();
            StartToolReferenceProbe(moveToSetter: true, approach, back);
        }

        /// <summary>
        /// Builds the approach to the tool setter and the move back to where the operator
        /// left the machine.
        /// <para>
        /// The two sets of coordinates arrive in different units, which is the trap here.
        /// <paramref name="setter"/> is raw $# output and follows $13, so it has to be
        /// converted into whatever the sequence sends in. <paramref name="start"/> came
        /// from MachineStateService, which converts to display units on the way in — and
        /// the sequence's unit word is chosen from that same display preference, so those
        /// values are already right. Converting them again drove a metric machine shown in
        /// inches to machine X0/Y0 instead of back to the captured position.
        /// </para>
        /// <para>
        /// Z is deliberately not restored: it returns to machine zero and stays there,
        /// because the pre-approach Z may be down in the work and dropping back to it
        /// unattended is not worth the convenience.
        /// </para>
        /// </summary>
        internal static (List<string> Approach, List<string> Return) BuildSetterProbeMoves(
            double[] setter, double[]? start, string unitSystem, bool machineIsMetric)
        {
            var sequenceIsMetric = unitSystem == "G21";
            string Setter(int axis) => ToSequenceUnits(setter[axis], machineIsMetric, sequenceIsMetric)
                .ToInvariantString("F3");

            var approach = new List<string>
            {
                unitSystem,
                "G90",
                "G53G0Z0",
                $"G53G0X{Setter(0)}Y{Setter(1)}",
                $"G53G0Z{Setter(2)}"
            };

            var back = start == null
                ? new List<string> { "G90", "G53G0Z0" }
                : new List<string>
                {
                    "G90",
                    "G53G0Z0",
                    $"G53G0X{start[0].ToInvariantString("F3")}Y{start[1].ToInvariantString("F3")}"
                };

            return (approach, back);
        }

        private void OnToolReferenceComplete()
        {
            // The last result is the latch pass, the accurate one.
            var probe = _phaseResults.Count > 0 ? _phaseResults[^1] : null;
            if (probe == null || !probe.ProbeSuccessful)
            {
                // $TLR is deliberately not sent here: after a failed probe it clears any
                // reference the controller was holding instead of setting a new one.
                ProbeStatus = "Probe failed — no contact, TLR not set";
                return;
            }

            _communicationManager.SendCommand("G90");
            _communicationManager.SendCommand(GrblHalConstants.ToolLengthReference);

            // Success only. A failed probe leaves grblHAL in a probe alarm that would refuse
            // these anyway, and the operator will want to see where it stopped.
            if (_toolReferenceReturn != null)
            {
                foreach (var move in _toolReferenceReturn)
                    _communicationManager.SendCommand(move);
            }

            ProbeStatus = "Tool length reference set — zero on the stock and start the job";
        }

        private void StartProbeZ()
        {
            if (IsProbing) return;
            // Enforced here too, so the rule does not rely on the view's IsEnabled.
            if (!CanProbe) return;
            if (!FieldsValid(ZProbeFields())) return;
            _probeFailureMessage = "Probe failed — no contact, Z not set";
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

            // Converted for display: [PRB:] reports in the machine's own units ($13), so on a
            // metric machine shown in inches this read as millimetres under an "in" label —
            // -56.335 where the operator expected about -2.2.
            ProbeZResult = ToSequenceUnits(_phaseResults[0].ZOffset.StringToDouble())
                .ToInvariantString("F3");

            // Set Z WCS: G10 L20 P0 Z{offset}
            _communicationManager.SendCommand($"G90");
            _communicationManager.SendCommand($"G10L20P0Z{zOffset.ToInvariantString("F3")}");

            // Then lift clear. The latch pass stops on contact, so without this the stylus is
            // left resting on the surface — no place to leave a probe, and it makes whatever
            // the operator does next a scrape across the work.
            //
            // By Latch Dist rather than Clearance Height, because Latch Dist is on the shared
            // panel and so is visible from this tab. Clearance Height only appears on the Corner
            // and Center tabs, which had an invisible field driving visible motion.
            _communicationManager.SendCommand("G91");
            _communicationManager.SendCommand($"G0Z{LatchDistance.ToInvariantString("F3")}");
            _communicationManager.SendCommand("G90");

            ProbeStatus = $"Z set. Offset: {zOffset.ToInvariantString("F3")}";
        }

        // ========== Probe Corner ==========
        private void StartProbeCorner()
        {
            if (IsProbing) return;
            // Enforced here too, so the rule does not rely on the view's IsEnabled.
            if (!CanProbe) return;
            if (!FieldsValid(CornerFields())) return;
            _probeFailureMessage = "Probe failed — no contact, corner not set";

            // Where the operator left the stylus. Every approach move is planned absolutely
            // from here, so the legs cannot drift lower as they go, and it is where the cycle
            // returns to at the end. Already in display units, which is what the sequence
            // sends in — see MachinePosition.
            _cornerStart = MachinePosition();
            if (_cornerStart == null)
            {
                ProbeStatus = "No machine position reported yet — cannot plan the approach";
                return;
            }

            ClearResults();
            ProbeStatus = $"Probing corner ({SelectedCorner})...";

            _probeJob = CreateJobBuilder();
            _phases = _probeJob.ProbeCorner(SelectedCorner, IncludeZInCorner,
                _cornerStart[0], _cornerStart[1], _cornerStart[2]);
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

            // The edge, in machine coordinates, from where each probe actually triggered.
            //
            // G10 L2 rather than L20, because L20 works from wherever the machine happens to
            // be standing and by now it is standing nowhere useful: each leg lifts, steps
            // clear and drops, so it is a lift and two lateral moves away from the X edge it
            // measured. The reported positions are the only record of where the edges were.
            //
            // Converted on the way in: PRB follows $13, while these go out under the
            // sequence's G20/G21, and the radius they are combined with came from the UI in
            // display units. See ToSequenceUnits.
            var xEdge = ToSequenceUnits(_phaseResults[phaseOffset].XOffset.StringToDouble())
                        + xSign * ProbeDiameter / 2.0;
            var yEdge = ToSequenceUnits(_phaseResults[phaseOffset + 1].YOffset.StringToDouble())
                        + ySign * ProbeDiameter / 2.0;

            ProbeXResult = xEdge.ToInvariantString("F3");
            ProbeYResult = yEdge.ToInvariantString("F3");

            _communicationManager.SendCommand("G90");
            var cmd = "G10L2P0";
            cmd += $"X{xEdge.ToInvariantString("F3")}";
            cmd += $"Y{yEdge.ToInvariantString("F3")}";

            if (IncludeZInCorner)
            {
                // Same treatment for Z: the top face sits below where the probe triggered by
                // the plate thickness, or by the stylus radius on a 3D probe.
                var zSurface = ToSequenceUnits(_phaseResults[0].ZOffset.StringToDouble())
                               - _probeJob.CalculateZOffset();
                cmd += $"Z{zSurface.ToInvariantString("F3")}";
                ProbeZResult = zSurface.ToInvariantString("F3");
            }

            _communicationManager.SendCommand(cmd);

            // Lift clear, then stand over the corner just measured. Stopping on the last
            // contact leaves the stylus pressed against the front face with the operator none
            // the wiser whether the datum took — and X0 Y0 is now that corner, since the G10
            // above put the work origin there.
            if (_cornerStart != null)
            {
                var safeZ = (_cornerStart[2] + ClearanceHeight).ToInvariantString("F3");
                _communicationManager.SendCommand($"G53G0Z{safeZ}");
                _communicationManager.SendCommand("G0X0Y0");
            }

            ProbeStatus = $"Corner set. X:{xEdge.ToInvariantString("F3")} Y:{yEdge.ToInvariantString("F3")}";
        }

        // ========== Probe Center ==========
        private void StartProbeCenter()
        {
            if (IsProbing) return;
            // Enforced here too, so the rule does not rely on the view's IsEnabled.
            if (!CanProbe) return;
            if (!FieldsValid(CenterFields())) return;
            _probeFailureMessage = "Probe failed — no contact, centre not set";

            // Captured before anything moves: the inside cycle returns here by machine
            // coordinate between phases rather than stepping back blindly, and both cycles
            // climb back to this height before crossing to the centre. The point is known to
            // be clear because the machine is standing on it. Already in display units, which
            // is what the sequence sends in — see MachinePosition.
            _centerStart = MachinePosition();
            if (_centerStart == null)
            {
                ProbeStatus = "No machine position reported yet — cannot plan the moves";
                return;
            }

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
                _phases = _probeJob.ProbeInsideCenter(_centerStart[0], _centerStart[1]);
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

            // Machine coordinates, converted into the unit this sequence is sending in: PRB
            // follows $13 and these are about to be combined with a UI diameter and sent as
            // coordinates. Left unconverted, a metric machine on an imperial display puts the
            // centre out by 25.4x — the same fault that sent the G59.3 return to the home
            // corner.
            var xPos = ToSequenceUnits(xPosResult.XOffset.StringToDouble());
            var xNeg = ToSequenceUnits(xNegResult.XOffset.StringToDouble());
            var yPos = ToSequenceUnits(yPosResult.YOffset.StringToDouble());
            var yNeg = ToSequenceUnits(yNegResult.YOffset.StringToDouble());

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

            _communicationManager.SendCommand("G90");

            // Climb back to the starting height before crossing to the centre. On a boss the
            // last probe leaves the stylus alongside the feature and below its top face, so
            // going straight across drives into it. A bore got away with it because that same
            // position is inside the hole.
            if (_centerStart != null)
            {
                _communicationManager.SendCommand(
                    $"G53G0Z{_centerStart[2].ToInvariantString("F3")}");
            }

            // Then to the computed centre, and call it X0 Y0.
            //
            // G53 on the move, because the centre was averaged from PRB and so is a machine
            // coordinate. Sent without it, G0 reads those numbers in the active work system —
            // which is offset by however far work zero sits from machine zero, so the machine
            // rapids somewhere else entirely and then that spot gets stamped as the origin.
            _communicationManager.SendCommand(
                $"G53G0X{centerX.ToInvariantString("F3")}Y{centerY.ToInvariantString("F3")}");
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

        /// <summary>
        /// Files a probe report against the phase that produced it, keeping one entry per phase
        /// — that phase's last result, which is the latch pass and the accurate one.
        /// <para>
        /// Every <c>ProbeSingleAxis</c> probes twice, search then latch, so a phase reports two
        /// <c>[PRB:]</c> lines. Appending them all left the handlers reading one phase's latch
        /// as the next phase's result. The centre finder averaged the X+ search against the X+
        /// latch, called the midpoint of those two the centre of the bore — which is the +X
        /// wall — and rapided into it. The corner took its Y datum off the X phase the same way.
        /// </para>
        /// </summary>
        internal static void RecordPhaseResult(List<ProbeState> results, int phaseIndex,
            ProbeState result)
        {
            while (results.Count <= phaseIndex)
                results.Add(result);

            results[phaseIndex] = result;
        }

        private void OnProbeResult(object sender, ProbeState e)
        {
            RecordPhaseResult(_phaseResults, _phaseIndex, e);

            // Stop the whole sequence on the first miss. G38.3 does not error when it fails to
            // make contact, and nothing else here was watching, so the remaining phases went on
            // running against a position that meant nothing — dead reckoning off a touch that
            // never happened. The completion handlers do refuse to write an offset from a bad
            // result, but by then the machine has already made the moves.
            //
            // grblHAL sends [PRB:...] ahead of the "ok" for the probe line, so aborting here
            // unsubscribes before the ack that would have sent the next command.
            if (!e.ProbeSuccessful)
                AbortSequence(_probeFailureMessage);
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
            RestoreModalUnits();

            ProbeStatus = status;
        }

        private void CompleteAllPhases()
        {
            _communicationManager.OnCommandAck -= OnCommandAck;
            _communicationManager.OnProbeResults -= OnProbeResult;
            IsProbing = false;

            // Absolute mode first: the completion handlers all assume it.
            _communicationManager.SendCommand("G90");

            _onAllPhasesComplete?.Invoke();

            // The unit goes back last, after the handler has had its say. Those handlers
            // emit numbers computed in the sequence's unit — the tool reference return
            // move, the Z work offset, the move to a computed center — so restoring first
            // has the controller read every one of them in the wrong unit. That is what
            // sent the G59.3 return to the home corner: X -1.360in went out behind a
            // restored G21 and was taken as -1.360mm. The Z leg hid it, because its return
            // is G53G0Z0 and zero is zero in either unit.
            RestoreModalUnits();
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
