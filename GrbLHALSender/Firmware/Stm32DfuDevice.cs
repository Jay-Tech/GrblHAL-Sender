using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace GrbLHALSender.Firmware;

/// <summary>
/// Thrown for firmware-install failures with a message suitable for direct
/// display in the UI (driver hints, protocol errors, etc.).
/// </summary>
public class FirmwareInstallException : Exception
{
    public FirmwareInstallException(string message) : base(message) { }
    public FirmwareInstallException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// STM32 ROM bootloader (DfuSe, AN3156) client over libusb.
/// Handles the built-in USB DFU bootloader that STM32F4 parts expose after
/// a $DFU reboot: sector erase, program, verify (read-back) and leave.
/// </summary>
public sealed class Stm32DfuDevice : IDisposable
{
    public const int StVendorId = 0x0483;
    public const int DfuProductId = 0xDF11;

    // DFU class requests (USB DFU 1.1)
    private const byte ReqDnload = 1;
    private const byte ReqUpload = 2;
    private const byte ReqGetStatus = 3;
    private const byte ReqClrStatus = 4;
    private const byte ReqAbort = 6;

    // bmRequestType: class request to interface
    private const byte RequestOut = 0x21;
    private const byte RequestIn = 0xA1;

    // DFU states
    private const byte StateDfuIdle = 2;
    private const byte StateDnBusy = 4;
    private const byte StateDnloadIdle = 5;
    private const byte StateManifest = 7;
    private const byte StateUploadIdle = 9;
    private const byte StateError = 10;

    private static readonly string[] StatusNames =
    {
        "OK", "errTARGET", "errFILE", "errWRITE", "errERASE", "errCHECK_ERASED",
        "errPROG", "errVERIFY", "errADDRESS", "errNOTDONE", "errFIRMWARE",
        "errVENDOR", "errUSBR", "errPOR", "errUNKNOWN", "errSTALLEDPKT"
    };

    // LibUsbDotNet devices are permanently tied to the UsbContext object that
    // enumerated them — disposing the context invalidates every device from it
    // ("Cannot operate on UsbDevice whose originating context has been disposed").
    // So one context is kept alive for the whole app session and never disposed.
    private static UsbContext? _sharedContext;
    private static readonly object ContextLock = new();

    private readonly IUsbDevice _device;
    private readonly int _interfaceNumber;

    public int TransferSize { get; }
    public IReadOnlyList<FlashSector> Sectors { get; }
    public string FlashDescription { get; }

    public record FlashSector(uint Address, uint Size, bool Writable);

    private Stm32DfuDevice(IUsbDevice device, int interfaceNumber,
        int transferSize, List<FlashSector> sectors, string flashDescription)
    {
        _device = device;
        _interfaceNumber = interfaceNumber;
        TransferSize = transferSize;
        Sectors = sectors;
        FlashDescription = flashDescription;
    }

    private static UsbContext GetContext()
    {
        lock (ContextLock)
            return _sharedContext ??= new UsbContext();
    }

    /// <summary>
    /// Returns true if an STM32 DFU bootloader device is currently attached,
    /// without attempting to open it.
    /// </summary>
    public static bool IsPresent()
    {
        try
        {
            using var devices = GetContext().List();
            return devices.Any(d => d.VendorId == StVendorId && d.ProductId == DfuProductId);
        }
        catch
        {
            return false; // libusb unavailable — treated as "not detected"
        }
    }

    /// <summary>
    /// Finds and opens the STM32 DFU bootloader, reading its transfer size and
    /// flash sector layout from the descriptors. Returns null when no DFU device
    /// is attached; throws with a driver hint when one is attached but can't be opened.
    /// </summary>
    public static Stm32DfuDevice? Open()
    {
        // Keep the matched device from the enumeration and dispose the rest —
        // devices stay valid because the shared context is never disposed.
        IUsbDevice? target = null;
        foreach (var device in GetContext().List())
        {
            if (target == null &&
                device.VendorId == StVendorId && device.ProductId == DfuProductId)
                target = device;
            else
                device.Dispose();
        }

        if (target == null)
            return null;

        if (!target.TryOpen())
        {
            target.Dispose();
            throw new FirmwareInstallException(
                "An STM32 DFU device was detected but could not be opened. " +
                "On Windows the bootloader needs the WinUSB driver: install it once with " +
                "STM32CubeProgrammer, or with Zadig (select 'STM32 BOOTLOADER' and install WinUSB), " +
                "then try again.");
        }

        try
        {
            return Configure(target);
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    private static Stm32DfuDevice Configure(IUsbDevice device)
    {
        // Read the raw configuration descriptor to locate the DFU interface,
        // its alternate setting for internal flash, and the DFU functional
        // descriptor's wTransferSize.
        var config = ReadConfigDescriptor(device);
        int interfaceNumber = -1;
        int altSetting = -1;
        byte flashStringIndex = 0;
        int transferSize = 1024;
        var foundInternalFlash = false;

        for (var i = 0; i + 1 < config.Length && config[i] > 0; i += config[i])
        {
            var length = config[i];
            var type = config[i + 1];

            if (type == 0x04 && length >= 9 &&
                config[i + 5] == 0xFE && config[i + 6] == 0x01)
            {
                // DFU interface alt setting — prefer the internal flash segment
                // (its name starts with "@Internal Flash").
                var iInterface = config[i + 8];
                var name = GetString(device, iInterface);
                var isInternalFlash =
                    name.StartsWith("@Internal Flash", StringComparison.OrdinalIgnoreCase);
                if (interfaceNumber < 0 || (isInternalFlash && !foundInternalFlash))
                {
                    interfaceNumber = config[i + 2];
                    altSetting = config[i + 3];
                    flashStringIndex = iInterface;
                    foundInternalFlash |= isInternalFlash;
                }
            }
            else if (type == 0x21 && length >= 9)
            {
                transferSize = config[i + 5] | (config[i + 6] << 8);
            }
        }

        if (interfaceNumber < 0)
            throw new FirmwareInstallException("Device does not expose a DFU interface.");

        var flashName = GetString(device, flashStringIndex);
        var sectors = ParseSectorMap(flashName);
        if (sectors.Count == 0)
            throw new FirmwareInstallException(
                $"Could not parse the flash layout reported by the bootloader: \"{flashName}\"");

        device.ClaimInterface(interfaceNumber);
        if (altSetting > 0)
            device.SetAltInterface(altSetting);

        return new Stm32DfuDevice(device, interfaceNumber,
            transferSize, sectors, flashName);
    }

    private static byte[] ReadConfigDescriptor(IUsbDevice device)
    {
        var header = new byte[9];
        var setup = new UsbSetupPacket(0x80, 0x06, 0x0200, 0, (short)header.Length);
        var read = device.ControlTransfer(setup, header, 0, header.Length);
        if (read < 9)
            throw new FirmwareInstallException("Failed to read the USB configuration descriptor.");

        var totalLength = header[2] | (header[3] << 8);
        var full = new byte[totalLength];
        setup = new UsbSetupPacket(0x80, 0x06, 0x0200, 0, (short)totalLength);
        read = device.ControlTransfer(setup, full, 0, full.Length);
        if (read < totalLength)
            throw new FirmwareInstallException("Short read of the USB configuration descriptor.");
        return full;
    }

    private static string GetString(IUsbDevice device, byte index)
    {
        if (index == 0) return "";
        try
        {
            return device.GetStringDescriptor(index, failSilently: true) ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Parses a DfuSe flash description such as
    /// "@Internal Flash  /0x08000000/04*016Kg,01*064Kg,07*128Kg"
    /// into the list of erasable sectors.
    /// </summary>
    internal static List<FlashSector> ParseSectorMap(string description)
    {
        var sectors = new List<FlashSector>();
        var parts = description.Split('/');
        // parts: [name, baseAddress, segments, baseAddress2, segments2, ...]
        for (var p = 1; p + 1 < parts.Length; p += 2)
        {
            var addressText = parts[p].Trim();
            if (addressText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                addressText = addressText[2..];
            if (!uint.TryParse(addressText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
                continue;

            foreach (var segment in parts[p + 1].Split(','))
            {
                // Format: <count>*<size><unit letter><access letter> e.g. 04*016Kg
                var star = segment.IndexOf('*');
                if (star < 0) continue;
                if (!uint.TryParse(segment[..star].Trim(), out var count)) continue;

                var sizePart = segment[(star + 1)..].Trim();
                var digits = 0;
                while (digits < sizePart.Length && char.IsDigit(sizePart[digits])) digits++;
                if (digits == 0 || !uint.TryParse(sizePart[..digits], out var size)) continue;

                var suffix = sizePart[digits..];
                if (suffix.StartsWith('K')) size *= 1024;
                else if (suffix.StartsWith('M')) size *= 1024 * 1024;

                // Access letter: 'a'..'g' — bit 2 (>= 'e') means writable
                var access = suffix.Length > 0 ? suffix[^1] : 'g';
                var writable = access >= 'd';

                for (var s = 0; s < count; s++)
                {
                    sectors.Add(new FlashSector(address, size, writable));
                    address += size;
                }
            }
        }
        return sectors;
    }

    // ---- DFU protocol primitives ----

    private (byte Status, int PollTimeoutMs, byte State) GetStatus()
    {
        var buffer = new byte[6];
        var setup = new UsbSetupPacket(RequestIn, ReqGetStatus, 0, (short)_interfaceNumber, 6);
        var read = _device.ControlTransfer(setup, buffer, 0, 6);
        if (read != 6)
            throw new FirmwareInstallException("DFU GETSTATUS failed.");
        var pollTimeout = buffer[1] | (buffer[2] << 8) | (buffer[3] << 16);
        return (buffer[0], pollTimeout, buffer[4]);
    }

    private void ClearStatus()
    {
        var setup = new UsbSetupPacket(RequestOut, ReqClrStatus, 0, (short)_interfaceNumber, 0);
        _device.ControlTransfer(setup);
    }

    private void Abort()
    {
        var setup = new UsbSetupPacket(RequestOut, ReqAbort, 0, (short)_interfaceNumber, 0);
        _device.ControlTransfer(setup);
    }

    private void Dnload(int blockNumber, byte[] data, int length)
    {
        var setup = new UsbSetupPacket(RequestOut, ReqDnload, (short)blockNumber,
            (short)_interfaceNumber, (short)length);
        var written = _device.ControlTransfer(setup, data, 0, length);
        if (written != length)
            throw new FirmwareInstallException("DFU DNLOAD transfer was truncated.");
    }

    private int Upload(int blockNumber, byte[] buffer, int length)
    {
        var setup = new UsbSetupPacket(RequestIn, ReqUpload, (short)blockNumber,
            (short)_interfaceNumber, (short)length);
        return _device.ControlTransfer(setup, buffer, 0, length);
    }

    /// <summary>
    /// Polls GETSTATUS until the pending download operation completes,
    /// honouring the bwPollTimeout the bootloader asks for (erases can take seconds).
    /// </summary>
    private void WaitForDnloadComplete(string operation)
    {
        while (true)
        {
            var (status, pollTimeout, state) = GetStatus();

            if (state == StateError || (status != 0 && status < StatusNames.Length))
            {
                var name = status < StatusNames.Length ? StatusNames[status] : $"0x{status:X2}";
                ClearStatus();
                throw new FirmwareInstallException($"{operation} failed: DFU status {name}.");
            }

            if (state == StateDnBusy)
            {
                Thread.Sleep(Math.Max(pollTimeout, 1));
                continue;
            }

            // dfuDNLOAD-IDLE (or back to idle) — operation finished
            return;
        }
    }

    private void EnsureIdle()
    {
        var (_, _, state) = GetStatus();
        if (state == StateError)
            ClearStatus();
        else if (state is StateDnloadIdle or StateUploadIdle)
            Abort();
    }

    private void SetAddressPointer(uint address)
    {
        var command = new byte[5];
        command[0] = 0x21;
        WriteAddress(command, address);
        Dnload(0, command, command.Length);
        WaitForDnloadComplete("Set address");
    }

    private void EraseSector(uint address)
    {
        var command = new byte[5];
        command[0] = 0x41;
        WriteAddress(command, address);
        Dnload(0, command, command.Length);
        WaitForDnloadComplete($"Erase sector 0x{address:X8}");
    }

    private static void WriteAddress(byte[] command, uint address)
    {
        command[1] = (byte)(address & 0xFF);
        command[2] = (byte)((address >> 8) & 0xFF);
        command[3] = (byte)((address >> 16) & 0xFF);
        command[4] = (byte)((address >> 24) & 0xFF);
    }

    // ---- High-level operations ----

    /// <summary>
    /// Erases every flash sector overlapping [address, address + length).
    /// </summary>
    public void Erase(uint address, int length, Action<double>? progress = null,
        CancellationToken cancellation = default)
    {
        var affected = Sectors
            .Where(s => s.Address < address + (uint)length && s.Address + s.Size > address)
            .ToList();

        if (affected.Count == 0)
            throw new FirmwareInstallException(
                $"Image address range 0x{address:X8}-0x{address + (uint)length:X8} is outside " +
                $"the device flash ({FlashDescription}).");
        if (affected.Any(s => !s.Writable))
            throw new FirmwareInstallException("Part of the target flash range is write-protected.");

        EnsureIdle();
        for (var i = 0; i < affected.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            EraseSector(affected[i].Address);
            progress?.Invoke((i + 1) / (double)affected.Count);
        }
    }

    /// <summary>
    /// Programs the image starting at the given flash address.
    /// </summary>
    public void Program(uint address, byte[] data, Action<double>? progress = null,
        CancellationToken cancellation = default)
    {
        EnsureIdle();
        SetAddressPointer(address);

        var block = 2; // wValue 0/1 are reserved for DfuSe commands
        for (var offset = 0; offset < data.Length; offset += TransferSize, block++)
        {
            cancellation.ThrowIfCancellationRequested();
            var chunkLength = Math.Min(TransferSize, data.Length - offset);
            var chunk = new byte[chunkLength];
            Array.Copy(data, offset, chunk, 0, chunkLength);
            Dnload(block, chunk, chunkLength);
            WaitForDnloadComplete($"Program at 0x{address + (uint)offset:X8}");
            progress?.Invoke(Math.Min(1.0, (offset + chunkLength) / (double)data.Length));
        }
    }

    /// <summary>
    /// Reads back the programmed range and compares it with the image.
    /// </summary>
    public void Verify(uint address, byte[] data, Action<double>? progress = null,
        CancellationToken cancellation = default)
    {
        EnsureIdle();
        SetAddressPointer(address);
        Abort(); // return to dfuIDLE so UPLOAD starts from the address pointer

        var buffer = new byte[TransferSize];
        var block = 2;
        for (var offset = 0; offset < data.Length; offset += TransferSize, block++)
        {
            cancellation.ThrowIfCancellationRequested();
            var chunkLength = Math.Min(TransferSize, data.Length - offset);
            var read = Upload(block, buffer, chunkLength);
            if (read != chunkLength)
                throw new FirmwareInstallException(
                    $"Verify read at 0x{address + (uint)offset:X8} was truncated.");

            for (var i = 0; i < chunkLength; i++)
            {
                if (buffer[i] != data[offset + i])
                    throw new FirmwareInstallException(
                        $"Verify mismatch at 0x{address + (uint)(offset + i):X8}: " +
                        $"wrote 0x{data[offset + i]:X2}, read 0x{buffer[i]:X2}.");
            }
            progress?.Invoke(Math.Min(1.0, (offset + chunkLength) / (double)data.Length));
        }
        Abort();
    }

    /// <summary>
    /// Tells the bootloader to jump to the application at the given address.
    /// The device drops off the bus, so USB errors during manifest are expected.
    /// </summary>
    public void Leave(uint address)
    {
        try
        {
            EnsureIdle();
            SetAddressPointer(address);
            Dnload(2, Array.Empty<byte>(), 0); // zero-length download = manifest/leave
            var (_, _, state) = GetStatus();
            _ = state == StateManifest; // expected; device resets immediately after
        }
        catch
        {
            // The device re-enumerating mid-request surfaces as a USB error — ignore.
        }
    }

    public void Dispose()
    {
        try { _device.ReleaseInterface(_interfaceNumber); } catch { /* device may be gone */ }
        try { _device.Dispose(); } catch { }
        // The shared UsbContext is intentionally left alive — disposing it would
        // invalidate any device enumerated from it elsewhere in the session.
    }
}
