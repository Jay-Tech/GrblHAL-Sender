using System;
using System.Collections.Generic;

namespace GrbLHALSender.Gcode
{
    public class ToolpathBuilder
    {
        private int _motionMode; // 0=G0, 1=G1, 2=G2, 3=G3
        private float _x, _y, _z;
        private bool _absoluteMode = true; // G90
        private bool _fileIsMetric = true; // tracks G20/G21 in the file (G21=true, G20=false)
        private float _feedRate;        // current F feed rate (display units/min)
        private double _totalTimeSeconds; // accumulated job time

        private const float MmPerInch = 25.4f;

        // Default rapid rate when machine settings are not available (display units/min)
        private const float DefaultRapidRate = 5000f;

        /// <summary>
        /// Optional: set machine rapid rates from $110/$111/$112 settings before building.
        /// Should be in the display unit (already converted via DisplayXRapid etc.).
        /// </summary>
        public float RapidRate { get; set; } = DefaultRapidRate;

        /// <summary>
        /// Set to true if the machine's display unit is metric (mm), false for imperial (inches).
        /// Derived from MachineSettings.ReportInMetric ($13 setting).
        /// When the G-code file unit (G20/G21) differs from the display unit, coordinates are scaled
        /// so the toolpath always renders in the display unit's coordinate space.
        /// </summary>
        public bool DisplayIsMetric { get; set; } = true;

        /// <summary>
        /// Returns the scale factor to convert from the file's current unit to the display unit.
        /// File mm → Display inches: 1/25.4.  File inches → Display mm: 25.4.  Same unit: 1.0.
        /// </summary>
        private float UnitScale => (_fileIsMetric == DisplayIsMetric)
            ? 1f
            : _fileIsMetric ? 1f / MmPerInch : MmPerInch;

        public ToolpathData BuildToolpath(List<GCodeLine> gCodeLines)
        {
            var toolpath = new ToolpathData();
            _motionMode = 0;
            _x = 0f; _y = 0f; _z = 0f;
            _absoluteMode = true;
            _fileIsMetric = true; // G-code defaults to G21 (mm) until G20 is encountered
            _feedRate = 0f;
            _totalTimeSeconds = 0;

            var lineToFirstSegment = new int[gCodeLines.Count + 1];

            for (int i = 0; i < gCodeLines.Count; i++)
            {
                lineToFirstSegment[i] = toolpath.Segments.Count;
                ProcessLine(gCodeLines[i].Text, toolpath);

                // Tag all segments produced by this line
                for (int s = lineToFirstSegment[i]; s < toolpath.Segments.Count; s++)
                    toolpath.Segments[s].SourceLineIndex = i;
            }

            // Sentinel: one past the last line maps to total segment count
            lineToFirstSegment[gCodeLines.Count] = toolpath.Segments.Count;
            toolpath.LineToFirstSegment = lineToFirstSegment;

            toolpath.TimeEstimateSeconds = _totalTimeSeconds;
            CalculateBounds(toolpath);
            return toolpath;
        }

        private void ProcessLine(string line, ToolpathData toolpath)
        {
            var cmd = new GCodeCommand(line);

            // Handle coordinate mode
            if (cmd.GCode == 90) { _absoluteMode = true; return; }
            if (cmd.GCode == 91) { _absoluteMode = false; return; }

            // Skip non-motion commands
            if (cmd.GCode.HasValue)
            {
                switch (cmd.GCode.Value)
                {
                    case 0: _motionMode = 0; break;
                    case 1: _motionMode = 1; break;
                    case 2: _motionMode = 2; break;
                    case 3: _motionMode = 3; break;
                    case 20: _fileIsMetric = false; return; // inches
                    case 21: _fileIsMetric = true; return;  // mm
                    case 4:  // dwell
                    case 17: // XY plane
                    case 18: // XZ plane
                    case 19: // YZ plane
                    case 28: // home
                    case 43: // tool length comp
                    case 49: // cancel tool length comp
                    case 53: // machine coords
                    case 54:
                    case 55:
                    case 56:
                    case 57:
                    case 58:
                    case 59: // WCS
                    case 80: // cancel canned cycle
                    case 94: // feed per minute
                        return;
                    default:
                        if (cmd.GCode.Value >= 10 && !cmd.HasParam('X') && !cmd.HasParam('Y') && !cmd.HasParam('Z'))
                            return;
                        break;
                }
            }

            // If no axis words present and no G code change, skip
            bool hasAxisWord = cmd.HasParam('X') || cmd.HasParam('Y') || cmd.HasParam('Z');
            if (!hasAxisWord && !cmd.GCode.HasValue)
                return;
            if (!hasAxisWord)
                return;

            var start = new Point3D(_x, _y, _z);

            // Scale factor to convert file coordinates to display unit
            float scale = UnitScale;

            // Compute target position (modal: keep current if not specified)
            // Raw file values are scaled to the display unit before use.
            float targetX = cmd.HasParam('X') ? cmd.GetParam('X') * scale : _x;
            float targetY = cmd.HasParam('Y') ? cmd.GetParam('Y') * scale : _y;
            float targetZ = cmd.HasParam('Z') ? cmd.GetParam('Z') * scale : _z;

            if (!_absoluteMode)
            {
                targetX = _x + (cmd.HasParam('X') ? cmd.GetParam('X') * scale : 0f);
                targetY = _y + (cmd.HasParam('Y') ? cmd.GetParam('Y') * scale : 0f);
                targetZ = _z + (cmd.HasParam('Z') ? cmd.GetParam('Z') * scale : 0f);
            }

            // Update feed rate if F parameter is present (scale to display units/min)
            if (cmd.HasParam('F'))
                _feedRate = cmd.GetParam('F') * scale;

            var end = new Point3D(targetX, targetY, targetZ);
            var moveType = DetermineMoveType(_motionMode, targetZ);

            if (_motionMode <= 1)
            {
                // Linear move (G0 or G1)
                toolpath.Segments.Add(new ToolpathSegment
                {
                    Start = start,
                    End = end,
                    Type = moveType
                });

                // Accumulate time: distance / rate
                float distance = Distance3D(start, end);
                float rate = _motionMode == 0 ? RapidRate : _feedRate;
                if (rate > 0)
                    _totalTimeSeconds += (distance / rate) * 60.0; // rate is mm/min → seconds
            }
            else if (_motionMode == 2 || _motionMode == 3)
            {
                // Arc move (G2=CW, G3=CCW)
                float i = cmd.GetParam('I', 0f) * scale;
                float j = cmd.GetParam('J', 0f) * scale;
                float arcLength = InterpolateArc(start, end, i, j, _motionMode == 2, moveType, toolpath);

                // Accumulate time for arc
                if (_feedRate > 0)
                    _totalTimeSeconds += (arcLength / _feedRate) * 60.0;
            }

            // Update modal position
            _x = targetX;
            _y = targetY;
            _z = targetZ;
        }

        private MoveType DetermineMoveType(int motionMode, float z)
        {
            if (motionMode == 0) return MoveType.Rapid;
            return z < 0 ? MoveType.Cut : MoveType.Traverse;
        }

        /// <summary>
        /// Interpolates an arc into line segments and returns the total arc length.
        /// </summary>
        private float InterpolateArc(Point3D start, Point3D end, float i, float j,
            bool isClockwise, MoveType type, ToolpathData toolpath)
        {
            // Arc center is relative to start point
            float centerX = start.X + i;
            float centerY = start.Y + j;

            float startAngle = MathF.Atan2(start.Y - centerY, start.X - centerX);
            float endAngle = MathF.Atan2(end.Y - centerY, end.X - centerX);

            float sweep = endAngle - startAngle;

            if (isClockwise)
            {
                if (sweep >= 0) sweep -= 2f * MathF.PI;
            }
            else
            {
                if (sweep <= 0) sweep += 2f * MathF.PI;
            }

            // Handle full circle case (start == end)
            if (MathF.Abs(start.X - end.X) < 0.001f &&
                MathF.Abs(start.Y - end.Y) < 0.001f &&
                (MathF.Abs(i) > 0.001f || MathF.Abs(j) > 0.001f))
            {
                sweep = isClockwise ? -2f * MathF.PI : 2f * MathF.PI;
            }

            float radius = MathF.Sqrt(i * i + j * j);

            // Number of segments: ~5 degrees each, minimum 4
            int numSegments = Math.Max(4, (int)(MathF.Abs(sweep) * 180f / MathF.PI / 5f));
            float angleStep = sweep / numSegments;
            float zStep = (end.Z - start.Z) / numSegments;

            var prev = start;
            for (int seg = 1; seg <= numSegments; seg++)
            {
                float angle = startAngle + angleStep * seg;
                float segZ = start.Z + zStep * seg;

                // Use exact endpoint for the last segment to avoid drift
                Point3D next;
                if (seg == numSegments)
                {
                    next = end;
                }
                else
                {
                    next = new Point3D(
                        centerX + radius * MathF.Cos(angle),
                        centerY + radius * MathF.Sin(angle),
                        segZ
                    );
                }

                toolpath.Segments.Add(new ToolpathSegment
                {
                    Start = prev,
                    End = next,
                    Type = type
                });

                prev = next;
            }

            // Arc length: 2D arc + helical Z component
            float arcLen2D = radius * MathF.Abs(sweep);
            float dz = end.Z - start.Z;
            return MathF.Sqrt(arcLen2D * arcLen2D + dz * dz);
        }

        private static float Distance3D(Point3D a, Point3D b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float dz = b.Z - a.Z;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private void CalculateBounds(ToolpathData toolpath)
        {
            if (toolpath.Segments.Count == 0)
            {
                toolpath.MinBounds = new Point3D(0, 0, 0);
                toolpath.MaxBounds = new Point3D(0, 0, 0);
                toolpath.Center = new Point3D(0, 0, 0);
                toolpath.MaxDimension = 1f;
                return;
            }

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            foreach (var seg in toolpath.Segments)
            {
                UpdateBounds(seg.Start, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                UpdateBounds(seg.End, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            }

            toolpath.MinBounds = new Point3D(minX, minY, minZ);
            toolpath.MaxBounds = new Point3D(maxX, maxY, maxZ);
            toolpath.Center = new Point3D(
                (minX + maxX) / 2f,
                (minY + maxY) / 2f,
                (minZ + maxZ) / 2f
            );
            toolpath.MaxDimension = MathF.Max(maxX - minX, MathF.Max(maxY - minY, maxZ - minZ));
            if (toolpath.MaxDimension < 0.001f) toolpath.MaxDimension = 1f;
        }

        private static void UpdateBounds(Point3D p,
            ref float minX, ref float minY, ref float minZ,
            ref float maxX, ref float maxY, ref float maxZ)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z > maxZ) maxZ = p.Z;
        }
    }
}
