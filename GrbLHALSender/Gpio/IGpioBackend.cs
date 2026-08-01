using System;

namespace GrbLHALSender.Gpio;

/// <summary>
/// The pin-level operations the output service needs, behind an interface so the
/// app still builds and runs where there is no GPIO — Windows and macOS desktop
/// builds, and any Linux box without a gpiochip.
/// </summary>
public interface IGpioBackend : IDisposable
{
    bool IsAvailable { get; }

    /// <summary>Why <see cref="IsAvailable"/> is false, for the status line.</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// Claims a pin as an output already holding <paramref name="initialValue"/>.
    /// The initial value is part of the open so the pin never spends a moment at
    /// whatever the output register happened to hold — enough to click a relay.
    /// </summary>
    bool TryOpenOutput(int pin, bool initialValue);

    void Write(int pin, bool value);

    void CloseAll();
}
