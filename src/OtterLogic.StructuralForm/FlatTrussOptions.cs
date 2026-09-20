using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Inputs to <see cref="FlatTrussGenerator"/> other than the two chords.
/// <para>
/// A behaviour-free record, deliberately: this is the kind of data that crosses
/// the boundary into Grasshopper and Rhino, so it stays immutable and trivially
/// serialisable. The logic lives in the generator.
/// </para>
/// </summary>
public sealed record FlatTrussOptions
{
    /// <summary>Web bracing pattern.</summary>
    public TrussType Type { get; init; } = TrussType.Warren;

    /// <summary>
    /// Mirror every diagonal within its own panel. Pratt becomes Howe, and the
    /// Warren zigzag starts the other way up. Patterns with no diagonals to
    /// mirror — Vierendeel — and symmetric ones — cross-braced — are unaffected.
    /// </summary>
    public bool Flip { get; init; }

    /// <summary>Close the truss with a post at each end.</summary>
    public bool GenerateEndPosts { get; init; } = true;

    /// <summary>
    /// Extra points to force a node at.
    /// <para>
    /// A snap point has to lie <em>on</em> one of the two chords, within
    /// <see cref="SnapTolerance"/>. Anything else is discounted — reported on
    /// <see cref="FlatTruss.OffChordSnapPoints"/>, never quietly dropped.
    /// </para>
    /// <para>
    /// That requirement is what keeps the rule simple enough to predict. A
    /// point floating beside the truss has no single honest answer: projected
    /// square onto a sloped chord it lands at the foot of the perpendicular,
    /// which is not the plan position it was picked at, and the further off it
    /// sits the further that drifts. On the chord there is nothing to decide —
    /// the point is already at a station, and that station is where the node
    /// goes.
    /// </para>
    /// <para>
    /// These are the <em>secondary</em> snap targets. The chords' own natural
    /// points — polyline vertices and curve kinks — are checked first and take
    /// every station they can reach; these are offered whatever is left over.
    /// </para>
    /// </summary>
    public IReadOnlyList<Point3d> AdditionalSnapPoints { get; init; } = Array.Empty<Point3d>();

    /// <summary>
    /// Number of panels, laid out evenly before snapping — along the chords, or
    /// on plan under <see cref="MeasureOnPlan"/>. This is the primary driver: when set, it fixes how many
    /// verticals and diagonals there are, and the resulting stations then
    /// migrate onto nearby snap points rather than adding to them — after which
    /// whatever did not snap is spread evenly between the ones that did.
    /// <para>
    /// Zero — the default — hands control back to the geometry, and every
    /// detected point becomes a node in its own right.
    /// </para>
    /// <para>
    /// <see cref="Strictness"/> decides what happens when a snap point sits
    /// somewhere the even layout cannot reach.
    /// </para>
    /// </summary>
    public int Divisions { get; init; }

    /// <summary>
    /// Target panel spacing, in model units, measured the way
    /// <see cref="MeasureOnPlan"/> says. The secondary way to say
    /// <see cref="Divisions"/>, for when you care about panel length rather
    /// than panel count — the span is divided by this and rounded to whole
    /// panels. <see cref="Divisions"/> overrides it whenever both are set, and
    /// zero from both leaves the panel count to the geometry.
    /// </summary>
    public double Spacing { get; init; }

    /// <summary>
    /// Measure <see cref="Divisions"/> and <see cref="Spacing"/> on plan — the
    /// chords projected onto world XY — rather than along the chords.
    /// <para>
    /// Off by default, so the division is pure curve geometry: each chord is
    /// divided by its own length. That is the only reading that works for a
    /// truss standing on end, and the only honest one for a truss running
    /// through space, where plan means nothing in particular.
    /// </para>
    /// <para>
    /// Turn it on for a roof truss. A pitched top chord is longer than the
    /// level bottom chord under it, so dividing each by its own length leaves
    /// every vertical leaning; dividing both by plan distance stands them up.
    /// A chord with no plan length — seen edge-on from above — is measured
    /// along itself either way.
    /// </para>
    /// </summary>
    public bool MeasureOnPlan { get; init; }

    /// <summary>
    /// Whether an awkwardly placed snap point moves the division, or the
    /// division ignores it. See <see cref="SnapStrictness"/>.
    /// <para>
    /// <see cref="SnapStrictness.Relaxed"/> by default: a regular truss is what
    /// most chords want, and the points that could not be used are reported on
    /// <see cref="FlatTruss.UnusedSnapPoints"/> rather than lost quietly.
    /// </para>
    /// </summary>
    public SnapStrictness Strictness { get; init; } = SnapStrictness.Relaxed;

    /// <summary>
    /// Distance below which two nodes are treated as the same one. Also the
    /// planarity check's allowance, and how near a point in
    /// <see cref="AdditionalSnapPoints"/> has to be to a chord to count as
    /// being on it.
    /// <para>
    /// Both front-ends set this from the document tolerance, which is the value
    /// Rhino itself uses to decide whether a point is on a curve — so a point
    /// placed with an On-Curve osnap qualifies, and one merely near the truss
    /// does not.
    /// </para>
    /// </summary>
    public double SnapTolerance { get; init; } = 0.01;
}
