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
    /// Target panel spacing along the chords, in model units. Zero — the default —
    /// means take nodes only from the geometry and the additional points, which
    /// on a bare line leaves a single panel. Set it to subdivide.
    /// </summary>
    public double SnapSpacing { get; init; }

    /// <summary>
    /// Distance below which two nodes are treated as the same one. Also the
    /// planarity check's allowance.
    /// </summary>
    public double SnapTolerance { get; init; } = 0.01;
}
