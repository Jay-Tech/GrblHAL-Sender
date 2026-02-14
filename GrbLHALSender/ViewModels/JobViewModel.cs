using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
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

        public IReadOnlyList<IStorageFile>? SelectedFiles { get; set; }
        public Core.Interaction<string, IReadOnlyList<IStorageFile>?> SelectFilesInteraction { get; } = new();
        public JobState JobState { get; set; }
        public ICommand StartJobCommand { get; }
        public ICommand CloseGCodeConsole { get; }
        public ICommand OpenGCodePanel { get; }
        public ICommand CloseFilesCommand { get; }
        public ICommand PauseJobCommand { get; }
        public ICommand StopJobCommand { get; }
        private string _estimatedTime;
        private string _runTime;
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

        public JobViewModel(CommunicationManager manager)
        {
            _commsManager = manager;
            GCodeOutPut = new ObservableCollection<GCodeLine>();
            CloseGCodeConsole = ReactiveCommand.Create(CloseGcodeConsole);
            OpenGCodePanel = ReactiveCommand.Create(GCodeControl);
            StartJobCommand = ReactiveCommand.Create(StartJob);
            CloseFilesCommand = ReactiveCommand.Create(CloseFile);
            PauseJobCommand = ReactiveCommand.Create(PauseJob);
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
            ListenToState(true);
            SendJobLoop(JobState.Start);
        }

        private void StopJob()
        {
            JobState = JobState.Stop;
            _commsManager.Adapter.WriteByte(GrblHalConstants.Stop);
            JobCompete();
            GcodeFileIndex = 0;
        }

        private void PauseJob()
        {
            _commsManager.Adapter.WriteByte(GrblHalConstants.FeedHold);
        }

        private void CloseFile()
        {
            GCodeOutPut.Clear();
            FileLoaded = false;
            FileName = string.Empty;
            ToolpathData = null;
            EstimatedTime = string.Empty;
        }

        private void _commsManager_OnStateReceived(object? sender, RealTImeState e)
        {
            var state = e.GrblHalState;
            JobState = state switch
            {
                "Hold" => JobState.Hold,
                "Tool" => JobState.Tool,
                "Running" => JobState.Running,
                "Alarm" => JobState.Alarm,
                "Stop" => JobState.Stop,
                _ => JobState
            };
            // SendJobLoop(JobState);
        }

        private void _commsManager_OnCommandAck(object? sender, EventArgs e)
        {
            if (JobState is JobState.Running or JobState.Start)
            {
                JobState = JobState.Running;
            }
            SendJobLoop(JobState);
            //if (JobState is JobState.Hold or JobState.Tool)
            //{
            //     _commsManager.Adapter.WriteByte(GrblHalConstants.CycleStart);
            //}
        }

        private void ListenToState(bool b)
        {
            if (b)
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
        public void SendJobLoop(JobState lineProcessed)
        {
            switch (JobState)
            {
                case JobState.Tool:
                    _commsManager.Adapter.WriteByte(GrblHalConstants.ToolAck);
                    break;
                case JobState.Hold:
                    _commsManager.Adapter.WriteByte(GrblHalConstants.CycleStart);
                    JobState = JobState.Running;
                    break;
                case JobState.Start:
                    JobState = JobState.Start;

                    break;
            }

            if (JobState is JobState.Running or JobState.SendNextLine or JobState.Start)
            {
                if (_index <= GCodeOutPut.Count - 1)
                {
                    _commsManager.SendCommand(GCodeOutPut[_index].Text);
                    GcodeFileIndex = _index;
                    _index++;
                }
                else
                {
                    JobCompete();
                }
            }

        }
        private void JobCompete()
        {
            JobState = JobState.ProgramComplete;
            _index = 0;
            ListenToState(false);
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
        Hold,
        Running,
        Tool,
        Stop,
        Alarm,
        ProgramComplete,
        SendNextLine
    }

}
