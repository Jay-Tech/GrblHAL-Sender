namespace GrbLHALSender.SdCard;

/// <summary>
/// Configuration for SD card file transfer (FTP credentials and options).
/// </summary>
public class SdCardConfig
{
    public int FtpPort { get; set; } = 21;
    public string FtpUsername { get; set; } = "";
    public string FtpPassword { get; set; } = "";
    public bool AutoMountOnConnect { get; set; } = true;
}
