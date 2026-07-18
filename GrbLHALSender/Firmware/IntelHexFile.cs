using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GrbLHALSender.Firmware;

/// <summary>
/// Parses an Intel HEX (.hex) file into a single contiguous binary image.
/// Gaps between records are filled with 0xFF (erased-flash value).
/// </summary>
public class IntelHexFile
{
    public uint StartAddress { get; private set; }
    public byte[] Data { get; private set; } = Array.Empty<byte>();
    public uint EndAddress => (uint)(StartAddress + Data.Length);

    // A gap larger than this almost certainly means the hex file targets
    // multiple memory regions (e.g. flash + option bytes) and shouldn't be
    // flattened into one padded image.
    private const int MaxGapBytes = 512 * 1024;

    public static IntelHexFile Load(string path)
    {
        return Parse(File.ReadAllLines(path));
    }

    public static IntelHexFile Parse(IEnumerable<string> lines)
    {
        var segments = new List<(uint Address, byte[] Bytes)>();
        uint upperAddress = 0;
        var lineNumber = 0;
        var eofSeen = false;

        foreach (var rawLine in lines)
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line[0] != ':')
                throw new FormatException($"Line {lineNumber}: missing ':' record start.");
            if (line.Length < 11 || (line.Length - 1) % 2 != 0)
                throw new FormatException($"Line {lineNumber}: invalid record length.");

            var record = new byte[(line.Length - 1) / 2];
            for (var i = 0; i < record.Length; i++)
                record[i] = byte.Parse(line.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber);

            byte checksum = 0;
            foreach (var b in record) checksum += b;
            if (checksum != 0)
                throw new FormatException($"Line {lineNumber}: checksum mismatch.");

            var byteCount = record[0];
            if (record.Length != byteCount + 5)
                throw new FormatException($"Line {lineNumber}: length field does not match record size.");

            var offset = (uint)((record[1] << 8) | record[2]);
            var recordType = record[3];

            switch (recordType)
            {
                case 0x00: // Data
                    var bytes = new byte[byteCount];
                    Array.Copy(record, 4, bytes, 0, byteCount);
                    segments.Add((upperAddress + offset, bytes));
                    break;
                case 0x01: // End of file
                    eofSeen = true;
                    break;
                case 0x02: // Extended segment address (bits 4-19)
                    upperAddress = (uint)(((record[4] << 8) | record[5]) << 4);
                    break;
                case 0x04: // Extended linear address (upper 16 bits)
                    upperAddress = (uint)(((record[4] << 8) | record[5]) << 16);
                    break;
                case 0x03: // Start segment address — execution entry, not data
                case 0x05: // Start linear address — execution entry, not data
                    break;
                default:
                    throw new FormatException($"Line {lineNumber}: unsupported record type 0x{recordType:X2}.");
            }

            if (eofSeen) break;
        }

        if (!eofSeen)
            throw new FormatException("Missing end-of-file record — file may be truncated.");
        if (segments.Count == 0)
            throw new FormatException("Hex file contains no data records.");

        segments.Sort((a, b) => a.Address.CompareTo(b.Address));

        var start = segments[0].Address;
        var end = start;
        foreach (var (address, bytes) in segments)
        {
            if (address < end)
                throw new FormatException($"Overlapping data at address 0x{address:X8}.");
            if (address - end > MaxGapBytes)
                throw new FormatException(
                    $"Gap of {address - end} bytes at 0x{end:X8} — hex file spans disjoint memory regions.");
            end = address + (uint)bytes.Length;
        }

        var image = new byte[end - start];
        image.AsSpan().Fill(0xFF);
        foreach (var (address, bytes) in segments)
            bytes.CopyTo(image, (int)(address - start));

        return new IntelHexFile { StartAddress = start, Data = image };
    }
}
