using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace GrbLHALSender.Gpio;

/// <summary>
/// Live state for one configured output. Bound directly by the workspace buttons and
/// the config editor; the command is attached by the view model, the same way
/// <c>AuxOutputItem</c> is decorated in <c>AuxOutputViewModel</c>.
/// </summary>
public partial class GpioOutput : ObservableObject
{
    public GpioOutputConfig Config { get; init; } = new();

    [ObservableProperty] private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeLabel))]
    private GpioOutputMode _mode = GpioOutputMode.Auto;

    /// <summary>True when the load is energised, whatever the pin polarity is.</summary>
    [ObservableProperty] private bool _isOn;

    /// <summary>
    /// False when the pin could not be claimed. The button stays visible and greyed
    /// rather than vanishing, so a typo in the pin number is obvious instead of
    /// silently costing you a button.
    /// </summary>
    [ObservableProperty] private bool _isPinReady;

    public bool HasFollow => Config.Follow != GpioFollowSource.None;

    /// <summary>Deadline for a delayed switch-off; null when none is pending.</summary>
    internal DateTime? PendingOffUtc;

    public string ModeLabel => Mode switch
    {
        GpioOutputMode.On => "ON",
        GpioOutputMode.Off => "OFF",
        _ => "AUTO",
    };

    [JsonIgnore]
    public ICommand? ToggleCommand { get; set; }
}
