using System;

namespace GrbLHALSender.SdCard;

/// <summary>
/// CRC16-XMODEM calculation used by the YModem protocol.
/// Polynomial: 0x1021, Initial value: 0x0000.
/// </summary>
internal static class YModemCrc
{
    public static ushort Calculate(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }
        return crc;
    }
}
