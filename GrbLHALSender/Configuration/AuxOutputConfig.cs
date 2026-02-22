namespace GrbLHALSender.Configuration;

public class AuxOutputConfig
{
    public string Name { get; set; } = "";
    public string OnCommand { get; set; } = "";
    public string OffCommand { get; set; } = "";
    public string StateKey { get; set; } = "";

    public static readonly AuxOutputConfig[] Presets =
    [
        new() { Name = "Mist", OnCommand = "M7", OffCommand = "M9", StateKey = "M" },
        new() { Name = "Flood", OnCommand = "M8", OffCommand = "M9", StateKey = "F" },
    ];
}
