using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrbLHAL_Sender.Utility
{
    public static class GrblHalConstants
    {
        public const byte GrblReset = 0x18;

        //Spindle
        public const byte SpindleReset = 0x99;
        public const byte SpindleFineMinus = 0x9D;
        public const byte SpindleFinePlus = 0x9C;
        public const byte SpindleCoarseMinus = 0x9B;
        public const byte SpindleCoarsePlus = 0x9A;
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
        public const string SpindleCw = "M3";
        public const string SpindleCCw = "M4";
        public const string SpindleOff = "M5";
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
        //Alarm
        public const string Getalarmcodes = "$EA";
        public const string Geterrorcodes = "$EE";
        public const string ProgramDemarcation = "%";
        //sd Card
        public const string SdcardMount = "$FM";
        public const string SdcardDir = "$F";
        public const string SdcardDirAll = "$F+";
        public const string SdcardRewind = "$FR";
        public const string SdcardRun = "$F=";
        public const string SdcardUnlink = "$FD=";
        public const string SdcardDump = "$F<=";


    }
}
