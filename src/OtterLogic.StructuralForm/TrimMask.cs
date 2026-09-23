using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// What a trimmed surface leaves out: the openings cut through it, and
/// whatever lies outside its trimmed edge.
/// <para>
/// Asked in 3D rather than in the surface's parameters. The grid is built on a
/// NURBS copy of the surface whose parameterisation need not match the face's
/// own — a plane or a surface of revolution is reparameterised on the way —
/// so a (u, v) from one is not a (u, v) on the other. A point, on the other
/// hand, is the same point on both, and the face knows where its own nearest
/// parameter is. For the nodes that is exact, since they sit on the surface;
/// for the middle of a straight member it is the surface point under it, which
/// is the question being asked.
/// </para>
/// <para>
/// Holds its own copy of the face. The Brep a grid was built from belongs to
/// whoever picked it — a Grasshopper parameter, a document object — and can be
/// disposed under a result that outlives it.
/// </para>
/// </summary>
internal sealed class TrimMask
{
    private readonly BrepFace _face;
    private readonly double _tolerance;

    internal TrimMask(BrepFace face, double tolerance)
    {
        Brep own = face.DuplicateFace(duplicateMeshes: false);

        _face = own.Faces[0];
        _tolerance = tolerance;
    }

    /// <summary>
    /// True when <paramref name="point"/> lies over an opening, or outside the
    /// trimmed edge. A point on the trim boundary itself is kept: a node on the
    /// rim of an opening is exactly where a member round it should stop.
    /// </summary>
    internal bool Excludes(Point3d point)
    {
        if (!_face.ClosestPoint(point, out double u, out double v))
            return false;

        return _face.IsPointOnFace(u, v, _tolerance) == PointFaceRelation.Exterior;
    }

    /// <summary>Whether the middle of a member lies over an opening: the test every member gets.</summary>
    internal bool Excludes(Line member) => Excludes(member.PointAt(0.5));
}
