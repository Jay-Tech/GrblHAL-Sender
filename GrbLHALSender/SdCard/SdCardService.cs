using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GrbLHALSender.SdCard;

/// <summary>
/// Central service for all SD card operations.
/// Coordinates file commands ($FM, $F, $FD=, $F=) and routes transfers
/// to YModem (serial) or FTP (network) based on the active connection type.
/// </summary>
public class SdCardService
{
    private readonly CommunicationManager _commManager;
    private readonly ConfigManager _configManager;

    private YModemTransfer? _ymodem;
    private FtpTransferService? _ftp;

    public SdCardService(CommunicationManager commManager, ConfigManager configManager)
    {
        _commManager = commManager;
        _configManager = configManager;
    }

    /// <summary>
    /// Determine the available transfer mode based on the active connection.
    /// </summary>
    public TransferMode GetTransferMode()
    {
        if (_commManager.ActiveAdapterType == typeof(Serial))
            return TransferMode.YModem;

        if (_commManager.ActiveAdapterType == typeof(Tcp) ||
            _commManager.ActiveAdapterType == typeof(WebSocket))
            return TransferMode.Ftp;

        return TransferMode.None;
    }

    // ---- SD Card commands (work over any connection) ----

    /// <summary>Mount the SD card ($FM).</summary>
    public async Task<bool> MountSdCardAsync()
    {
        try
        {
            return await _commManager.SendAsyncCommand(GrblHalConstants.SdcardMount, "ok", 3000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SD mount error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// List files on the SD card using the $F+ command.
    /// Parses response lines like "[FILE:filename|SIZE:12345]".
    /// </summary>
    public async Task<List<SdCardFileInfo>> ListFilesViaCommandAsync(CancellationToken ct)
    {
        try
        {
            var lines = await _commManager.SendCommandCollectResponsesAsync(
                GrblHalConstants.SdcardDirAll, "ok", 10000);

            var files = new List<SdCardFileInfo>();
            foreach (var line in lines)
            {
                var file = ParseFileEntry(line);
                if (file != null)
                    files.Add(file);
            }
            return files;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SD list error: {ex.Message}");
            return new List<SdCardFileInfo>();
        }
    }

    /// <summary>Delete a file from the SD card ($FD=filename).</summary>
    public async Task<bool> DeleteFileAsync(string fileName)
    {
        try
        {
            return await _commManager.SendAsyncCommand(
                GrblHalConstants.SdcardUnlink + fileName, "ok", 3000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SD delete error: {ex.Message}");
            return false;
        }
    }

    /// <summary>Run a G-code file from the SD card ($F=filename).</summary>
    public void RunFile(string fileName)
    {
        _commManager.SendCommand(GrblHalConstants.SdcardRun + fileName);
    }

    // ---- File transfer (routed to YModem or FTP) ----

    /// <summary>Upload a file to the SD card using the appropriate transfer method.</summary>
    public async Task<bool> UploadFileAsync(string localFilePath, string remoteFileName,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var mode = GetTransferMode();

        switch (mode)
        {
            case TransferMode.YModem:
                _ymodem ??= new YModemTransfer(_commManager);
                return await _ymodem.UploadFileAsync(localFilePath, remoteFileName, progress, ct);

            case TransferMode.Ftp:
                _ftp ??= new FtpTransferService(_commManager, _configManager);
                return await _ftp.UploadFileAsync(localFilePath, remoteFileName, progress, ct);

            default:
                progress?.Report(new TransferProgress
                {
                    StatusMessage = "No transfer method available. Check connection."
                });
                return false;
        }
    }

    /// <summary>Download a file from the SD card using the appropriate transfer method.</summary>
    public async Task<bool> DownloadFileAsync(string remoteFileName, string localFilePath,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var mode = GetTransferMode();

        switch (mode)
        {
            case TransferMode.Ftp:
                _ftp ??= new FtpTransferService(_commManager, _configManager);
                return await _ftp.DownloadFileAsync(remoteFileName, localFilePath, progress, ct);

            case TransferMode.YModem:
                progress?.Report(new TransferProgress
                {
                    StatusMessage = "YModem download not supported. Use FTP or $F<= command."
                });
                return false;

            default:
                progress?.Report(new TransferProgress
                {
                    StatusMessage = "No transfer method available. Check connection."
                });
                return false;
        }
    }

    /// <summary>
    /// List files on the SD card.
    /// Always uses the $F+ command which works over any connection type (serial, TCP, WebSocket).
    /// FTP is only used for binary file upload/download, not for listing.
    /// </summary>
    public async Task<List<SdCardFileInfo>> ListFilesAsync(CancellationToken ct)
    {
        return await ListFilesViaCommandAsync(ct);
    }

    // ---- Parsing helpers ----

    /// <summary>
    /// Parse a file entry line from the $F+ response.
    /// Expected formats:
    ///   "[FILE:filename.nc|SIZE:12345]"
    ///   or just "filename.nc" (some firmware versions)
    /// </summary>
    private static SdCardFileInfo? ParseFileEntry(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var trimmed = line.Trim().Trim('[', ']');

        // Format: "FILE:name|SIZE:bytes"
        if (trimmed.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split('|');
            var fileName = "";
            long fileSize = 0;

            foreach (var part in parts)
            {
                if (part.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase))
                    fileName = part[5..].Trim();
                else if (part.StartsWith("SIZE:", StringComparison.OrdinalIgnoreCase))
                    long.TryParse(part[5..].Trim(), out fileSize);
            }

            if (!string.IsNullOrEmpty(fileName))
                return new SdCardFileInfo { FileName = fileName, FileSize = fileSize };
        }

        // Simple filename format (no metadata)
        if (!trimmed.StartsWith("error", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            return new SdCardFileInfo { FileName = trimmed, FileSize = 0 };
        }

        return null;
    }
}
