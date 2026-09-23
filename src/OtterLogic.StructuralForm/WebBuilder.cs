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
/// <para>
/// Chords are handed over as sequences of indices into one node list rather
/// than as runs of points, because a space truss's chords are the lines of a
/// lattice — every fifth node, say — and the face between a line of the top
/// layer and the line under it is the same face a flat truss has. The builder
/// looks a node up; it does not care how the caller numbered them.
/// </para>
/// </summary>
internal sealed class WebBuilder
{
    private readonly IReadOnlyList<Point3d> _nodes;
    private readonly double _tolerance;
    private readonly Func<Line, bool>? _excluded;
    private readonly List<TrussMember> _members = new();
    private readonly HashSet<(int, int)> _seen = new();

    /// <param name="nodes">Every node a member can refer to; member indices are indices into it.</param>
    /// <param name="excluded">
    /// A member to leave out however it was arrived at — one crossing an
    /// opening in a clipped grid. Null leaves every member in.
    /// </param>
    internal WebBuilder(IReadOnlyList<Point3d> nodes, double tolerance, Func<Line, bool>? excluded = null)
    {
        _nodes = nodes;
        _tolerance = tolerance;
        _excluded = excluded;
    }

    internal List<TrussMember> Members => _members;

    /// <summary>How many members were left out for <c>excluded</c> saying so.</summary>
    internal int Excluded { get; private set; }

    internal void Add(int startNode, int endNode, TrussMemberRole role)
    {
        if (startNode == endNode) return;

        var key = startNode < endNode ? (startNode, endNode) : (endNode, startNode);
        if (!_seen.Add(key)) return;

        Point3d start = _nodes[startNode], end = _nodes[endNode];
        if (start.DistanceTo(end) <= _tolerance) return;   // drop degenerate members

        var line = new Line(start, end);

        if (_excluded is not null && _excluded(line))
        {
            Excluded++;
            return;
        }

        _members.Add(new TrussMember(line, role, startNode, endNode));
    }

    /// <summary>One chord, split at every node.</summary>
    internal void AddChord(IReadOnlyList<int> nodes, TrussMemberRole role)
    {
        for (int i = 0; i < nodes.Count - 1; i++)
            Add(nodes[i], nodes[i + 1], role);
    }

    /// <summary>
    /// The web between two chords. <paramref name="a"/> plays the part the top
    /// chord plays in a flat truss, which only matters to which way a diagonal
    /// is said to fall.
    /// </summary>
    /// <param name="closed">
    /// The chords run round in a ring — a truss round a tower — so the last
    /// node is followed by the first. There are then no ends: no end posts, no
    /// meeting at them, and a vertical at every node rather than every
    /// interior one.
    /// </param>
    internal void AddFace(
        IReadOnlyList<int> a,
        IReadOnlyList<int> b,
        TrussType type, bool flip, bool endPosts,
        bool meetAtStart, bool meetAtEnd,
        FaceRoles roles,
        bool closed = false)
    {
        if (a.Count != b.Count)
            throw new ArgumentException("A face needs the same number of nodes on each chord.", nameof(b));

        int count = a.Count;
        int panels = closed ? count : count - 1;

        int Next(int i) => closed ? (i + 1) % count : i + 1;

        void AddVertical(int i) => Add(a[i], b[i], roles.Vertical);
        void AddDown(int i) => Add(a[i], b[Next(i)], roles.Diagonal);
        void AddUp(int i) => Add(b[i], a[Next(i)], roles.Diagonal);

        bool verticals = type is TrussType.Vierendeel
            or TrussType.WarrenWithVerticals
            or TrussType.Pratt
            or TrussType.Howe
            or TrussType.CrossBraced;

        if (verticals)
            for (int i = closed ? 0 : 1; i < panels; i++)
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
            if (!closed && ((meetAtStart && i == 0) || (meetAtEnd && i == panels - 1)))
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

        if (endPosts && !closed)
        {
            // Where the chords meet, the post would collapse onto the shared
            // point and clash with the chords running into it.
            if (!meetAtStart)
                Add(a[0], b[0], roles.End);

            if (!meetAtEnd)
                Add(a[panels], b[panels], roles.End);
        }
    }
}
