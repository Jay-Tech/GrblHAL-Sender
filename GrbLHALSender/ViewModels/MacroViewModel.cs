using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gcode;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels;

public class MacroViewModel : ViewModelBase, IDialogCloseable
{
    private readonly ConfigManager _configManger;
    private readonly CommunicationManager _commsManager;
    private readonly GcodeEventInjector _eventInjector;
    public ObservableCollection<Macro> MacroList { get; set; }

    public Macro SelectedItem
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    public int MacroSelectedIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    public bool MacroNameEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    public string MacroName
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    public bool DisplayMacroControl
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    public string MacroCommandText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Whether a macro may be run. Pushed in by MainViewModel, which owns the machine
    /// state and job status: a macro is arbitrary g-code, and running one mid-job
    /// interleaves it into the program the controller is already executing.
    /// </summary>
    public bool CanRunMacro
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    public ICommand RunMacroCommand { get; }
    public ICommand DeleteMacroCommand { get; }
    public ICommand SaveMacroCommand { get; }
    public ICommand NewMacroCommand { get; }
    public ICommand OpenMacroPanel { get; }
    public ICommand CloseMacroCommand { get; }
    public ICommand CloseCommand { get; }
    public Action? CloseAction { get; set; }

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
        MacroSelectedIndex = -1;
        this.WhenAnyValue(x => x.MacroSelectedIndex).Subscribe(IndexChange);
        this.WhenAnyValue(x => x.SelectedItem).Subscribe(SelectedItemChanged);
    }

    private void SelectedItemChanged(Macro selectedItem)
    {
        MacroCommandText = SelectedItem?.Command ?? string.Empty;
    }

    private void IndexChange(int index)
    {
        MacroNameEnabled = index == -1;
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
        if (string.IsNullOrWhiteSpace(macroId))
        {
            if (string.IsNullOrWhiteSpace(SelectedItem?.Id)) return;
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

        MacroName = string.Empty;
        MacroCommandText = string.Empty;
        MacroSelectedIndex = -1;
        SaveConfig();
        return;

        Macro BuildMacro()
        {
            var m = new Macro
            {
                Id = macroId,
                Command = MacroCommandText
            };
            return m;
        }
    }

    private void SaveConfig()
    {
        try
        {
            _configManger.GHalSenderConfig?.MacroList = MacroList;
            _configManger.SaveConfig();
        }
        catch (Exception ex)
        {
            // ConfigManager writes atomically and lets failures propagate, so this is
            // the only place a save error surfaces. Swallowing it silently loses the
            // macro on the next start with nothing shown. Console.Error rather than
            // Debug.WriteLine: the latter is [Conditional("DEBUG")] and would leave a
            // release build on the Pi just as silent.
            Console.Error.WriteLine($"Macro config save failed: {ex.Message}");
        }
    }

    private void DeleteMacro(Macro macro)
    {
        if (macro?.Id == null) return;
        MacroList.Remove(macro);
        SaveConfig();

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
        if (command.Command != null) SendCommand(command.Command);
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

public  class Macro : ReactiveObject
{
    public string? Id
    {
        get;    
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? Command
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
}