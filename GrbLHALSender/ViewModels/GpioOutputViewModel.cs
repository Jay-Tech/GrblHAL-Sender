using GrbLHALSender.Configuration;
using GrbLHALSender.Gpio;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels;

/// <summary>
/// Config editing and button wiring for the Pi GPIO relay outputs. The live state and
/// all the switching logic belong to <see cref="GpioOutputService"/>; this only edits
/// the definitions and hands the buttons a command.
/// </summary>
public class GpioOutputViewModel : ViewModelBase, ISavableViewModel
{
    /// <summary>
    /// Offered in the order they are handed out. Skips the pins with another job on the
    /// 40-pin header: 0/1 (HAT EEPROM), 2/3 (I2C, permanently pulled up), 7-11 (SPI) and
    /// 14/15 (UART). All of these boot with a pull-down, so an active-high board reads
    /// off until the app claims the pin.
    /// </summary>
    private static readonly int[] SuggestedPins = [17, 27, 22, 23, 24, 25, 5, 6, 12, 13, 16, 19, 20, 21, 26];

    private readonly ConfigManager _configManager;
    private readonly GpioOutputService _service;
    private bool _isEnabled;

    /// <summary>Live outputs — what the workspace buttons bind to.</summary>
    public ObservableCollection<GpioOutput> Outputs => _service.Outputs;

    /// <summary>
    /// Drives the divider above the aux column. Re-raised on rebuild rather than tracking
    /// collection changes: outputs are only ever replaced wholesale, never appended to.
    /// </summary>
    public bool HasOutputs => _service.Outputs.Count > 0;

    /// <summary>
    /// Definition rows for the config screen. Holds the same instances as the loaded
    /// config and as <see cref="GpioOutput.Config"/>, so a mode tapped on a workspace
    /// button is already in the object that gets serialised.
    /// </summary>
    public ObservableCollection<GpioOutputConfig> EditableOutputs { get; } = [];

    public IReadOnlyList<GpioFollowSource> FollowSources { get; } =
        Enum.GetValues<GpioFollowSource>();

    public bool IsEnabled
    {
        get => _isEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

    /// <summary>Surfaces why nothing switches when the hardware is not reachable.</summary>
    public string StatusText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "";

    public ICommand AddOutputCommand { get; }
    public ICommand RemoveOutputCommand { get; }

    public GpioOutputViewModel(ConfigManager configManager, GpioOutputService service)
    {
        _configManager = configManager;
        _service = service;

        AddOutputCommand = ReactiveCommand.Create(AddOutput);
        RemoveOutputCommand = ReactiveCommand.Create<GpioOutputConfig>(RemoveOutput);

        _configManager.OnConfigLoaded += OnConfigLoaded;
        _service.OutputsRebuilt += OnOutputsRebuilt;

        // The service is a singleton resolved before this view model, so its outputs may
        // already exist by the time we get here.
        AttachCommands();
        if (_configManager.GHalSenderConfig?.Gpio is { } gpio) LoadFrom(gpio);
    }

    private void OnConfigLoaded(object? sender, GHalSenderConfig e) => LoadFrom(e.Gpio ?? new GpioConfig());

    private void LoadFrom(GpioConfig config)
    {
        IsEnabled = config.Enabled;
        EditableOutputs.Clear();
        foreach (var output in config.Outputs)
            EditableOutputs.Add(output);
        UpdateStatus();
    }

    private void OnOutputsRebuilt(object? sender, EventArgs e)
    {
        AttachCommands();
        UpdateStatus();
        this.RaisePropertyChanged(nameof(HasOutputs));
    }

    private void AttachCommands()
    {
        foreach (var output in _service.Outputs)
        {
            var target = output;
            target.ToggleCommand ??= ReactiveCommand.Create(() => _service.CycleMode(target));
        }
    }

    private void UpdateStatus()
    {
        if (!IsEnabled)
        {
            StatusText = "GPIO outputs are disabled.";
            return;
        }

        if (!_service.IsAvailable)
        {
            StatusText = _service.UnavailableReason is { Length: > 0 } reason
                ? $"GPIO unavailable — {reason}"
                : "GPIO unavailable on this machine.";
            return;
        }

        StatusText = $"{_service.Outputs.Count} output(s) active.";
    }

    private void AddOutput()
    {
        var taken = EditableOutputs.Select(o => o.Pin).ToHashSet();
        var pin = SuggestedPins.FirstOrDefault(p => !taken.Contains(p), -1);
        if (pin < 0) return;

        EditableOutputs.Add(new GpioOutputConfig
        {
            Name = $"Output {EditableOutputs.Count + 1}",
            Pin = pin,
        });
    }

    private void RemoveOutput(GpioOutputConfig output) => EditableOutputs.Remove(output);

    public void Save()
    {
        var config = _configManager.GHalSenderConfig;
        if (config == null) return;

        config.Gpio ??= new GpioConfig();
        config.Gpio.Enabled = IsEnabled;
        config.Gpio.Outputs = EditableOutputs.ToList();
    }
}
