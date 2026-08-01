using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GrbLHALSender.Gpio;

/// <summary>
/// What a GPIO output tracks when it is left in <see cref="GpioOutputMode.Auto"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GpioFollowSource
{
    /// <summary>Manual only — the output has no Auto mode, just On and Off.</summary>
    None,

    /// <summary>
    /// Follows the spindle as reported by the controller's accessory ("A:") field.
    /// Preferred over watching M3/M5 in the stream: it reflects what the machine is
    /// actually doing, so it picks up a spindle started from the console and it stays
    /// honest when a job aborts part way through.
    /// </summary>
    Spindle,

    /// <summary>Follows the controller connection — useful for shop lighting.</summary>
    Connected,
}

/// <summary>
/// Tri-state so a relay can be driven by hand without fighting its follow rule.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GpioOutputMode
{
    Off,
    Auto,
    On,
}

public class GpioConfig
{
    /// <summary>
    /// Off by default. Nothing touches a GPIO pin until the user opts in, so a
    /// non-Pi machine (or a Pi wired for something else) is never poked at startup.
    /// </summary>
    public bool Enabled { get; set; } = false;

    public List<GpioOutputConfig> Outputs { get; set; } = new();
}

/// <summary>
/// Observable because the config editor binds these rows two-way, and the workspace
/// buttons and the editor share the same instances — an edit in one has to show in
/// the other.
/// </summary>
public partial class GpioOutputConfig : ObservableObject
{
    [ObservableProperty] private string _name = "Output";

    /// <summary>
    /// BCM pin number. 0/1 are reserved for HAT ID EEPROM and 28+ are not on the
    /// 40-pin header, so the service only accepts 2-27.
    /// </summary>
    [ObservableProperty] private int _pin = -1;

    /// <summary>
    /// True when the driver board switches the load on a HIGH pin.
    /// <para>
    /// Prefer active-high hardware. BCM 9-27 boot with a pull-down, so an active-high
    /// board reads "off" while the Pi boots, while the app is starting, and after it
    /// exits or crashes — the fail-safe state costs nothing. Active-low boards invert
    /// all of that and want an external pull-up to stay safe.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _activeHigh = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowsSpindle))]
    [NotifyPropertyChangedFor(nameof(HasFollow))]
    private GpioFollowSource _follow = GpioFollowSource.None;

    /// <summary>Gates the editor fields that only mean something for a given source.</summary>
    [JsonIgnore] public bool FollowsSpindle => Follow == GpioFollowSource.Spindle;

    [JsonIgnore] public bool HasFollow => Follow != GpioFollowSource.None;

    /// <summary>
    /// Spindle speed at or above which <see cref="GpioFollowSource.Spindle"/> counts as
    /// running. 0 disables the check, so any spindle-on state qualifies.
    /// <para>
    /// Exists for ATC systems — a RapidChange and similar turn the spindle at a low speed
    /// to thread and unthread the holder, which is a spindle-on state with no cutting and
    /// no chips. Without a threshold the dust collector fires on every tool change.
    /// </para>
    /// <para>
    /// Compared against the *programmed* speed (the S word), not tacho feedback: it steps
    /// cleanly when the macro commands a speed instead of ramping through the threshold on
    /// every spin-up and spin-down.
    /// </para>
    /// </summary>
    [ObservableProperty] private int _minSpindleRpm;

    /// <summary>
    /// Seconds to keep the output on after its follow source goes inactive.
    /// <para>
    /// Two jobs: it clears the hose of chips still in flight after a cut, and it stops
    /// a program full of tool changes and M3/M5 pairs from cycling a contactor every
    /// few seconds. Only applies to Auto — an explicit Off is immediate.
    /// </para>
    /// </summary>
    [ObservableProperty] private double _offDelaySeconds = 15;

    /// <summary>
    /// Last mode the user left this output in. Restored on load, except that an
    /// output with a follow source never comes back as <see cref="GpioOutputMode.On"/>
    /// — returning from a restart or a power cut with the dust collector latched on is
    /// not what anyone means by "remember my setting". Manual-only outputs (shop
    /// lights) do restore On.
    /// </summary>
    [ObservableProperty] private GpioOutputMode _mode = GpioOutputMode.Auto;
}
