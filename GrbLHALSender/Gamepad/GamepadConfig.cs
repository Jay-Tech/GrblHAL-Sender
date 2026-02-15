using System.Collections.Generic;

namespace GrbLHALSender.Gamepad;

public class GamepadConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Dead zone threshold for thumbstick axes (0-32767).
    /// </summary>
    public int DeadZone { get; set; } = 8000;

    /// <summary>
    /// SDL event poll interval in milliseconds (~50Hz).
    /// </summary>
    public int PollIntervalMs { get; set; } = 20;

    /// <summary>
    /// Interval between proportional jog commands in milliseconds (~13Hz).
    /// </summary>
    public int JogCommandIntervalMs { get; set; } = 75;

    /// <summary>
    /// Base jog distance (mm) per tick at full stick deflection.
    /// </summary>
    public double JogIncrementMm { get; set; } = 2.0;

    /// <summary>
    /// Base jog distance (inches) per tick at full stick deflection.
    /// </summary>
    public double JogIncrementInch { get; set; } = 0.08;

    /// <summary>
    /// Response curve exponent. 1.0=linear, 2.0=quadratic.
    /// Higher values give finer control at low deflections.
    /// </summary>
    public double ResponseCurveExponent { get; set; } = 1.5;

    /// <summary>
    /// Threshold for triggers to fire their mapped action (0-32767).
    /// Triggers report 0 (released) to 32767 (fully pressed).
    /// Action fires once when crossing above threshold, resets when falling below.
    /// </summary>
    public int TriggerThreshold { get; set; } = 16000;

    /// <summary>
    /// Maps SDL axis names to CNC axis letters for proportional jog.
    /// Keys: LeftX, LeftY, RightX, RightY
    /// Values: X, Y, Z, A, B, C
    /// Note: LeftTrigger/RightTrigger should use TriggerMapping instead.
    /// </summary>
    public Dictionary<string, string> AxisMapping { get; set; } = new()
    {
        { "LeftX", "X" },
        { "LeftY", "Y" },
        { "RightY", "Z" },
    };

    /// <summary>
    /// Per-axis inversion. SDL Y-axes are inverted (up = negative).
    /// </summary>
    public Dictionary<string, bool> AxisInverted { get; set; } = new()
    {
        { "LeftX", false },
        { "LeftY", true },
        { "RightY", true },
    };

    /// <summary>
    /// Maps trigger axes to GamepadAction names.
    /// Triggers act as analog buttons — action fires when crossing TriggerThreshold.
    /// Keys: LeftTrigger (LT), RightTrigger (RT)
    /// Values: any GamepadAction name
    /// </summary>
    public Dictionary<string, string> TriggerMapping { get; set; } = new()
    {
        { "LeftTrigger", "JogRateDown" },
        { "RightTrigger", "JogRateUp" },
    };

    /// <summary>
    /// Maps SDL button names to GamepadAction names.
    /// Stored as strings for clean JSON serialization.
    ///
    /// Button names (SDL3 positional names with Xbox/PlayStation equivalents):
    ///   South  = Xbox A  / PS Cross     (bottom face button)
    ///   East   = Xbox B  / PS Circle    (right face button)
    ///   West   = Xbox X  / PS Square    (left face button)
    ///   North  = Xbox Y  / PS Triangle  (top face button)
    ///   LeftShoulder  = LB / L1
    ///   RightShoulder = RB / R1
    ///   LeftStick     = L3 (left stick click)
    ///   RightStick    = R3 (right stick click)
    ///   DpadUp, DpadDown, DpadLeft, DpadRight
    ///   Start, Back (Select), Guide
    ///
    /// Xbox aliases are also accepted:
    ///   A=South, B=East, X=West, Y=North, LB=LeftShoulder, RB=RightShoulder,
    ///   L3=LeftStick, R3=RightStick, Select=Back
    /// </summary>
    public Dictionary<string, string> ButtonMapping { get; set; } = new()
    {
        { "South", "JogCancel" },
        { "East", "CycleStart" },
        { "West", "FeedHold" },
        { "North", "Home" },
        { "LeftShoulder", "JogStepDown" },
        { "RightShoulder", "JogStepUp" },
        { "DpadUp", "JogYPos" },
        { "DpadDown", "JogYNeg" },
        { "DpadLeft", "JogXNeg" },
        { "DpadRight", "JogXPos" },
        { "LeftStick", "FeedOverrideReset" },
        { "RightStick", "SpindleOverrideReset" },
        { "Start", "Unlock" },
        { "Back", "EStop" },
    };
}
