using System;
using System.Collections.Generic;
using System.Globalization;

namespace GrbLHALSender.Gpio;

/// <summary>
/// The identify response from a PICOGPIO device, e.g.
/// <c>PICOGPIO 1 pins=0-22,26-28 wd=5000</c>.
/// <para>
/// Parsed separately from the transport because it is the gate that decides whether the app
/// writes to a serial port at all — and the port next to it might be the CNC controller.
/// See <c>docs/pico-gpio-protocol.md</c>.
/// </para>
/// </summary>
internal sealed class PicoBanner
{
    /// <summary>Highest protocol major this build speaks.</summary>
    public const int SupportedVersion = 1;

    private const string Marker = "PICOGPIO";

    public int Version { get; private init; }
    public int WatchdogMs { get; private init; }
    private IReadOnlyList<(int Low, int High)> PinRanges { get; init; } = [];

    public bool IsValidPin(int pin)
    {
        foreach (var (low, high) in PinRanges)
            if (pin >= low && pin <= high) return true;
        return false;
    }

    /// <summary>
    /// Interval to send heartbeats at — half the device's watchdog, so a single dropped
    /// line cannot trip it. Zero when the device reports no watchdog.
    /// </summary>
    public int HeartbeatMs => WatchdogMs <= 0 ? 0 : Math.Max(250, WatchdogMs / 2);

    public static bool TryParse(string? line, out PicoBanner banner)
    {
        banner = null!;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        if (!parts[0].Equals(Marker, StringComparison.Ordinal)) return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
            return false;

        var ranges = new List<(int, int)>();
        var watchdogMs = 0;

        // Unknown key=value fields are skipped rather than rejected, so a later firmware can
        // add one without this build refusing to talk to it.
        for (int i = 2; i < parts.Length; i++)
        {
            var field = parts[i];
            if (field.StartsWith("pins=", StringComparison.OrdinalIgnoreCase))
                ParsePins(field[5..], ranges);
            else if (field.StartsWith("wd=", StringComparison.OrdinalIgnoreCase))
                int.TryParse(field[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out watchdogMs);
        }

        if (ranges.Count == 0) return false;

        banner = new PicoBanner
        {
            Version = version,
            WatchdogMs = watchdogMs,
            PinRanges = ranges,
        };
        return true;
    }

    /// <summary>Reads "0-22,26-28" or "5,6,16" into inclusive ranges.</summary>
    private static void ParsePins(string spec, List<(int, int)> ranges)
    {
        foreach (var token in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = token.IndexOf('-');
            if (dash > 0)
            {
                if (int.TryParse(token[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var low) &&
                    int.TryParse(token[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var high) &&
                    low <= high)
                    ranges.Add((low, high));
            }
            else if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var single))
            {
                ranges.Add((single, single));
            }
        }
    }

    /// <summary>
    /// A major this build does not speak. Refused rather than attempted: the commands may
    /// mean something different, and the thing on the other end switches mains hardware.
    /// </summary>
    public bool IsSupported => Version == SupportedVersion;
}
