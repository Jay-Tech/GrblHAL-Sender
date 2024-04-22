using System;

public class RealTImeState
{
    public string RawRt { get; set; }
    public string[] MPos { get; set; } = [];
    public string[] Wco { get; set; } = [];
    public string GrblHalState { get; set; }
    public bool MpgActive { get; set; }
    public bool Home { get; set; }
    public string SubState { get; set; }

    public RealTImeState()
    {

    }
}