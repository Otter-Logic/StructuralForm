using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// One straight member of a space truss, tagged with its role and the node
/// indices it spans.
/// </summary>
/// <param name="Role">
/// Top chord, bottom chord, vertical, diagonal or end post — the same roles a
/// flat truss has, since the sections are chosen the same way.
/// </param>
/// <param name="GridRole">
/// For a chord, which part of its layer's grid it is: along U, along V, a
/// diagonal across a cell, or an edge. Null for the web. A chord's role is
/// then two-level — top chord, U member — because the top layer of a space
/// truss is the surface grid it was built on, and a definition that sized an
/// edge beam apart on the grid should be able to on the truss.
/// </param>
/// <param name="StartNode">Index into <see cref="SpaceTruss.Nodes"/>.</param>
/// <param name="EndNode">Index into <see cref="SpaceTruss.Nodes"/>.</param>
public sealed record SpaceTrussMember(
    Line Line, TrussMemberRole Role, GridMemberRole? GridRole, int StartNode, int EndNode)
{
    public double Length => Line.Length;
}

/// <summary>
/// A generated space truss: the surface grid it stands on, the second layer
/// under it, and the members between and along them.
/// <para>
/// Node indices run through the top layer first — the grid's own
/// <see cref="Lattice.Nodes"/>, unchanged — and then the bottom layer's, so a
/// member's <see cref="SpaceTrussMember.StartNode"/> is either a grid node or
/// <see cref="TopNodes"/>.Count plus a bottom-lattice index. A definition
/// that already reads the grid by position reads the truss the same way.
/// </para>
/// </summary>
public sealed class SpaceTruss
{
    internal SpaceTruss(
        SurfaceGrid grid,
        Lattice bottomLattice,
        List<SpaceTrussMember> members,
        SpaceTrussOptions options,
        bool facesSideways,
        int crossingOpenings,
        bool hasNoLines)
    {
        Grid = grid;
        BottomLattice = bottomLattice;
        Members = members.AsReadOnly();
        Options = options;
        FacesSideways = facesSideways;
        CrossingOpenings = crossingOpenings;
        HasNoLines = hasNoLines;
    }

    /// <summary>The grid the truss was built on, which is its top layer exactly.</summary>
    public SurfaceGrid Grid { get; }

    public SpaceTrussOptions Options { get; }

    public SpaceTrussType Type => Options.Type;

    public double Depth => Options.Depth;

    /// <summary>The top layer's lattice: the grid's own.</summary>
    public Lattice TopLattice => Grid.Lattice;

    /// <summary>
    /// The bottom layer as a lattice of its own, so it can be read by position
    /// like the top. Under <see cref="SpaceTrussType.Aligned"/> it has the
    /// top's rows and columns, one node under each. Under
    /// <see cref="SpaceTrussType.Offset"/> it has a node per <em>cell</em> of
    /// the top — one row and one column fewer, unless the grid wraps — or,
    /// under a diagrid, the top's rows and columns with a node only at the
    /// centre of each diamond. Positions with no node, whichever the reason,
    /// are absent: <see cref="Lattice.IsPresent(int, int)"/> says.
    /// </summary>
    public Lattice BottomLattice { get; }

    public IReadOnlyList<SpaceTrussMember> Members { get; }

    public IReadOnlyList<Point3d> TopNodes => Grid.Lattice.Nodes;

    public IReadOnlyList<Point3d> BottomNodes => BottomLattice.Nodes;

    /// <summary>
    /// Every node, top layer then bottom, absent positions included. Indices
    /// match <see cref="SpaceTrussMember.StartNode"/>, so this is the list to
    /// index into; <see cref="UsedNodes"/> is the one to draw.
    /// </summary>
    public IReadOnlyList<Point3d> Nodes => Array.AsReadOnly(TopNodes.Concat(BottomNodes).ToArray());

    /// <summary>The nodes members actually meet at — no absent positions, no pole stacked on itself.</summary>
    public IReadOnlyList<Point3d> UsedNodes
    {
        get
        {
            IReadOnlyList<Point3d> nodes = Nodes;

            return Members
                .SelectMany(m => new[] { m.StartNode, m.EndNode })
                .Distinct()
                .OrderBy(index => index)
                .Select(index => nodes[index])
                .ToArray();
        }
    }

    /// <summary>
    /// True when the surface stands on end — its normals have no upward or
    /// downward lean on the whole — so there was no underside to put the
    /// second layer on. It went on the side the surface faces.
    /// </summary>
    public bool FacesSideways { get; }

    /// <summary>
    /// Members of the bottom layer and web left out of a truss on a clipped
    /// grid for crossing an opening, over and above what the grid itself left
    /// out. A pyramid whose apex would sit over an opening is counted by its
    /// members.
    /// </summary>
    public int CrossingOpenings { get; }

    /// <summary>
    /// True when an aligned truss found no grid line to run along: a diagrid
    /// closed on itself both ways has diagonals with no ends to start from.
    /// </summary>
    public bool HasNoLines { get; }

    public IEnumerable<Line> MembersOf(TrussMemberRole role)
        => Members.Where(m => m.Role == role).Select(m => m.Line);

    /// <summary>The chord members of one layer that play one part in its grid — the top layer's edge beams, say.</summary>
    public IEnumerable<Line> ChordsOf(TrussMemberRole layer, GridMemberRole part)
        => Members.Where(m => m.Role == layer && m.GridRole == part).Select(m => m.Line);

    public IEnumerable<Line> TopChord => MembersOf(TrussMemberRole.TopChord);
    public IEnumerable<Line> BottomChord => MembersOf(TrussMemberRole.BottomChord);
    public IEnumerable<Line> Verticals => MembersOf(TrussMemberRole.Vertical);
    public IEnumerable<Line> Diagonals => MembersOf(TrussMemberRole.Diagonal);
    public IEnumerable<Line> EndPosts => MembersOf(TrussMemberRole.EndPost);

    /// <summary>Verticals and diagonals together — everything between the layers, end posts aside.</summary>
    public IEnumerable<Line> Web => Members
        .Where(m => m.Role is TrussMemberRole.Vertical or TrussMemberRole.Diagonal)
        .Select(m => m.Line);

    public double TotalLength => Members.Sum(m => m.Length);

    /// <summary>
    /// What is worth telling the user about this truss, if anything. The
    /// grid's own notes are the grid's, and were said when it was made.
    /// </summary>
    public IReadOnlyList<FormNote> Notes
    {
        get
        {
            var notes = new List<FormNote>(4);

            if (FacesSideways)
                notes.Add(Options.DepthAlong == DepthDirection.Vertical
                    ? new FormNote(
                        FormNoteLevel.Warning,
                        "The surface stands on end, so a depth measured vertically puts the second layer in "
                        + "the surface's own plane. Measure the depth along the surface normal instead.")
                    : new FormNote(
                        FormNoteLevel.Remark,
                        "The surface stands on end, so it has no underside: the second layer was put on the "
                        + "side the surface faces. Set Flip Depth to put it on the other."));

            if (HasNoLines)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    "The grid closes on itself both ways, so its lines have no ends for a truss to start "
                    + "from, and no web was drawn. Offset puts a pyramid on every cell instead."));

            if (Options.Type == SpaceTrussType.Offset && (Options.Web != TrussType.Warren || Options.FlipWeb))
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    "Web and Flip Web are read by an aligned truss. An offset truss is a pyramid on every "
                    + "cell, and has no pattern to choose."));

            if (CrossingOpenings > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    CrossingOpenings == 1
                        ? "One member of the second layer or web was left out for crossing an opening."
                        : $"{CrossingOpenings} members of the second layer and web were left out for crossing an opening."));

            return notes.AsReadOnly();
        }
    }
}
