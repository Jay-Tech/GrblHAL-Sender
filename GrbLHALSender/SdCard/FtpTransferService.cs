using FluentFTP;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GrbLHALSender.SdCard;

/// <summary>
/// FTP-based SD card file transfer service for grblHAL controllers with networking.
/// Uses FluentFTP for all FTP operations (upload, download, list, delete).
/// </summary>
public class FtpTransferService : ISdCardTransferService
{
    private readonly CommunicationManager _commManager;
    private readonly ConfigManager _configManager;

    public bool IsAvailable =>
        _commManager.ActiveAdapterType == typeof(Tcp) ||
        _commManager.ActiveAdapterType == typeof(WebSocket);

    public FtpTransferService(CommunicationManager commManager, ConfigManager configManager)
    {
        _commManager = commManager;
        _configManager = configManager;
    }

    public async Task<bool> UploadFileAsync(string localFilePath, string remoteFileName,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        try
        {
            using var client = await ConnectAsync(ct);
            var fileInfo = new FileInfo(localFilePath);
            var totalBytes = fileInfo.Length;

            var ftpProgress = new Progress<FtpProgress>(p =>
            {
                progress?.Report(new TransferProgress
                {
                    BytesTransferred = (long)(p.Progress / 100.0 * totalBytes),
                    TotalBytes = totalBytes,
                    StatusMessage = $"Uploading... {p.Progress:F0}%"
                });
            });

            var result = await client.UploadFile(localFilePath, "/" + remoteFileName,
                FtpRemoteExists.Overwrite, false, FtpVerify.None, ftpProgress, ct);

            progress?.Report(new TransferProgress
            {
                BytesTransferred = totalBytes,
                TotalBytes = totalBytes,
                StatusMessage = result == FtpStatus.Success ? "Upload complete" : "Upload failed"
            });

            return result == FtpStatus.Success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FTP upload error: {ex.Message}");
            progress?.Report(new TransferProgress
            {
                StatusMessage = $"FTP error: {ex.Message}"
            });
            return false;
        }
    }

    public async Task<bool> DownloadFileAsync(string remoteFileName, string localFilePath,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        try
        {
            using var client = await ConnectAsync(ct);

            // Get file size for progress tracking
            long totalBytes = await client.GetFileSize("/" + remoteFileName, -1, ct);
            if (totalBytes < 0) totalBytes = 0;

            var ftpProgress = new Progress<FtpProgress>(p =>
            {
                progress?.Report(new TransferProgress
                {
                    BytesTransferred = totalBytes > 0 ? (long)(p.Progress / 100.0 * totalBytes) : 0,
                    TotalBytes = totalBytes,
                    StatusMessage = $"Downloading... {p.Progress:F0}%"
                });
            });

            var result = await client.DownloadFile(localFilePath, "/" + remoteFileName,
                FtpLocalExists.Overwrite, FtpVerify.None, ftpProgress, ct);

            progress?.Report(new TransferProgress
            {
                BytesTransferred = totalBytes,
                TotalBytes = totalBytes,
                StatusMessage = result == FtpStatus.Success ? "Download complete" : "Download failed"
            });

            return result == FtpStatus.Success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FTP download error: {ex.Message}");
            progress?.Report(new TransferProgress
            {
                StatusMessage = $"FTP error: {ex.Message}"
            });
            return false;
        }
    }

    /// <summary>List all files on the SD card via FTP.</summary>
    public async Task<List<SdCardFileInfo>> ListFilesAsync(CancellationToken ct)
    {
        using var client = await ConnectAsync(ct);
        var items = await client.GetListing("/", FtpListOption.Auto, ct);

        return items
            .Where(i => i.Type == FtpObjectType.File)
            .Select(i => new SdCardFileInfo
            {
                FileName = i.Name,
                FileSize = i.Size
            })
            .ToList();
    }

    /// <summary>Delete a file from the SD card via FTP.</summary>
    public async Task<bool> DeleteFileAsync(string remoteFileName, CancellationToken ct)
    {
        try
        {
            using var client = await ConnectAsync(ct);
            await client.DeleteFile("/" + remoteFileName, ct);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FTP delete error: {ex.Message}");
            return false;
        }
    }

    // ---- Connection helper ----

    private async Task<AsyncFtpClient> ConnectAsync(CancellationToken ct)
    {
        var config = _configManager.GHalSenderConfig;
        var sdConfig = config?.SdCardConfig ?? new SdCardConfig();

        // Derive IP address from the active network connection settings
        string ipAddress = GetControllerIpAddress();
        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new InvalidOperationException("No controller IP address available. Check your TCP/WebSocket connection settings.");

        var client = new AsyncFtpClient(ipAddress,
            string.IsNullOrEmpty(sdConfig.FtpUsername) ? "anonymous" : sdConfig.FtpUsername,
            sdConfig.FtpPassword ?? "",
            sdConfig.FtpPort);

        client.Config.EncryptionMode = FtpEncryptionMode.None;
        client.Config.DataConnectionType = FtpDataConnectionType.PASV;
        client.Config.ConnectTimeout = 2000;   // fast fail if no FTP server
        client.Config.ReadTimeout = 5000;

        await client.Connect(ct);
        return client;
    }

    private string GetControllerIpAddress()
    {
        var config = _configManager.GHalSenderConfig;
        if (config == null) return "";

        // Use the IP from the active connection type
        if (_commManager.ActiveAdapterType == typeof(Tcp))
            return config.TcpSettings?.IpAddress ?? "";

        if (_commManager.ActiveAdapterType == typeof(WebSocket))
            return config.WebSocketSettings?.IpAddress ?? "";

        return "";
    }
}
