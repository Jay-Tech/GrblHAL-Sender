using System;

namespace GrbLHALSender.Settings;

/// <summary>
/// A settings group as reported by grblHAL's <c>$EG</c> command
/// (<c>[SETTINGGROUP:&lt;id&gt;|&lt;parent&gt;|&lt;name&gt;]</c>).
///
/// Groups form a shallow tree — "X-axis" hangs off "Axis", for example — so the
/// display name is resolved by walking parents rather than stored flat.
/// </summary>
public sealed class SettingGroup
{
    public int Id { get; init; }
    public int ParentId { get; init; }
    public string Name { get; init; } = string.Empty;

    public static SettingGroup? Parse(Span<string> data)
    {
        // data[0] is "SETTINGGROUP:<id>"
        var head = data[0].Split(':');
        var idText = head.Length > 1 ? head[1] : head[0];

        if (data.Length < 3 || !int.TryParse(idText, out var id)) return null;
        if (!int.TryParse(data[1], out var parentId)) parentId = 0;

        return new SettingGroup { Id = id, ParentId = parentId, Name = data[2] };
    }
}
