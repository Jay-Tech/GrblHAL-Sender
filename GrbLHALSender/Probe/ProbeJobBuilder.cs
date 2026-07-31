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
        /// <summary>Inside a round hole.</summary>
        Bore,
        /// <summary>Inside a rectangular pocket.</summary>
        Rectangle,
        /// <summary>Outside a round boss.</summary>
        Boss,
        /// <summary>Outside a rectangular or square boss.</summary>
        RectangularBoss
    }

    public class ProbeJobBuilder
    {
        public const string ProbeCommand = "G38.3";

        // Probe values are stored/exchanged as dot-decimal strings; never parse with the OS culture.
        private static double ParseInvariant(string value) =>
            double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

        public string ProbeSearchRate { get; set; }
        public string ProbeLatchRate { get; set; }
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
        /// Builds the outside centre finder sequence, for a round boss or a rectangular one.
        /// The operator positions the stylus above the middle of the feature. Returns 4 phases,
        /// touching the +X, -X, +Y and -Y faces in that order — the order the results are read
        /// back in.
        /// <para>
        /// Each leg lifts to safe, moves to a stand-off beside the feature, drops below its top
        /// face, and probes inward. Every position is absolute, from the point the cycle began.
        /// The previous version stepped relative to wherever the last probe stopped, which is
        /// the same dead reckoning that drove the bore cycle into a wall.
        /// </para>
        /// <para>
        /// Stand-off is half the approximate size plus <see cref="ClearanceHeight"/>, and the
        /// probe then travels up to <see cref="ProbeDistance"/> to find the face. Keeping those
        /// two apart matters: folding the probe distance into the stand-off, as this used to,
        /// meant an over-estimated size could never be reached and an under-estimated one
        /// dropped the stylus onto the feature, with only a narrow band of sizes working at all.
        /// Now the size only has to be close enough that clearance covers the error.
        /// </para>
        /// </summary>
        /// <param name="approxWidth">Rough size across X. A round boss uses it for both axes.</param>
        /// <param name="approxHeight">Rough size across Y.</param>
        public List<List<string>> ProbeOutsideCenter(double approxWidth, double approxHeight,
            double startX, double startY, double startZ)
        {
            var clear = ParseInvariant(ClearanceHeight);
            var safeZ = (startZ + clear).ToInvariantString("F3");
            var probeZ = (startZ - ParseInvariant(ProbeDepth)).ToInvariantString("F3");

            var standX = approxWidth / 2 + clear;
            var standY = approxHeight / 2 + clear;

            // Probing direction is inward, so it opposes the side being touched: the +X face is
            // reached by standing off beyond it and probing back in the -X direction.
            return
            [
                OutsideLeg(startX + standX, startY, safeZ, probeZ, "X", -1),
                OutsideLeg(startX - standX, startY, safeZ, probeZ, "X", 1),
                OutsideLeg(startX, startY + standY, safeZ, probeZ, "Y", -1),
                OutsideLeg(startX, startY - standY, safeZ, probeZ, "Y", 1)
            ];
        }

        /// <summary>
        /// One leg of an outside centre probe. The cross-axis is held on the feature's centre
        /// line, which a rectangle does not care about but a round boss does — touching a circle
        /// away from its centre line reads a chord rather than the diameter.
        /// </summary>
        private List<string> OutsideLeg(double x, double y, string safeZ, string probeZ,
            string axis, int probeSign)
        {
            var phase = new List<string>
            {
                UnitSystem,
                "G90",
                $"G53G0Z{safeZ}",
                $"G53G0X{x.ToInvariantString("F3")}Y{y.ToInvariantString("F3")}",
                $"G53G0Z{probeZ}"
            };
            phase.AddRange(ProbeSingleAxis(axis, probeSign));

            return phase;
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
        /// How far below the trigger position the surface actually is.
        /// <para>
        /// Touch plate: the plate thickness, since the trigger happens at the top of the plate
        /// and the surface is its thickness below.
        /// </para>
        /// <para>
        /// 3D probe: nothing. The stylus meets a Z surface with the bottom of the ball, directly
        /// beneath its centre, so the trigger position <em>is</em> the surface. Stylus radius
        /// compensates a <em>side</em> touch, where the ball contacts on its flank and its
        /// centre ends up a radius away from the edge — it has no part in a Z touch. Returning
        /// the radius here set work Z one radius below the surface, measured on hardware as
        /// 0.039in low with a 0.0787in stylus, which would have cut everything that much deep.
        /// </para>
        /// </summary>
        public double CalculateZOffset() =>
            ToolType == ProbeToolType.TouchPlate ? ParseInvariant(TouchPlateThickness) : 0;

        // Edge compensation deliberately does not live here. It is applied in
        // OnProbeCornerComplete, against the machine coordinate the probe reported and in the
        // opposite sense to the work-coordinate offset this class used to return — the edge is
        // at contact + sign * radius. Keeping a second, oppositely signed version of that
        // arithmetic here with no caller was asking for the wrong one to be picked up later.
    }
}
