using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace GrbLHALSender.Gpio;

/// <summary>
/// Drives relay outputs on a PICOGPIO device attached to the host over USB, so shop outputs
/// work on a mini PC or a Mac rather than only on a Pi header. Protocol lives in
/// <c>docs/pico-gpio-protocol.md</c>.
/// <para>
/// The port is always supplied explicitly — this never scans. A grblHAL controller and a
/// Pico both enumerate as <c>/dev/ttyACM*</c> and are indistinguishable by name, and an
/// identify string written at a controller is at best noise in its console.
/// </para>
/// </summary>
internal sealed class PicoGpioBackend : IGpioBackend
{
    private const int HandshakeTimeoutMs = 1500;

    private readonly object _lock = new();
    private readonly HashSet<int> _claimed = new();

    private SerialPort? _port;
    private PicoBanner? _banner;
    private Timer? _heartbeat;

    public bool IsAvailable => _port != null && _banner != null;
    public string? UnavailableReason { get; private set; }

    public PicoGpioBackend(string portName, int baudRate = 115200)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            UnavailableReason = "No serial port selected for the GPIO device.";
            return;
        }

        try
        {
            var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                // Reads are only used for the handshake and for draining acks; both are
                // bounded, and nothing here should ever block the UI thread for long.
                ReadTimeout = HandshakeTimeoutMs,
                WriteTimeout = 500,
                NewLine = "\n",
                Handshake = Handshake.None,
                DtrEnable = true,
            };
            port.Open();

            if (!Identify(port, out var banner, out var reason))
            {
                UnavailableReason = reason;
                try { port.Dispose(); } catch { }
                return;
            }

            _port = port;
            _banner = banner;
            StartHeartbeat();
        }
        catch (Exception ex)
        {
            // Port in use by something else, gone, or no permission (the dialout group on
            // Linux). The message reaches the GPIO config screen.
            UnavailableReason = $"{portName}: {ex.Message}";
        }
    }

    /// <summary>
    /// Sends <c>?</c> and requires a banner this build understands before anything is ever
    /// driven. A device that does not answer correctly is left completely alone.
    /// </summary>
    private static bool Identify(SerialPort port, out PicoBanner? banner, out string? reason)
    {
        banner = null;
        reason = null;

        try
        {
            port.DiscardInBuffer();
            port.WriteLine("?");

            // A device may emit its reset banner before the reply, and USB CDC can deliver a
            // partial line first, so read a few lines rather than trusting the first.
            var deadline = Environment.TickCount64 + HandshakeTimeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                string line;
                try { line = port.ReadLine(); }
                catch (TimeoutException) { break; }

                if (!PicoBanner.TryParse(line, out var parsed)) continue;

                if (!parsed.IsSupported)
                {
                    reason = $"Device speaks PICOGPIO v{parsed.Version}; this build supports " +
                             $"v{PicoBanner.SupportedVersion}.";
                    return false;
                }

                banner = parsed;
                return true;
            }

            reason = "No PICOGPIO device answered on that port — check it is the adapter and " +
                     "not the controller.";
            return false;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public bool IsValidPin(int pin) => _banner?.IsValidPin(pin) ?? false;

    public bool TryOpenOutput(int pin, bool initialValue)
    {
        if (!IsAvailable || !IsValidPin(pin)) return false;

        // Level is part of the claim so the pin is never briefly at whatever the register
        // held — long enough to click a relay.
        if (!Send($"C {pin.ToString(CultureInfo.InvariantCulture)} {(initialValue ? 1 : 0)}"))
            return false;

        lock (_lock) _claimed.Add(pin);
        return true;
    }

    public void Write(int pin, bool value)
    {
        if (!IsAvailable) return;
        lock (_lock) { if (!_claimed.Contains(pin)) return; }

        Send($"O {pin.ToString(CultureInfo.InvariantCulture)} {(value ? 1 : 0)}");
    }

    public void CloseAll()
    {
        if (IsAvailable) Send("X");
        lock (_lock) _claimed.Clear();
    }

    /// <summary>
    /// Writes a command without waiting for its <c>ok</c>. Switching happens seconds apart
    /// and this is called from the UI thread, so a round trip per relay would be paid in
    /// interface latency for no benefit — a device that stops responding is caught by its
    /// own watchdog, which drops the outputs safely without the host being involved.
    /// </summary>
    private bool Send(string command)
    {
        lock (_lock)
        {
            var port = _port;
            if (port == null) return false;

            try
            {
                port.WriteLine(command);
                DrainResponses(port);
                return true;
            }
            catch (Exception ex)
            {
                // The cable went away mid-write. Tear down rather than retrying: the device
                // watchdog will drop the outputs on its own, and the config screen shows why.
                UnavailableReason = $"GPIO device write failed: {ex.Message}";
                Teardown();
                return false;
            }
        }
    }

    /// <summary>
    /// Reads whatever acks are already buffered and throws them away, so the receive buffer
    /// cannot grow unbounded over a long session. Never blocks: only what has arrived.
    /// </summary>
    private static void DrainResponses(SerialPort port)
    {
        try
        {
            while (port.BytesToRead > 0)
            {
                var chunk = port.ReadExisting();
                if (string.IsNullOrEmpty(chunk)) break;
            }
        }
        catch (Exception)
        {
            // Draining is housekeeping; a failure here is reported by the next real write.
        }
    }

    private void StartHeartbeat()
    {
        var interval = _banner?.HeartbeatMs ?? 0;
        if (interval <= 0) return;

        // Feeds the device watchdog. Without this the outputs drop a few seconds after the
        // app stops talking — which is the point of the watchdog, but only if a healthy app
        // keeps it fed.
        _heartbeat = new Timer(_ => Send("H"), null, interval, interval);
    }

    private void Teardown()
    {
        _heartbeat?.Dispose();
        _heartbeat = null;

        var port = _port;
        _port = null;
        _banner = null;
        _claimed.Clear();

        if (port == null) return;
        try { port.Dispose(); } catch { }
    }

    public void Dispose()
    {
        // Explicit release first: the watchdog would get there anyway, but only after its
        // timeout, and leaving the vac running for five seconds after quitting is sloppy.
        CloseAll();
        lock (_lock) Teardown();
    }
}
