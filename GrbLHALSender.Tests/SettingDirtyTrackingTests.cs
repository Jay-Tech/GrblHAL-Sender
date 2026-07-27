using System;
using GrbLHALSender.Settings;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests that a changed setting is queued for saving however the change arrived.
/// <para>
/// Reported from testing on Linux: changing $342 from 30 to 125 in the settings UI did not
/// reach the controller, while the same change typed into MDI did. The editor flagged the
/// setting from the TextBox's KeyUp event, which the on-screen keyboard never raises — it
/// assigns Text directly. So on a touchscreen the edit was displayed and silently dropped,
/// and on Windows with a physical keyboard it worked.
/// </para>
/// </summary>
public class SettingDirtyTrackingTests
{
    [Fact]
    public void AValueFromTheController_IsNotDirty()
    {
        var setting = new GrblHalSetting(342, "30");

        Assert.False(setting.NeedsSaving);
        Assert.Equal("30", setting.SettingValue);
    }

    [Fact]
    public void AnEditedValue_IsDirty()
    {
        // This is the path the on-screen keyboard takes: the bound property is assigned,
        // with no key event anywhere.
        var setting = new GrblHalSetting(342, "30");

        setting.SettingValue = "125";

        Assert.True(setting.NeedsSaving);
    }

    [Fact]
    public void RevertingAnEdit_ClearsDirty()
    {
        var setting = new GrblHalSetting(342, "30");

        setting.SettingValue = "125";
        setting.SettingValue = "30";

        Assert.False(setting.NeedsSaving);
    }

    [Fact]
    public void ReReadingFromTheController_ClearsDirty()
    {
        // After a save the controller reports the value back; that is the new baseline and
        // the row must stop being queued.
        var setting = new GrblHalSetting(342, "30");
        setting.SettingValue = "125";
        Assert.True(setting.NeedsSaving);

        setting.SetReportedValue("125");

        Assert.False(setting.NeedsSaving);
    }

    [Fact]
    public void AControllerValueThatDiffers_IsNotTreatedAsAnEdit()
    {
        // A refresh that brings a different value is the machine's truth, not a pending
        // local change — it must not queue itself to be written straight back.
        var setting = new GrblHalSetting(342, "30");

        setting.SetReportedValue("125");

        Assert.False(setting.NeedsSaving);
        Assert.Equal("125", setting.SettingValue);
    }

    [Fact]
    public void AddSettingValue_UpdatesTheBaseline()
    {
        var settings = new GrblHALSettings();
        settings.SettingCollection.Add(new GrblHalSetting(342, "30"));

        string[] data = ["342", "125"];
        settings.AddSettingValue(data.AsSpan());

        var setting = settings.SettingCollection[0];
        Assert.Equal("125", setting.SettingValue);
        Assert.False(setting.NeedsSaving);
    }
}
