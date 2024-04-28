using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using GrbLHAL_Sender.Communication;
using GrbLHAL_Sender.Configuration;
using GrbLHAL_Sender.Gcode;
using ReactiveUI;

namespace GrbLHAL_Sender.ViewModels
{
    public partial class JobViewModel : ViewModelBase
    {
        private ObservableCollection<Macro> _macroList;
        private ObservableCollection<GCodeLine> _gCodeOutPut;

        private readonly CommunicationManager _manager;
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
  
        public IReadOnlyList<IStorageFile>? SelectedFiles { get; set; }
        public Core.Interaction<string, IReadOnlyList<IStorageFile>?> SelectFilesInteraction { get; } = new();
        public ICommand RunMacroCommand { get; }
        public ICommand StartJobCommand { get; }
        public ICommand CloseGCodeConsole { get; }
        public ICommand DeleteMacroCommand { get; }
        public ICommand SaveMacroCommand { get; }
        public ICommand NewMacroCommand { get; }
        public ICommand CloseMacroCommand { get; }
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
                MacroCommandText = value?.Command?? " ";
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

        public JobViewModel(CommunicationManager manager, ConfigManager configManger)
        {
            _manager = manager;
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
                        m.Command = m.Command;
                    }
                }
            }
            Macro BuildMacro()
            {
              var m =  new Macro
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
            _manager.SendCommand(command);
        }
        public void FileComplete(List<GCodeLine> gCodeJob)
        {
            GCodeOutPut.Clear();
            GCodeOutPut.AddRange(gCodeJob);
        }

        public void StartJob()
        {

        }
    }
}
public partial class Macro : ObservableObject
{
    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private string _command;
}