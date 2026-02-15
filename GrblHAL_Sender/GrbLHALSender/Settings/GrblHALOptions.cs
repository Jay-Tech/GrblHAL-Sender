using System.Collections.Generic;

namespace GrbLHALSender.Settings;

public class GrblHALOptions
{
    public int AxesCount { get; set; }
    public int ToolTableCount { get; set; }
    public int BlockBufferSize { get; set; }
    public int RxBufferSize { get; set; }
    public bool HasToolTable => ToolTableCount > 0;
    public List<char> AxisLabels { get; set; } = new();
    public List<char> SignalLabels { get; set; } = new();
    public List<string> Options { get; set; } = new();

}