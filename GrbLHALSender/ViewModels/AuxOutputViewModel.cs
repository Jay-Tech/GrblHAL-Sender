using CommunityToolkit.Mvvm.ComponentModel;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels;

public class AuxOutputViewModel : ViewModelBase, ISavableViewModel
{
    private readonly ConfigManager _configManager;
    private readonly CommunicationManager _commManager;
    private readonly MachineStateService _machineStateService;
    private readonly ConfigManager _configManger;
    private AuxOutputConfig? _selectedAuxPreset;
   

    public ObservableCollection<AuxOutputConfig> AuxOutputPresets { get; } =
        new(AuxOutputConfig.Presets);

    public ObservableCollection<AuxOutputItem> AuxOutputItems { get; set; } = [];


    public AuxOutputConfig? SelectedAuxPreset
    {
        get => _selectedAuxPreset;
        set => this.RaiseAndSetIfChanged(ref _selectedAuxPreset, value);
    }
    public ICommand AddPresetCommand { get; }
    public ICommand RemoveAuxOutputCommand { get; }


    public AuxOutputViewModel(ConfigManager configManager, 
        CommunicationManager commManager,
        MachineStateService machineStateService)
    {
        _configManager = configManager;
        _commManager = commManager;
        _machineStateService = machineStateService;
        AddPresetCommand = ReactiveCommand.Create(AddSelectedPreset);
        RemoveAuxOutputCommand = ReactiveCommand.Create<AuxOutputItem>(RemoveAuxOutput);
         _configManager.OnConfigLoaded += OnConfigChanged;
        _machineStateService.PropertyChanged += OnMachineStateChanged;
        _commManager.OnAuxPinsDiscovered += OnAuxPinsDiscovered;
    }

    private void OnConfigChanged(object? sender, GHalSenderConfig e)
    {
        LoadAuxOutputs(e.AuxOutputs);
    }
    private void LoadAuxOutputs(List<AuxOutputConfig> configs)
    {
        AuxOutputItems.Clear();
        foreach (var btn in configs.Select(cfg => new AuxOutputItem
                 {
                     Name = cfg.Name,
                     OnCommand = cfg.OnCommand,
                     OffCommand = cfg.OffCommand,
                     StateKey = cfg.StateKey
                 }))
        {
            btn.ToggleCommand = ReactiveCommand.Create(() => Toggle(btn));
            AuxOutputItems.Add(btn);
        }
    }
    private void AddSelectedPreset()
    {
        if (SelectedAuxPreset == null) return;
        // Don't add duplicates (check by StateKey since Name is editable)
        if (AuxOutputItems.Any(i => i.StateKey == SelectedAuxPreset.StateKey)) return;
        var btn = new AuxOutputItem
        {
            Name = SelectedAuxPreset.Name,
            OnCommand = SelectedAuxPreset.OnCommand,
            OffCommand = SelectedAuxPreset.OffCommand,
            StateKey = SelectedAuxPreset.StateKey
        };
        btn.ToggleCommand = ReactiveCommand.Create(() => Toggle(btn));
        AuxOutputItems.Add(btn);
    }

    private void RemoveAuxOutput(AuxOutputItem item)
    {
        AuxOutputItems.Remove(item);
    }

    private void OnAuxPinsDiscovered(object? sender, List<AuxPinInfo> pins)
    {
        foreach (var pin in pins)
        {
            var stateKey = $"DOUT:{pin.PortNumber}";
            // Skip if this pin is already in the preset list
            if (AuxOutputPresets.Any(p => p.StateKey == stateKey)) continue;
            AuxOutputPresets.Add(new AuxOutputConfig
            {
                Name = $"Aux {pin.PortNumber}",
                OnCommand = $"{GrblHalConstants.AuxOutOn} P{pin.PortNumber}",
                OffCommand = $"{GrblHalConstants.AuxOutOff} P{pin.PortNumber}",
                StateKey = stateKey
            });
        }
    }
    private void OnMachineStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MachineStateService.AccessoryState)) return;

        var accessoryState = _machineStateService.AccessoryState;
        foreach (var btn in AuxOutputItems)
        {
            // Only update buttons that track state via the A: field (single char keys like "M", "F")
            if (btn.StateKey.Length == 1)
            {
                btn.IsActive = accessoryState.Contains(btn.StateKey[0]);
            }
        }
    }

    
    private async void Toggle(AuxOutputItem btn)
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
    }

    public void Save()
    {
        _configManager?.GHalSenderConfig?.AuxOutputs = AuxOutputItems.Select(item => new AuxOutputConfig
        {
            Name = item.Name,
            OnCommand = item.OnCommand,
            OffCommand = item.OffCommand,
            StateKey = item.StateKey
        }).ToList();
    }
}

public partial class AuxOutputItem : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _onCommand = "";
    [ObservableProperty] private string _offCommand = "";
    [ObservableProperty] private string _stateKey = "";
    [JsonIgnore]
    [ObservableProperty] private bool _isActive;
    [JsonIgnore]
    public ICommand ToggleCommand { get; set; } = null!;

}
