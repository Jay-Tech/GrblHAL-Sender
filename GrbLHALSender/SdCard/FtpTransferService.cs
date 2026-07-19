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
        AsyncFtpClient? client = null;
        try
        {
            // Fetch the file size on its own short-lived session. grblHAL's
            // embedded FTP server reliably serves ONE data operation per
            // session (LIST here, RETR below) — chaining LIST then RETR on the
            // same session stalls the second operation. This also lets us pass
            // the size to OpenRead so FluentFTP never issues SIZE (which the
            // server doesn't implement).
            long totalBytes = 0;
            try
            {
                using var sizeClient = await ConnectAsync(ct);
                var items = await sizeClient.GetListing("/", FtpListOption.Auto, ct);
                totalBytes = items.FirstOrDefault(i => i.Name == remoteFileName)?.Size ?? 0;
            }
            catch { /* size is a nicety; proceed without it */ }

            client = await ConnectAsync(ct);
            client.LegacyLogger = (_, msg) =>
            {
                lastFtpLine = msg;
                Debug.WriteLine($"FTP: {msg}");
            };

            progress?.Report(new TransferProgress
            {
                TotalBytes = totalBytes,
                StatusMessage = "Opening data connection..."
            });

            // IMPORTANT: never pass FluentFTP a token we intend to cancel.
            // Cancelling its socket read fires an internal cleanup continuation
            // (CloseDataStream → wait for "226") that throws
            // OperationCanceledException on a bare thread-pool thread — an
            // unhandled exception that kills the entire process. Timeouts are
            // enforced with Task.WhenAny; on timeout the client is disposed,
            // which terminates the abandoned operation via socket closure.
            var openTask = client.OpenRead("/" + remoteFileName, FtpDataType.Binary, 0, totalBytes, CancellationToken.None);
            Observe(openTask);
            if (await Task.WhenAny(openTask, Task.Delay(15000, ct)) != openTask)
                return Fail("server did not start the transfer within 15 s");

            var remote = await openTask;
            long transferred = 0;
            try
            {
                await using var local = File.Create(localFilePath);
                var buffer = new byte[8192];
                while (true)
                {
                    var readTask = remote.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
                    Observe(readTask);
                    if (await Task.WhenAny(readTask, Task.Delay(15000, ct)) != readTask)
                        return Fail("transfer stalled (no data for 15 s)");

                    int read = await readTask;
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
            finally
            {
                // Graceful close reads the "226" end-of-transfer reply. Give
                // it a bounded wait; the finally below hard-disposes the
                // client if the server never sends it.
                try
                {
                    var closeTask = remote.DisposeAsync().AsTask();
                    Observe(closeTask);
                    await Task.WhenAny(closeTask, Task.Delay(3000));
                }
                catch { /* best effort */ }
            }

            if (transferred == 0)
                return Fail("no data received");

            progress?.Report(new TransferProgress
            {
                BytesTransferred = transferred,
                TotalBytes = transferred,
                StatusMessage = "Download complete"
            });
            return true;

            bool Fail(string reason)
            {
                Debug.WriteLine($"FTP download failed: {reason}. Last server line: {lastFtpLine}");
                // Report FIRST, then dispose — and dispose off-thread.
                // AsyncFtpClient.Dispose() blocks trying to QUIT politely,
                // and against an unresponsive server that block is indefinite:
                // it previously swallowed this failure report and hung the
                // finally (leaving the status poll suspended).
                TryDeleteEmptyFile(localFilePath);
                progress?.Report(new TransferProgress
                {
                    StatusMessage = $"Download failed: {reason}. Last FTP reply: {lastFtpLine}"
                });
                DisposeInBackground(client);
                client = null;
                return false;
            }
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
            DisposeInBackground(client);
            _commManager.ResumeAfterTransfer();
        }
    }

    /// <summary>
    /// Attaches a continuation that observes a task's eventual exception so an
    /// abandoned (timed-out) FluentFTP operation can never surface as an
    /// unobserved or unhandled exception.
    /// </summary>
    private static void Observe(Task task) =>
        task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.NotOnRanToCompletion);

    /// <summary>
    /// Disposes an FTP client on a worker thread. Dispose attempts a graceful
    /// QUIT and can block indefinitely against an unresponsive server — it
    /// must never run inline on a path that reports progress or resumes polling.
    /// </summary>
    private static void DisposeInBackground(AsyncFtpClient? client)
    {
        if (client == null) return;
        _ = Task.Run(() =>
        {
            try { client.Dispose(); }
            catch { /* best effort */ }
        });
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
