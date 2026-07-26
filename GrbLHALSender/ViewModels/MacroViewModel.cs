using CommunityToolkit.Mvvm.ComponentModel;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gcode;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels;

public class MacroViewModel : ViewModelBase, IDialogCloseable
{
    private Macro _selectedItem;
    private int _macroSelectedIndex;
    private bool _macroNameEnabled;
    private string _macroName;
    private bool _displayMacroControl;
    private string _macroCommandText;
    private bool _canRunMacro;
    private readonly ConfigManager _configManger;
    private readonly CommunicationManager _commsManager;
    private readonly GcodeEventInjector _eventInjector;
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

    /// <summary>
    /// Whether a macro may be run. Pushed in by MainViewModel, which owns the machine
    /// state and job status: a macro is arbitrary g-code, and running one mid-job
    /// interleaves it into the program the controller is already executing.
    /// </summary>
    public bool CanRunMacro
    {
        get => _canRunMacro;
        set => this.RaiseAndSetIfChanged(ref _canRunMacro, value);
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
    public MacroViewModel(ConfigManager configManger, CommunicationManager commsManager,
        GcodeEventInjector eventInjector)
    {
        _configManger = configManger;
        _commsManager = commsManager;
        _eventInjector = eventInjector;
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
        // Enforced here too, so the rule does not rely on the view's IsEnabled.
        if (!CanRunMacro) return;
        var command = MacroList.First(x => x.Id == macroId);
        SendCommand(command.Command);
    }
    /// <summary>
    /// Macros are operator-issued G-code, so configured event rules apply. A macro
    /// body may hold several commands; each is matched on its own line.
    /// </summary>
    private void SendCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        if (_eventInjector.IsEmpty)
        {
            _commsManager.SendCommand(command);
            return;
        }

        foreach (var line in _eventInjector.ExpandBlock(command, GcodeEventScope.Manual))
            _commsManager.SendCommand(line);
    }
}

public partial class Macro : ObservableObject
{
    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private string _command;
}