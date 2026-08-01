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

    public bool TryOpenOutput(int pin, bool initialValue) => true;
    public void Write(int pin, bool value) { }
    public void CloseAll() { }
    public void Dispose() { }
}
