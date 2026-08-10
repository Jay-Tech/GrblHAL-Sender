namespace GrbLHALSender.Utility
{
    public static class GrblHalConstants
    {
        public const byte GrblReset = 0x18;
        public const byte Exit = 0x03;
        public const byte Reset = 0x18;
        public const byte Stop = 0x19;
        public const byte StatusReport = 0x80;
        public const byte CycleStart = 0x81;
        public const byte FeedHold = 0x82;
        public const byte GcodeReport = 0x83;
        public const byte SafetyDoor = 0x84;
        public const byte JogCancel = 0x85;
        public const byte OptionalStopToggle = 0x88;

        //Spindle
        public const byte SpindleReset = 0x99;
        public const byte SpindleFineMinus = 0x9D;
        public const byte SpindleFinePlus = 0x9C;
        public const byte SpindleCoarseMinus = 0x9B;
        public const byte SpindleCoarsePlus = 0x9A;
        // Toggles the spindle off and on again, and only while the machine is in
        // HOLD - outside a hold the controller ignores it. Deliberate: stopping the
        // spindle while the tool is still moving through material is worse than
        // leaving it running, so the hold decelerates first. A cycle start restores
        // the spindle on its own, so the toggle back is not something a caller has
        // to remember. Unlike M05 this is real time, which means grblHAL acts on it
        // whichever stream sends it - the only spindle control that survives MPG mode.
        public const byte SpindleStopToggle = 0x9E;
        //Feed
        public const byte FeedOrReset = 0x90;
        public const byte FeedOrCoarsePlus = 0x91;
        public const byte FeedOrCoarseMinus = 0x92;
        public const byte FeedOrFinePlus = 0x93;
        public const byte FeedOrFineMinus = 0x94;
        public const byte RapidOrReset = 0x95;
        public const byte RapidOrMedium = 0x96;
        public const byte RapidOrLow = 0x97;
        //Tool
        public const byte ToolAck = 0xA3;
        //Spindle
        public const string SpindleCw = "M03";
        public const string SpindleCCw = "M04";
        public const string SpindleOff = "M05";
        //Settings
        public const string Getsettings = "$$";
        public const string GetsettingsAll = "$+";
        public const string Getparserstate = "$G";
        public const string Getinfo = "$I";
        public const string GetinfoExtended = "$I+";
        public const string Getngcparameters = "$#";
        public const string Getstartuplines = "$N";
        public const string Getsettingsdetails = "$ES";
        public const string Getsettingsgroups = "$EG";
        // Per-setting description: "$SED=<id>" -> [SETTINGDESCR:<id>|<text>].
        // Not all firmware builds include the description strings.
        public const string GetsettingDescription = "$SED=";
        public const string GetPins = "$PINS";
        //Alarm
        public const string Alarmcodes = "$EA";
        public const string Errorcodes = "$EE";
        public const string ProgramDemarcation = "%";
        //sd Card
        public const string SdcardMount = "$FM";
        public const string SdcardDir = "$F";
        public const string SdcardDirAll = "$F+";
        public const string SdcardRewind = "$FR";
        public const string SdcardRun = "$F=";
        public const string SdcardUnlink = "$FD=";
        public const string SdcardDump = "$F<=";
        //Coolant
        public const string MistOn = "M7";
        public const string FloodOn = "M8";
        public const string CoolantOff = "M9";
        //Aux Output
        public const string AuxOutOn = "M64";
        public const string AuxOutOff = "M65";
        public const string GetPinState = "$pinstate";
        //  Settings
        public const int ReportUnits = 13;
        public const int XRapid = 110;
        public const int XAxisLength = 130;
        public const int YRapid = 111;
        public const int YAxisLength = 131;
        public const int ZRapid = 112;
        public const int ZAxisLength = 132;
        public const int ToolChangeMode = 341;
        //  Tool change
        /// <summary>Tool Probe Workpiece — probes the new tool and applies the offset ($341=1 and 2 only).</summary>
        public const string ToolProbeWorkpiece = "$TPW";
        /// <summary>
        /// Tool Length Reference — captures the last successful probe as the baseline that
        /// $TPW measures against. Issued without a successful probe it clears the reference.
        /// </summary>
        public const string ToolLengthReference = "$TLR";
    }
}
