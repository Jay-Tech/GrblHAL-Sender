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

    /// <summary>
    /// Per-operation rates and distances.
    /// <para>
    /// Each probe cycle owns its own, because they were one shared set and that was unsafe in
    /// both directions: setting up a corner probe rewrote - and persisted over - the numbers
    /// that had established the tool length reference, so the reference survived while the
    /// values that would reproduce it quietly did not. Nothing looks wrong until the next
    /// reference lands somewhere else.
    /// </para>
    /// <para>
    /// The fields above stay shared deliberately. Tool type, stylus diameter and plate
    /// thickness describe what is fitted to the spindle, not what a particular cycle does with
    /// it, so they belong outside the tabs where the UI already puts them.
    /// </para>
    /// </summary>
    public ProbeParameters Z { get; set; } = new();

    /// <inheritdoc cref="Z"/>
    public ProbeParameters Corner { get; set; } = new();

    /// <inheritdoc cref="Z"/>
    public ProbeParameters Center { get; set; } = new();

    /// <inheritdoc cref="Z"/>
    public ProbeParameters ToolReference { get; set; } = new();
}

/// <summary>
/// One operation's rates and distances. See <see cref="ProbeConfig.Z"/> for why these are
/// held per operation rather than once.
/// </summary>
public class ProbeParameters
{
    /// <summary>
    /// False until seeded. An existing configuration seeds every set from the shared values
    /// that were serving all of them before the split, so nothing changes underfoot on the
    /// first launch after upgrading - each tab opens with the numbers it was already using.
    /// </summary>
    public bool Initialized { get; set; }

    public double SearchRate { get; set; } = 100;
    public double LatchRate { get; set; } = 20;
    public double LatchDistance { get; set; } = 1;

    /// <summary>
    /// How far a probe travels looking for contact.
    /// <para>
    /// Present for every operation because every cycle emits it - ProbeSingleAxis builds its
    /// move from this, and the outside-centre cycle relies on it to reach a face when the
    /// approximate size was under-estimated. The corner and centre tabs do not offer it for
    /// editing, but the value still has to exist or those moves have no length.
    /// </para>
    /// </summary>
    public double ProbeDistance { get; set; } = 10;

    /// <summary>Safe height to retract to. Used by the cycles that move over stock.</summary>
    public double ClearanceHeight { get; set; } = 5;

    /// <summary>
    /// How far below the starting height the stylus drops before probing sideways, so it is
    /// beside the stock rather than above it. Separate from ClearanceHeight, which is the safe
    /// height it retracts to: too shallow and the probe passes over the edge, too deep and it
    /// can reach the table.
    /// </summary>
    public double ProbeDepth { get; set; } = 5;

    /// <summary>Rough size of an outside feature across X. A round boss uses it for both.</summary>
    public double ApproxWidth { get; set; } = 25;

    /// <summary>Rough size of an outside feature across Y.</summary>
    public double ApproxHeight { get; set; } = 25;
}