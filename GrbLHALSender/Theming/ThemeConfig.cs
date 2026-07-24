namespace GrbLHALSender.Theming;

/// <summary>
/// Persisted theme choice. Serialized as part of GHalSenderConfig — configs written
/// before this existed simply deserialize to the defaults.
/// </summary>
public class ThemeConfig
{
    /// <summary>Id of a <see cref="ThemePresets"/> entry. Unknown values fall back to the default preset.</summary>
    public string Preset { get; set; } = ThemePresets.SlateDark.Id;

    /// <summary>User accent override as "#RRGGBB". Null means "use the preset's accent".</summary>
    public string? AccentColor { get; set; }
}
