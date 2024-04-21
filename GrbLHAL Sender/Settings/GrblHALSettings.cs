using System;
using System.Collections.Generic;
using System.Linq;

namespace GrbLHAL_Sender.Settings;

public class GrblHALSettings
{

    public List<GrblHalSetting> SettingCollection { get; set; }
    public GrblHALSettings()
    {
        SettingCollection = new List<GrblHalSetting>();
    }

    public void AddSettingValue(Span<string> data)
    {
        var id = int.Parse(data[0]);
        if (SettingCollection.Any(x => x.Id.Equals(id)))
        {
            var setting = SettingCollection.First(x => x.Id.Equals(id));
            setting.SettingValue = data[1];

        }
        else
        {
            SettingCollection.Add(new GrblHalSetting(id, data[1]));
        }
    }
}