namespace GrbLHALSender.Gpio;

/// <summary>
/// Stands in wherever there is no GPIO. Every call succeeds and does nothing, so the
/// service, view model and bindings behave identically on a dev desktop — the buttons
/// just don't switch anything.
/// </summary>
internal sealed class NullGpioBackend : IGpioBackend
{
    public NullGpioBackend(string? reason = null) => UnavailableReason = reason;

    public bool IsAvailable => false;
    public string? UnavailableReason { get; }

    // Mirrors the Pi header range rather than accepting anything: a config edited on a
    // desktop should keep the same outputs when it reaches the machine, instead of silently
    // dropping pins the real hardware rejects.
    public bool IsValidPin(int pin) => pin >= 2 && pin <= 27;

    public bool TryOpenOutput(int pin, bool initialValue) => true;
    public void Write(int pin, bool value) { }
    public void CloseAll() { }
    public void Dispose() { }
}
