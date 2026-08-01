using System;
using System.Collections.Generic;

namespace GrbLHALSender.Gpio;

/// <summary>
/// The output state machine, with no dependency on Avalonia, the config file or the
/// machine state service. "Now" is passed in and the follow sources are read through a
/// delegate, so all of the behaviour that is easy to get subtly wrong — off delays,
/// mode cycling, pin polarity — can be tested directly.
/// <para>
/// <see cref="GpioOutputService"/> is the adapter that feeds this from the real clock,
/// the real config and the real machine state.
/// </para>
/// </summary>
internal sealed class GpioOutputController
{
    private readonly IGpioBackend _backend;
    private readonly Func<GpioFollowSource, bool> _isSourceActive;

    public List<GpioOutput> Outputs { get; } = new();

    public GpioOutputController(IGpioBackend backend, Func<GpioFollowSource, bool> isSourceActive)
    {
        _backend = backend;
        _isSourceActive = isSourceActive;
    }

    /// <summary>
    /// Claims the pin and registers the output. The pin is opened already holding the
    /// de-energised level for its polarity, so it never sits at an arbitrary level long
    /// enough to click a relay.
    /// </summary>
    public GpioOutput Add(GpioOutputConfig config)
    {
        var output = new GpioOutput
        {
            Config = config,
            Name = string.IsNullOrWhiteSpace(config.Name) ? $"GPIO {config.Pin}" : config.Name,
            // An output that follows something never comes back On after a restart;
            // see GpioOutputConfig.Mode.
            Mode = config.Mode == GpioOutputMode.On && config.Follow != GpioFollowSource.None
                ? GpioOutputMode.Auto
                : config.Mode,
        };

        output.IsPinReady = _backend.TryOpenOutput(config.Pin, PinLevelFor(config, on: false));
        Outputs.Add(output);
        return output;
    }

    /// <summary>
    /// Pushes the current state of one follow source to every Auto output tracking it.
    /// <paramref name="forceInactive"/> covers comms loss, where the last known source
    /// value is stale and must not be trusted.
    /// </summary>
    public void EvaluateFollow(GpioFollowSource source, DateTime now, bool forceInactive = false)
    {
        var active = !forceInactive && _isSourceActive(source);
        foreach (var output in Outputs)
        {
            if (output.Config.Follow != source) continue;
            if (output.Mode != GpioOutputMode.Auto) continue;
            RequestState(output, active, now);
        }
    }

    /// <summary>Steps Off → Auto → On → Off, skipping Auto where it means nothing.</summary>
    public void CycleMode(GpioOutput output, DateTime now)
    {
        output.Mode = NextMode(output.Mode, output.HasFollow);
        ApplyMode(output, now);

        // Remember the choice in the config object, but do not save the file from here:
        // this runs on every button tap and SaveConfig writes and fsyncs the whole file.
        // The shutdown handler persists it.
        output.Config.Mode = output.Mode;
    }

    internal static GpioOutputMode NextMode(GpioOutputMode current, bool hasFollow) => current switch
    {
        GpioOutputMode.Off => hasFollow ? GpioOutputMode.Auto : GpioOutputMode.On,
        GpioOutputMode.Auto => GpioOutputMode.On,
        _ => GpioOutputMode.Off,
    };

    public void ApplyMode(GpioOutput output, DateTime now)
    {
        switch (output.Mode)
        {
            case GpioOutputMode.On:
                output.PendingOffUtc = null;
                Write(output, true);
                break;

            case GpioOutputMode.Off:
                // Immediate. Someone tapping Off wants it off now, not in fifteen seconds.
                output.PendingOffUtc = null;
                Write(output, false);
                break;

            case GpioOutputMode.Auto:
                RequestState(output, _isSourceActive(output.Config.Follow), now);
                break;
        }
    }

    private void RequestState(GpioOutput output, bool wanted, DateTime now)
    {
        if (wanted)
        {
            // A fresh demand inside the delay window cancels the pending switch-off. This
            // is what collapses a program full of tool changes and M3/M5 pairs into one
            // continuous run instead of cycling a contactor every few seconds.
            output.PendingOffUtc = null;
            Write(output, true);
            return;
        }

        if (!output.IsOn) return;

        if (output.Config.OffDelaySeconds <= 0)
        {
            Write(output, false);
            return;
        }

        // ??= so repeated off-requests do not keep pushing the deadline out; it is
        // measured from the first one.
        output.PendingOffUtc ??= now.AddSeconds(output.Config.OffDelaySeconds);
    }

    /// <summary>Drops any output whose delayed switch-off has come due.</summary>
    public void Tick(DateTime now)
    {
        foreach (var output in Outputs)
        {
            if (output.PendingOffUtc is not { } due || now < due) continue;
            output.PendingOffUtc = null;
            Write(output, false);
        }
    }

    /// <summary>De-energises everything, cancelling pending delays. Used on teardown.</summary>
    public void AllOff()
    {
        foreach (var output in Outputs)
        {
            output.PendingOffUtc = null;
            Write(output, false);
        }
    }

    private void Write(GpioOutput output, bool on)
    {
        if (!output.IsPinReady) return;
        if (output.IsOn == on) return;

        _backend.Write(output.Config.Pin, PinLevelFor(output.Config, on));
        output.IsOn = on;
    }

    /// <summary>
    /// Maps "load energised" to a pin level. On an active-low board the off state is a
    /// HIGH pin, which is why this cannot just be the boolean itself.
    /// </summary>
    internal static bool PinLevelFor(GpioOutputConfig config, bool on) => on == config.ActiveHigh;
}
