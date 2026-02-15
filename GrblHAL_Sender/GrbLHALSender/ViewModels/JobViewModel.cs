using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using GrbLHALSender.Communication;
using GrbLHALSender.Gcode;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using ReactiveUI;

namespace GrbLHALSender.ViewModels
{
    public partial class JobViewModel : ViewModelBase
    {

        private readonly CommunicationManager _commsManager;

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

        public IReadOnlyList<IStorageFile>? SelectedFiles { get; set; }
        public Core.Interaction<string, IReadOnlyList<IStorageFile>?> SelectFilesInteraction { get; } = new();
        public JobState JobState { get; set; }
        public ICommand StartJobCommand { get; }
        public ICommand CloseGCodeConsole { get; }
        public ICommand OpenGCodePanel { get; }
        public ICommand CloseFilesCommand { get; }
        public ICommand PauseJobCommand { get; }
        public ICommand StopJobCommand { get; }

        public ObservableCollection<GCodeLine> GCodeOutPut { get; set; }

        public bool FileLoaded
        {
            get => _fileLoaded;
            set => this.RaiseAndSetIfChanged(ref _fileLoaded, value);
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
            set => this.RaiseAndSetIfChanged(ref _jobRunning, value);
        }

        public JobViewModel(CommunicationManager manager)
        {
            _commsManager = manager;
            GCodeOutPut = new ObservableCollection<GCodeLine>();
            CloseGCodeConsole = ReactiveCommand.Create(CloseGcodeConsole);
            OpenGCodePanel = ReactiveCommand.Create(GCodeControl);
            StartJobCommand = ReactiveCommand.Create(StartJob);
            CloseFilesCommand = ReactiveCommand.Create(CloseFile);
            PauseJobCommand = ReactiveCommand.Create(TogglePause);
            StopJobCommand = ReactiveCommand.Create(StopJob);
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

            // Use machine rapid rates from $110/$111/$112 if available, fallback to 5000 mm/min
            var machine = _commsManager.MachineData;
            if (machine != null)
            {
                var rapids = new[] { machine.XRapid, machine.YRapid, machine.ZRapid };
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
                FileLoaded = true;
                ToolpathData = toolpath;
                EstimatedTime = FormatTimeEstimate(toolpath.TimeEstimateSeconds);
            }));
        }

        public void StartJob()
        {
            if (JobRunning && JobState == JobState.Hold || JobState == JobState.Tool)
            {
                ResumeJob();
                return;
            }
            if (JobRunning) return;
            if (GCodeOutPut.Count == 0) return;

            _index = 0;
            _pendingLine = 0;
            _ackPending = 0;
            _rxBufferUsed = 0;
            lock (_bufferLock) { _lineLengths.Clear(); }

            // Use the real RX buffer size from the controller if available
            var reportedRxSize = _commsManager.Options?.RxBufferSize ?? 0;
            _rxBufferSize = reportedRxSize > 0 ? reportedRxSize : DefaultRxBufferSize;

            JobState = JobState.Start;

            // Dispose previous token if any
            _cancelToken?.Dispose();
            _cancelToken = new CancellationTokenSource();

            ListenToState(true);
            JobRunning = true;

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
            if (!JobRunning) return;
            _commsManager.Adapter?.WriteByte(GrblHalConstants.FeedHold);
            // Don't set JobState here — let _commsManager_OnStateReceived
            // update it to Hold when grblHAL actually confirms the hold
        }

        private void ResumeJob()
        {
            if (!JobRunning && JobState != JobState.Hold && JobState != JobState.Tool) return;
            _commsManager.Adapter?.WriteByte(GrblHalConstants.CycleStart);
            FillBuffer();

            // Let _commsManager_OnStateReceived update to Running,
            // then refill the buffer in case acks arrived while paused

        }

        private void TogglePause()
        {
            if (JobState == JobState.Hold) return;
            PauseJob();
        }

        private void CloseFile()
        {
            if (JobRunning) StopJob();
            GCodeOutPut.Clear();
            FileLoaded = false;
            FileName = string.Empty;
            ToolpathData = null;
            EstimatedTime = string.Empty;
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

                var idx = _index;
                Dispatcher.UIThread.Post(() => GcodeFileIndex = idx);
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
            _cancelToken?.Cancel();
            JobState = finalState;
            _index = 0;
            _pendingLine = 0;
            _ackPending = 0;
            _rxBufferUsed = 0;
            lock (_bufferLock) { _lineLengths.Clear(); }
            GcodeFileIndex = _index;
            JobRunning = false;
            _cancelToken?.Dispose();
            _cancelToken = null;
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
        ProgramComplete,
        SendNextLine
    }

}
