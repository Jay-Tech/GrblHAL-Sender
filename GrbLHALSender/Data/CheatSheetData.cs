using System;
using System.Collections.Generic;
using System.Linq;

namespace GrbLHALSender.Data;

public sealed class CheatEntry
{
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsHal { get; init; }
    public string? Note { get; init; }
    // Set by the owning section so the row template can color the code
    // column without reaching back up the visual tree.
    public string CodeColor { get; internal set; } = "#E8A02E";
}

public sealed class CheatSection
{
    public string Title { get; }
    public string Category { get; }
    public string Subtitle { get; }
    public bool IsAlarm { get; }
    public IReadOnlyList<CheatEntry> Entries { get; }
    public string TitleColor => IsAlarm ? "#E06666" : "#E8A02E";

    public CheatSection(string title, string category, string subtitle, bool isAlarm, IReadOnlyList<CheatEntry> entries)
    {
        Title = title;
        Category = category;
        Subtitle = subtitle;
        IsAlarm = isAlarm;
        Entries = entries;
        foreach (var e in entries)
            e.CodeColor = isAlarm ? "#E06666" : "#E8A02E";
    }

    public CheatSection? Filter(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return this;
        var matches = Entries.Where(e =>
                e.Code.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (e.Note?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
        return matches.Count == 0 ? null : new CheatSection(Title, Category, Subtitle, IsAlarm, matches);
    }
}

/// <summary>
/// Static grblHAL quick-reference content. Error and alarm texts follow the
/// grblHAL core sources (errors.c / alarms.h); codes tagged IsHal are grblHAL
/// extensions beyond classic Grbl 1.1.
/// </summary>
public static class CheatSheetData
{
    public static readonly IReadOnlyList<string> Categories =
        new[] { "All", "G-codes", "M-codes", "Errors", "Alarms", "Realtime" };

    public static readonly IReadOnlyList<CheatSection> Sections = new List<CheatSection>
    {
        new("G-codes — Motion", "G-codes", "Modal motion commands.", false, new List<CheatEntry>
        {
            new() { Code = "G0", Description = "Rapid positioning (non-cutting move at max rate)" },
            new() { Code = "G1", Description = "Linear feed move at current feed rate (F)" },
            new() { Code = "G2", Description = "Clockwise arc (I/J/K offsets or R radius)" },
            new() { Code = "G3", Description = "Counter-clockwise arc" },
            new() { Code = "G5", Description = "Cubic spline", IsHal = true },
            new() { Code = "G33", Description = "Spindle-synchronized motion (threading, lathe)", IsHal = true },
            new() { Code = "G38.2", Description = "Probe toward workpiece, error on failure" },
            new() { Code = "G38.3", Description = "Probe toward workpiece, no error on failure" },
            new() { Code = "G38.4", Description = "Probe away from workpiece, error on failure" },
            new() { Code = "G38.5", Description = "Probe away from workpiece, no error on failure" },
            new() { Code = "G80", Description = "Cancel motion mode / canned cycle" },
        }),

        new("G-codes — Canned cycles", "G-codes", "grblHAL extension; availability depends on the build.", false, new List<CheatEntry>
        {
            new() { Code = "G73", Description = "Drilling cycle with chip breaking", IsHal = true },
            new() { Code = "G76", Description = "Threading cycle (lathe builds)", IsHal = true },
            new() { Code = "G81", Description = "Drilling cycle", IsHal = true },
            new() { Code = "G82", Description = "Drilling cycle with dwell", IsHal = true },
            new() { Code = "G83", Description = "Peck drilling cycle", IsHal = true },
            new() { Code = "G85", Description = "Boring cycle, feed out", IsHal = true },
            new() { Code = "G86", Description = "Boring cycle, spindle stop, rapid out", IsHal = true },
            new() { Code = "G89", Description = "Boring cycle with dwell", IsHal = true },
            new() { Code = "G98", Description = "Canned cycle: retract to initial Z", IsHal = true },
            new() { Code = "G99", Description = "Canned cycle: retract to R plane", IsHal = true },
        }),

        new("G-codes — Setup & modal state", "G-codes", "Coordinate systems, offsets, units and feed modes.", false, new List<CheatEntry>
        {
            new() { Code = "G4", Description = "Dwell (P = seconds)" },
            new() { Code = "G7 / G8", Description = "Lathe diameter / radius mode", IsHal = true },
            new() { Code = "G10 L2", Description = "Set work coordinate system origin (P1–P9 = G54–G59.3)" },
            new() { Code = "G10 L20", Description = "Set WCS origin so current position equals given value" },
            new() { Code = "G17/18/19", Description = "Plane select: XY / ZX / YZ" },
            new() { Code = "G20 / G21", Description = "Units: inches / millimeters" },
            new() { Code = "G28 / G28.1", Description = "Go to / set predefined position 1" },
            new() { Code = "G30 / G30.1", Description = "Go to / set predefined position 2" },
            new() { Code = "G40", Description = "Cutter radius compensation off (only mode supported)" },
            new() { Code = "G43 H", Description = "Tool length offset from tool table", IsHal = true },
            new() { Code = "G43.1", Description = "Dynamic tool length offset" },
            new() { Code = "G43.2", Description = "Additive tool length offset", IsHal = true },
            new() { Code = "G49", Description = "Cancel tool length offset" },
            new() { Code = "G53", Description = "Move in machine coordinates (with G0/G1 only)" },
            new() { Code = "G54–G59.3", Description = "Select work coordinate system 1–9" },
            new() { Code = "G61", Description = "Exact path mode", IsHal = true },
            new() { Code = "G90 / G91", Description = "Distance mode: absolute / incremental" },
            new() { Code = "G90.1 / G91.1", Description = "Arc IJK distance mode: absolute / incremental", IsHal = true },
            new() { Code = "G92", Description = "Set coordinate offset (current position = given value)" },
            new() { Code = "G92.1", Description = "Clear G92 offsets" },
            new() { Code = "G93", Description = "Inverse time feed mode" },
            new() { Code = "G94", Description = "Units per minute feed mode (default)" },
            new() { Code = "G95", Description = "Units per revolution feed mode", IsHal = true },
            new() { Code = "G96 / G97", Description = "Constant surface speed on / off (lathe)", IsHal = true },
        }),

        new("M-codes", "M-codes", "I/O-dependent codes (M62–M68) need the driver/board to expose aux ports.", false, new List<CheatEntry>
        {
            new() { Code = "M0", Description = "Program pause (resume with cycle start)" },
            new() { Code = "M1", Description = "Optional pause (if optional-stop enabled)" },
            new() { Code = "M2", Description = "Program end" },
            new() { Code = "M30", Description = "Program end (and pallet change on supported builds)" },
            new() { Code = "M3", Description = "Spindle on, clockwise (S = RPM)" },
            new() { Code = "M4", Description = "Spindle on, counter-clockwise" },
            new() { Code = "M5", Description = "Spindle stop" },
            new() { Code = "M6 T", Description = "Tool change (behavior set by $341 tool change mode)", IsHal = true },
            new() { Code = "M61 Q", Description = "Set current tool number without a change", IsHal = true },
            new() { Code = "M7", Description = "Mist coolant on" },
            new() { Code = "M8", Description = "Flood coolant on" },
            new() { Code = "M9", Description = "All coolant off" },
            new() { Code = "M48 / M49", Description = "Enable / disable feed & spindle override switches", IsHal = true },
            new() { Code = "M50 P", Description = "Feed override control (P0 disable, P1 enable)", IsHal = true },
            new() { Code = "M51 P", Description = "Spindle speed override control", IsHal = true },
            new() { Code = "M53 P", Description = "Feed stop control", IsHal = true },
            new() { Code = "M56 P", Description = "Parking motion override control", IsHal = true },
            new() { Code = "M62 / M63", Description = "Digital output on / off, synchronized with motion (P = port)", IsHal = true },
            new() { Code = "M64 / M65", Description = "Digital output on / off, immediate", IsHal = true },
            new() { Code = "M66", Description = "Wait on input (P/E = port, L = wait mode, Q = timeout)", IsHal = true },
            new() { Code = "M67 / M68", Description = "Analog output, synchronized / immediate", IsHal = true },
            new() { Code = "M70–M73", Description = "Save / invalidate / restore modal state", IsHal = true },
            new() { Code = "M99", Description = "Return from filesystem macro", IsHal = true },
        }),

        new("Error codes", "Errors", "Returned as error:N when a command is rejected — the block was not executed.", false, new List<CheatEntry>
        {
            new() { Code = "1", Description = "G-code word letter not found (words are a letter + value)" },
            new() { Code = "2", Description = "Missing or badly formatted numeric value in G-code word" },
            new() { Code = "3", Description = "'$' system command not recognized or supported" },
            new() { Code = "4", Description = "Negative value received where a positive one is expected" },
            new() { Code = "5", Description = "Homing cycle failure — homing not enabled in settings ($22)" },
            new() { Code = "6", Description = "Step pulse time must be ≥ 2 microseconds ($0)" },
            new() { Code = "7", Description = "Settings read failed — affected settings restored to defaults" },
            new() { Code = "8", Description = "'$' command only allowed when controller is IDLE" },
            new() { Code = "9", Description = "G-code locked out during alarm or jog state" },
            new() { Code = "10", Description = "Soft limits require homing to be enabled too" },
            new() { Code = "11", Description = "Max characters per line exceeded — line not executed" },
            new() { Code = "12", Description = "Setting value would exceed the max supported step rate" },
            new() { Code = "13", Description = "Safety door detected as open; door state initiated" },
            new() { Code = "14", Description = "Build info / startup line exceeds length limit — not stored" },
            new() { Code = "15", Description = "Jog target exceeds machine travel — jog ignored" },
            new() { Code = "16", Description = "Jog command missing '=' or contains prohibited G-code" },
            new() { Code = "17", Description = "Laser mode requires a PWM-capable spindle output" },
            new() { Code = "18", Description = "Reset asserted", IsHal = true },
            new() { Code = "19", Description = "Non-positive value", IsHal = true },
            new() { Code = "20", Description = "Unsupported or invalid G-code command in block" },
            new() { Code = "21", Description = "More than one command from the same modal group in block" },
            new() { Code = "22", Description = "Feed rate has not yet been set or is undefined" },
            new() { Code = "23", Description = "Command requires an integer value" },
            new() { Code = "24", Description = "Two or more commands that both use axis words in block" },
            new() { Code = "25", Description = "Repeated G-code word in block" },
            new() { Code = "26", Description = "No axis words found where the command requires them" },
            new() { Code = "27", Description = "Line number value is invalid" },
            new() { Code = "28", Description = "Command is missing a required value word" },
            new() { Code = "29", Description = "Selected work coordinate system not supported" },
            new() { Code = "30", Description = "G53 only allowed with G0 and G1 motion modes" },
            new() { Code = "31", Description = "Axis words found when no command uses them" },
            new() { Code = "32", Description = "G2/G3 arcs need at least one in-plane axis word" },
            new() { Code = "33", Description = "Motion command target is invalid" },
            new() { Code = "34", Description = "Arc radius value is invalid" },
            new() { Code = "35", Description = "G2/G3 arcs need at least one in-plane offset word (I/J/K)" },
            new() { Code = "36", Description = "Unused value words found in block" },
            new() { Code = "37", Description = "G43.1 offset not assigned to the configured tool length axis" },
            new() { Code = "38", Description = "Tool number greater than max supported, or undefined tool" },
            new() { Code = "39", Description = "Value out of range", IsHal = true },
            new() { Code = "40", Description = "Command not allowed while a tool change is pending", IsHal = true },
            new() { Code = "41", Description = "Spindle not running when motion commanded in CSS or spindle-sync mode", IsHal = true },
            new() { Code = "42", Description = "Plane must be ZX for threading", IsHal = true },
            new() { Code = "43", Description = "Max feed rate exceeded", IsHal = true },
            new() { Code = "44", Description = "RPM out of range", IsHal = true },
            new() { Code = "45", Description = "Only homing allowed while a limit switch is engaged", IsHal = true },
            new() { Code = "46", Description = "Home machine to continue ($H)", IsHal = true },
            new() { Code = "47", Description = "ATC: current tool not set — set it with M61", IsHal = true },
            new() { Code = "48", Description = "Value word conflict", IsHal = true },
            new() { Code = "49", Description = "Power-on self test failed — hard reset required", IsHal = true },
            new() { Code = "50", Description = "Emergency stop active", IsHal = true },
            new() { Code = "51", Description = "Motor fault", IsHal = true },
            new() { Code = "52", Description = "Setting value out of range", IsHal = true },
            new() { Code = "53", Description = "Setting not available (limited driver support)", IsHal = true },
            new() { Code = "54", Description = "Retract position is less than drill depth", IsHal = true },
            new() { Code = "55", Description = "Attempt to home two auto-squared axes at the same time", IsHal = true },
            new() { Code = "56", Description = "Coordinate system is locked", IsHal = true },
            new() { Code = "57", Description = "Unexpected file demarcation (%)", IsHal = true },
            new() { Code = "58", Description = "Aux port not available", IsHal = true },
            new() { Code = "60–64", Description = "SD card / filesystem: mount failed, file open/read failed, directory listing failed, directory not found, not mounted", IsHal = true },
            new() { Code = "70", Description = "Bluetooth initialization failed", IsHal = true },
            new() { Code = "71–76", Description = "Expression errors: unknown operator, divide by zero, argument out of range, invalid argument, syntax error, NAN/infinity result", IsHal = true },
            new() { Code = "77–79", Description = "Authentication required / access denied / not allowed during critical event", IsHal = true },
            new() { Code = "80–83", Description = "Flow control (macro o-words): only allowed in filesystem macro, unknown statement, stack overflow, out of memory", IsHal = true },
            new() { Code = "253", Description = "User-defined error (raised from macro/plugin)", IsHal = true },
        }),

        new("Alarm codes", "Alarms", "Reported as ALARM:N. Unlock with $X, or re-home with $H; E-stop and motor faults need the cause removed plus a reset (Ctrl-X).", true, new List<CheatEntry>
        {
            new() { Code = "1", Description = "Hard limit triggered", Note = "Position likely lost — re-home before continuing." },
            new() { Code = "2", Description = "Soft limit: motion target exceeds machine travel", Note = "Position retained; $X to unlock, check WCS zero and job size." },
            new() { Code = "3", Description = "Reset while in motion — position lost", Note = "Re-home before continuing." },
            new() { Code = "4", Description = "Probe fail: probe not in expected initial state before start" },
            new() { Code = "5", Description = "Probe fail: no contact within programmed travel (G38.2/.4)" },
            new() { Code = "6", Description = "Homing fail: reset during active homing cycle" },
            new() { Code = "7", Description = "Homing fail: safety door opened during homing" },
            new() { Code = "8", Description = "Homing fail: limit switch still engaged after pull-off", Note = "Increase $27 pull-off distance or check switch wiring." },
            new() { Code = "9", Description = "Homing fail: limit switch not found within search distance", Note = "Check $130-series max travel and switch wiring." },
            new() { Code = "10", Description = "E-stop asserted", IsHal = true },
            new() { Code = "11", Description = "Homing required — machine must be homed before use", IsHal = true },
            new() { Code = "12", Description = "Limit switch engaged — clear it before continuing", IsHal = true },
            new() { Code = "13", Description = "Probe protection triggered", IsHal = true },
            new() { Code = "14", Description = "Spindle fault (e.g. at-speed timeout)", IsHal = true },
            new() { Code = "15", Description = "Homing fail: auto-squaring approach failed", IsHal = true },
            new() { Code = "16", Description = "Power-on self test failed", IsHal = true },
            new() { Code = "17", Description = "Motor fault", IsHal = true },
            new() { Code = "18", Description = "Homing fail (general)", IsHal = true },
            new() { Code = "19", Description = "Modbus exception (VFD / spindle comms)", IsHal = true },
            new() { Code = "20", Description = "I/O expander exception", IsHal = true },
            new() { Code = "21", Description = "Non-volatile storage (settings) failure", IsHal = true },
        }),

        new("Realtime commands", "Realtime", "Single bytes acted on immediately, anywhere in the stream — they never wait in the planner queue.", false, new List<CheatEntry>
        {
            new() { Code = "?", Description = "Status report query" },
            new() { Code = "~", Description = "Cycle start / resume" },
            new() { Code = "!", Description = "Feed hold" },
            new() { Code = "0x18", Description = "Soft reset (Ctrl-X)" },
            new() { Code = "0x84", Description = "Safety door" },
            new() { Code = "0x85", Description = "Jog cancel" },
            new() { Code = "0x90–0x94", Description = "Feed override: reset 100% / +10 / −10 / +1 / −1" },
            new() { Code = "0x95–0x97", Description = "Rapid override: 100% / 50% / 25%" },
            new() { Code = "0x99–0x9D", Description = "Spindle override: reset 100% / +10 / −10 / +1 / −1" },
            new() { Code = "0x9E", Description = "Toggle spindle stop (during feed hold)" },
            new() { Code = "0xA0", Description = "Toggle flood coolant" },
            new() { Code = "0xA1", Description = "Toggle mist coolant" },
        }),
    };
}
