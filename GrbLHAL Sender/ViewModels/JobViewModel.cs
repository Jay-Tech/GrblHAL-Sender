using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using GrbLHAL_Sender.Communication;
using GrbLHAL_Sender.Configuration;
using GrbLHAL_Sender.Gcode;
using GrbLHAL_Sender.Utility;
using ReactiveUI;

namespace GrbLHAL_Sender.ViewModels
{
    public partial class JobViewModel : ViewModelBase
    {
        private ObservableCollection<Macro> _macroList;
        private ObservableCollection<GCodeLine> _gCodeOutPut;

        private readonly CommunicationManager _commsManager;
        private readonly ConfigManager _configManger;
        private ReactiveCommand<object, Unit> _doubleTapConsoleCommand;
        private ReactiveCommand<object, Unit> _doubleMacroTapCommand;
        private bool _showGCodeConsole;
        private Macro _selectedItem;
        private int _macroSelectedIndex;
        private bool _macroNameEnabled;
        private string _macroName;
        private bool _displayMacroControl;
        private string _macroCommandText;
        private int _gCodeFileIndex;
        private int _index = 0;

        public IReadOnlyList<IStorageFile>? SelectedFiles { get; set; }
        public Core.Interaction<string, IReadOnlyList<IStorageFile>?> SelectFilesInteraction { get; } = new();
        public ICommand RunMacroCommand { get; }
        public ICommand StartJobCommand { get; }
        public ICommand CloseGCodeConsole { get; }
        public ICommand DeleteMacroCommand { get; }
        public ICommand SaveMacroCommand { get; }
        public ICommand NewMacroCommand { get; }
        public ICommand CloseMacroCommand { get; }
        public ICommand OpenGCodePanel { get; }
        public ICommand OpenMacroPanel { get; }
        public ICommand CloseFilesCommand { get; }
        public ICommand PauseJobCommand { get; }
        public ICommand StopJobCommand { get; }
        public bool FileLoaded { get; set; }
        public ReactiveCommand<object, Unit> DoubleTapConsoleCommand
        {
            get => _doubleTapConsoleCommand;
            set => _doubleTapConsoleCommand = value;
        }

        public ReactiveCommand<object, Unit> DoubleMacroTapCommand
        {
            get => _doubleMacroTapCommand;
            set => _doubleMacroTapCommand = value;
        }
        public bool ShowGCodeConsole
        {
            get => _showGCodeConsole;
            set => this.RaiseAndSetIfChanged(ref _showGCodeConsole, value);
        }
        public ObservableCollection<Macro> MacroList
        {
            get => _macroList;
            set => this.RaiseAndSetIfChanged(ref _macroList, value);
        }
        public ObservableCollection<GCodeLine> GCodeOutPut
        {
            get => _gCodeOutPut;
            set => this.RaiseAndSetIfChanged(ref _gCodeOutPut, value);
        }
        public Macro SelectedItem
        {
            get => _selectedItem;
            set
            {
                MacroCommandText = value?.Command ?? " ";
                this.RaiseAndSetIfChanged(ref _selectedItem, value);
            }
        }
        public int MacroSelectedIndex
        {
            get => _macroSelectedIndex;
            set
            {
                MacroNameEnabled = value == -1;
                this.RaiseAndSetIfChanged(ref _macroSelectedIndex, value);
            }
        }
        public bool MacroNameEnabled
        {
            get => _macroNameEnabled;
            set => this.RaiseAndSetIfChanged(ref _macroNameEnabled, value);
        }
        public string MacroName
        {
            get => _macroName;
            set => this.RaiseAndSetIfChanged(ref _macroName, value);
        }
        public bool DisplayMacroControl
        {
            get => _displayMacroControl;
            set => this.RaiseAndSetIfChanged(ref _displayMacroControl, value);
        }

        public string MacroCommandText
        {
            get => _macroCommandText;
            set => this.RaiseAndSetIfChanged(ref _macroCommandText, value);
        }

        public int GcodeFileIndex
        {
            get => _gCodeFileIndex;
            set => this.RaiseAndSetIfChanged(ref _gCodeFileIndex, value);
        }

        public JobViewModel(CommunicationManager manager, ConfigManager configManger)
        {
            _commsManager = manager;
            _configManger = configManger;
            _configManger.OnConfigLoaded += _configManger_OnConfigLoaded;
            GCodeOutPut = new ObservableCollection<GCodeLine>();
            RunMacroCommand = ReactiveCommand.Create<string>(RunMacro);
            StartJobCommand = ReactiveCommand.Create(StartJob);
            DoubleTapConsoleCommand = ReactiveCommand.Create<object>(DoubleTap);
            DoubleMacroTapCommand = ReactiveCommand.Create<object>(DoubleTapMacroControl);
            CloseGCodeConsole = ReactiveCommand.Create(CloseGcodeConsole);
            DeleteMacroCommand = ReactiveCommand.Create<Macro>(DeleteMacro);
            SaveMacroCommand = ReactiveCommand.Create<string>(SaveMacro);
            NewMacroCommand = ReactiveCommand.Create(NewMacro);
            CloseMacroCommand = ReactiveCommand.Create(CloseMacroControl);
            OpenGCodePanel = ReactiveCommand.Create(GCodeControl);
            OpenMacroPanel = ReactiveCommand.Create(MacroControl);
            CloseFilesCommand = ReactiveCommand.Create(CloseFile);
            PauseJobCommand = ReactiveCommand.Create(PauseJob);
            StopJobCommand = ReactiveCommand.Create(StopJob);

        }

        private void _commsManager_OnStateReceived(object? sender, RealTImeState e)
        {
            var state = e.GrblHalState;
            JobState = state switch
            {
                "Hold" => JobState.Pause,
                "Tool" => JobState.Tool,
                "Running" => JobState.Running,
                "Alarm" => JobState.Alarm,
                "Stop" => JobState.Stop,
                _ => JobState
            };
        }

        private void ListenToState(bool b)
        {
            if (b)
                _commsManager.OnStateReceived += _commsManager_OnStateReceived;
            else
            {
                _commsManager.OnStateReceived -= _commsManager_OnStateReceived;
            }
        }
        private void MacroControl()
        {
            DisplayMacroControl = !DisplayMacroControl;
        }

        private void GCodeControl()
        {
            ShowGCodeConsole = !ShowGCodeConsole;
        }

        private void _configManger_OnConfigLoaded(object? sender, GHalSenderConfig e)
        {
            MacroList = e.MacroList;
        }

        private void DoubleTapMacroControl(object p)
        {
            DisplayMacroControl = !Convert.ToBoolean(p);
        }

        private void CloseMacroControl()
        {
            DisplayMacroControl = !DisplayMacroControl;
        }

        private void SaveMacro(string macroId)
        {
            if (string.IsNullOrEmpty(macroId))
            {
                if (SelectedItem?.Id == " ") return;
                macroId = SelectedItem.Id;
            }
            if (MacroList.Count == 0)
            {
                MacroList.Add(BuildMacro());
            }
            if (MacroList.All(x => x.Id != macroId))
            {
                MacroList.Add(BuildMacro());
            }
            else
            {
                foreach (var m in MacroList)
                {
                    if (m.Id == macroId)
                    {
                        m.Command = MacroCommandText;
                    }
                }
            }
            Macro BuildMacro()
            {
                var m = new Macro
                {
                    Id = macroId,
                    Command = MacroCommandText
                };
                return m;
            }

            MacroName = string.Empty;
            MacroCommandText = string.Empty;
            MacroSelectedIndex = -1;
            _configManger.GHalSenderConfig.MacroList = MacroList;
            _configManger.SaveConfig();
        }
        private void DeleteMacro(Macro macro)
        {
            if (macro?.Id != null)
            {
                MacroList.Remove(macro);
            }
        }
        private void NewMacro()
        {
            MacroSelectedIndex = -1;
        }
        private void CloseGcodeConsole()
        {
            ShowGCodeConsole = !ShowGCodeConsole;
        }

        [RelayCommand]
        private async Task SelectFilesAsync()
        {
            SelectedFiles = await SelectFilesInteraction.HandleAsync("Choose .NC File");
            var selectFile = SelectedFiles;
            if (selectFile?.Count <= 0) return;
            var file = new GCodeParser();
            file.ParseGCodeFile(SelectedFiles[0].Path.AbsolutePath, FileComplete);
        }
        private void DoubleTap(object p)
        {
            ShowGCodeConsole = !Convert.ToBoolean(p);
        }
        private void RunMacro(string macroId)
        {
            var command = MacroList.First(x => x.Id == macroId);
            SendCommand(command.Command);
        }
        private void SendCommand(string command)
        {
            _commsManager.SendCommand(command);
        }
        public void FileComplete(List<GCodeLine> gCodeJob)
        {
            Dispatcher.UIThread.Invoke((() =>
            {
                GCodeOutPut.Clear();
                GCodeOutPut.AddRange(gCodeJob);
                GcodeFileIndex = 0;
            }));

        }
        public JobState JobState { get; set; }
        private void StopJob()
        {
             JobState = JobState.Stop;
            _commsManager.Adapter.WriteByte(GrblHalConstants.Stop);
            JobCompete();
            GcodeFileIndex = 1;
        }
        private void PauseJob()
        {
            _commsManager.Adapter.WriteByte(GrblHalConstants.FeedHold);
        }
        private void CloseFile()
        {
            GCodeOutPut.Clear();
            FileLoaded = false;
        }
        public void StartJob()
        {
            ListenToState(true);
            _commsManager.StartJob(SendJobLoop);
            SendJobLoop("start");
        }

        public void SendJobLoop(string lineProcessed)
        {

            if (JobState == JobState.Tool)
                _commsManager.Adapter.WriteByte(GrblHalConstants.ToolAck);
            if (JobState == JobState.Pause)
            {
                _commsManager.Adapter.WriteByte(GrblHalConstants.CycleStart);
                JobState = JobState.Running;
            }
            if (JobState != JobState.Running) return;
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

        private void JobCompete()
        {
            _commsManager.EndJob();
            _index = 0;
            ListenToState(false);
           
        }
    }

    public enum JobState
    {
        Running,
        Pause,
        Tool,
        Stop,
        Alarm
    }
}
public partial class Macro : ObservableObject
{
    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private string _command;
}