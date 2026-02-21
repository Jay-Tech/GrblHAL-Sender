using GrbLHALSender.Communication;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GrbLHALSender.SdCard;

/// <summary>
/// YModem-1K file transfer implementation for grblHAL SD card upload.
///
/// Key grblHAL-specific behaviour:
///   - The controller does NOT send the initial 'C' handshake character.
///   - The sender must initiate the transfer by sending Block 0 immediately.
///   - Requires grblHAL build 20210421+ with SDCARD_ENABLE set to 2.
/// </summary>
public class YModemTransfer : ISdCardTransferService
{
    // YModem protocol constants
    private const byte SOH = 0x01;  // 128-byte data block header
    private const byte STX = 0x02;  // 1024-byte data block header
    private const byte EOT = 0x04;  // End of transmission
    private const byte ACK = 0x06;  // Acknowledgment
    private const byte NAK = 0x15;  // Negative acknowledgment
    private const byte CAN = 0x18;  // Cancel transfer
    private const byte C_CHAR = 0x43; // 'C' — CRC mode request

    private const int SOH_DATA_SIZE = 128;
    private const int STX_DATA_SIZE = 1024;
    private const int MAX_RETRIES = 3;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BlockAckTimeout = TimeSpan.FromSeconds(5);

    private readonly CommunicationManager _commManager;
    private readonly ConcurrentQueue<byte> _rxQueue = new();
    private readonly SemaphoreSlim _rxSignal = new(0, int.MaxValue);

    public bool IsAvailable => _commManager.ActiveAdapterType == typeof(Serial);

    public YModemTransfer(CommunicationManager commManager)
    {
        _commManager = commManager;
    }

    public async Task<bool> UploadFileAsync(string localFilePath, string remoteFileName,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("YModem upload requires a serial connection.");

        var adapter = _commManager.Adapter;
        var fileBytes = await File.ReadAllBytesAsync(localFilePath, ct);
        long totalBytes = fileBytes.Length;

        // Clear the receive queue
        while (_rxQueue.TryDequeue(out _)) { }

        try
        {
            // Suspend polling so 0x87 status bytes don't interfere
            _commManager.SuspendForTransfer();

            // Switch serial adapter to raw byte mode
            adapter.OnRawDataReceived += OnRawData;
            adapter.EnterRawMode();

            // Small delay to let the controller settle
            await Task.Delay(100, ct);

            // --- Block 0: filename + file size ---
            progress?.Report(new TransferProgress
            {
                BytesTransferred = 0,
                TotalBytes = totalBytes,
                StatusMessage = "Sending file header..."
            });

            var block0 = BuildBlock0(remoteFileName, totalBytes);
            await SendPacketWithRetry(adapter, block0, ct);

            // Wait for ACK then 'C' from the controller
            await WaitForByte(ACK, HandshakeTimeout, ct);
            await WaitForByte(C_CHAR, HandshakeTimeout, ct);

            // --- Data blocks (1024-byte STX packets) ---
            byte seqNum = 1;
            int offset = 0;

            while (offset < totalBytes)
            {
                ct.ThrowIfCancellationRequested();

                int remaining = (int)(totalBytes - offset);
                int chunkSize = Math.Min(STX_DATA_SIZE, remaining);

                var dataBlock = new byte[STX_DATA_SIZE];
                Array.Copy(fileBytes, offset, dataBlock, 0, chunkSize);

                // Pad last block with 0x1A (SUB/CTRL-Z)
                if (chunkSize < STX_DATA_SIZE)
                {
                    for (int i = chunkSize; i < STX_DATA_SIZE; i++)
                        dataBlock[i] = 0x1A;
                }

                var packet = BuildDataPacket(seqNum, dataBlock);
                await SendPacketWithRetry(adapter, packet, ct);
                await WaitForByte(ACK, BlockAckTimeout, ct);

                offset += chunkSize;
                seqNum++;

                progress?.Report(new TransferProgress
                {
                    BytesTransferred = Math.Min(offset, totalBytes),
                    TotalBytes = totalBytes,
                    StatusMessage = $"Uploading... {Math.Min(offset, totalBytes) * 100 / totalBytes}%"
                });
            }

            // --- EOT ---
            progress?.Report(new TransferProgress
            {
                BytesTransferred = totalBytes,
                TotalBytes = totalBytes,
                StatusMessage = "Finishing transfer..."
            });

            adapter.WriteBytes(new[] { EOT }, 0, 1);
            await WaitForByte(ACK, BlockAckTimeout, ct);

            // --- Empty Block 0 (end of batch) ---
            var endBlock = BuildEmptyBlock0();
            adapter.WriteBytes(endBlock, 0, endBlock.Length);
            await WaitForByte(ACK, BlockAckTimeout, ct);

            progress?.Report(new TransferProgress
            {
                BytesTransferred = totalBytes,
                TotalBytes = totalBytes,
                StatusMessage = "Upload complete"
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            // User cancelled — send CAN to abort
            SendCancel(adapter);
            throw;
        }
        catch (TimeoutException ex)
        {
            Debug.WriteLine($"YModem timeout: {ex.Message}");
            SendCancel(adapter);
            progress?.Report(new TransferProgress
            {
                BytesTransferred = 0,
                TotalBytes = totalBytes,
                StatusMessage = $"Transfer failed: {ex.Message}"
            });
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"YModem error: {ex.Message}");
            SendCancel(adapter);
            progress?.Report(new TransferProgress
            {
                BytesTransferred = 0,
                TotalBytes = totalBytes,
                StatusMessage = $"Transfer failed: {ex.Message}"
            });
            return false;
        }
        finally
        {
            // Always restore normal communication mode
            adapter.OnRawDataReceived -= OnRawData;
            try { adapter.ExitRawMode(); } catch { /* best effort */ }
            _commManager.ResumeAfterTransfer();
        }
    }

    public Task<bool> DownloadFileAsync(string remoteFileName, string localFilePath,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        // YModem download from grblHAL is not supported (use $F<= to dump file contents)
        throw new NotSupportedException(
            "YModem download is not supported by grblHAL. Use the $F<= command to dump file contents.");
    }

    // ---- Packet builders ----

    /// <summary>Build Block 0 (SOH) containing filename and file size.</summary>
    private static byte[] BuildBlock0(string fileName, long fileSize)
    {
        var payload = new byte[SOH_DATA_SIZE];

        // filename\0
        var nameBytes = Encoding.ASCII.GetBytes(fileName);
        Array.Copy(nameBytes, payload, Math.Min(nameBytes.Length, SOH_DATA_SIZE - 1));
        int pos = nameBytes.Length;
        payload[pos++] = 0; // null terminator

        // file size as ASCII decimal
        var sizeStr = Encoding.ASCII.GetBytes(fileSize.ToString());
        if (pos + sizeStr.Length < SOH_DATA_SIZE)
            Array.Copy(sizeStr, 0, payload, pos, sizeStr.Length);

        // Remaining bytes are already 0x00 (padding)
        return WrapPacket(SOH, 0x00, payload);
    }

    /// <summary>Build a 1024-byte data packet (STX).</summary>
    private static byte[] BuildDataPacket(byte seqNum, byte[] data)
    {
        return WrapPacket(STX, seqNum, data);
    }

    /// <summary>Build an empty Block 0 to signal end of YModem batch.</summary>
    private static byte[] BuildEmptyBlock0()
    {
        var payload = new byte[SOH_DATA_SIZE]; // all zeros
        return WrapPacket(SOH, 0x00, payload);
    }

    /// <summary>Wrap payload with header byte, sequence numbers, and CRC16.</summary>
    private static byte[] WrapPacket(byte header, byte seqNum, byte[] payload)
    {
        // header(1) + seq(1) + ~seq(1) + payload(128|1024) + crc(2)
        var packet = new byte[1 + 1 + 1 + payload.Length + 2];
        packet[0] = header;
        packet[1] = seqNum;
        packet[2] = (byte)~seqNum;
        Array.Copy(payload, 0, packet, 3, payload.Length);

        var crc = YModemCrc.Calculate(payload);
        packet[^2] = (byte)(crc >> 8);   // CRC high byte
        packet[^1] = (byte)(crc & 0xFF); // CRC low byte

        return packet;
    }

    // ---- Communication helpers ----

    private void OnRawData(object? sender, byte[] data)
    {
        foreach (var b in data)
        {
            _rxQueue.Enqueue(b);
            _rxSignal.Release();
        }
    }

    /// <summary>Wait for a specific byte from the controller.</summary>
    private async Task WaitForByte(byte expected, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            // Try to get a byte with remaining timeout
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            if (await _rxSignal.WaitAsync(remaining, ct))
            {
                if (_rxQueue.TryDequeue(out var received))
                {
                    if (received == expected) return;

                    // NAK during data phase: caller handles retry
                    if (received == NAK)
                        throw new TimeoutException($"NAK received while waiting for 0x{expected:X2}");

                    if (received == CAN)
                        throw new OperationCanceledException("Transfer cancelled by controller (CAN received).");

                    // Ignore unexpected bytes
                    Debug.WriteLine($"YModem: ignoring unexpected byte 0x{received:X2} (waiting for 0x{expected:X2})");
                }
            }
        }

        throw new TimeoutException($"Timeout waiting for 0x{expected:X2} from controller.");
    }

    /// <summary>Send a packet, retrying on NAK up to MAX_RETRIES times.</summary>
    private async Task SendPacketWithRetry(ICommsAdapter adapter, byte[] packet, CancellationToken ct)
    {
        for (int attempt = 0; attempt < MAX_RETRIES; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            adapter.WriteBytes(packet, 0, packet.Length);

            try
            {
                return; // packet sent — ACK is waited for by the caller
            }
            catch (TimeoutException) when (attempt < MAX_RETRIES - 1)
            {
                Debug.WriteLine($"YModem: NAK on attempt {attempt + 1}, retrying...");
                await Task.Delay(100, ct);
            }
        }

        throw new TimeoutException($"Failed to send packet after {MAX_RETRIES} attempts.");
    }

    /// <summary>Send CAN CAN CAN to abort the transfer.</summary>
    private static void SendCancel(ICommsAdapter adapter)
    {
        try
        {
            var cancelBytes = new byte[] { CAN, CAN, CAN };
            adapter.WriteBytes(cancelBytes, 0, cancelBytes.Length);
        }
        catch { /* best effort */ }
    }
}
