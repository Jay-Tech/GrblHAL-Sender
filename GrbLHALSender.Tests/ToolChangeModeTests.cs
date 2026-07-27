using GrbLHALSender.Settings;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for reading grblHAL's $341 tool change mode, which decides whether the operator
/// is shown a touch-off control.
/// <para>
/// Getting this wrong is not cosmetic. Offering $TPW on a machine that does not implement
/// it (modes 0, 3 and 4) invites a command the controller will reject, and hiding it on a
/// machine that needs it (modes 1 and 2) leaves the tool change impossible to complete —
/// which is exactly the hole this fills.
/// </para>
/// </summary>
public class ToolChangeModeTests
{
    [Theory]
    [InlineData("0", 0)]   // Normal
    [InlineData("1", 1)]   // Manual touch off
    [InlineData("2", 2)]   // Manual touch off @ G59.3
    [InlineData("3", 3)]   // Automatic touch off @ G59.3
    [InlineData("4", 4)]   // Ignore M6
    public void SetToolChangeMode_ReadsTheSetting(string value, int expected)
    {
        var settings = new MachineSettings();
        settings.SetToolChangeMode(value);

        Assert.Equal(expected, settings.ToolChangeMode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ManualModes_NeedTouchOff(int mode)
    {
        var settings = new MachineSettings();
        settings.SetToolChangeMode(mode.ToString());

        Assert.True(settings.ToolChangeNeedsTouchOff);
    }

    [Theory]
    [InlineData(0)]  // program does everything
    [InlineData(3)]  // controller probes by itself after cycle start
    [InlineData(4)]  // M6 ignored entirely
    public void EveryOtherMode_DoesNot(int mode)
    {
        var settings = new MachineSettings();
        settings.SetToolChangeMode(mode.ToString());

        Assert.False(settings.ToolChangeNeedsTouchOff);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void AnUnreadableSetting_FallsBackToNoTouchOff(string value)
    {
        // A controller that did not report $341 must not produce a touch-off button on
        // speculation — the safe default is the control being absent.
        var settings = new MachineSettings();
        settings.SetToolChangeMode(value);

        Assert.Equal(0, settings.ToolChangeMode);
        Assert.False(settings.ToolChangeNeedsTouchOff);
    }

    [Fact]
    public void ChangingTheMode_NotifiesTheDerivedFlag()
    {
        // The button's visibility binds through ToolChangeNeedsTouchOff, so it has to
        // raise when the mode is read at connect.
        var settings = new MachineSettings();
        var raised = false;
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MachineSettings.ToolChangeNeedsTouchOff))
                raised = true;
        };

        settings.SetToolChangeMode("1");

        Assert.True(raised);
    }
}
