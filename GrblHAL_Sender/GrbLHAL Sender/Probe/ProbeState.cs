using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrbLHAL_Sender.Probe
{
    public class ProbeState
    {
        public bool ProbeSuccessful { get; set; }

        public string XOffset { get; set; }
        public string YOffset { get; set; }
        public string ZOffset { get; set; }
        public ProbeState()
        {

        }
    }
}
