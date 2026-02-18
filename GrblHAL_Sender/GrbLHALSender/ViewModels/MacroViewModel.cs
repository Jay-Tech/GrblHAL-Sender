using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using ReactiveUI;

namespace GrbLHALSender.ViewModels;

public class MacroViewModel: ViewModelBase, IDialogCloseable
{
    private Macro _selectedItem;
    private int _macroSelectedIndex;
    private bool _macroNameEnabled;
    private string _macroName;
    private bool _displayMacroControl;
    private string _macroCommandText;
    private readonly ConfigManager _configManger;
    private readonly CommunicationManager _commsManager;
    public ObservableCollection<Macro> MacroList { get; set; }
        
       
       
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
    public ICommand RunMacroCommand { get; }
    public ICommand DeleteMacroCommand { get; }
    public ICommand SaveMacroCommand { get; }
    public ICommand NewMacroCommand { get; }
    public ICommand OpenMacroPanel { get; }
    public ICommand CloseMacroCommand { get; }
    public Action? CloseAction { get; set; }
    public ICommand CloseCommand { get; }


    private ReactiveCommand<object, Unit> _doubleMacroTapCommand;
    public MacroViewModel(ConfigManager configManger, CommunicationManager commsManager)
    {
        _configManger = configManger;
        _commsManager = commsManager;
        _configManger.OnConfigLoaded += _configManger_OnConfigLoaded;
        RunMacroCommand = ReactiveCommand.Create<string>(RunMacro);
        DeleteMacroCommand = ReactiveCommand.Create<Macro>(DeleteMacro);
        SaveMacroCommand = ReactiveCommand.Create<string>(SaveMacro);
        NewMacroCommand = ReactiveCommand.Create(NewMacro);
        CloseMacroCommand = ReactiveCommand.Create(CloseMacroControl);
        OpenMacroPanel = ReactiveCommand.Create(MacroControl);
        CloseCommand = ReactiveCommand.Create(() => CloseAction?.Invoke());
    }

    private void _configManger_OnConfigLoaded(object? sender, GHalSenderConfig e)
    {
        MacroList = e.MacroList;
    }
    private void MacroControl()
    {
        DisplayMacroControl = !DisplayMacroControl;
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
        _configManger.GHalSenderConfig?.MacroList = MacroList;
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
    private void RunMacro(string macroId)
    {
        var command = MacroList.First(x => x.Id == macroId);
        SendCommand(command.Command);
    }
    private void SendCommand(string command)
    {
        _commsManager.SendCommand(command);
    }
}

public partial class Macro : ObservableObject
{
    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private string _command;
}