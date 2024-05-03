using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrbLHAL_Sender.Probe
{
    public class ProbeJobBuilder
    {
        public const string ProbeCommand = $"G38.3";
        public string MovementRate { get; set; }
        public string ProbeHeight { get; set; }
        public string ProbeSearchRate { get; set; }
        public string ProbeLatchRate { get; set; }
        public string ProbeDiameter { get; set; }
        public string ProbeLatchHeight { get; set; }

        public ProbeState ProbeState { get; set; }

        public ProbeJobBuilder()
        {
            
        }

        public List<string> ProbeZ(string diameter, string rapid, string probeHeight, string probeSearchRate, string probeLatchRate, string probeLatchHeight)
        {
            ProbeDiameter = diameter;
            ProbeSearchRate = probeSearchRate;
            ProbeLatchRate = probeLatchRate;
            MovementRate = rapid;
            ProbeHeight = probeHeight;
            ProbeLatchHeight = probeLatchHeight;

            return
            [
                $"G91F{ProbeSearchRate}",
                $"{ProbeCommand}F{ProbeSearchRate}Z-{ProbeHeight}",
                $"G{MovementRate}Z{ProbeLatchHeight}",
                $"{ProbeCommand}F{ProbeLatchRate}Z-{ProbeLatchHeight}"
            ];
        }
    }
}
