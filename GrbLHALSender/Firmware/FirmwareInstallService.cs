using GrbLHALSender.Communication;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GrbLHALSender.Firmware;

public record FirmwareProgress(string Phase, double Percent, string Message);

/// <summary>
/// Orchestrates a firmware install: reboots a connected grblHAL controller
/// into the STM32 ROM bootloader with $DFU, then erases, programs, verifies
/// and restarts using the DfuSe protocol.
/// </summary>
public class FirmwareInstallService
{
    public const string WebBuilderUrl = "https://svn.io-engineering.com:8443/";
    private const string EnterDfuCommand = "$DFU";

    // STM32F4 internal flash lives at 0x08000000; anything outside means the
    // hex file wasn't built for this MCU family.
    private const uint FlashBase = 0x08000000;
    private const uint FlashLimit = 0x08200000;

    private readonly CommunicationManager _commManager;

    public FirmwareInstallService(CommunicationManager commManager)
    {
        _commManager = commManager;
    }

    public bool IsDfuDevicePresent() => Stm32DfuDevice.IsPresent();

    /// <summary>
    /// Loads and validates a hex file, returning a summary string for the UI.
    /// </summary>
    public static string DescribeHexFile(string path)
    {
        var hex = IntelHexFile.Load(path);
        ValidateAddressRange(hex);
        return $"{hex.Data.Length / 1024.0:F1} KB at 0x{hex.StartAddress:X8}";
    }

    private static void ValidateAddressRange(IntelHexFile hex)
    {
        if (hex.StartAddress < FlashBase || hex.EndAddress > FlashLimit)
            throw new FirmwareInstallException(
                $"Image targets 0x{hex.StartAddress:X8}-0x{hex.EndAddress:X8}, which is outside " +
                "STM32 internal flash (0x08000000). This does not look like an STM32 firmware file.");
    }

    public async Task InstallAsync(string hexPath, IProgress<FirmwareProgress> progress,
        CancellationToken cancellation)
    {
        progress.Report(new FirmwareProgress("Preparing", 0, "Reading hex file..."));
        var hex = IntelHexFile.Load(hexPath);
        ValidateAddressRange(hex);

        // If the controller is connected over serial/TCP and no bootloader is
        // on the bus yet, ask the firmware to reboot into DFU mode.
        if (_commManager.Adapter?.IsConnected == true && !Stm32DfuDevice.IsPresent())
        {
            progress.Report(new FirmwareProgress("Rebooting", 0,
                "Sending $DFU — controller is rebooting into the bootloader..."));
            _commManager.StopPoll();
            _commManager.SendCommand(EnterDfuCommand);
            await Task.Delay(1500, cancellation);
            try
            {
                _commManager.Adapter.Close();
            }
            catch
            {
                // The port usually vanishes when the MCU re-enumerates — expected.
            }
        }

        progress.Report(new FirmwareProgress("Waiting", 0, "Waiting for the DFU bootloader..."));
        using var dfu = await WaitForBootloaderAsync(cancellation);

        progress.Report(new FirmwareProgress("Erasing", 0,
            $"Erasing flash ({dfu.FlashDescription.Split('/')[0].Trim().TrimStart('@')})..."));
        await Task.Run(() => dfu.Erase(hex.StartAddress, hex.Data.Length,
            p => progress.Report(new FirmwareProgress("Erasing", p * 100, "Erasing flash sectors...")),
            cancellation), cancellation);

        progress.Report(new FirmwareProgress("Programming", 0, "Writing firmware..."));
        await Task.Run(() => dfu.Program(hex.StartAddress, hex.Data,
            p => progress.Report(new FirmwareProgress("Programming", p * 100,
                $"Writing firmware ({p * hex.Data.Length / 1024.0:F0} / {hex.Data.Length / 1024.0:F0} KB)...")),
            cancellation), cancellation);

        progress.Report(new FirmwareProgress("Verifying", 0, "Verifying flash contents..."));
        await Task.Run(() => dfu.Verify(hex.StartAddress, hex.Data,
            p => progress.Report(new FirmwareProgress("Verifying", p * 100, "Verifying flash contents...")),
            cancellation), cancellation);

        progress.Report(new FirmwareProgress("Restarting", 100, "Restarting controller..."));
        dfu.Leave(hex.StartAddress);

        progress.Report(new FirmwareProgress("Done", 100,
            "Firmware installed successfully. Reconnect to the controller."));
    }

    private static async Task<Stm32DfuDevice> WaitForBootloaderAsync(CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();

            // Open() throws a descriptive error if the device is present but the
            // WinUSB driver is missing — let that propagate to the UI immediately.
            var dfu = await Task.Run(Stm32DfuDevice.Open, cancellation);
            if (dfu != null)
                return dfu;

            if (DateTime.UtcNow > deadline)
                throw new FirmwareInstallException(
                    "No STM32 DFU bootloader was detected. If the controller did not reboot " +
                    "automatically, put it in DFU mode manually: hold the BOOT0 button (or set the " +
                    "BOOT0 jumper) while pressing reset, then click Install again.");

            await Task.Delay(500, cancellation);
        }
    }
}
