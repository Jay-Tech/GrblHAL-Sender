using System.Text.Json.Serialization;

namespace GrbLHALSender.Probe;

public class ProbeConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProbeToolType ToolType { get; set; } = ProbeToolType.TouchPlate;

    public double TouchPlateThickness { get; set; } = 1.0;
    public double ProbeDiameter { get; set; } = 2.0;
    public double SearchRate { get; set; } = 100;
    public double LatchRate { get; set; } = 20;
    public double ProbeDistance { get; set; } = 10;
    public double LatchDistance { get; set; } = 1;
    public double ClearanceHeight { get; set; } = 5;

    /// <summary>
    /// How far below the starting height the stylus drops before probing sideways, so it is
    /// beside the stock rather than above it. Separate from ClearanceHeight, which is the
    /// safe height it retracts to: too shallow here and the probe passes over the edge,
    /// too deep and it can reach the table.
    /// </summary>
    public double ProbeDepth { get; set; } = 5;

    public double ApproxSize { get; set; } = 25;
}
