using CommunityToolkit.Mvvm.ComponentModel;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels;

public class AuxOutputViewModel : ViewModelBase
{
    private readonly ConfigManager _configManager;
    private readonly CommunicationManager _commManager;
    private readonly MachineStateService _machineStateService;

    public ObservableCollection<AuxOutputButton> Buttons { get; set; } = [];

    public AuxOutputViewModel(ConfigManager configManager, CommunicationManager commManager,
        MachineStateService machineStateService)
    {
        _configManager = configManager;
        _commManager = commManager;
        _machineStateService = machineStateService;
        _configManager.OnConfigLoaded += OnConfigChanged;
        _configManager.OnConfigSaved += OnConfigChanged;
        _machineStateService.PropertyChanged += OnMachineStateChanged;
    }

    private void OnConfigChanged(object? sender, GHalSenderConfig e)
    {
        Buttons.Clear();
        foreach (var cfg in e.AuxOutputs)
        {
            AddButton(cfg);
        }
    }

    private void OnMachineStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MachineStateService.AccessoryState)) return;

        var accessoryState = _machineStateService.AccessoryState;
        foreach (var btn in Buttons)
        {
            // Only update buttons that track state via the A: field (single char keys like "M", "F")
            if (btn.StateKey.Length == 1)
            {
                btn.IsActive = accessoryState.Contains(btn.StateKey[0]);
            }
        }
    }

    private void AddButton(AuxOutputConfig cfg)
    {
        var btn = new AuxOutputButton
        {
            Name = cfg.Name,
            OnCommand = cfg.OnCommand,
            OffCommand = cfg.OffCommand,
            StateKey = cfg.StateKey
        };
        btn.ToggleCommand = ReactiveCommand.Create(() => Toggle(btn));
        Buttons.Add(btn);
    }

    private async void Toggle(AuxOutputButton btn)
    {
        var command = btn.IsActive ? btn.OffCommand : btn.OnCommand;
        _commManager.SendCommand(command);

        // For DOUT pins, query $pinstate to confirm actual state
        if (btn.StateKey.StartsWith("DOUT:"))
        {
            // Small delay to let the controller process the command
            await Task.Delay(200);
            var states = await _commManager.QueryPinStatesAsync();
            if (int.TryParse(btn.StateKey.AsSpan(5), out var portNumber))
            {
                if (states.TryGetValue(portNumber, out var isOn))
                {
                    btn.IsActive = isOn;
                }
            }
        }
        // For A: field buttons (Mist/Flood), state updates come automatically
        // via MachineStateService.AccessoryState property changes
    }
}

public partial class AuxOutputButton : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _onCommand = "";
    [ObservableProperty] private string _offCommand = "";
    [ObservableProperty] private string _stateKey = "";
    [ObservableProperty] private bool _isActive;
    public ICommand ToggleCommand { get; set; } = null!;
}
