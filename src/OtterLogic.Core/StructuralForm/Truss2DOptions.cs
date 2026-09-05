using Rhino.Geometry;

namespace OtterLogic.Core.StructuralForm;

/// <summary>
/// Inputs to <see cref="Truss2DGenerator"/> other than the two chords.
/// <para>
/// A behaviour-free record, deliberately: this is the kind of data that crosses
/// the boundary into Grasshopper and Rhino, so it stays immutable and trivially
/// serialisable. The logic lives in the generator.
/// </para>
/// </summary>
public sealed record Truss2DOptions
{
    /// <summary>Web bracing pattern.</summary>
    public TrussType Type { get; init; } = TrussType.Warren;

    /// <summary>Close the truss with a post at each end.</summary>
    public bool GenerateEndPosts { get; init; } = true;

    /// <summary>
    /// Extra points to force a node at. Each is pulled onto whichever chord is
    /// nearer, so you can pick points off either one.
    /// </summary>
    public IReadOnlyList<Point3d> AdditionalSnapPoints { get; init; } = Array.Empty<Point3d>();

    /// <summary>
    /// Number of panels to lay out evenly before snapping. This is the primary
    /// driver: when set, it fixes how many verticals and diagonals there are,
    /// and the resulting stations then migrate onto nearby snap points rather
    /// than adding to them.
    /// <para>
    /// Zero — the default — hands control back to the geometry, and every
    /// detected point becomes a node in its own right.
    /// </para>
    /// </summary>
    public int Divisions { get; init; }

    /// <summary>
    /// Target panel spacing along the chords, in model units. An alternative way
    /// to say <see cref="Divisions"/> when you care about panel length rather
    /// than panel count; <see cref="Divisions"/> wins if both are set. Zero
    /// leaves the panel count to the geometry.
    /// </summary>
    public double SnapSpacing { get; init; }

    /// <summary>
    /// Distance below which two nodes are treated as the same one. Also the
    /// planarity check's allowance.
    /// </summary>
    public double SnapTolerance { get; init; } = 0.01;
}
