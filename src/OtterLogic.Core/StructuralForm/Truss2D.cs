using Rhino.Geometry;

namespace OtterLogic.Core.StructuralForm;

/// <summary>
/// A generated truss: paired chord nodes plus the members between them.
/// <para>
/// Node <c>i</c> of <see cref="TopNodes"/> and node <c>i</c> of
/// <see cref="BottomNodes"/> sit at the same station along their chords, which
/// is what makes the web patterns expressible as index arithmetic.
/// </para>
/// </summary>
public sealed class Truss2D
{
    internal Truss2D(
        IReadOnlyList<Point3d> topNodes,
        IReadOnlyList<Point3d> bottomNodes,
        IReadOnlyList<TrussMember> members,
        TrussType type,
        bool isPlanar,
        bool chordsMeetAtStart,
        bool chordsMeetAtEnd)
    {
        TopNodes = topNodes;
        BottomNodes = bottomNodes;
        Members = members;
        Type = type;
        IsPlanar = isPlanar;
        ChordsMeetAtStart = chordsMeetAtStart;
        ChordsMeetAtEnd = chordsMeetAtEnd;
    }

    public IReadOnlyList<Point3d> TopNodes { get; }
    public IReadOnlyList<Point3d> BottomNodes { get; }
    public IReadOnlyList<TrussMember> Members { get; }
    public TrussType Type { get; }

    /// <summary>
    /// False when the two chords do not share a plane within tolerance. The
    /// generator still produces geometry — a warped truss is a modelling
    /// mistake, not an error — but front-ends should say so.
    /// </summary>
    public bool IsPlanar { get; }

    /// <summary>
    /// True when the two chords converge to a shared point at station 0. No end
    /// post is generated there — it would collapse onto that point and sit on
    /// top of the chords themselves.
    /// </summary>
    public bool ChordsMeetAtStart { get; }

    /// <summary>As <see cref="ChordsMeetAtStart"/>, for the far end.</summary>
    public bool ChordsMeetAtEnd { get; }

    /// <summary>Number of bays between chord nodes.</summary>
    public int PanelCount => TopNodes.Count - 1;

    public IEnumerable<Line> MembersOf(TrussMemberRole role)
        => Members.Where(m => m.Role == role).Select(m => m.Line);

    public IEnumerable<Line> TopChord => MembersOf(TrussMemberRole.TopChord);
    public IEnumerable<Line> BottomChord => MembersOf(TrussMemberRole.BottomChord);
    public IEnumerable<Line> Web => MembersOf(TrussMemberRole.Web);
    public IEnumerable<Line> EndPosts => MembersOf(TrussMemberRole.EndPost);

    /// <summary>Every node, top chord first. Indices match <see cref="TrussMember"/>.</summary>
    public IReadOnlyList<Point3d> Nodes => TopNodes.Concat(BottomNodes).ToArray();

    public double TotalLength => Members.Sum(m => m.Length);
}
