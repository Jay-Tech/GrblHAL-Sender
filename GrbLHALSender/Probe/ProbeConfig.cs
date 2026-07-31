using System.Text.Json.Serialization;

namespace GrbLHALSender.Probe;

public class ProbeConfig
{
    /// <summary>
    /// False until these settings have been seeded for the unit the operator actually works
    /// in. The defaults below are one fixed set of numbers and cannot suit both millimetres
    /// and inches — 10 is a sane probe distance in one and twenty-five times too far in the
    /// other — so the first load replaces them with a set chosen for the display unit. After
    /// that the operator's own values win and this is never consulted again.
    /// </summary>
    public bool Initialized { get; set; }

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

    /// <summary>
    /// Rough size of an outside feature across X, used to work out how far to stand off before
    /// dropping beside it. A round boss uses this for both axes.
    /// </summary>
    public double ApproxWidth { get; set; } = 25;

    /// <summary>Rough size of an outside feature across Y.</summary>
    public double ApproxHeight { get; set; } = 25;
}
