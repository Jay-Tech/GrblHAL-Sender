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
        // We track how many bytes are "in-flight" (sent but not yet acked).
        // Each line costs: text.Length + 1 (\r terminator added by WriteCommand).
        // Each "ok" response frees the bytes for the oldest in-flight line.
        // Streaming is EVENT-DRIVEN: OnCommandAck directly calls FillBuffer().
        private const int DefaultRxBufferSize = 128;
        private int _rxBufferSize = DefaultRxBufferSize;
        private int _rxBufferUsed;
        private int _pendingLine;  // Next line awaiting "ok" acknowledgment
        private int _ackPending;   // Number of unacknowledged commands
        private readonly Queue<int> _lineLengths = new(); // byte length of each in-flight line
        private readonly object _bufferLock = new();
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



        // Fed from MachineStateService via PropertyChanged subscription
        private bool _canStartJob;
        private Point3D? _workCoordinateOffset;
        private Point3D? _currentSpindlePosition;
        // Set by MainViewModel — references config object so changes take effect immediately
        internal GHalSenderConfig? Config;
        private bool _toolChangeVisible;


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
        }

        public bool ToolChangeVisible
        {
            get => _toolChangeVisible;
            set => this.RaiseAndSetIfChanged(ref _toolChangeVisible, value);
        }

        public JobViewModel(CommunicationManager manager, MachineStateService machineStateService)
        {
            _commsManager = manager;
            _machineStateService = machineStateService;
            _machineStateService.PropertyChanged += OnMachineStateChanged;
            GCodeOutPut = new ObservableCollection<GCodeLine>();
            CloseGCodeConsole = ReactiveCommand.Create(CloseGcodeConsole);
            OpenGCodePanel = ReactiveCommand.Create(GCodeControl);
            StartJobCommand = ReactiveCommand.Create(StartJob);
            CloseFilesCommand = ReactiveCommand.Create(CloseFile);
            PauseJobCommand = ReactiveCommand.Create(TogglePause);
            StopJobCommand = ReactiveCommand.Create(StopJob);
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
            file.ParseGCodeFile(SelectedFiles[0].Path.AbsolutePath, FileComplete);
        }

        public void FileComplete(List<GCodeLine> gCodeJob)
        {
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
            _ackPending = 0;
            _rxBufferUsed = 0;
            CompletedSegmentIndex = -1;
            lock (_bufferLock) { _lineLengths.Clear(); }

            // Use the real RX buffer size from the controller if available
            var reportedRxSize = _commsManager.Options?.RxBufferSize ?? DefaultRxBufferSize;
            var precentBuffer = reportedRxSize * _bufferPercentage / 100;
            _rxBufferSize =  Math.Max(precentBuffer, DefaultRxBufferSize);

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

        private void ResumeJob()
        {
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
            EstimatedTime = string.Empty;
            if (ShowGCodeConsole) ShowGCodeConsole = false;
        }

        private void ListenToState(bool subscribe)
        {
            if (subscribe)
            {
                _commsManager.OnStateReceived += _commsManager_OnStateReceived;
                _commsManager.OnCommandAck += _commsManager_OnCommandAck;
            }
            else
            {
                _commsManager.OnStateReceived -= _commsManager_OnStateReceived;
                _commsManager.OnCommandAck -= _commsManager_OnCommandAck;
            }
        }

        private void _commsManager_OnStateReceived(object? sender, RealTImeState e)
        {
            var state = e.GrblHalState;
            var previousState = JobState;

            JobState = state switch
            {
                "Hold" => JobState.Hold,
                "Tool" => JobState.Tool,
                "Run" => JobState.Running,
                "Alarm" => JobState.Alarm,
                "Home" => JobState.Running,
                "Idle" => JobState.Idle,
                "Door" => JobState.Hold,
                _ => JobState
            };

            // Alarm during job — abort
            if (JobState == JobState.Alarm && JobRunning)
            {
                CancelAndCleanup(JobState.Alarm);
            }

            // Tool change — acknowledge when grblHAL reports Tool state
            if (JobState == JobState.Tool)
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

        private void _commsManager_OnCommandAck(object? sender, EventArgs e)
        {
            if (!JobRunning) return;

            if (JobState is JobState.Start)
                JobState = JobState.Running;

            // Free the oldest in-flight line's bytes from the RX buffer
            if (_ackPending > 0)
                _ackPending--;

            lock (_bufferLock)
            {
                if (_lineLengths.Count > 0)
                {
                    var freed = _lineLengths.Dequeue();
                    _rxBufferUsed = Math.Max(0, _rxBufferUsed - freed);
                }
            }

            _pendingLine++;
            _latestPendingLine = _pendingLine;

            // Check for job completion: all lines sent AND all acks received
            if (_pendingLine >= GCodeOutPut.Count && _ackPending == 0)
            {
                Dispatcher.UIThread.Post(() => JobComplete());
                return;
            }

            // Don't send more while paused or in tool change
            if (JobState is JobState.Hold or JobState.Tool)
                return;

            // Event-driven: immediately refill the buffer
            FillBuffer();

        }

        /// <summary>
        /// Sends as many queued lines as will fit in grblHAL's serial RX buffer.
        /// Each line costs text.Length + 1 byte (\r terminator).
        /// Called from StartJob (pre-fill) and from OnCommandAck (event-driven refill).
        /// </summary>
        private void FillBuffer()
        {
            while (_index < GCodeOutPut.Count)
            {
                var line = GCodeOutPut[_index].Text;

                // Skip empty/comment lines — still send "()" so grblHAL acks them
                if (string.IsNullOrEmpty(line))
                {
                    _index++;
                    continue;
                }

                int lineBytes = line.Length + 1; // +1 for \r appended by WriteCommand

                // Will this line fit in the remaining RX buffer space?
                if (_rxBufferUsed + lineBytes > _rxBufferSize)
                    break; // No room — wait for an ack to free space

                // Send the line
                _commsManager.SendCommand(line);
                _rxBufferUsed += lineBytes;
                _ackPending++;
                lock (_bufferLock) { _lineLengths.Enqueue(lineBytes); }

                _latestFileIndex = _index;
                _index++;
            }
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
            _ackPending = 0;
            _rxBufferUsed = 0;
            lock (_bufferLock) { _lineLengths.Clear(); }
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
