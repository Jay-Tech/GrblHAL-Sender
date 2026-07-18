using GrbLHALSender.Utility;
using System.Collections.Generic;
using System.Text;

namespace GrbLHALSender.Toolpaths
{
    public class SurfacingOptions
    {
        public double StockWidth { get; set; }
        public double StockHeight { get; set; }
        public double ToolDiameter { get; set; }
        public double StepoverPercent { get; set; } = 40.0;
        public double CutDepthPerPass { get; set; }
        public int NumberOfPasses { get; set; }
        public int SpindleRpm { get; set; }
        public double FeedRate { get; set; }
        public double SafeZ { get; set; } = 1.0;
        public bool UseMetric { get; set; } = true;
        public bool SpindleCw { get; set; } = true;
        public int ToolNumber { get; set; }
    }

    public static class SurfacingGenerator
    {
        public static string Generate(SurfacingOptions o)
        {
            var sb = new StringBuilder();
            var unitCmd = o.UseMetric ? "G21" : "G20";
            var unitLabel = o.UseMetric ? "mm" : "in";
            var spindleCmd = o.SpindleCw ? "M3" : "M4";

            var stepover = o.ToolDiameter * (o.StepoverPercent / 100.0);
            if (stepover <= 0)
                stepover = o.ToolDiameter * 0.4;

            sb.AppendLine("(Generated surfacing toolpath)");
            sb.AppendLine($"(Stock: {o.StockWidth.ToInvariantString()} x {o.StockHeight.ToInvariantString()} {unitLabel})");
            sb.AppendLine($"(Tool dia: {o.ToolDiameter.ToInvariantString()} {unitLabel}, stepover: {stepover.ToInvariantString("F3")} {unitLabel})");
            sb.AppendLine($"(Depth per pass: {o.CutDepthPerPass.ToInvariantString()} {unitLabel}, passes: {o.NumberOfPasses})");
            sb.AppendLine($"(Feed: {o.FeedRate.ToInvariantString()} {unitLabel}/min, RPM: {o.SpindleRpm})");
            sb.AppendLine();

            sb.AppendLine("G90");
            sb.AppendLine(unitCmd);
            sb.AppendLine("G17");
            if (o.ToolNumber > 0)
                sb.AppendLine($"T{o.ToolNumber} M6");
            sb.AppendLine($"{spindleCmd} S{o.SpindleRpm}");
            sb.AppendLine($"G4 P1");
            sb.AppendLine($"G0 Z{o.SafeZ.ToInvariantString("F3")}");
            sb.AppendLine("G0 X0 Y0");
            sb.AppendLine();

            var yPositions = new List<double>();
            double y = 0;
            while (y < o.StockHeight)
            {
                yPositions.Add(y);
                y += stepover;
            }
            if (yPositions.Count == 0 || yPositions[^1] < o.StockHeight)
                yPositions.Add(o.StockHeight);

            for (int p = 1; p <= o.NumberOfPasses; p++)
            {
                double z = -(o.CutDepthPerPass * p);
                sb.AppendLine($"(--- Pass {p} of {o.NumberOfPasses}, Z={z.ToInvariantString("F3")} ---)");
                sb.AppendLine("G0 X0 Y0");
                sb.AppendLine($"G1 Z{z.ToInvariantString("F3")} F{o.FeedRate.ToInvariantString()}");

                for (int i = 0; i < yPositions.Count; i++)
                {
                    bool goingRight = (i % 2) == 0;
                    double xTarget = goingRight ? o.StockWidth : 0.0;
                    sb.AppendLine($"G1 X{xTarget.ToInvariantString("F3")}");

                    if (i < yPositions.Count - 1)
                        sb.AppendLine($"G1 Y{yPositions[i + 1].ToInvariantString("F3")}");
                }

                sb.AppendLine($"G0 Z{o.SafeZ.ToInvariantString("F3")}");
                sb.AppendLine();
            }

            sb.AppendLine("M5");
            sb.AppendLine("G0 X0 Y0");
            sb.AppendLine("M30");

            return sb.ToString();
        }
    }
}
