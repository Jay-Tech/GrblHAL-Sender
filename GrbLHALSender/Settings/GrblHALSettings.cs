using System;
using System.Collections.Generic;
using System.Linq;

namespace GrbLHALSender.Settings;

public class GrblHALSettings
{
    public List<GrblHalSetting> SettingCollection { get; set; }

    /// <summary>Groups reported by <c>$EG</c>, keyed by group id. Empty on firmware that lacks the command.</summary>
    public Dictionary<int, SettingGroup> Groups { get; } = new();

    public GrblHALSettings()
    {
        SettingCollection = new List<GrblHalSetting>();
    }

    public void AddGroup(SettingGroup group) => Groups[group.Id] = group;

    /// <summary>
    /// Display name for a group id, walking parents so nested groups read as
    /// "Axis / X-axis". Falls back to "Group N" when <c>$EG</c> returned nothing,
    /// so older firmware still gets usable headers instead of a blank one.
    /// </summary>
    public string GroupNameFor(int groupId)
    {
        if (!Groups.TryGetValue(groupId, out var group))
            return $"Group {groupId}";

        var name = group.Name;
        var parentId = group.ParentId;

        // Depth-guarded: a malformed parent chain must not spin here.
        for (var depth = 0; depth < 4 && parentId != 0; depth++)
        {
            if (!Groups.TryGetValue(parentId, out var parent) || parent.Id == group.Id)
                break;
            name = $"{parent.Name} / {name}";
            parentId = parent.ParentId;
        }

        return name;
    }

    public void AddSettingValue(Span<string> data)
    {
        var id = int.Parse(data[0]);
        if (SettingCollection.Any(x => x.Id.Equals(id)))
        {
            var setting = SettingCollection.First(x => x.Id.Equals(id));
            setting.SetReportedValue(data[1]);

        }
        else
        {
            SettingCollection.Add(new GrblHalSetting(id, data[1]));
        }
    }
}
