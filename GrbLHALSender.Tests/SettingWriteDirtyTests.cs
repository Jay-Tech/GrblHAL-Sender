using GrbLHALSender.Settings;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests the state a setting is in along each route a write can arrive by, which is what
/// decides whether recording that write is allowed to skip its work.
/// <para>
/// The two routes differ in a way that is easy to miss. The settings editor writes the
/// typed value into the setting and only then sends it, so when the write goes out the
/// value already matches and only the dirty flag says anything happened. From the MDI
/// nothing has touched the setting, so the value itself still differs. Skipping on the
/// value alone therefore skipped the editor — the route that prompted all of this.
/// </para>
/// </summary>
public class SettingWriteDirtyTests
{
    [Fact]
    public void TheEditorRoute_LeavesTheValueMatchingButDirty()
    {
        var setting = new GrblHalSetting(341, "0");

        // What the grid does when the operator types into it.
        setting.SettingValue = "2";

        Assert.Equal("2", setting.SettingValue);
        Assert.True(setting.NeedsSaving);
    }

    [Fact]
    public void TheMdiRoute_LeavesTheValueStale()
    {
        // Nothing touches the setting; the command goes straight out.
        var setting = new GrblHalSetting(341, "0");

        Assert.Equal("0", setting.SettingValue);
        Assert.False(setting.NeedsSaving);
    }

    [Fact]
    public void RecordingTheWrite_ClearsTheDirtyFlag()
    {
        // The controller holds it now, so it becomes the clean baseline.
        var setting = new GrblHalSetting(341, "0");
        setting.SettingValue = "2";

        setting.SetReportedValue("2");

        Assert.Equal("2", setting.SettingValue);
        Assert.False(setting.NeedsSaving);
    }

    [Fact]
    public void AnEditRevertedByHand_IsNotDirty()
    {
        // The skip condition relies on this: value matching and not dirty means the write
        // has already been recorded and there is genuinely nothing to do.
        var setting = new GrblHalSetting(341, "2");
        setting.SettingValue = "3";
        setting.SettingValue = "2";

        Assert.False(setting.NeedsSaving);
    }
}
