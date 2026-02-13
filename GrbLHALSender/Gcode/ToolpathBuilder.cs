using System;
using System.Collections.Generic;

namespace GrbLHALSender.Gcode
{
    public class ToolpathBuilder
    {
        private int _motionMode; // 0=G0, 1=G1, 2=G2, 3=G3
        private float _x, _y, _z;
        private bool _absoluteMode = true; // G90

        public ToolpathData BuildToolpath(List<GCodeLine> gCodeLines)
        {
            var toolpath = new ToolpathData();
            _motionMode = 0;
            _x = 0f; _y = 0f; _z = 0f;
            _absoluteMode = true;

            foreach (var line in gCodeLines)
            {
                ProcessLine(line.Text, toolpath);
            }

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
                    case 4:  // dwell
                    case 17: // XY plane
                    case 18: // XZ plane
                    case 19: // YZ plane
                    case 20: // inches
                    case 21: // mm
                    case 28: // home
                    case 43: // tool length comp
                    case 49: // cancel tool length comp
                    case 53: // machine coords
                    case 54: case 55: case 56: case 57: case 58: case 59: // WCS
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

            // Compute target position (modal: keep current if not specified)
            float targetX = cmd.HasParam('X') ? cmd.GetParam('X') : _x;
            float targetY = cmd.HasParam('Y') ? cmd.GetParam('Y') : _y;
            float targetZ = cmd.HasParam('Z') ? cmd.GetParam('Z') : _z;

            if (!_absoluteMode)
            {
                targetX = _x + (cmd.HasParam('X') ? cmd.GetParam('X') : 0f);
                targetY = _y + (cmd.HasParam('Y') ? cmd.GetParam('Y') : 0f);
                targetZ = _z + (cmd.HasParam('Z') ? cmd.GetParam('Z') : 0f);
            }

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
            }
            else if (_motionMode == 2 || _motionMode == 3)
            {
                // Arc move (G2=CW, G3=CCW)
                float i = cmd.GetParam('I', 0f);
                float j = cmd.GetParam('J', 0f);
                InterpolateArc(start, end, i, j, _motionMode == 2, moveType, toolpath);
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

        private void InterpolateArc(Point3D start, Point3D end, float i, float j,
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
