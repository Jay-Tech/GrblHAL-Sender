using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GrbLHALSender.Gcode;

/// <summary>
/// Minimal G-code word inspection for callers that need to recognise one specific
/// command in a line without parsing the whole block.
/// </summary>
public static class GcodeWords
{
    // Letter + number, tolerating the space some posts emit ("M 6", "T 3").
    private static readonly Regex WordRegex =
        new(@"([A-Z])\s*(-?\d*\.?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// True when the line contains the given word at the given value, comparing
    /// numerically so <c>M6</c> matches <c>M06</c> and <c>T3M6</c> but not <c>M61</c>.
    /// Comments are ignored.
    /// </summary>
    public static bool HasWord(string? line, char letter, double value)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;

        letter = char.ToUpperInvariant(letter);

        foreach (Match word in WordRegex.Matches(StripComments(line)))
        {
            if (char.ToUpperInvariant(word.Groups[1].Value[0]) != letter) continue;
            if (double.TryParse(word.Groups[2].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed) &&
                Math.Abs(parsed - value) < 0.0001)
                return true;
        }

        return false;
    }

    /// <summary>
    /// True for a line that asks the controller for a tool change. grblHAL suspends the
    /// program there and rejects any further g-code with error:40 until it is resolved,
    /// so a streamer has to treat it as a barrier.
    /// </summary>
    public static bool IsToolChange(string? line) => HasWord(line, 'M', 6);

    private static string StripComments(string line)
    {
        var text = Regex.Replace(line, @"\(.*?\)", "");
        int semicolon = text.IndexOf(';');
        return semicolon >= 0 ? text[..semicolon] : text;
    }
}
