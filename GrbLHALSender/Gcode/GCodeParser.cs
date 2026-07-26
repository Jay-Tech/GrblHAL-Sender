using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace GrbLHALSender.Gcode;

public class GCodeParser
{
    private string? _line;
    public List<GCodeLine> GCodeJob { get; set; }

    /// <summary>
    /// Parses the file on a background task. Any exception thrown while reading the file
    /// or inside <paramref name="callBack"/> is reported through <paramref name="onError"/>
    /// instead of being silently swallowed (which left the UI with no loaded file and a
    /// permanently disabled Start button).
    /// </summary>
    public void ParseGCodeFile(string file, Action<List<GCodeLine>> callBack, Action<Exception>? onError = null)
    {
        Task.Factory.StartNew((() =>
        {
            try
            {
                callBack(ParseJob(file, callBack));
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }));
    }

    private List<GCodeLine> ParseJob(string file, Action<List<GCodeLine>> callback)
    {
        GCodeJob = new List<GCodeLine>();
        int index = 0;
        using StreamReader sr = new StreamReader(file);
        _line = sr.ReadLine();
        while (_line != null)
        {
            if (!string.IsNullOrEmpty(_line) && !_line.StartsWith("(") && !_line.EndsWith(")"))
            {
                GCodeJob.Add(new GCodeLine(_line, index));
                index++;
            }
            _line = sr.ReadLine();

        }
        return GCodeJob;
    }
}


public class GCodeLine
{
    public int LineNumber { get; set; }
    public string Text { get; set; }

    /// <summary>
    /// True for a line added by a configured G-code event rule rather than read
    /// from the file, so the UI can distinguish injected commands from the job.
    /// </summary>
    public bool IsInjected { get; set; }

    public GCodeLine(string text, int lineNumber)
    {
        Text = text;
        LineNumber = lineNumber;
    }
}