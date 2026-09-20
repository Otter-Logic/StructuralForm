using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// A generated box truss: three or four chords, the nodes along each, and the
/// members between them.
/// <para>
/// Every chord is divided at the same stations, so node <c>i</c> of any chord
/// and node <c>i</c> of any other belong to the same cross-section through the
/// truss. With three chords that section is a triangle, with four a
/// quadrilateral — and the class is named for the second because that is what
/// the thing is called in either case.
/// </para>
/// </summary>
public sealed class BoxTruss
{
    internal BoxTruss(
        Point3d[][] topNodes,
        Point3d[][] bottomNodes,
        List<TrussMember> members,
        BoxTrussOptions options,
        bool endsSuppressed,
        int unusedSnapPoints,
        int offChordSnapPoints)
    {
        TopNodes = Array.AsReadOnly(topNodes.Select(n => (IReadOnlyList<Point3d>)Array.AsReadOnly(n)).ToArray());
        BottomNodes = Array.AsReadOnly(bottomNodes.Select(n => (IReadOnlyList<Point3d>)Array.AsReadOnly(n)).ToArray());
        Members = members.AsReadOnly();
        Options = options;
        EndsSuppressed = endsSuppressed;
        UnusedSnapPoints = unusedSnapPoints;
        OffChordSnapPoints = offChordSnapPoints;
    }

    /// <summary>
    /// Panel points per top chord — one list or two. Where there are two bottom
    /// chords as well, top chord <c>k</c> is the one over bottom chord <c>k</c>,
    /// whichever order they were handed in.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Point3d>> TopNodes { get; }

    /// <summary>Panel points per bottom chord. See <see cref="TopNodes"/> for the pairing.</summary>
    public IReadOnlyList<IReadOnlyList<Point3d>> BottomNodes { get; }

    public IReadOnlyList<TrussMember> Members { get; }

    public BoxTrussOptions Options { get; }

    /// <summary>Three for a triangular truss, four for a box.</summary>
    public int ChordCount => TopNodes.Count + BottomNodes.Count;

    /// <summary>Number of bays between panel points.</summary>
    public int PanelCount => TopNodes[0].Count - 1;

    /// <summary>
    /// True when two chords of some face ran into each other at an end, so the
    /// end member and end diagonal of that face were left out there — see
    /// <see cref="FlatTruss.ChordsMeetAtStart"/> for why. Two top chords drawn
    /// to a point over a single support is the usual cause.
    /// </summary>
    public bool EndsSuppressed { get; }

    /// <summary>As <see cref="FlatTruss.OffChordSnapPoints"/>.</summary>
    public int OffChordSnapPoints { get; }

    /// <summary>As <see cref="FlatTruss.UnusedSnapPoints"/>.</summary>
    public int UnusedSnapPoints { get; }

    public IEnumerable<Line> MembersOf(TrussMemberRole role)
        => Members.Where(m => m.Role == role).Select(m => m.Line);

    public IEnumerable<Line> TopChord => MembersOf(TrussMemberRole.TopChord);
    public IEnumerable<Line> BottomChord => MembersOf(TrussMemberRole.BottomChord);
    public IEnumerable<Line> Verticals => MembersOf(TrussMemberRole.Vertical);
    public IEnumerable<Line> Diagonals => MembersOf(TrussMemberRole.Diagonal);
    public IEnumerable<Line> EndPosts => MembersOf(TrussMemberRole.EndPost);
    public IEnumerable<Line> Struts => MembersOf(TrussMemberRole.Strut);
    public IEnumerable<Line> Lacing => MembersOf(TrussMemberRole.Lacing);

    /// <summary>
    /// Every node, chord by chord — top chords first, then bottom — including
    /// any that sit on top of each other. Indices match
    /// <see cref="TrussMember.StartNode"/>, so this is the list to index into;
    /// <see cref="DistinctNodes"/> is the one to draw.
    /// </summary>
    public IReadOnlyList<Point3d> Nodes
        => Array.AsReadOnly(TopNodes.Concat(BottomNodes).SelectMany(chord => chord).ToArray());

    /// <summary>The nodes with coincident ones merged. See <see cref="FlatTruss.DistinctNodes"/>.</summary>
    public IReadOnlyList<Point3d> DistinctNodes
    {
        get
        {
            var kept = new List<Point3d>();

            foreach (Point3d node in Nodes)
                if (!kept.Any(k => k.DistanceTo(node) <= Options.Sides.SnapTolerance))
                    kept.Add(node);

            return kept.AsReadOnly();
        }
    }

    /// <summary>What is worth telling the user about this truss, if anything.</summary>
    public IReadOnlyList<FormNote> Notes
    {
        get
        {
            var notes = new List<FormNote>(4);

            StationNotes.AddTo(
                notes, ChordCount, OffChordSnapPoints, UnusedSnapPoints,
                Options.Sides.Strictness, Options.Sides.Divisions, PanelCount);

            if (Options.Sides.GenerateEndPosts && EndsSuppressed)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    "Some of the chords meet at an end, so the end members between them were left out."));

            return notes.AsReadOnly();
        }
    }

    public double TotalLength => Members.Sum(m => m.Length);
}
