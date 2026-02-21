using System;
using System.Threading;
using System.Threading.Tasks;

namespace GrbLHALSender.SdCard;

/// <summary>
/// Progress information for SD card file transfers.
/// </summary>
public class TransferProgress
{
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public double Percentage => TotalBytes > 0 ? (double)BytesTransferred / TotalBytes * 100.0 : 0;
    public string? StatusMessage { get; set; }
}

/// <summary>
/// Transfer mode based on the active connection type.
/// </summary>
public enum TransferMode
{
    None,
    YModem,
    Ftp
}

/// <summary>
/// Interface for SD card file transfer implementations (YModem, FTP).
/// </summary>
public interface ISdCardTransferService
{
    /// <summary>Upload a local file to the SD card.</summary>
    Task<bool> UploadFileAsync(string localFilePath, string remoteFileName,
        IProgress<TransferProgress>? progress, CancellationToken ct);

    /// <summary>Download a file from the SD card to a local path.</summary>
    Task<bool> DownloadFileAsync(string remoteFileName, string localFilePath,
        IProgress<TransferProgress>? progress, CancellationToken ct);

    /// <summary>Whether this transfer service is available with the current connection.</summary>
    bool IsAvailable { get; }
}
