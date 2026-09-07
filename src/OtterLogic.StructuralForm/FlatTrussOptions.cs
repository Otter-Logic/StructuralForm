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
    /// Extra points to force a node at. Each is pulled onto whichever chord is
    /// nearer, so you can pick points off either one.
    /// <para>
    /// These are the <em>secondary</em> snap targets. The chords' own natural
    /// points — polyline vertices and curve kinks — are checked first and take
    /// every station they can reach; these are offered whatever is left over.
    /// </para>
    /// </summary>
    public IReadOnlyList<Point3d> AdditionalSnapPoints { get; init; } = Array.Empty<Point3d>();

    /// <summary>
    /// How near a truss node has to come to one of
    /// <see cref="AdditionalSnapPoints"/> for it to snap: the radius of a
    /// sphere around each picked point, in model units.
    /// <para>
    /// Zero — the default — means no limit, so a point reaches its chord however
    /// far to the side it sits. That is what lets one run of the Rhino command
    /// hand the same points to a whole bay of trusses and get a node in the
    /// same place on every one of them; set a radius when you would rather a
    /// point only affect the trusses it is actually near.
    /// </para>
    /// <para>
    /// It never overrides the chords' own natural points, and it never moves a
    /// station past its neighbour: half a panel each way is the hard limit
    /// whatever this is set to.
    /// </para>
    /// </summary>
    public double SnapDistance { get; init; }

    /// <summary>
    /// Number of panels, laid out evenly by <em>plan</em> distance before
    /// snapping. This is the primary driver: when set, it fixes how many
    /// verticals and diagonals there are, and the resulting stations then
    /// migrate onto nearby snap points rather than adding to them — after which
    /// whatever did not snap is spread evenly between the ones that did.
    /// <para>
    /// Zero — the default — hands control back to the geometry, and every
    /// detected point becomes a node in its own right.
    /// </para>
    /// </summary>
    public int Divisions { get; init; }

    /// <summary>
    /// Target panel spacing on plan, in model units. An alternative way to say
    /// <see cref="Divisions"/> when you care about panel length rather than
    /// panel count; <see cref="Divisions"/> wins if both are set. Zero leaves
    /// the panel count to the geometry.
    /// </summary>
    public double SnapSpacing { get; init; }

    /// <summary>
    /// Distance below which two nodes are treated as the same one. Also the
    /// planarity check's allowance.
    /// </summary>
    public double SnapTolerance { get; init; } = 0.01;
}
