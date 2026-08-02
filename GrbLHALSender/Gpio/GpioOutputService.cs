using Avalonia.Threading;
using GrbLHALSender.Configuration;
using GrbLHALSender.States;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace GrbLHALSender.Gpio;

/// <summary>
/// Drives Raspberry Pi GPIO outputs — relay boards for shop lights, dust collection and
/// similar. Each output is Off / Auto / On: Auto follows a machine signal, On and Off are
/// the manual override for cleanup before and after a job.
/// <para>
/// Deliberately not for anything safety-related or time-critical. E-stop and safety door
/// belong hardwired to the controller, where they do not depend on a userland app being
/// responsive.
/// </para>
/// <para>
/// This is the adapter: config, machine state, the clock and the tick timer. The state
/// machine itself is <see cref="GpioOutputController"/>.
/// </para>
/// <para>
/// Single-threaded by construction: <see cref="MachineStateService"/> raises its changes
/// on the UI thread via its own DispatcherTimer, config load/save happens on the UI
/// thread, and the tick is a DispatcherTimer. Nothing here needs a lock — keep it that way.
/// </para>
/// </summary>
public class GpioOutputService : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly MachineStateService _machineState;

    private IGpioBackend _backend = new NullGpioBackend();
    private GpioOutputController? _controller;
    private DispatcherTimer? _tickTimer;
    private string _appliedSignature = "";
    private bool _subscribed;

    public ObservableCollection<GpioOutput> Outputs { get; } = new();

    public bool IsAvailable => _backend.IsAvailable;
    public string? UnavailableReason => _backend.UnavailableReason;

    /// <summary>Fires when outputs are rebuilt, so the view model can re-attach commands.</summary>
    public event EventHandler? OutputsRebuilt;

    /// <summary>Fires when the device is lost mid-session, so the status line can follow.</summary>
    public event EventHandler? DeviceStateChanged;

    public GpioOutputService(ConfigManager configManager, MachineStateService machineState)
    {
        _configManager = configManager;
        _machineState = machineState;
        _configManager.OnConfigLoaded += OnConfigChanged;
        // Rebuild on save too, so edits in the config screen take effect without a
        // restart. Unchanged definitions are filtered by the signature check below, so
        // saving an unrelated setting will not cycle a running relay.
        _configManager.OnConfigSaved += OnConfigChanged;
    }

    private void OnConfigChanged(object? sender, GHalSenderConfig e) => Apply(e.Gpio ?? new GpioConfig());

    private void Apply(GpioConfig config)
    {
        var signature = BuildSignature(config);
        if (signature == _appliedSignature) return;
        _appliedSignature = signature;

        TearDown();

        if (!config.Enabled) return;

        _backend = CreateBackend(config);
        _controller = new GpioOutputController(_backend, IsSourceActive, () => _machineState.SpindleRpm);

        var usedPins = new HashSet<int>();
        foreach (var cfg in config.Outputs)
        {
            if (!_backend.IsValidPin(cfg.Pin)) continue;
            // A pin listed twice would give two buttons fighting over one relay.
            if (!usedPins.Add(cfg.Pin)) continue;

            Outputs.Add(_controller.Add(cfg));
        }

        var now = DateTime.UtcNow;
        foreach (var output in _controller.Outputs)
            _controller.ApplyMode(output, now);

        Subscribe();
        StartTicking();
        OutputsRebuilt?.Invoke(this, EventArgs.Empty);
    }

    private static IGpioBackend CreateBackend(GpioConfig config)
    {
        if (config.Device == GpioDeviceType.UsbSerial)
            return new PicoGpioBackend(config.PortName);

        if (!OperatingSystem.IsLinux())
            return new NullGpioBackend(
                "The Pi header needs Linux. On this machine, use a USB GPIO device instead.");

        if (!File.Exists("/dev/gpiochip0"))
            return new NullGpioBackend("No /dev/gpiochip0 — this machine has no GPIO header.");

        return new LinuxGpioBackend();
    }

    /// <summary>
    /// Everything that changes what the outputs are or how they behave. Mode is left out
    /// on purpose: the user flipping a button writes config, and rebuilding on that would
    /// close and reopen the pin under a live relay.
    /// </summary>
    private static string BuildSignature(GpioConfig config)
    {
        var sb = new StringBuilder();
        sb.Append(config.Enabled).Append('|')
          .Append(config.Device).Append('|').Append(config.PortName).Append('|');
        foreach (var o in config.Outputs)
            sb.Append(o.Pin).Append(':').Append(o.Name).Append(':')
              .Append(o.ActiveHigh).Append(':').Append(o.Follow).Append(':')
              .Append(o.OffDelaySeconds).Append(':').Append(o.MinSpindleRpm).Append(';');
        return sb.ToString();
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _machineState.PropertyChanged += OnMachineStateChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _machineState.PropertyChanged -= OnMachineStateChanged;
        _subscribed = false;
    }

    private void OnMachineStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_controller == null) return;
        var now = DateTime.UtcNow;

        switch (e.PropertyName)
        {
            case nameof(MachineStateService.SpindleDirection):
                _controller.EvaluateFollow(GpioFollowSource.Spindle, now);
                break;

            // Speed has to be watched as well as direction. An ATC macro turns the spindle
            // at a low speed to swap the holder and the program then raises it to cutting
            // speed with the spindle never stopping — the direction never changes across
            // that, so a threshold that only re-evaluated on direction would stay latched
            // at whatever it decided during the tool change.
            case nameof(MachineStateService.SpindleRpm):
                _controller.EvaluateFollow(GpioFollowSource.Spindle, now);
                break;

            case nameof(MachineStateService.Connected):
                _controller.EvaluateFollow(GpioFollowSource.Connected, now);

                // Status reports stop arriving when the link drops, so SpindleDirection
                // freezes at whatever it last was. Left alone, a spindle that happened to
                // be running when comms died would hold the dust collector on forever.
                // Routed through the normal path so the off-delay still applies.
                if (!_machineState.Connected)
                    _controller.EvaluateFollow(GpioFollowSource.Spindle, now, forceInactive: true);
                break;
        }
    }

    private bool IsSourceActive(GpioFollowSource source) => source switch
    {
        GpioFollowSource.Spindle => _machineState.Connected &&
                                    _machineState.SpindleDirection != SpindleDirection.Off,
        GpioFollowSource.Connected => _machineState.Connected,
        _ => false,
    };

    public void CycleMode(GpioOutput output) => _controller?.CycleMode(output, DateTime.UtcNow);

    /// <summary>
    /// Rebuilds against the current config even though nothing in it changed.
    /// <para>
    /// Opening a device can fail for reasons that clear up on their own — an IDE still
    /// holding the serial port, a USB device not enumerated yet, a cable moved to another
    /// socket. Without this the first failure was permanent, because the signature check
    /// correctly saw identical config and skipped the rebuild, so the only ways out were
    /// editing a setting or restarting the app.
    /// </para>
    /// </summary>
    public void Reconnect()
    {
        _appliedSignature = "";
        Apply(_configManager.GHalSenderConfig?.Gpio ?? new GpioConfig());
    }

    private void StartTicking()
    {
        if (_tickTimer != null || Outputs.Count == 0) return;

        // One shared 1 Hz sweep rather than a timer per output. The delays here are tens
        // of seconds, so second-granularity is well inside what matters.
        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += OnTick;
        _tickTimer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _controller?.Tick(DateTime.UtcNow);
        CheckDeviceHealth();
    }

    /// <summary>
    /// Greys the buttons if the device has gone since startup — a USB adapter unplugged, or
    /// a write that failed and tore the connection down.
    /// <para>
    /// Readiness was previously decided once at startup and never revisited, which left the
    /// buttons looking live while they switched nothing. A control that appears to work and
    /// does not is worse than one that is visibly unavailable.
    /// </para>
    /// </summary>
    private void CheckDeviceHealth()
    {
        if (_controller == null || _backend.IsAvailable) return;

        var changed = false;
        foreach (var output in Outputs)
        {
            if (!output.IsPinReady) continue;
            output.IsPinReady = false;
            // The relay is not ours to command any more; the device's own watchdog drops it.
            output.IsOn = false;
            changed = true;
        }

        if (changed) DeviceStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TearDown()
    {
        _tickTimer?.Stop();
        if (_tickTimer != null) _tickTimer.Tick -= OnTick;
        _tickTimer = null;

        Unsubscribe();

        _controller?.AllOff();
        _controller = null;
        Outputs.Clear();

        _backend.Dispose();
        _backend = new NullGpioBackend();
    }

    /// <summary>
    /// Drives every output off and releases the pins. Called from the app's shutdown
    /// handler so closing the sender does not leave the dust collector running.
    /// </summary>
    public void Stop()
    {
        TearDown();
        _appliedSignature = "";
    }

    public void Dispose()
    {
        _configManager.OnConfigLoaded -= OnConfigChanged;
        _configManager.OnConfigSaved -= OnConfigChanged;
        Stop();
    }
}
