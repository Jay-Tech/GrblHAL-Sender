using System;
using System.Collections.Generic;

namespace GrbLHALSender.Probe
{
    public enum ProbeToolType
    {
        TouchPlate,
        Probe3D
    }

    public enum CornerDirection
    {
        FrontLeft,
        FrontRight,
        BackLeft,
        BackRight
    }

    public enum CenterFinderType
    {
        Bore,
        Rectangle,
        Boss
    }

    public class ProbeJobBuilder
    {
        public const string ProbeCommand = "G38.3";

        public string ProbeSearchRate { get; set; }
        public string ProbeLatchRate { get; set; }
        public string ProbeDiameter { get; set; }
        public string ProbeDistance { get; set; }
        public string LatchDistance { get; set; }
        public string ClearanceHeight { get; set; }
        public string TouchPlateThickness { get; set; }
        public ProbeToolType ToolType { get; set; }

        public ProbeState ProbeState { get; set; }

        /// <summary>
        /// Builds the G-code sequence for a Z height probe.
        /// Probes down, backs off, then latches at slow speed.
        /// </summary>
        public List<string> ProbeZ()
        {
            return
            [
                "G91",
                $"{ProbeCommand}F{ProbeSearchRate}Z-{ProbeDistance}",
                $"G0Z{LatchDistance}",
                $"{ProbeCommand}F{ProbeLatchRate}Z-{LatchDistance}"
            ];
        }

        /// <summary>
        /// Builds the G-code for probing a single axis (X or Y).
        /// Direction sign: +1 probes in positive direction, -1 in negative.
        /// </summary>
        public List<string> ProbeSingleAxis(string axis, int directionSign)
        {
            var sign = directionSign > 0 ? "" : "-";
            var retractSign = directionSign > 0 ? "-" : "";

            return
            [
                "G91",
                $"{ProbeCommand}F{ProbeSearchRate}{axis}{sign}{ProbeDistance}",
                $"G0{axis}{retractSign}{LatchDistance}",
                $"{ProbeCommand}F{ProbeLatchRate}{axis}{sign}{LatchDistance}"
            ];
        }

        /// <summary>
        /// Builds the full corner probe sequence.
        /// Probes X edge then Y edge, with optional Z probe first.
        /// Returns command groups: each sub-list is a phase that ends with a probe result capture.
        /// </summary>
        public List<List<string>> ProbeCorner(CornerDirection corner, bool includeZ)
        {
            GetCornerDirections(corner, out var xSign, out var ySign);
            var phases = new List<List<string>>();

            if (includeZ)
            {
                phases.Add(ProbeZ());
            }

            // Retract Z to clearance, then probe X
            var xPhase = new List<string>
            {
                "G91",
                $"G0Z{ClearanceHeight}"
            };
            var xProbe = ProbeSingleAxis("X", xSign);
            xPhase.AddRange(xProbe);
            phases.Add(xPhase);

            // Retract X, move to Y probe position, probe Y
            var xRetractSign = xSign > 0 ? "-" : "";
            var yPhase = new List<string>
            {
                "G91",
                $"G0X{xRetractSign}{ProbeDistance}"
            };
            var yProbe = ProbeSingleAxis("Y", ySign);
            yPhase.AddRange(yProbe);
            phases.Add(yPhase);

            return phases;
        }

        /// <summary>
        /// Builds the center finder probe sequence (bore or rectangle).
        /// Probes X+, X-, then Y+, Y- from the current position (approximate center).
        /// Returns 4 phases: X+, X-, Y+, Y-
        /// </summary>
        public List<List<string>> ProbeInsideCenter()
        {
            var phases = new List<List<string>>();

            // Phase 1: Probe X+
            phases.Add(ProbeSingleAxis("X", 1));

            // Phase 2: Return to start, probe X-
            var xNegPhase = new List<string>
            {
                "G91",
                $"G0X-{ProbeDistance}"  // move back past start toward X-
            };
            xNegPhase.AddRange(ProbeSingleAxis("X", -1));
            phases.Add(xNegPhase);

            // Phase 3: Probe Y+
            phases.Add(ProbeSingleAxis("Y", 1));

            // Phase 4: Return to start, probe Y-
            var yNegPhase = new List<string>
            {
                "G91",
                $"G0Y-{ProbeDistance}"
            };
            yNegPhase.AddRange(ProbeSingleAxis("Y", -1));
            phases.Add(yNegPhase);

            return phases;
        }

        /// <summary>
        /// Builds the boss (outside) center finder probe sequence.
        /// User positions above boss center. Probes from outside toward boss on each axis.
        /// Requires approximate boss size to know how far to offset before probing inward.
        /// Returns 4 phases: X+, X-, Y+, Y-
        /// </summary>
        public List<List<string>> ProbeBossCenter(string approxSize)
        {
            var halfSize = (double.Parse(approxSize) / 2 + double.Parse(ProbeDistance)).ToString();
            var phases = new List<List<string>>();

            // Phase 1: Move to +X side, drop to probe height, probe X- (toward boss)
            var xPosPhase = new List<string>
            {
                "G91",
                $"G0X{halfSize}",
                $"G0Z-{ClearanceHeight}"
            };
            xPosPhase.AddRange(ProbeSingleAxis("X", -1));
            phases.Add(xPosPhase);

            // Phase 2: Retract Z, move to -X side, drop, probe X+ (toward boss)
            var xNegPhase = new List<string>
            {
                "G91",
                $"G0Z{ClearanceHeight}",
                $"G0X-{(double.Parse(halfSize) * 2).ToString()}",
                $"G0Z-{ClearanceHeight}"
            };
            xNegPhase.AddRange(ProbeSingleAxis("X", 1));
            phases.Add(xNegPhase);

            // Phase 3: Retract Z, return to center X, move to +Y side, drop, probe Y-
            var yPosPhase = new List<string>
            {
                "G91",
                $"G0Z{ClearanceHeight}",
                $"G0X{halfSize}",
                $"G0Y{halfSize}",
                $"G0Z-{ClearanceHeight}"
            };
            yPosPhase.AddRange(ProbeSingleAxis("Y", -1));
            phases.Add(yPosPhase);

            // Phase 4: Retract Z, move to -Y side, drop, probe Y+
            var yNegPhase = new List<string>
            {
                "G91",
                $"G0Z{ClearanceHeight}",
                $"G0Y-{(double.Parse(halfSize) * 2).ToString()}",
                $"G0Z-{ClearanceHeight}"
            };
            yNegPhase.AddRange(ProbeSingleAxis("Y", 1));
            phases.Add(yNegPhase);

            return phases;
        }

        /// <summary>
        /// Gets the probe direction signs for each corner.
        /// Sign indicates which direction to probe toward the workpiece edge.
        /// </summary>
        public static void GetCornerDirections(CornerDirection corner, out int xSign, out int ySign)
        {
            switch (corner)
            {
                case CornerDirection.FrontLeft:
                    xSign = 1;  // probe toward +X (workpiece to the right)
                    ySign = 1;  // probe toward +Y (workpiece behind)
                    break;
                case CornerDirection.FrontRight:
                    xSign = -1; // probe toward -X
                    ySign = 1;
                    break;
                case CornerDirection.BackLeft:
                    xSign = 1;
                    ySign = -1;
                    break;
                case CornerDirection.BackRight:
                    xSign = -1;
                    ySign = -1;
                    break;
                default:
                    xSign = 1;
                    ySign = 1;
                    break;
            }
        }

        /// <summary>
        /// Calculates the Z WCS offset from probe result.
        /// Touch plate: offset = plate thickness (Z=0 is plate thickness above contact)
        /// 3D probe: offset = stylus radius (ball center is radius above contact)
        /// </summary>
        public double CalculateZOffset()
        {
            if (ToolType == ProbeToolType.TouchPlate)
                return double.Parse(TouchPlateThickness);
            else
                return double.Parse(ProbeDiameter) / 2.0;
        }

        /// <summary>
        /// Calculates the X or Y WCS offset from probe result, accounting for probe diameter.
        /// directionSign: the direction the probe moved (+1 or -1)
        /// For edge probing, offset by half the probe diameter opposite to probe direction.
        /// </summary>
        public double CalculateXYOffset(int directionSign)
        {
            var radius = double.Parse(ProbeDiameter) / 2.0;
            // If we probed in +X direction, the edge is at (result - radius)
            // If we probed in -X direction, the edge is at (result + radius)
            return -directionSign * radius;
        }
    }
}
