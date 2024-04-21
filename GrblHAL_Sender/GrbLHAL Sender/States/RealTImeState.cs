using System;

public class RealTImeState
{
    public string RawRt { get; set; }
    public string[] MPos { get; set; } = Array.Empty<string>();
    public string[] WCO { get; set; } = Array.Empty<string>();
    public string GrblHALState { get; set; }

    public bool MpgActive { get; set; }
    public bool Home { get; set; }
    public string SubState { get; set; }

    public RealTImeState()
    {

    }
}