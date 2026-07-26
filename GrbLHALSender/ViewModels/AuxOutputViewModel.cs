using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.States;
using GrbLHALSender.Utility;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
        _commManager.OnCommandSent += OnCommandSent;
    }

    /// <summary>
    /// Keeps a DOUT button in step with aux-output commands this view model did not
    /// send — typed into MDI, run from a macro, streamed from a job file, or injected
    /// by a G-code event rule. Without this the button only ever changed when tapped,
    /// so a rule toggling the pin left it showing the wrong state indefinitely.
    /// <para>
    /// The state is taken from the command itself rather than confirmed with a
    /// $pinstate query: a query mid-job would put an extra command and its "ok" into
    /// the stream, which the job streamer's character counting does not account for.
    /// </para>
    /// </summary>
    private void OnCommandSent(object? sender, CommandSentEventArgs e)
    {
        // Applies to job lines as well as manual commands: a file that toggles the pin
        // should move the button too.
        var command = e.Command;

        // Runs for every streamed line, so reject the common case before parsing.
        if (AuxOutputItems.Count == 0) return;
        if (string.IsNullOrEmpty(command)) return;
        if (command.IndexOf('M') < 0 && command.IndexOf('m') < 0) return;

        if (!TryParseAuxOutputCommand(command, out var port, out var isOn)) return;

        var stateKey = $"DOUT:{port}";
        foreach (var btn in AuxOutputItems)
        {
            if (!btn.StateKey.Equals(stateKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (btn.IsActive == isOn) continue;

            // Commands can be sent from the comms thread (job streaming) — IsActive is
            // bound, so the change has to reach the UI thread.
            var target = btn;
            Dispatcher.UIThread.Post(() => target.IsActive = isOn);
        }
    }

    /// <summary>
    /// Recognizes the digital-output M-codes and the port they act on:
    /// M62/M63 (on/off, synchronized with motion) and M64/M65 (on/off, immediate).
    /// <para>
    /// For the synchronized pair the controller applies the change when the queued
    /// motion reaches it, so the button can lead the machine slightly.
    /// </para>
    /// </summary>
    internal static bool TryParseAuxOutputCommand(string command, out int port, out bool isOn)
    {
        port = 0;
        isOn = false;

        // Walked word by word rather than parsed into a dictionary, so the P that
        // belongs to the M-code is the one used. A single block can legally hold more
        // than one P — "M65P1 G4P0.2" sets port 1 and dwells 0.2s — and taking the
        // last P there would report the wrong port.
        var text = StripComments(command);
        bool? pendingOn = null;

        foreach (Match word in AuxWordRegex.Matches(text))
        {
            var letter = char.ToUpperInvariant(word.Groups[1].Value[0]);
            if (!float.TryParse(word.Groups[2].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value))
                continue;

            switch (letter)
            {
                case 'M':
                    // A later M-code starts a new command; drop any half-read one.
                    pendingOn = (int)value switch
                    {
                        62 or 64 => true,
                        63 or 65 => false,
                        _ => null
                    };
                    break;

                case 'P' when pendingOn.HasValue:
                    if (value < 0) return false;
                    port = (int)value;
                    isOn = pendingOn.Value;
                    return true;

                case 'G':
                    // A G-word before the P means the M-code had no port of its own.
                    pendingOn = null;
                    break;
            }
        }

        return false;
    }

    private static readonly Regex AuxWordRegex =
        new(@"([A-Z])\s*(-?\d*\.?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string StripComments(string line)
    {
        var text = Regex.Replace(line, @"\(.*?\)", "");
        int semicolon = text.IndexOf(';');
        return semicolon >= 0 ? text[..semicolon] : text;
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

        // For DOUT pins, query $pinstate to confirm actual state — but not mid-job,
        // where an extra command would break the streamer's byte accounting. The
        // OnCommandSent handler above has already set the state from the command, so
        // skipping the confirmation only costs the read-back.
        if (btn.StateKey.StartsWith("DOUT:") && !_commManager.IsStreaming)
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
