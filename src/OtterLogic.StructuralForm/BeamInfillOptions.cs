namespace OtterLogic.StructuralForm;

/// <summary>
/// Inputs to <see cref="BeamInfillGenerator"/> other than the beams.
/// <para>
/// A behaviour-free record, for the same reason <see cref="FlatTrussOptions"/>
/// is one: it crosses the boundary into Grasshopper and Rhino, so it stays
/// immutable and trivially serialisable. The logic lives in the generator.
/// </para>
/// </summary>
public sealed record BeamInfillOptions
{
    /// <summary>
    /// Number of bays each panel is divided into, so one fewer than this is the
    /// number of members placed in it. The primary driver, and it overrides
    /// <see cref="Spacing"/> whenever both are set.
    /// <para>
    /// Zero from both places nothing: the panels are still found and reported,
    /// which is the way to check the selection reads as intended before
    /// deciding how to fill it.
    /// </para>
    /// </summary>
    public int Divisions { get; init; }

    /// <summary>
    /// Target spacing between members, in model units, measured along the
    /// beams they land on. The longer of a panel's two supporting edges is
    /// divided by this and rounded to whole bays — so it is a target, not a
    /// limit, and the same value gives different counts in panels of different
    /// size. That is the point of it: one number across a floor of uneven bays.
    /// </summary>
    public double Spacing { get; init; }

    /// <summary>
    /// Run the members the short way across each panel instead of the long way.
    /// <para>
    /// The long way is the default because it is how secondary beams are
    /// usually laid: they span the longer dimension and land on primaries
    /// spanning the shorter, which keeps the heavier member short. It is a
    /// convention rather than a rule, which is why this exists.
    /// </para>
    /// </summary>
    public bool Flip { get; init; }

    /// <summary>
    /// How sharply a panel's outline has to turn, in degrees, before the turn
    /// counts as a corner.
    /// <para>
    /// A panel is filled between opposite sides, so it has to be known where
    /// one side ends and the next begins. A faceted edge beam turns a few
    /// degrees at every vertex and is still one side; the corner of a skewed
    /// bay turns sixty or more. Thirty sits clear of both.
    /// </para>
    /// </summary>
    public double CornerAngle { get; init; } = 30.0;

    /// <summary>
    /// How close two beams have to come to count as meeting, and the distance
    /// below which two nodes are the same node. Both front-ends set this from
    /// the document tolerance.
    /// </summary>
    public double Tolerance { get; init; } = 0.01;
}
