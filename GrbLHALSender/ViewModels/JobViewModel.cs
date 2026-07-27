using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gcode;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels
{
    public partial class JobViewModel : ViewModelBase
    {

        private readonly CommunicationManager _commsManager;
        private readonly MachineStateService _machineStateService;
        private readonly GcodeEventInjector _eventInjector;

        private bool _showGCodeConsole;
        private int _gCodeFileIndex;
        private int _index = 0;
        private bool _fileLoaded;
        private string _fileName;
        private ToolpathData? _toolpathData;
        private string _estimatedTime;
        private string _runTime;
        private bool _jobRunning;
        private CancellationTokenSource? _cancelToken;
        private readonly DispatcherTimer _jobTimer;
        private DateTime _startTime;
        private bool _canHoldJob;


        // Character-counting streaming protocol:
        // grblHAL reports its serial RX buffer size via $I+ (OPT line).
        // StreamAccounting tracks how many bytes are "in-flight" (sent but not yet
        // acked) and which outstanding command each "ok" belongs to — including
        // commands sent from outside the streamer, which occupy the same buffer.
        // Streaming is EVENT-DRIVEN: OnCommandAck directly calls FillBuffer().
        private const int DefaultRxBufferSize = 128;
        private readonly StreamAccounting _accounting = new();
        private int _pendingLine;  // Job lines acknowledged so far
        private string _holdButtonText;
        private JobState _jobState;
        private bool _connected;
        private int _bufferPercentage = 40;

        // Throttled GcodeFileIndex: store latest value, push to UI on a timer
        private volatile int _latestFileIndex;
        private volatile int _latestPendingLine;
        private DispatcherTimer? _fileIndexTimer;
        private int _completedSegmentIndex = -1;
        private string _selectedLineInfo = "";
        private string _jobError = "";
        private string _controllerMessage = "";
        // Last state grblHAL reported, so the tool-change ack can fire on the transition
        // into Tool rather than on every status report while it lasts.
        private string _lastMachineState = "";
        // Ordinal of the job line carrying an unanswered M6, or 0 when none is outstanding.
        private int _toolChangeLine;
        // Streaming is held at a tool change until the controller has finished it.
        private readonly ToolChangeBarrier _toolChange = new();
        // Reported once per job so a persistent mismatch cannot flood the console.
        private bool _unmatchedAckReported;

        /// <summary>
        /// Streaming diagnostics worth putting in front of the operator. Raised on the
        /// comms thread, so subscribers must marshal to the UI themselves.
        /// </summary>
        public event EventHandler<string>? DiagnosticMessage;
        private int _ackedLineIndex;



        // Fed from MachineStateService via PropertyChanged subscription
        private bool _canStartJob;
        private Point3D? _workCoordinateOffset;
        private Point3D? _currentSpindlePosition;
        // Set by MainViewModel — references config object so changes take effect immediately
        internal GHalSenderConfig? Config;
        private bool _toolChangeVisible;
        private bool _toolChangeNeedsTouchOff;
        private bool _toolReferenceSet;


        public IReadOnlyList<IStorageFile>? SelectedFiles { get; set; }
        public ObservableCollection<GCodeLine> GCodeOutPut { get; set; }
        public Core.Interaction<string, IReadOnlyList<IStorageFile>?> SelectFilesInteraction { get; } = new();
        public JobState JobState
        {
            get => _jobState;
            set
            {
                this.RaiseAndSetIfChanged(ref _jobState, value);
                UpdateButtonStates();
            }
        }

        public bool Connected
        {
            get => _connected;
            set
            {
                this.RaiseAndSetIfChanged(ref _connected, value);
                UpdateButtonStates();
            }
        }
        
        public bool FileLoaded
        {
            get => _fileLoaded;
            set
            {
                this.RaiseAndSetIfChanged(ref _fileLoaded, value);
                UpdateButtonStates();
            }
        }

        public bool ShowGCodeConsole
        {
            get => _showGCodeConsole;
            set => this.RaiseAndSetIfChanged(ref _showGCodeConsole, value);
        }

        public int GcodeFileIndex
        {
            get => _gCodeFileIndex;
            set => this.RaiseAndSetIfChanged(ref _gCodeFileIndex, value);
        }

        // Number of lines (1-based count) that have been ack'd by grblHAL.
        // Lines with 1-based number <= AckedLineIndex are acknowledged.
        public int AckedLineIndex
        {
            get => _ackedLineIndex;
            set => this.RaiseAndSetIfChanged(ref _ackedLineIndex, value);
        }

        // Segment the user has selected (yellow highlight in 3D view).
        // -1 = nothing selected. Two-way bound to both render controls.
        private int _selectedSegmentIndex = -1;
        public int SelectedSegmentIndex
        {
            get => _selectedSegmentIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedSegmentIndex, value);
        }

        /// <summary>
        /// Selects the toolpath segment produced by the given 0-based gcode
        /// line. Called from the gcode text view when the user clicks a line,
        /// so the 3D model can highlight the matching segment in yellow.
        /// </summary>
        public void SelectGcodeLine(int lineIndex)
        {
            var toolpath = _toolpathData;
            if (toolpath?.LineToFirstSegment == null
                || lineIndex < 0
                || lineIndex >= toolpath.LineToFirstSegment.Length)
            {
                SelectedSegmentIndex = -1;
                SelectedLineInfo = "";
                return;
            }

            int segIdx = toolpath.LineToFirstSegment[lineIndex];

            // Lines that produce no segments share the next line's first segment.
            // Only count it as a real match if the segment actually originated
            // from this line.
            if (segIdx < 0 || segIdx >= toolpath.Segments.Count ||
                toolpath.Segments[segIdx].SourceLineIndex != lineIndex)
            {
                SelectedSegmentIndex = -1;
                SelectedLineInfo = "";
                return;
            }

            SelectedSegmentIndex = segIdx;
            SelectedLineInfo = $"Line: {lineIndex + 1}";
        }

        public string FileName
        {
            get => _fileName;
            set => this.RaiseAndSetIfChanged(ref _fileName, value);
        }

        public ToolpathData? ToolpathData
        {
            get => _toolpathData;
            set => this.RaiseAndSetIfChanged(ref _toolpathData, value);
        }

        public string EstimatedTime
        {
            get => _estimatedTime;
            set => this.RaiseAndSetIfChanged(ref _estimatedTime, value);
        }

        public string RunTime
        {
            get => _runTime;
            set => this.RaiseAndSetIfChanged(ref _runTime, value);
        }

        public bool JobRunning
        {
            get => _jobRunning;
            set
            {
                this.RaiseAndSetIfChanged(ref _jobRunning, value);
                UpdateButtonStates();
            }
        }

        public int CompletedSegmentIndex
        {
            get => _completedSegmentIndex;
            set => this.RaiseAndSetIfChanged(ref _completedSegmentIndex, value);
        }

        public string SelectedLineInfo
        {
            get => _selectedLineInfo;
            set => this.RaiseAndSetIfChanged(ref _selectedLineInfo, value);
        }

        /// <summary>
        /// Why the job stopped, when it stopped for a reason the operator would not
        /// otherwise see. Empty at all other times.
        /// </summary>
        public string JobError
        {
            get => _jobError;
            set => this.RaiseAndSetIfChanged(ref _jobError, value);
        }

        /// <summary>
        /// The last thing the controller said in words during this job.
        /// <para>
        /// Kept until the next job starts rather than cleared on resume: a tool change
        /// macro that stops on "tool 3 failed zone 1, manually unload and unlock to
        /// continue" is exactly the message the operator still needs to be reading after
        /// they have dealt with it.
        /// </para>
        /// </summary>
        public string ControllerMessage
        {
            get => _controllerMessage;
            set => this.RaiseAndSetIfChanged(ref _controllerMessage, value);
        }

        public bool CanHoldJob
        {
            get => _canHoldJob;
            set => this.RaiseAndSetIfChanged(ref _canHoldJob, value);
        }

        public bool CanStartJob
        {
            get => _canStartJob;
            set => this.RaiseAndSetIfChanged(ref _canStartJob, value);
        }

        public Point3D? WorkCoordinateOffset
        {
            get => _workCoordinateOffset;
            set => this.RaiseAndSetIfChanged(ref _workCoordinateOffset, value);
        }

        public Point3D? CurrentSpindlePosition
        {
            get => _currentSpindlePosition;
            set => this.RaiseAndSetIfChanged(ref _currentSpindlePosition, value);
        }
     
        public ICommand StartJobCommand { get; }
        public ICommand CloseGCodeConsole { get; }
        public ICommand OpenGCodePanel { get; }
        public ICommand CloseFilesCommand { get; }
        public ICommand PauseJobCommand { get; }
        public ICommand StopJobCommand { get; }
        public ICommand TouchOffCommand { get; }
        public ICommand SetToolReferenceCommand { get; }
        /// <summary>
        /// Reacts to MachineStateService property changes (fires on UI thread at ~10 Hz).
        /// Feeds Connected, GrblState, and SpindlePosition from the centralized service.
        /// </summary>
        

        private void UpdateButtonStates()
        {
            // Hold: enabled when connected, disabled when already in Hold state
            CanHoldJob = Connected &&
                         JobState is not JobState.Hold;

            // Start: enabled when:
            //   - Machine is in Hold or Tool state (resume, no file required)
            //   - File loaded and no job running (idle, program complete, or stopped)
            CanStartJob = JobState is JobState.Hold or JobState.Tool ||
                          (FileLoaded && !JobRunning &&
                           JobState is (JobState.Idle or JobState.ProgramComplete or JobState.Stop));

            ToolChangeVisible = JobState == JobState.Tool;
            this.RaisePropertyChanged(nameof(TouchOffVisible));
        }

        public bool ToolChangeVisible
        {
            get => _toolChangeVisible;
            set => this.RaiseAndSetIfChanged(ref _toolChangeVisible, value);
        }

        /// <summary>
        /// Whether this machine's tool change mode expects the operator to touch the new
        /// tool off. Pushed in by MainViewModel from $341: true for modes 1 and 2 only —
        /// mode 3 probes by itself, and modes 0 and 4 have no touch-off step.
        /// </summary>
        public bool ToolChangeNeedsTouchOff
        {
            get => _toolChangeNeedsTouchOff;
            set
            {
                this.RaiseAndSetIfChanged(ref _toolChangeNeedsTouchOff, value);
                this.RaisePropertyChanged(nameof(TouchOffVisible));
            }
        }

        /// <summary>Shown only while a job is paused at a tool change that needs one.</summary>
        public bool TouchOffVisible => ToolChangeVisible && ToolChangeNeedsTouchOff;

        /// <summary>
        /// Whether the controller currently holds a tool length reference, from the TLR
        /// field of the status report. Colours the Set Ref control rather than hiding it:
        /// a reference can legitimately be re-established — after moving the tool setter,
        /// or re-zeroing with a different tool — so the action stays available, and the
        /// colour says whether it is still needed.
        /// </summary>
        public bool ToolReferenceSet
        {
            get => _toolReferenceSet;
            set => this.RaiseAndSetIfChanged(ref _toolReferenceSet, value);
        }

        public JobViewModel(CommunicationManager manager, MachineStateService machineStateService,
            GcodeEventInjector eventInjector)
        {
            _commsManager = manager;
            _machineStateService = machineStateService;
            _eventInjector = eventInjector;
            _machineStateService.PropertyChanged += OnMachineStateChanged;
            GCodeOutPut = new ObservableCollection<GCodeLine>();
            CloseGCodeConsole = ReactiveCommand.Create(CloseGcodeConsole);
            OpenGCodePanel = ReactiveCommand.Create(GCodeControl);
            StartJobCommand = ReactiveCommand.Create(StartJob);
            CloseFilesCommand = ReactiveCommand.Create(CloseFile);
            PauseJobCommand = ReactiveCommand.Create(TogglePause);
            StopJobCommand = ReactiveCommand.Create(StopJob);
            TouchOffCommand = ReactiveCommand.Create(TouchOff);
            SetToolReferenceCommand = ReactiveCommand.Create(SetToolReference);
            RunTime = "00:00:00";
            _jobTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (sender, args) =>
            {
                RunTime = (DateTime.Now - _startTime).ToString(@"hh\:mm\:ss");
            });
            _jobTimer.Stop();
        }

        [RelayCommand]
        private async Task SelectFilesAsync()
        {
            SelectedFiles = await SelectFilesInteraction.HandleAsync("Choose .NC File");
            var selectFile = SelectedFiles;
            if (selectFile?.Count <= 0) return;
            FileName = SelectedFiles[0]?.Name;
            var file = new GCodeParser();
            // LocalPath (not AbsolutePath) — AbsolutePath percent-encodes spaces ("%20"),
            // which made loads of files in paths with spaces fail with FileNotFound.
            file.ParseGCodeFile(SelectedFiles[0].Path.LocalPath, FileComplete, OnFileLoadFailed);
        }

        /// <summary>
        /// Called from the parser's background task when a file fails to load or parse.
        /// Without this the exception was silently swallowed and the Start button
        /// simply never enabled, with no feedback to the user.
        /// </summary>
        private void OnFileLoadFailed(Exception ex)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                FileLoaded = false;
                EstimatedTime = string.Empty;
                FileName = $"Load failed: {ex.Message}";
            });
        }

        public void FileComplete(List<GCodeLine> gCodeJob)
        {
            // Inject configured pre/post event commands here, before the toolpath is
            // built and before the lines reach GCodeOutPut. Doing it at load rather
            // than mid-stream keeps one line list behind everything downstream — the
            // character-counting streamer, the gcode view and the progress mapping all
            // see exactly what gets sent. Rules edited after a load apply on next load.
            gCodeJob = _eventInjector.ApplyToJob(gCodeJob);

            var builder = new ToolpathBuilder();

            // Use machine rapid rates in display units (mm or inches depending on $13)
            // $110/$111/$112 are always stored in mm; Display* converts to inches when $13=1
            var machine = _commsManager.MachineData;
            if (machine != null)
            {
                builder.DisplayIsMetric = machine.ReportInMetric;
                var rapids = new[] { machine.DisplayXRapid, machine.DisplayYRapid, machine.DisplayZRapid };
                var validRapids = rapids.Where(r => r > 0).ToArray();
                if (validRapids.Length > 0)
                    builder.RapidRate = (float)validRapids.Min();
            }

            var toolpath = builder.BuildToolpath(gCodeJob);

            Dispatcher.UIThread.Invoke((() =>
            {
                GCodeOutPut.Clear();
                GCodeOutPut.AddRange(gCodeJob);
                GcodeFileIndex = 0;
                CompletedSegmentIndex = -1;
                FileLoaded = true;
                ToolpathData = toolpath;
                EstimatedTime = FormatTimeEstimate(toolpath.TimeEstimateSeconds);
            }));
        }

        private void OnMachineStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MachineStateService.Connected):
                    Connected = _machineStateService.Connected;
                    break;
                case nameof(MachineStateService.GrblState):
                    if (!JobRunning)
                    {
                        JobState = _machineStateService.GrblState switch
                        {
                            GrblState.Idle => JobState.Idle,
                            GrblState.Alarm => JobState.Alarm,
                            GrblState.Hold => JobState.Hold,
                            GrblState.Tool => JobState.Tool,
                            _ => JobState
                        };
                    }
                    break;
                case nameof(MachineStateService.TLR):
                    ToolReferenceSet = _machineStateService.TLR;
                    break;
                case nameof(MachineStateService.SpindlePosition):
                    CurrentSpindlePosition = _machineStateService.SpindlePosition;
                    break;
                case nameof(MachineStateService.WorkCoordinateOffset):
                    WorkCoordinateOffset = _machineStateService.WorkCoordinateOffset;
                    break;
            }
        }
        public void StartJob()
        {
            if (JobState == JobState.Hold && !JobRunning )
            {
                _commsManager.Adapter?.WriteByte(GrblHalConstants.CycleStart);
                return;
            }
            if (JobRunning && JobState is JobState.Hold or JobState.Tool)
            {
                ResumeJob();
                return;
            }
            if (JobRunning) return;
            if (GCodeOutPut.Count == 0) return;
            _startTime = DateTime.Now;
            _jobTimer.Start();
            _index = 0;
            _pendingLine = 0;
            _latestPendingLine = 0;
            CompletedSegmentIndex = -1;
            JobError = "";
            ControllerMessage = "";
            _lastMachineState = "";
            _toolChangeLine = 0;
            _toolChange.Reset();
            _unmatchedAckReported = false;
            _accounting.Reset();

            // Use the real RX buffer size from the controller if available
            var reportedRxSize = _commsManager.Options?.RxBufferSize ?? DefaultRxBufferSize;
            var precentBuffer = reportedRxSize * _bufferPercentage / 100;
            _accounting.Capacity = Math.Max(precentBuffer, DefaultRxBufferSize);
            JobState = JobState.Start;

            // Dispose previous token if any
            _cancelToken?.Dispose();
            _cancelToken = new CancellationTokenSource();

            ListenToState(true);
            JobRunning = true;

            // Start throttled UI update for file index (~5 Hz)
            _fileIndexTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _fileIndexTimer.Tick += FileIndexTimerTick;
            _fileIndexTimer.Start();

            // Pre-fill the buffer — event-driven from here on
            // (each "ok" ack calls FillBuffer to keep the buffer topped off)
            FillBuffer();
        }

        private void StopJob()
        {
            if (!JobRunning) return;

            // Send feed hold first to decelerate, then reset
            _commsManager.Adapter?.WriteByte(GrblHalConstants.FeedHold);
            _commsManager.Adapter?.WriteByte(GrblHalConstants.GrblReset);

            CancelAndCleanup(JobState.Stop);
        }

        private void PauseJob()
        {
            // Don't set JobState here — let _commsManager_OnStateReceived
            // update it to Hold when grblHAL actually confirms the hold
            _commsManager.Adapter?.WriteByte(GrblHalConstants.FeedHold);
        }

        /// <summary>
        /// Probes the newly fitted tool and applies the offset, the step grblHAL's manual
        /// tool change modes require before cycle start. $TPW measures against the last
        /// probe rather than an absolute surface, so touch plate thickness plays no part —
        /// but it does mean a reference has to have been established with the first tool,
        /// or the very first change of a session applies a delta against nothing.
        /// </summary>
        private void TouchOff()
        {
            // Only meaningful while the controller is waiting at a tool change, and only
            // in the modes that implement the command.
            if (JobState != JobState.Tool || !ToolChangeNeedsTouchOff) return;

            _commsManager.SendCommand(GrblHalConstants.ToolProbeWorkpiece);
        }

        /// <summary>
        /// Captures the probe just taken as the tool length reference that $TPW measures
        /// against. Needed once, on the first tool change of a session, because $TPW
        /// applies a difference and a difference needs a baseline.
        /// <para>
        /// Order matters: this must follow a successful probe. Issued before one, or after
        /// a failed one, grblHAL clears the reference instead of setting it.
        /// </para>
        /// </summary>
        private void SetToolReference()
        {
            if (JobState != JobState.Tool || !ToolChangeNeedsTouchOff) return;

            // Refused once the controller holds a reference. Sending it again re-bases the
            // datum onto whatever was last probed — silently, with no error — so a second
            // press after touching off a later tool would shift work Z zero by the
            // difference between the tools. Deliberate re-establishing goes through MDI.
            if (ToolReferenceSet) return;

            _commsManager.SendCommand(GrblHalConstants.ToolLengthReference);
        }

        private void ResumeJob()
        {
            // Note that a cycle start was issued for an outstanding tool change: it is
            // what triggers the controller's restore move, and only once that finishes is
            // the change actually over.
            _toolChange.CycleStartIssued();

            // Let _commsManager_OnStateReceived update to Running,
            // then refill the buffer in case acks arrived while paused
            _commsManager.Adapter?.WriteByte(GrblHalConstants.CycleStart);
            FillBuffer();
        }
        private void TogglePause()
        {
            if (JobState == JobState.Hold) return;
            PauseJob();
        }

        private void CloseFile()
        {
            RunTime = "00:00:00";
            if (JobRunning) StopJob();
            GCodeOutPut.Clear();
            FileLoaded = false;
            FileName = string.Empty;
            ToolpathData = null;
            CompletedSegmentIndex = -1;
            SelectedLineInfo = "";
            JobError = "";
            ControllerMessage = "";
            EstimatedTime = string.Empty;
            AckedLineIndex = 0;
            if (ShowGCodeConsole) ShowGCodeConsole = false;
        }

        private void ListenToState(bool subscribe)
        {
            if (subscribe)
            {
                _commsManager.OnStateReceived += _commsManager_OnStateReceived;
                _commsManager.OnCommandAck += _commsManager_OnCommandAck;
                _commsManager.OnCommandSent += _commsManager_OnCommandSent;
                _commsManager.OnControllerMessage += _commsManager_OnControllerMessage;
                // Claim the link for the duration. Tied to the ack subscription because
                // the two cover exactly the same window: while we are counting acks,
                // nothing else may send a command and consume one.
                _commsManager.BeginStreaming();
            }
            else
            {
                _commsManager.OnStateReceived -= _commsManager_OnStateReceived;
                _commsManager.OnCommandAck -= _commsManager_OnCommandAck;
                _commsManager.OnCommandSent -= _commsManager_OnCommandSent;
                _commsManager.OnControllerMessage -= _commsManager_OnControllerMessage;
                _commsManager.EndStreaming();
            }
        }

        private void _commsManager_OnStateReceived(object? sender, RealTImeState e)
        {
            var state = e.GrblHalState;
            var previousState = JobState;
            var previousMachineState = _lastMachineState;
            _lastMachineState = state;

            JobState = MapGrblState(state, JobState, JobRunning);

            if (JobRunning)
                UpdateToolChangeBarrier(state);

            // Alarm during job — abort
            if (JobState == JobState.Alarm && JobRunning)
            {
                CancelAndCleanup(JobState.Alarm);
            }

            // Tool change — acknowledge once, on the transition into Tool. grblHAL's
            // protocol is to send 0xA3 as soon as the state changes to Tool; it
            // acknowledges the event and does not resume the program, so repeating it at
            // poll rate for the length of the pause achieves nothing and puts needless
            // traffic in front of the operator's touch off.
            if (state == "Tool" && previousMachineState != "Tool")
            {
                _commsManager.Adapter?.WriteByte(GrblHalConstants.ToolAck);
            }

            // Resumed from Hold or Tool — refill the buffer to restart sending
            if (JobState == JobState.Running && JobRunning &&
                previousState is JobState.Hold or JobState.Tool)
            {
                FillBuffer();
            }
        }

        /// <summary>
        /// Maps grblHAL's reported state onto the job's own state.
        /// <para>
        /// Idle deliberately does not clear a tool change or a hold while a job is
        /// running. grblHAL reports Jog while the operator jogs to touch off and Idle once
        /// that jog finishes, and neither means the program resumed — it resumes only on
        /// cycle start, which shows up as Run. Treating Idle as a resume unlatched the
        /// pause: the TOOL banner disappeared and, worse, the streamer stopped holding
        /// back and emptied the rest of the file into the controller, which acknowledged
        /// each line as it buffered it. The machine sat waiting for cycle start while the
        /// file index ran to the end, with no error anywhere.
        /// </para>
        /// </summary>
        internal static JobState MapGrblState(string grblState, JobState current, bool jobRunning) =>
            grblState switch
            {
                "Hold" => JobState.Hold,
                "Tool" => JobState.Tool,
                "Run" => JobState.Running,
                "Alarm" => JobState.Alarm,
                "Home" => JobState.Running,
                "Door" => JobState.Hold,
                "Idle" => jobRunning && current is JobState.Tool or JobState.Hold
                    ? current
                    : JobState.Idle,
                // Jog, Check, Sleep and anything unrecognised leave the job state alone.
                _ => current
            };

        /// <summary>
        /// Records commands the streamer did not send — a jog during a tool change, an
        /// aux output button, a macro, an injected event command. They sit in the same
        /// controller RX buffer as our lines and each produces its own "ok", so leaving
        /// them out of the accounting made that "ok" advance the file index and free
        /// buffer room that was not actually free.
        /// </summary>
        private void _commsManager_OnCommandSent(object? sender, CommandSentEventArgs e)
        {
            if (!JobRunning) return;

            var cost = StreamAccounting.CostOf(e.Command);

            // A single character is written as a raw byte by the adapters, and grblHAL
            // answers realtime bytes with no "ok" — recording one would leave an entry at
            // the head of the queue that never clears, swallowing a real line's ack.
            if (!e.IsStreamLine && e.Command.Length <= 1) return;

            _accounting.RecordSent(cost, e.IsStreamLine);
        }

        /// <summary>
        /// Shows what the controller said while a job is running. This is the only place a
        /// macro's <c>(debug, ...)</c> text becomes visible without the console panel open,
        /// and the macro failure branches that end in M0 depend on the operator reading it.
        /// </summary>
        private void _commsManager_OnControllerMessage(object? sender, string message)
        {
            // Program end adds nothing the job panel does not already show, and letting it
            // through would overwrite the message explaining why a run stopped.
            if (message.StartsWith("Pgm End", StringComparison.OrdinalIgnoreCase))
                return;

            Dispatcher.UIThread.Post(() => ControllerMessage = message);
        }

        private void _commsManager_OnCommandAck(object? sender, CommandAck e)
        {
            if (!JobRunning) return;

            if (JobState is JobState.Start)
                JobState = JobState.Running;

            // Credit the response to the oldest outstanding command, whatever sent it.
            // "ok" and "error:N" both end a command and both free its bytes, so both
            // must be credited — an error that skipped this left the queue head stuck
            // forever, which put every later response one entry out of step and left the
            // job unable to reach its own end.
            var acked = _accounting.Ack();

            // Nothing outstanding: a response we have no record for. Ignoring it is the
            // point — crediting it to a job line is what walked the file index forward
            // during a tool change and ended jobs early. But it should never happen, and
            // it is the one visible symptom of the queue being out of step with the
            // controller, so say so once rather than swallow it silently.
            if (acked == StreamAccounting.AckKind.Unrecorded)
            {
                if (!_unmatchedAckReported)
                {
                    _unmatchedAckReported = true;
                    DiagnosticMessage?.Invoke(this,
                        "response received with no command outstanding — " +
                        "stream accounting may be out of step with the controller");
                }
                return;
            }

            if (e.IsError)
            {
                // A rejected line means the controller did not run the program that is in
                // the file. Carrying on would cut a toolpath with a hole in it, so stop.
                if (acked == StreamAccounting.AckKind.JobLine)
                {
                    Dispatcher.UIThread.Post(() => AbortOnLineError(e.ErrorCode));
                    return;
                }

                // A rejected manual command — a jog past a soft limit, say — is not the
                // job's problem. The queue is straight again, so carry on below.
            }
            else if (acked == StreamAccounting.AckKind.JobLine)
            {
                _pendingLine = _accounting.AckedJobLines;
                _latestPendingLine = _pendingLine;

                // Check for job completion: all lines sent AND all acks received
                if (_pendingLine >= GCodeOutPut.Count && _accounting.AckPending == 0)
                {
                    Dispatcher.UIThread.Post(() => JobComplete());
                    return;
                }
            }

            // Don't send more while paused or in tool change
            if (JobState is JobState.Hold or JobState.Tool)
                return;

            // Event-driven: immediately refill the buffer. A foreign command's ack also
            // frees room, so this runs for those too.
            FillBuffer();

        }

        /// <summary>
        /// Stops the job because the controller rejected one of its lines. Uses the same
        /// hold-then-reset path as the Stop button, and reports the code so the operator
        /// is not left guessing why the job ended.
        /// </summary>
        private void AbortOnLineError(int errorCode)
        {
            JobError = $"Job stopped: controller rejected a line (error:{errorCode})";
            StopJob();
        }

        /// <summary>
        /// Sends as many queued lines as will fit in grblHAL's serial RX buffer.
        /// Each line costs text.Length + 1 byte (\r terminator).
        /// Called from StartJob (pre-fill) and from OnCommandAck (event-driven refill).
        /// </summary>
        private void FillBuffer()
        {
            // Lock-step mode: only one line in flight at a time. Wait until the
            // previous line is ack'd before sending the next. This keeps the
            // displayed gcode line tightly aligned with actual machine motion.
            bool bufferAhead = Config?.StreamBufferAhead ?? true;
            if (!bufferAhead && _accounting.AckPending > 0)
                return;

            // A tool change is a barrier: nothing may go out until it clears.
            if (_toolChange.IsUp)
                return;

            while (_index < GCodeOutPut.Count)
            {
                var line = GCodeOutPut[_index].Text;

                // Skip empty/comment lines — still send "()" so grblHAL acks them
                if (string.IsNullOrEmpty(line))
                {
                    _index++;
                    continue;
                }

                // Will this line fit in the remaining RX buffer space? Commands sent
                // from elsewhere count against the same budget, so a burst of them
                // holds the stream here until they are acked.
                if (!_accounting.HasRoomFor(StreamAccounting.CostOf(line)))
                    break; // No room — wait for an ack to free space

                // Send the line. The accounting is recorded by OnCommandSent, which
                // fires inside this call, so the loop's next check sees fresh state.
                _commsManager.SendStreamLine(line);

                _latestFileIndex = _index;
                _index++;

                // Stop dead on a tool change. grblHAL suspends the program at M6 and
                // rejects any further g-code with error:40 ("command not allowed while a
                // tool change is pending") — so anything already sitting in its receive
                // buffer past this line is thrown away, not queued. On a short file the
                // whole remainder fitted in one fill and every line of it was rejected.
                //
                // The M6 line's own "ok" is NOT the signal to resume. In tool_change.c,
                // tool_change() sets the pending flag and returns, so the acknowledgement
                // arrives when the change *starts*. The flag is cleared much later, at the
                // end of the restore move that cycle start triggers. See the state handler
                // for what actually lifts this.
                if (GcodeWords.IsToolChange(line))
                {
                    _toolChangeLine = _accounting.AckedJobLines + _accounting.AckPending;
                    _toolChange.ToolChangeSent();
                    break;
                }

                // Lock-step mode: stop after the first line sent this call.
                if (!bufferAhead)
                    break;
            }
        }

        /// <summary>
        /// Feeds a status report to the tool-change barrier and resumes streaming on the
        /// report that lifts it. See ToolChangeBarrier for why no acknowledgement can
        /// serve as the signal.
        /// </summary>
        private void UpdateToolChangeBarrier(string machineState)
        {
            var lifted = _toolChange.Update(
                machineState,
                toolChangeLineAcked: _toolChangeLine > 0 &&
                                     _accounting.AckedJobLines >= _toolChangeLine);

            if (!lifted) return;

            _toolChangeLine = 0;
            FillBuffer();
        }

        private void JobComplete()
        {
            // Don't send Stop/Reset — let the motion buffer finish executing
            CancelAndCleanup(JobState.ProgramComplete);
        }

        /// <summary>
        /// Central cleanup for all job end scenarios (complete, stop, alarm).
        /// </summary>
        private void CancelAndCleanup(JobState finalState)
        {
            // Unsubscribe FIRST to prevent any more ack events from firing FillBuffer
            ListenToState(false);
            _jobTimer.Stop();
            _fileIndexTimer?.Stop();
            _fileIndexTimer = null;
            _cancelToken?.Cancel();
            JobState = finalState;
            _index = 0;
            _pendingLine = 0;
            _latestPendingLine = 0;
            _toolChangeLine = 0;
            _toolChange.Reset();
            _accounting.Reset();
            GcodeFileIndex = _index;

            // On program complete, mark all segments as completed (grey);
            // on stop/alarm, reset to no progress shown
            CompletedSegmentIndex = finalState == JobState.ProgramComplete && (Config?.ShowToolpathProgress ?? true)
                ? _toolpathData?.Segments.Count ?? -1
                : -1;

            JobRunning = false;
            _cancelToken?.Dispose();
            _cancelToken = null;
        }

        private void FileIndexTimerTick(object? sender, EventArgs e)
        {
            var idx = _latestFileIndex;
            if (idx != _gCodeFileIndex)
                GcodeFileIndex = idx;

            var acked = _latestPendingLine;
            if (acked != _ackedLineIndex)
                AckedLineIndex = acked;

            UpdateCompletedSegmentIndex();
        }

        private void UpdateCompletedSegmentIndex()
        {
            if (Config?.ShowToolpathProgress != true)
            {
                if (_completedSegmentIndex != -1)
                    CompletedSegmentIndex = -1;
                return;
            }

            var toolpath = _toolpathData;
            if (toolpath?.LineToFirstSegment == null || toolpath.LineToFirstSegment.Length == 0)
                return;

            var mapping = toolpath.LineToFirstSegment;
            int pendingLine = _latestPendingLine;
            int sentLine = _latestFileIndex + 1; // +1 because _latestFileIndex is 0-based last-sent

            // Clamp to valid range
            pendingLine = Math.Clamp(pendingLine, 0, mapping.Length - 1);
            sentLine = Math.Clamp(sentLine, pendingLine, mapping.Length - 1);

            int floorSegIdx = mapping[pendingLine];
            int ceilingSegIdx = mapping[sentLine];

            // Try to find the segment closest to the actual spindle position
            // within the buffer window. The ack'd line (floor) is ahead of the
            // spindle because grblHAL acks when parsed into the motion planner,
            // not when physically executed. So we search the full window to find
            // where the spindle really is.
            var spindlePos = CurrentSpindlePosition;

            if (spindlePos.HasValue && ceilingSegIdx > 0)
            {
                var pos = spindlePos.Value;
                float bestDistSq = float.MaxValue;
                int bestIdx = floorSegIdx;

                // Search a wider range: from a bit before the floor back to
                // the start of the window, to account for motion planner lag
                int searchStart = Math.Max(0, floorSegIdx - 30);
                int searchEnd = Math.Min(ceilingSegIdx, toolpath.Segments.Count);

                for (int i = searchStart; i < searchEnd; i++)
                {
                    var seg = toolpath.Segments[i];
                    float dx = seg.End.X - pos.X;
                    float dy = seg.End.Y - pos.Y;
                    float dz = seg.End.Z - pos.Z;
                    float distSq = dx * dx + dy * dy + dz * dz;

                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestIdx = i + 1; // +1 because this segment is completed
                    }
                }

                // Only use position match if reasonably close (within 5mm)
                if (bestDistSq <= 25f) // 5mm squared
                    CompletedSegmentIndex = bestIdx;
                else
                    CompletedSegmentIndex = floorSegIdx;
            }
            else
            {
                CompletedSegmentIndex = floorSegIdx;
            }
        }

        private void GCodeControl()
        {
            ShowGCodeConsole = !ShowGCodeConsole;
        }

        private void CloseGcodeConsole()
        {
            ShowGCodeConsole = !ShowGCodeConsole;
        }

        private static string FormatTimeEstimate(double totalSeconds)
        {
            var ts = TimeSpan.FromSeconds(totalSeconds);
            if (ts.TotalHours >= 1)
                return $"Est: {(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
            if (ts.TotalMinutes >= 1)
                return $"Est: {ts.Minutes}m {ts.Seconds:D2}s";
            return $"Est: {ts.Seconds}s";
        }

    }

    public enum JobState
    {
        Start,
        Idle,
        Hold,
        Running,
        Tool,
        Stop,
        Alarm,
        ProgramComplete
    }

}
