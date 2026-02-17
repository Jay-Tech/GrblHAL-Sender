using System;
using System.Collections.Generic;

namespace GrbLHALSender.Gcode
{
    public struct Point3D
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public Point3D(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public enum MoveType
    {
        Rapid,
        Cut,
        Traverse
    }

    public class ToolpathSegment
    {
        public Point3D Start { get; set; }
        public Point3D End { get; set; }
        public MoveType Type { get; set; }
        public int SourceLineIndex { get; set; }
    }

    public class ToolpathData
    {
        public List<ToolpathSegment> Segments { get; set; } = new();
        public Point3D MinBounds { get; set; }
        public Point3D MaxBounds { get; set; }
        public Point3D Center { get; set; }
        public float MaxDimension { get; set; }

        /// <summary>
        /// Estimated job time in seconds, calculated from move distances and feed rates.
        /// </summary>
        public double TimeEstimateSeconds { get; set; }

        /// <summary>
        /// Maps G-code line index to the first segment index produced by that line.
        /// Length = total G-code line count + 1 (sentinel at end = Segments.Count).
        /// Lines that produce no segments share the same value as the next line.
        /// </summary>
        public int[] LineToFirstSegment { get; set; } = Array.Empty<int>();
    }
}
