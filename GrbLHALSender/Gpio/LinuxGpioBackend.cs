using System;
using System.Collections.Generic;
using System.Device.Gpio;

namespace GrbLHALSender.Gpio;

/// <summary>
/// System.Device.Gpio backend. Pin numbers are BCM (the library's logical scheme).
/// </summary>
internal sealed class LinuxGpioBackend : IGpioBackend
{
    private GpioController? _controller;
    private readonly HashSet<int> _openPins = new();

    public bool IsAvailable => _controller != null;
    public string? UnavailableReason { get; private set; }

    public LinuxGpioBackend()
    {
        try
        {
            // Throws when there is no usable chip: no /dev/gpiochip*, no permission
            // (the user is not in the gpio group), or — on Pi 5, whose RP1 I/O chip
            // has no memory-mapped driver — libgpiod is missing.
            _controller = new GpioController();
        }
        catch (Exception ex)
        {
            UnavailableReason = ex.Message;
        }
    }

    // 0/1 are the HAT ID EEPROM, 28+ are off the 40-pin header.
    public bool IsValidPin(int pin) => pin >= 2 && pin <= 27;

    public bool TryOpenOutput(int pin, bool initialValue)
    {
        var controller = _controller;
        if (controller == null) return false;

        try
        {
            controller.OpenPin(pin, PinMode.Output, initialValue ? PinValue.High : PinValue.Low);
            _openPins.Add(pin);
            return true;
        }
        catch (Exception)
        {
            // One unusable pin (already claimed by an overlay, out of range for this
            // board) must not take the rest of the outputs down with it.
            return false;
        }
    }

    public void Write(int pin, bool value)
    {
        var controller = _controller;
        if (controller == null || !_openPins.Contains(pin)) return;

        try
        {
            controller.Write(pin, value ? PinValue.High : PinValue.Low);
        }
        catch (Exception)
        {
            // Nothing useful to do mid-write; the service reports state from its own
            // model and a persistently dead pin shows up as a relay that never moves.
        }
    }

    public void CloseAll()
    {
        var controller = _controller;
        if (controller == null) return;

        foreach (var pin in _openPins)
        {
            try { controller.ClosePin(pin); }
            catch (Exception) { }
        }
        _openPins.Clear();
    }

    public void Dispose()
    {
        CloseAll();
        try { _controller?.Dispose(); }
        catch (Exception) { }
        _controller = null;
    }
}
