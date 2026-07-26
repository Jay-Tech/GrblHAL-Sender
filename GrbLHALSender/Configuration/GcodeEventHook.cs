using CommunityToolkit.Mvvm.ComponentModel;

namespace GrbLHALSender.Configuration;

/// <summary>
/// A user-defined rule that wraps a G-code event with extra commands:
/// everything in <see cref="PreCommands"/> is sent before the triggering line,
/// everything in <see cref="PostCommands"/> after it.
/// <para>
/// Nothing about the trigger is hard-coded — the user types the command to watch
/// for (e.g. <c>$H</c>, <c>G28</c>, <c>M6</c>) and the commands to wrap it with
/// (e.g. lift a dust shoe on an aux output before homing, drop it afterwards).
/// </para>
/// </summary>
public partial class GcodeEventHook : ObservableObject
{
    [ObservableProperty] private bool _enabled = true;

    /// <summary>Label shown in the config list only — never sent to the controller.</summary>
    [ObservableProperty] private string _name = "";

    /// <summary>
    /// One or more commands to watch for, comma separated (e.g. <c>$H,G28</c>).
    /// See <c>GcodeEventInjector.TriggerMatches</c> for how each form is matched.
    /// </summary>
    [ObservableProperty] private string _trigger = "";

    /// <summary>Commands sent before the triggering line. Separate multiple with '|' or a newline.</summary>
    [ObservableProperty] private string _preCommands = "";

    /// <summary>Commands sent after the triggering line. Separate multiple with '|' or a newline.</summary>
    [ObservableProperty] private string _postCommands = "";

    /// <summary>Inject while streaming a loaded job file.</summary>
    [ObservableProperty] private bool _applyToJob = true;

    /// <summary>Inject for commands the operator issues directly (MDI, buttons, macros, gamepad).</summary>
    [ObservableProperty] private bool _applyToManual = true;

    public GcodeEventHook Clone() => new()
    {
        Enabled = Enabled,
        Name = Name,
        Trigger = Trigger,
        PreCommands = PreCommands,
        PostCommands = PostCommands,
        ApplyToJob = ApplyToJob,
        ApplyToManual = ApplyToManual,
    };

    /// <summary>
    /// Starting points offered in the config dialog. These are templates the user
    /// picks and then edits — the aux port numbers and dwell times here are just
    /// the common case, not a fixed behavior.
    /// </summary>
    public static readonly GcodeEventHook[] Presets =
    [
        new()
        {
            Name = "Dust shoe up for homing",
            Trigger = "$H,G28",
            PreCommands = "M65P0|G4P0.2",
            PostCommands = "",
        },
        new()
        {
            Name = "Dust shoe up for tool change",
            Trigger = "M6",
            PreCommands = "M65P0|G4P0.2",
            PostCommands = "M64P0",
        },
        new()
        {
            Name = "Blank rule",
            Trigger = "",
            PreCommands = "",
            PostCommands = "",
        },
    ];
}
