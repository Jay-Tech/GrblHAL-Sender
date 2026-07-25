namespace GrbLHALSender.States;

/// <summary>
/// Spindle running state as reported by the controller's accessory ("A:") field.
/// Single source of truth for the spindle buttons — they render this, they do not
/// hold a checked state of their own, so the UI cannot claim the spindle is running
/// when the controller never accepted the command.
/// </summary>
public enum SpindleDirection
{
    Off,
    CW,
    CCW
}
