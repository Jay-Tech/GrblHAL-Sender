using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GrbLHAL_Sender.Gcode
{
    public class GCodeCommand
    {
        private static readonly Regex ParamRegex = new(@"([A-Z])(-?\d+\.?\d*)", RegexOptions.Compiled);

        public int? GCode { get; private set; }
        public int? MCode { get; private set; }
        public Dictionary<char, float> Parameters { get; } = new();

        public GCodeCommand(string line)
        {
            ParseLine(line);
        }

        private void ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            // Remove inline comments: anything in parentheses or after semicolon
            line = Regex.Replace(line, @"\(.*?\)", "");
            int semicolonIdx = line.IndexOf(';');
            if (semicolonIdx >= 0)
                line = line[..semicolonIdx];

            line = line.Trim().ToUpperInvariant();

            var matches = ParamRegex.Matches(line);
            foreach (Match match in matches)
            {
                char letter = match.Groups[1].Value[0];
                float value = float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

                switch (letter)
                {
                    case 'G':
                        GCode = (int)value;
                        break;
                    case 'M':
                        MCode = (int)value;
                        break;
                    default:
                        Parameters[letter] = value;
                        break;
                }
            }
        }

        public bool HasParam(char param) => Parameters.ContainsKey(param);

        public float GetParam(char param, float defaultValue = 0f)
        {
            return Parameters.TryGetValue(param, out var value) ? value : defaultValue;
        }
    }
}
