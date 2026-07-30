using System.Collections.Generic;
using System.Globalization;
using GrbLHALSender.Utility;

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

        // Probe values are stored/exchanged as dot-decimal strings; never parse with the OS culture.
        private static double ParseInvariant(string value) =>
            double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

        public string ProbeSearchRate { get; set; }
        public string ProbeLatchRate { get; set; }
        public string ProbeDiameter { get; set; }
        public string ProbeDistance { get; set; }
        public string LatchDistance { get; set; }
        public string ClearanceHeight { get; set; }
        /// <summary>How far below the start height to drop before probing sideways.</summary>
        public string ProbeDepth { get; set; } = "5";
        public string TouchPlateThickness { get; set; }
        public ProbeToolType ToolType { get; set; }
        /// <summary>G21 (metric) or G20 (imperial) — prepended to probe sequences.</summary>
        public string UnitSystem { get; set; } = "G21";

        public ProbeState ProbeState { get; set; }

        /// <summary>
        /// Builds the G-code sequence for a Z height probe.
        /// Probes down, backs off, then latches at slow speed.
        /// </summary>
        public List<string> ProbeZ()
        {
            return
            [
                UnitSystem,
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
                UnitSystem,
                "G91",
                $"{ProbeCommand}F{ProbeSearchRate}{axis}{sign}{ProbeDistance}",
                $"G0{axis}{retractSign}{LatchDistance}",
                $"{ProbeCommand}F{ProbeLatchRate}{axis}{sign}{LatchDistance}"
            ];
        }

        /// <summary>
        /// Builds the full corner probe sequence, starting with the stylus over the corner.
        /// Probes the X edge then the Y edge, with an optional Z probe on the top face first.
        /// Returns command groups: each sub-list is a phase that ends with a probe result.
        /// <para>
        /// Each leg is lift, move clear of the stock, drop below the top face, then probe
        /// back toward it. The lift and the lateral move are what make the drop safe, and the
        /// drop is what puts the stylus beside the material instead of above it — probing
        /// sideways from the start height simply passes over the edge and touches nothing.
        /// </para>
        /// <para>
        /// The second leg first moves <em>into</em> the stock's footprint on the first axis,
        /// because the machine is left sitting on the edge it just found and a probe launched
        /// from there would run along the face rather than into the one at right angles.
        /// </para>
        /// </summary>
        /// <param name="startX">Machine X the operator left the stylus at, in sequence units.</param>
        /// <param name="startY">Machine Y the operator left the stylus at.</param>
        /// <param name="startZ">Machine Z the operator left the stylus at.</param>
        public List<List<string>> ProbeCorner(CornerDirection corner, bool includeZ,
            double startX, double startY, double startZ)
        {
            GetCornerDirections(corner, out var xSign, out var ySign);

            var clear = ParseInvariant(ClearanceHeight);
            var safeZ = (startZ + clear).ToInvariantString("F3");
            var probeZ = (startZ - ParseInvariant(ProbeDepth)).ToInvariantString("F3");

            var phases = new List<List<string>>();

            if (includeZ)
                phases.Add(ProbeZ());

            // Each leg stands off on the axis it is about to probe and steps *in* on the other,
            // so the stylus ends up against the middle of a face rather than off the end of it.
            // Standing off in X alone leaves it diagonally past the corner, where the probe
            // only grazes the edge.
            phases.Add(ApproachAndProbe(
                "X", startX - xSign * clear,
                "Y", startY + ySign * clear,
                safeZ, probeZ, xSign));

            phases.Add(ApproachAndProbe(
                "Y", startY - ySign * clear,
                "X", startX + xSign * clear,
                safeZ, probeZ, ySign));

            return phases;
        }

        /// <summary>
        /// One leg of a corner probe: up to safe, across to the stand-off, down to probing
        /// depth, then probe back toward the face.
        /// <para>
        /// Every position is absolute, by machine coordinate. Relative lifts and drops do not
        /// cancel across legs — lifting by Clearance and dropping by Clearance plus Depth each
        /// time left the second leg a whole Depth lower than the first, which is why the front
        /// probe plunged further than the left one.
        /// </para>
        /// </summary>
        private List<string> ApproachAndProbe(
            string standOffAxis, double standOffPos,
            string stepInAxis, double stepInPos,
            string safeZ, string probeZ, int probeSign)
        {
            var phase = new List<string>
            {
                UnitSystem,
                "G90",
                $"G53G0Z{safeZ}",
                $"G53G0{stepInAxis}{stepInPos.ToInvariantString("F3")}" +
                    $"{standOffAxis}{standOffPos.ToInvariantString("F3")}",
                $"G53G0Z{probeZ}"
            };
            phase.AddRange(ProbeSingleAxis(standOffAxis, probeSign));

            return phase;
        }

        /// <summary>
        /// Builds the center finder probe sequence (bore or rectangle), starting from the
        /// operator's approximate center. Returns 4 phases: X+, X-, Y+, Y-.
        /// <para>
        /// Every phase returns to the start point before probing the next direction, and it
        /// returns there absolutely, by machine coordinate. The previous version stepped back
        /// by ProbeDistance in G91 on the assumption that unwound the probe move — but a probe
        /// stops early, on contact, so the step overshot past center by however far short of
        /// ProbeDistance the wall was. Once that overshoot exceeded the radius it was a rapid
        /// into the opposite wall, which any bore narrower than twice the probe distance would
        /// do. Going back to the point the machine came from cannot overshoot.
        /// </para>
        /// <para>
        /// Y is probed from the start X rather than the measured center X, which is exact for
        /// a rectangle — a straight wall reads the same at any X — and for a bore leaves a
        /// chord error of roughly x²/2r for an eyeball x off center. Run it twice if that
        /// matters.
        /// </para>
        /// </summary>
        /// <param name="startX">Machine X the cycle began at, in the sequence's unit.</param>
        /// <param name="startY">Machine Y the cycle began at, in the sequence's unit.</param>
        public List<List<string>> ProbeInsideCenter(double startX, double startY)
        {
            var x = startX.ToInvariantString("F3");
            var y = startY.ToInvariantString("F3");

            return
            [
                // Probing X leaves Y alone, so the first three phases only ever need X put back.
                ProbeSingleAxis("X", 1),
                ReturnThenProbe($"G53G0X{x}", "X", -1),
                ReturnThenProbe($"G53G0X{x}", "Y", 1),
                ReturnThenProbe($"G53G0Y{y}", "Y", -1)
            ];
        }

        /// <summary>
        /// Drives absolutely back to a known-safe point, then probes. G90 is set for the
        /// return because G53 is only meaningful in absolute mode; ProbeSingleAxis puts the
        /// parser back into G91 for the probe itself.
        /// </summary>
        private List<string> ReturnThenProbe(string returnMove, string axis, int directionSign)
        {
            var phase = new List<string> { UnitSystem, "G90", returnMove };
            phase.AddRange(ProbeSingleAxis(axis, directionSign));
            return phase;
        }

        /// <summary>
        /// Builds the boss (outside) center finder probe sequence.
        /// User positions above boss center. Probes from outside toward boss on each axis.
        /// Requires approximate boss size to know how far to offset before probing inward.
        /// Returns 4 phases: X+, X-, Y+, Y-
        /// </summary>
        public List<List<string>> ProbeBossCenter(string approxSize)
        {
            var halfSize = (ParseInvariant(approxSize) / 2 + ParseInvariant(ProbeDistance)).ToInvariantString();
            var phases = new List<List<string>>();

            // Phase 1: Move to +X side, drop to probe height, probe X- (toward boss)
            var xPosPhase = new List<string>
            {
                UnitSystem,
                "G91",
                $"G0X{halfSize}",
                $"G0Z-{ClearanceHeight}"
            };
            xPosPhase.AddRange(ProbeSingleAxis("X", -1));
            phases.Add(xPosPhase);

            // Phase 2: Retract Z, move to -X side, drop, probe X+ (toward boss)
            var xNegPhase = new List<string>
            {
                UnitSystem,
                "G91",
                $"G0Z{ClearanceHeight}",
                $"G0X-{(ParseInvariant(halfSize) * 2).ToInvariantString()}",
                $"G0Z-{ClearanceHeight}"
            };
            xNegPhase.AddRange(ProbeSingleAxis("X", 1));
            phases.Add(xNegPhase);

            // Phase 3: Retract Z, return to center X, move to +Y side, drop, probe Y-
            var yPosPhase = new List<string>
            {
                UnitSystem,
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
                UnitSystem,
                "G91",
                $"G0Z{ClearanceHeight}",
                $"G0Y-{(ParseInvariant(halfSize) * 2).ToInvariantString()}",
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
                return ParseInvariant(TouchPlateThickness);
            else
                return ParseInvariant(ProbeDiameter) / 2.0;
        }

        /// <summary>
        /// Calculates the X or Y WCS offset from probe result, accounting for probe diameter.
        /// directionSign: the direction the probe moved (+1 or -1)
        /// For edge probing, offset by half the probe diameter opposite to probe direction.
        /// </summary>
        public double CalculateXYOffset(int directionSign)
        {
            var radius = ParseInvariant(ProbeDiameter) / 2.0;
            // If we probed in +X direction, the edge is at (result - radius)
            // If we probed in -X direction, the edge is at (result + radius)
            return -directionSign * radius;
        }
    }
}
