using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// The roles a face's members are given. A flat truss has one face and one
/// answer; a box truss has side faces and lacing faces, and sizes them apart.
/// </summary>
internal readonly record struct FaceRoles(
    TrussMemberRole Vertical, TrussMemberRole Diagonal, TrussMemberRole End);

/// <summary>
/// Builds the members of a truss, one chord and one face at a time, into a
/// single list with no member drawn twice.
/// <para>
/// A <em>face</em> is two chords and the web between them. Because every chord
/// shares one station list, node <c>i</c> of one and node <c>i</c> of the other
/// are a pair, and every web pattern is index arithmetic over the panels. A
/// flat truss is one face. A box truss is three or four that share chords,
/// which is why the list and the seen-set belong to the builder rather than to
/// a face: the chord two faces share is added once.
/// </para>
/// </summary>
internal sealed class WebBuilder
{
    private readonly double _tolerance;
    private readonly List<TrussMember> _members = new();
    private readonly HashSet<(int, int)> _seen = new();

    internal WebBuilder(double tolerance) => _tolerance = tolerance;

    internal List<TrussMember> Members => _members;

    private void Add(int startNode, int endNode, Point3d start, Point3d end, TrussMemberRole role)
    {
        var key = startNode < endNode ? (startNode, endNode) : (endNode, startNode);
        if (!_seen.Add(key)) return;
        if (start.DistanceTo(end) <= _tolerance) return;   // drop degenerate members

        _members.Add(new TrussMember(new Line(start, end), role, startNode, endNode));
    }

    /// <summary>One chord, split at every node. <paramref name="offset"/> is where its nodes start in the truss's node list.</summary>
    internal void AddChord(Point3d[] nodes, int offset, TrussMemberRole role)
    {
        for (int i = 0; i < nodes.Length - 1; i++)
            Add(offset + i, offset + i + 1, nodes[i], nodes[i + 1], role);
    }

    /// <summary>
    /// The web between two chords. <paramref name="a"/> plays the part the top
    /// chord plays in a flat truss, which only matters to which way a diagonal
    /// is said to fall.
    /// </summary>
    internal void AddFace(
        Point3d[] a, int aOffset,
        Point3d[] b, int bOffset,
        TrussType type, bool flip, bool endPosts,
        bool meetAtStart, bool meetAtEnd,
        FaceRoles roles)
    {
        int panels = a.Length - 1;

        void AddVertical(int i) => Add(aOffset + i, bOffset + i, a[i], b[i], roles.Vertical);
        void AddDown(int i) => Add(aOffset + i, bOffset + i + 1, a[i], b[i + 1], roles.Diagonal);
        void AddUp(int i) => Add(bOffset + i, aOffset + i + 1, b[i], a[i + 1], roles.Diagonal);

        bool verticals = type is TrussType.Vierendeel
            or TrussType.WarrenWithVerticals
            or TrussType.Pratt
            or TrussType.Howe
            or TrussType.CrossBraced;

        if (verticals)
            for (int i = 1; i < panels; i++)
                AddVertical(i);

        // Flip mirrors each diagonal within its own panel, which is the same as
        // swapping which way the two helpers run. Cross-braced draws both, so it
        // comes out identical either way.
        Action<int> down = flip ? AddUp : AddDown;
        Action<int> up = flip ? AddDown : AddUp;

        double midpoint = panels / 2.0;

        for (int i = 0; i < panels; i++)
        {
            // Where the chords converge — the tip of a cantilever, the apex of a
            // tapered truss — the end panel has no room for a diagonal. Both its
            // nodes at that end are the same point, so a diagonal out of it runs
            // from that point to the next node along one chord or the other,
            // which is the chord member itself drawn a second time. The seen-set
            // does not catch it: the same line, but between two different node
            // indices.
            if ((meetAtStart && i == 0) || (meetAtEnd && i == panels - 1))
                continue;

            switch (type)
            {
                case TrussType.Vierendeel:
                    break;

                // A continuous zigzag: alternating panels flip the diagonal.
                case TrussType.Warren:
                case TrussType.WarrenWithVerticals:
                    if (i % 2 == 0) down(i); else up(i);
                    break;

                // Diagonals fall toward mid-span, mirrored about it.
                case TrussType.Pratt:
                    if (i < midpoint) down(i); else up(i);
                    break;

                // Pratt mirrored: diagonals rise toward mid-span.
                case TrussType.Howe:
                    if (i < midpoint) up(i); else down(i);
                    break;

                case TrussType.CrossBraced:
                    down(i);
                    up(i);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Unhandled truss type.");
            }
        }

        if (endPosts)
        {
            // Where the chords meet, the post would collapse onto the shared
            // point and clash with the chords running into it.
            if (!meetAtStart)
                Add(aOffset, bOffset, a[0], b[0], roles.End);

            if (!meetAtEnd)
                Add(aOffset + panels, bOffset + panels, a[panels], b[panels], roles.End);
        }
    }
}
