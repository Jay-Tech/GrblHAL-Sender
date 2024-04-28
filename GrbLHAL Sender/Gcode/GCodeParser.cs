using Avalonia.Controls.Shapes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace GrbLHAL_Sender.Gcode
{
    public class GCodeParser 
    {
        private string? _line;
        public List<GCodeLine> GCodeJob { get; set; }
        
        public void ParseGCodeFile(string file, Action<List<GCodeLine>> callBack)
        {

            Task.Factory.StartNew((() =>
            {
                callBack(ParseJob(file, callBack));
            }));

        }

        private List<GCodeLine> ParseJob(string file, Action<List<GCodeLine>> callback)
        {
            GCodeJob = new List<GCodeLine>(); 
            int index = 0;
            StreamReader sr = new StreamReader(file);
            _line = sr.ReadLine();
            while (_line != null)
            {
                if (!string.IsNullOrEmpty(_line) && !_line.StartsWith("(") && !_line.EndsWith(")"))
                {
                    GCodeJob.Add(new GCodeLine(_line, index));
                    index++;
                }
                // Debug.WriteLine(_line);
                _line = sr.ReadLine();
               
            }
            sr.Close();
            return GCodeJob;
        }
    }
}


public class GCodeLine
{
    public int LineNumber { get; set; }
    public string Text { get; set; }

    public GCodeLine(string text, int lineNumber)
    {
        Text = text;
        LineNumber = lineNumber;
    }
}