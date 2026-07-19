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
        // Pause the 0x87 status poll during the transfer — the controller's
        // network stack struggles to serve FTP data and telnet chatter at the
        // same time (same reason YModem suspends it).
        _commManager.SuspendForTransfer();
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
        finally
        {
            _commManager.ResumeAfterTransfer();
        }
    }

    public async Task<bool> DownloadFileAsync(string remoteFileName, string localFilePath,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        // Manual RETR stream instead of FluentFTP's high-level DownloadFile:
        // the helper issues SIZE (and REST for resume support) before the
        // transfer, which grblHAL's minimal embedded FTP server does not
        // implement — the transfer dies after the local file was created,
        // leaving a 0-byte file. A bare OpenRead/RETR matches the simplicity
        // of the upload path, which works.
        string lastFtpLine = "";
        // Pause the 0x87 status poll during the transfer — the controller's
        // network stack struggles to serve FTP data and telnet chatter at the
        // same time (same reason YModem suspends it).
        _commManager.SuspendForTransfer();
        try
        {
            using var client = await ConnectAsync(ct);

            // Keep the server's side of the conversation for diagnostics —
            // async FluentFTP calls hang (not time out) on unanswered
            // commands, so when the watchdog trips this tells us where.
            client.LegacyLogger = (_, msg) =>
            {
                lastFtpLine = msg;
                Debug.WriteLine($"FTP: {msg}");
            };

            // Learn the file size from the directory listing. LIST is known
            // good on grblHAL's embedded server; SIZE is not implemented, and
            // OpenRead with fileLen 0 would issue SIZE internally and hang.
            long totalBytes = 0;
            var items = await client.GetListing("/", FtpListOption.Auto, ct);
            totalBytes = items.FirstOrDefault(i => i.Name == remoteFileName)?.Size ?? 0;

            progress?.Report(new TransferProgress
            {
                TotalBytes = totalBytes,
                StatusMessage = "Opening data connection..."
            });

            // Inactivity watchdog: async FluentFTP ignores ReadTimeout, so
            // without this an unresponsive server stalls forever. Re-armed on
            // every successful read — only 15 s of true silence trips it.
            using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(ct);
            watchdog.CancelAfter(15000);

            long transferred = 0;
            await using (var remote = await client.OpenRead("/" + remoteFileName, FtpDataType.Binary, 0, totalBytes, watchdog.Token))
            await using (var local = File.Create(localFilePath))
            {
                var buffer = new byte[8192];
                while (true)
                {
                    watchdog.CancelAfter(15000);
                    int read = await remote.ReadAsync(buffer.AsMemory(), watchdog.Token);
                    if (read <= 0) break;

                    await local.WriteAsync(buffer.AsMemory(0, read), ct);
                    transferred += read;
                    progress?.Report(new TransferProgress
                    {
                        BytesTransferred = transferred,
                        TotalBytes = totalBytes,
                        StatusMessage = totalBytes > 0
                            ? $"Downloading... {100 * transferred / totalBytes}%"
                            : $"Downloading... {transferred / 1024} KB"
                    });

                    // Some embedded servers leave the data socket open after
                    // the last byte; don't wait for a close that never comes.
                    if (totalBytes > 0 && transferred >= totalBytes) break;
                }
            }

            // Drain the server's end-of-transfer reply (226) so the control
            // connection is left in a clean state before disposal.
            try { await client.GetReply(ct); } catch { /* best effort */ }

            if (transferred == 0)
            {
                TryDeleteEmptyFile(localFilePath);
                progress?.Report(new TransferProgress { StatusMessage = "Download failed: no data received" });
                return false;
            }

            progress?.Report(new TransferProgress
            {
                BytesTransferred = transferred,
                TotalBytes = transferred,
                StatusMessage = "Download complete"
            });
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Debug.WriteLine($"FTP download stalled. Last server line: {lastFtpLine}");
            TryDeleteEmptyFile(localFilePath);
            progress?.Report(new TransferProgress
            {
                StatusMessage = $"Download stalled (no data for 15 s). Last FTP reply: {lastFtpLine}"
            });
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FTP download error: {ex}");
            TryDeleteEmptyFile(localFilePath);
            progress?.Report(new TransferProgress
            {
                StatusMessage = $"FTP error: {ex.Message}"
            });
            return false;
        }
        finally
        {
            _commManager.ResumeAfterTransfer();
        }
    }

    /// <summary>Removes a zero-byte artifact left behind by a failed download.</summary>
    private static void TryDeleteEmptyFile(string path)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length == 0)
                File.Delete(path);
        }
        catch { /* best effort */ }
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
