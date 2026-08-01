using GrbLHALSender.Gpio;
using System;
using System.Collections.Generic;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Covers the GPIO relay state machine: the off-delay that keeps a dust collector from
/// cycling on every tool change, the tri-state manual override, and pin polarity.
/// </summary>
public class GpioOutputTests
{
    /// <summary>Records pin levels so tests can assert on what the hardware was told.</summary>
    private sealed class FakeBackend : IGpioBackend
    {
        public Dictionary<int, bool> Levels { get; } = new();
        public List<(int Pin, bool Level)> Writes { get; } = new();
        public HashSet<int> Opened { get; } = new();
        public bool FailOpen { get; set; }

        public bool IsAvailable => true;
        public string? UnavailableReason => null;

        public bool TryOpenOutput(int pin, bool initialValue)
        {
            if (FailOpen) return false;
            Opened.Add(pin);
            Levels[pin] = initialValue;
            return true;
        }

        public void Write(int pin, bool value)
        {
            Levels[pin] = value;
            Writes.Add((pin, value));
        }

        public void CloseAll() { }
        public void Dispose() { }
    }

    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static GpioOutputConfig VacConfig(double delay = 15) => new()
    {
        Name = "Vac",
        Pin = 17,
        ActiveHigh = true,
        Follow = GpioFollowSource.Spindle,
        OffDelaySeconds = delay,
        Mode = GpioOutputMode.Auto,
    };

    // --- Mode cycling ---

    [Fact]
    public void CycleMode_WithFollowSource_StepsOffAutoOn()
    {
        Assert.Equal(GpioOutputMode.Auto, GpioOutputController.NextMode(GpioOutputMode.Off, hasFollow: true));
        Assert.Equal(GpioOutputMode.On, GpioOutputController.NextMode(GpioOutputMode.Auto, hasFollow: true));
        Assert.Equal(GpioOutputMode.Off, GpioOutputController.NextMode(GpioOutputMode.On, hasFollow: true));
    }

    [Fact]
    public void CycleMode_WithoutFollowSource_SkipsAuto()
    {
        // A manual-only output has nothing to follow, so Auto would be a dead stop on the
        // way round — a light would take three taps to turn on.
        Assert.Equal(GpioOutputMode.On, GpioOutputController.NextMode(GpioOutputMode.Off, hasFollow: false));
        Assert.Equal(GpioOutputMode.Off, GpioOutputController.NextMode(GpioOutputMode.On, hasFollow: false));
    }

    // --- Polarity ---

    [Fact]
    public void ActiveHigh_EnergisesOnHighPin()
    {
        var backend = new FakeBackend();
        var spindleOn = false;
        var controller = new GpioOutputController(backend, _ => spindleOn, () => 0);
        var output = controller.Add(VacConfig());

        // Opening must leave the relay de-energised, whatever the register held.
        Assert.False(backend.Levels[17]);

        spindleOn = true;
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        Assert.True(output.IsOn);
        Assert.True(backend.Levels[17]);
    }

    [Fact]
    public void ActiveLow_EnergisesOnLowPin_AndOpensHigh()
    {
        var backend = new FakeBackend();
        var spindleOn = false;
        var controller = new GpioOutputController(backend, _ => spindleOn, () => 0);
        var config = VacConfig();
        config.ActiveHigh = false;
        var output = controller.Add(config);

        // Off on an active-low board is a HIGH pin — the inverse of the boolean.
        Assert.True(backend.Levels[17]);

        spindleOn = true;
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        Assert.True(output.IsOn);
        Assert.False(backend.Levels[17]);
    }

    [Fact]
    public void UnclaimablePin_NeverWrites()
    {
        var backend = new FakeBackend { FailOpen = true };
        var spindleOn = true;
        var controller = new GpioOutputController(backend, _ => spindleOn, () => 0);
        var output = controller.Add(VacConfig());

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        Assert.False(output.IsPinReady);
        Assert.False(output.IsOn);
        Assert.Empty(backend.Writes);
    }

    // --- Off delay ---

    [Fact]
    public void SpindleStops_StaysOnUntilDelayElapses()
    {
        var backend = new FakeBackend();
        var spindleOn = true;
        var controller = new GpioOutputController(backend, _ => spindleOn, () => 0);
        var output = controller.Add(VacConfig(delay: 15));

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        Assert.True(output.IsOn);

        spindleOn = false;
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        Assert.True(output.IsOn);

        // Still inside the window — the hose is still clearing.
        controller.Tick(T0.AddSeconds(14));
        Assert.True(output.IsOn);

        controller.Tick(T0.AddSeconds(15));
        Assert.False(output.IsOn);
        Assert.False(backend.Levels[17]);
    }

    [Fact]
    public void SpindleRestartsInsideWindow_CancelsPendingOff()
    {
        // The tool-change case: M5, swap, M3. Without this the contactor would drop and
        // pull back in on every change.
        var backend = new FakeBackend();
        var spindleOn = true;
        var controller = new GpioOutputController(backend, _ => spindleOn, () => 0);
        var output = controller.Add(VacConfig(delay: 15));

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        spindleOn = false;
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        spindleOn = true;
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0.AddSeconds(5));

        controller.Tick(T0.AddSeconds(20));
        Assert.True(output.IsOn);

        // Exactly one energise write — the relay never moved.
        Assert.Single(backend.Writes);
    }

    [Fact]
    public void RepeatedOffRequests_DoNotExtendTheDeadline()
    {
        var backend = new FakeBackend();
        var spindleOn = true;
        var controller = new GpioOutputController(backend, _ => spindleOn, () => 0);
        var output = controller.Add(VacConfig(delay: 15));

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        spindleOn = false;

        // Status reports keep arriving at ~10 Hz with the spindle off; the deadline has to
        // stay pinned to the first one or it would never come due.
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0.AddSeconds(5));
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0.AddSeconds(10));

        controller.Tick(T0.AddSeconds(15));
        Assert.False(output.IsOn);
    }

    [Fact]
    public void ZeroDelay_SwitchesOffImmediately()
    {
        var backend = new FakeBackend();
        var spindleOn = true;
        var controller = new GpioOutputController(backend, _ => spindleOn, () => 0);
        var output = controller.Add(VacConfig(delay: 0));

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        spindleOn = false;
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        Assert.False(output.IsOn);
    }

    // --- Manual override ---

    [Fact]
    public void ManualOff_IsImmediate_NotDelayed()
    {
        // Someone tapping Off wants it off now, not in fifteen seconds.
        var backend = new FakeBackend();
        var spindleOn = true;
        var controller = new GpioOutputController(backend, _ => spindleOn, () => 0);
        var output = controller.Add(VacConfig(delay: 15));

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        Assert.True(output.IsOn);

        output.Mode = GpioOutputMode.Off;
        controller.ApplyMode(output, T0);

        Assert.False(output.IsOn);
        Assert.Null(output.PendingOffUtc);
    }

    [Fact]
    public void ManualOn_IgnoresTheFollowSource()
    {
        // The cleanup case: vac running with the spindle stopped, and it must stay running.
        var backend = new FakeBackend();
        var spindleOn = false;
        var controller = new GpioOutputController(backend, _ => spindleOn, () => 0);
        var output = controller.Add(VacConfig());

        output.Mode = GpioOutputMode.On;
        controller.ApplyMode(output, T0);
        Assert.True(output.IsOn);

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        controller.Tick(T0.AddSeconds(60));

        Assert.True(output.IsOn);
    }

    [Fact]
    public void ModeOff_IgnoresTheFollowSource()
    {
        var backend = new FakeBackend();
        var spindleOn = true;
        var controller = new GpioOutputController(backend, _ => spindleOn, () => 0);
        var output = controller.Add(VacConfig());

        output.Mode = GpioOutputMode.Off;
        controller.ApplyMode(output, T0);

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        Assert.False(output.IsOn);
    }

    [Fact]
    public void CycleMode_PersistsTheChoiceIntoConfig()
    {
        var backend = new FakeBackend();
        var controller = new GpioOutputController(backend, _ => false, () => 0);
        var config = VacConfig();
        var output = controller.Add(config);

        controller.CycleMode(output, T0);

        Assert.Equal(GpioOutputMode.On, output.Mode);
        Assert.Equal(GpioOutputMode.On, config.Mode);
    }

    // --- Comms loss ---

    [Fact]
    public void CommsLoss_DropsSpindleFollower_EvenWhenTheSourceStillReadsActive()
    {
        // SpindleDirection freezes at its last value when status reports stop, so the
        // source getter lies. Without the forced drop the vac would run indefinitely.
        var backend = new FakeBackend();
        var controller = new GpioOutputController(backend, _ => true, () => 0);
        var output = controller.Add(VacConfig(delay: 15));

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        Assert.True(output.IsOn);

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0, forceInactive: true);
        controller.Tick(T0.AddSeconds(15));

        Assert.False(output.IsOn);
    }

    // --- Restart behaviour ---

    [Fact]
    public void SavedOnMode_DoesNotComeBackOn_ForAnOutputThatFollowsSomething()
    {
        // Returning from a restart or a power cut with the dust collector latched on is
        // not what anyone means by "remember my setting".
        var backend = new FakeBackend();
        var controller = new GpioOutputController(backend, _ => false, () => 0);
        var config = VacConfig();
        config.Mode = GpioOutputMode.On;

        var output = controller.Add(config);

        Assert.Equal(GpioOutputMode.Auto, output.Mode);
    }

    [Fact]
    public void SavedOnMode_IsRestored_ForAManualOnlyOutput()
    {
        // Shop lights left on should still be on after a restart.
        var backend = new FakeBackend();
        var controller = new GpioOutputController(backend, _ => false, () => 0);
        var config = new GpioOutputConfig
        {
            Name = "Lights",
            Pin = 22,
            Follow = GpioFollowSource.None,
            Mode = GpioOutputMode.On,
        };

        var output = controller.Add(config);
        controller.ApplyMode(output, T0);

        Assert.Equal(GpioOutputMode.On, output.Mode);
        Assert.True(output.IsOn);
    }

    // --- Spindle RPM threshold (ATC tool changes) ---

    [Fact]
    public void BelowThreshold_SpindleOnDoesNotEnergise()
    {
        // A RapidChange ATC turns the spindle slowly to thread the holder. That is a
        // spindle-on state with no cutting and no chips, and it happens at every tool
        // change — without the threshold the dust collector fires each time.
        var backend = new FakeBackend();
        var rpm = 800;
        var controller = new GpioOutputController(backend, _ => true, () => rpm);
        var config = VacConfig();
        config.MinSpindleRpm = 2500;
        var output = controller.Add(config);

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        Assert.False(output.IsOn);
        Assert.Empty(backend.Writes);
    }

    [Fact]
    public void RisingThroughThreshold_EnergisesWithoutDirectionChanging()
    {
        // The tool change ends and the program comes back up to cutting speed with the
        // spindle never stopping. Direction is unchanged across that, so this only works
        // because speed is watched too.
        var backend = new FakeBackend();
        var rpm = 800;
        var controller = new GpioOutputController(backend, _ => true, () => rpm);
        var config = VacConfig();
        config.MinSpindleRpm = 2500;
        var output = controller.Add(config);

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        Assert.False(output.IsOn);

        rpm = 18000;
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        Assert.True(output.IsOn);
    }

    [Fact]
    public void DroppingUnderThreshold_UsesTheOffDelay()
    {
        // Falling back to ATC speed mid-job is the same shape as the spindle stopping, so
        // it goes through the delay rather than dropping the contactor instantly.
        var backend = new FakeBackend();
        var rpm = 18000;
        var controller = new GpioOutputController(backend, _ => true, () => rpm);
        var config = VacConfig(delay: 15);
        config.MinSpindleRpm = 2500;
        var output = controller.Add(config);

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        Assert.True(output.IsOn);

        rpm = 800;
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        Assert.True(output.IsOn);

        controller.Tick(T0.AddSeconds(15));
        Assert.False(output.IsOn);
    }

    [Fact]
    public void ToolChangeInsideTheDelayWindow_NeverCyclesTheRelay()
    {
        // The whole point, end to end: cutting, drop to ATC speed to swap the holder, back
        // to cutting — all inside the off-delay. The relay must not move.
        var backend = new FakeBackend();
        var rpm = 18000;
        var controller = new GpioOutputController(backend, _ => true, () => rpm);
        var config = VacConfig(delay: 15);
        config.MinSpindleRpm = 2500;
        var output = controller.Add(config);

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        rpm = 800;
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0.AddSeconds(1));
        controller.Tick(T0.AddSeconds(5));

        rpm = 18000;
        controller.EvaluateFollow(GpioFollowSource.Spindle, T0.AddSeconds(8));
        controller.Tick(T0.AddSeconds(30));

        Assert.True(output.IsOn);
        Assert.Single(backend.Writes);
    }

    [Fact]
    public void ThresholdIsInclusive()
    {
        var backend = new FakeBackend();
        var rpm = 2500;
        var controller = new GpioOutputController(backend, _ => true, () => rpm);
        var config = VacConfig();
        config.MinSpindleRpm = 2500;
        var output = controller.Add(config);

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        Assert.True(output.IsOn);
    }

    [Fact]
    public void ZeroThreshold_AcceptsAnySpindleOnState()
    {
        // The default, and what every pre-threshold config deserialises to.
        var backend = new FakeBackend();
        var controller = new GpioOutputController(backend, _ => true, () => 1);
        var output = controller.Add(VacConfig());

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        Assert.True(output.IsOn);
    }

    [Fact]
    public void ThresholdIsPerOutput_NotPerSource()
    {
        var backend = new FakeBackend();
        var controller = new GpioOutputController(backend, _ => true, () => 800);

        var vacConfig = VacConfig();
        vacConfig.MinSpindleRpm = 2500;
        var vac = controller.Add(vacConfig);

        var lightConfig = VacConfig();
        lightConfig.Name = "Spindle lamp";
        lightConfig.Pin = 22;
        var lamp = controller.Add(lightConfig);

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);

        Assert.False(vac.IsOn);
        Assert.True(lamp.IsOn);
    }

    // --- Config persistence ---

    [Fact]
    public void Config_RoundTripsThroughJson_WithReadableEnumNames()
    {
        // The config file is hand-editable, so the enums have to serialise as names. This
        // also guards the source-generated properties actually being seen by the
        // serialiser — GpioOutputConfig is an ObservableObject, not a plain POCO.
        var config = new GpioConfig
        {
            Enabled = true,
            Outputs =
            [
                new GpioOutputConfig
                {
                    Name = "Vac",
                    Pin = 17,
                    ActiveHigh = false,
                    Follow = GpioFollowSource.Spindle,
                    OffDelaySeconds = 20,
                    MinSpindleRpm = 2500,
                    Mode = GpioOutputMode.Auto,
                },
            ],
        };

        var json = System.Text.Json.JsonSerializer.Serialize(config);
        Assert.Contains("\"Spindle\"", json);
        Assert.Contains("\"Auto\"", json);

        var restored = System.Text.Json.JsonSerializer.Deserialize<GpioConfig>(json);

        Assert.NotNull(restored);
        Assert.True(restored!.Enabled);
        var output = Assert.Single(restored.Outputs);
        Assert.Equal("Vac", output.Name);
        Assert.Equal(17, output.Pin);
        Assert.False(output.ActiveHigh);
        Assert.Equal(GpioFollowSource.Spindle, output.Follow);
        Assert.Equal(20, output.OffDelaySeconds);
        Assert.Equal(2500, output.MinSpindleRpm);
        Assert.Equal(GpioOutputMode.Auto, output.Mode);
    }

    [Fact]
    public void AllOff_DeEnergisesEverythingAndClearsPendingDelays()
    {
        var backend = new FakeBackend();
        var controller = new GpioOutputController(backend, _ => true, () => 0);
        var vac = controller.Add(VacConfig());
        var lights = controller.Add(new GpioOutputConfig
        {
            Name = "Lights",
            Pin = 22,
            Follow = GpioFollowSource.Connected,
        });

        controller.EvaluateFollow(GpioFollowSource.Spindle, T0);
        controller.EvaluateFollow(GpioFollowSource.Connected, T0);
        Assert.True(vac.IsOn);
        Assert.True(lights.IsOn);

        controller.AllOff();

        Assert.False(vac.IsOn);
        Assert.False(lights.IsOn);
        Assert.Null(vac.PendingOffUtc);
        Assert.False(backend.Levels[17]);
        Assert.False(backend.Levels[22]);
    }
}
