namespace GrbLHALSender.Gamepad;

public enum GamepadAction
{
    None = 0,

    // Jog step/rate cycling
    JogStepUp,
    JogStepDown,
    JogRateUp,
    JogRateDown,

    // Machine control
    FeedHold,
    CycleStart,
    JogCancel,
    Home,
    Unlock,
    ClearAlarm,
    EStop,
    SafetyDoor,

    // Feed override
    FeedOverridePlus,
    FeedOverrideMinus,
    FeedOverrideReset,

    // Spindle override
    SpindleOverridePlus,
    SpindleOverrideMinus,
    SpindleOverrideReset,

    // Step jog (D-pad)
    JogXPos,
    JogXNeg,
    JogYPos,
    JogYNeg,
    JogZPos,
    JogZNeg,
}
