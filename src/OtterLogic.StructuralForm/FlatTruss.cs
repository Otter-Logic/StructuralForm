using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// A generated truss: paired chord nodes plus the members between them.
/// <para>
/// Node <c>i</c> of <see cref="TopNodes"/> and node <c>i</c> of
/// <see cref="BottomNodes"/> are one panel point of the truss — at the same
/// station along their chords unless a snap point on each moved them apart, which
/// is what makes the web patterns expressible as index arithmetic.
/// </para>
/// </summary>
public sealed class FlatTruss
{
    internal FlatTruss(
        Point3d[] topNodes,
        Point3d[] bottomNodes,
        List<TrussMember> members,
        FlatTrussOptions options,
        bool isPlanar,
        bool chordsMeetAtStart,
        bool chordsMeetAtEnd,
        int unusedSnapPoints,
        int offChordSnapPoints)
    {
        // Wrapped, not just typed as read-only. A generated truss is a result,
        // and a result that a caller can reach into and edit is not one — an
        // IReadOnlyList over a live array or list is a promise the type system
        // does not keep, since the concrete type is one cast away.
        TopNodes = Array.AsReadOnly(topNodes);
        BottomNodes = Array.AsReadOnly(bottomNodes);
        Members = members.AsReadOnly();
        Options = options;
        IsPlanar = isPlanar;
        ChordsMeetAtStart = chordsMeetAtStart;
        ChordsMeetAtEnd = chordsMeetAtEnd;
        UnusedSnapPoints = unusedSnapPoints;
        OffChordSnapPoints = offChordSnapPoints;
    }

    public IReadOnlyList<Point3d> TopNodes { get; }
    public IReadOnlyList<Point3d> BottomNodes { get; }
    public IReadOnlyList<TrussMember> Members { get; }

    /// <summary>
    /// What this truss was generated from. Kept so the truss can answer
    /// questions about itself that depend on what was asked for — whether a
    /// missing end post is a suppression or simply not wanted — instead of every
    /// caller having to hold the options alongside the result.
    /// </summary>
    public FlatTrussOptions Options { get; }

    public TrussType Type => Options.Type;

    /// <summary>
    /// False when the two chords do not share a plane within tolerance. The
    /// generator still produces geometry — a warped truss is a modelling
    /// mistake, not an error — but front-ends should say so.
    /// </summary>
    public bool IsPlanar { get; }

    /// <summary>
    /// True when the two chords converge to a shared point at station 0 — the
    /// tip of a cantilever, or the apex of a tapered truss.
    /// <para>
    /// Neither an end post nor an end diagonal is generated there. The post
    /// would collapse onto the shared point, and a diagonal out of it would run
    /// to the next node along one chord or the other, which is that chord
    /// member drawn twice.
    /// </para>
    /// </summary>
    public bool ChordsMeetAtStart { get; }

    /// <summary>As <see cref="ChordsMeetAtStart"/>, for the far end.</summary>
    public bool ChordsMeetAtEnd { get; }

    /// <summary>
    /// How many of <see cref="FlatTrussOptions.AdditionalSnapPoints"/> were
    /// discounted for not lying on either chord.
    /// <para>
    /// A snap point has to be on the top or bottom chord, within
    /// <see cref="FlatTrussOptions.SnapTolerance"/>. One picked in mid-air is
    /// not a near miss to be rescued — there is no defensible station for it —
    /// but it is also the easiest mistake to make and the hardest to see, since
    /// the truss comes out looking perfectly reasonable. Hence the count.
    /// </para>
    /// </summary>
    public int OffChordSnapPoints { get; }

    /// <summary>
    /// How many snap points were on a chord, but did not end up moving a node.
    /// <para>
    /// Under <see cref="SnapStrictness.Relaxed"/> a point is used only if a
    /// panel point can reach it, so one placed away from the regular spacing
    /// does nothing at all. That is the rule working as intended, but it is
    /// invisible on the geometry — the truss looks exactly as it would have
    /// done had the point never been picked. Counting them is what lets a
    /// front-end say so, and point at
    /// <see cref="SnapStrictness.Strict"/> as the way to honour them.
    /// </para>
    /// <para>
    /// Always zero under <see cref="SnapStrictness.Strict"/>, where every point
    /// on a chord becomes a node, and when no division is set, where every
    /// point is a node already. Points counted here are a separate matter from
    /// <see cref="OffChordSnapPoints"/>, and have a different remedy.
    /// </para>
    /// </summary>
    public int UnusedSnapPoints { get; }

    /// <summary>Number of bays between chord nodes.</summary>
    public int PanelCount => TopNodes.Count - 1;

    public IEnumerable<Line> MembersOf(TrussMemberRole role)
        => Members.Where(m => m.Role == role).Select(m => m.Line);

    public IEnumerable<Line> TopChord => MembersOf(TrussMemberRole.TopChord);
    public IEnumerable<Line> BottomChord => MembersOf(TrussMemberRole.BottomChord);
    public IEnumerable<Line> Verticals => MembersOf(TrussMemberRole.Vertical);
    public IEnumerable<Line> Diagonals => MembersOf(TrussMemberRole.Diagonal);
    public IEnumerable<Line> EndPosts => MembersOf(TrussMemberRole.EndPost);

    /// <summary>
    /// Verticals and diagonals together — everything bracing the two chords,
    /// end posts aside. The two roles exist separately because they are
    /// specified separately; this is for when the distinction does not matter.
    /// </summary>
    public IEnumerable<Line> Web => Members
        .Where(m => m.Role is TrussMemberRole.Vertical or TrussMemberRole.Diagonal)
        .Select(m => m.Line);

    /// <summary>
    /// Every node, top chord first, including the pairs that sit on top of each
    /// other. Indices match <see cref="TrussMember.StartNode"/>, so this is the
    /// list to index into; <see cref="DistinctNodes"/> is the one to draw.
    /// </summary>
    public IReadOnlyList<Point3d> Nodes => Array.AsReadOnly(TopNodes.Concat(BottomNodes).ToArray());

    /// <summary>
    /// The nodes with coincident ones merged — the list worth handing to a
    /// front-end.
    /// <para>
    /// Where the chords meet, top node <c>i</c> and bottom node <c>i</c> are the
    /// same point, and two points on top of each other is a snapping nuisance
    /// for whatever picks the truss up next. Connectivity still runs off
    /// <see cref="Nodes"/>, whose indices the members refer to.
    /// </para>
    /// </summary>
    public IReadOnlyList<Point3d> DistinctNodes
    {
        get
        {
            var kept = new List<Point3d>(TopNodes.Count + BottomNodes.Count);

            foreach (Point3d node in Nodes)
                if (!kept.Any(k => k.DistanceTo(node) <= Options.SnapTolerance))
                    kept.Add(node);

            return kept.AsReadOnly();
        }
    }

    /// <summary>
    /// What is worth telling the user about this truss, if anything.
    /// <para>
    /// Both front-ends were making the same three observations in the same
    /// words. The wording is domain knowledge — it describes the truss, not the
    /// host — so it lives here, and each front-end only decides how to show it.
    /// </para>
    /// </summary>
    public IReadOnlyList<FormNote> Notes
    {
        get
        {
            var notes = new List<FormNote>(4);

            if (!IsPlanar)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    "The two chords are not coplanar, so this truss is warped."));

            StationNotes.AddTo(
                notes, 2, OffChordSnapPoints, UnusedSnapPoints, Options.Strictness, Options.Divisions, PanelCount);

            // Only worth saying when posts were asked for: chords meeting is
            // otherwise just the shape of the truss.
            if (Options.GenerateEndPosts && (ChordsMeetAtStart || ChordsMeetAtEnd))
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    ChordsMeetAtStart && ChordsMeetAtEnd
                        ? "The chords meet at both ends, so no end posts were generated."
                        : "The chords meet at one end, so only one end post was generated."));

            return notes.AsReadOnly();
        }
    }

    public double TotalLength => Members.Sum(m => m.Length);
}
