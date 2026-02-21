namespace GrbLHALSender.SdCard;

/// <summary>
/// Represents a single file on the controller's SD card.
/// </summary>
public class SdCardFileInfo
{
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }

    public string DisplaySize => FormatSize(FileSize);

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    public override string ToString() => $"{FileName} ({DisplaySize})";
}
