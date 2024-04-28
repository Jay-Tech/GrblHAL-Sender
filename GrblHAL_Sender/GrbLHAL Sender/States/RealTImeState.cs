using System;
using System.Collections.Generic;
using System.Dynamic;

public class RealTImeState
{
    public bool TLR { get; set; }
    public string RawRt { get; set; }
    public string[] MPos { get; set; } = [];
    public string[] Wco { get; set; } = [];
    public string GrblHalState { get; set; }
    public bool MpgActive { get; set; }
    public bool Home { get; set; }
    public string SubState { get; set; }
    public string WCS { get; set; }
    public string Tool { get; set; }
    public string FeedRate { get; set; }
    public string FeedOverRide { get; set; }
    public string RapidOverRide { get; set; }
    public string RpmOverRide { get; set; }
    public string ProgramRPM { get; set; }
    public string ActualRpm { get; set; }
    public List<char> SignalStatus { get; set; } = [];

    public RealTImeState()
    {

    }
}