using OtterLogic.Core;
using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>What part of a grid a member belongs to — one per likely section group.</summary>
public enum GridMemberRole
{
    /// <summary>Along a grid line in the U direction, inside the surface.</summary>
    U,

    /// <summary>Along a grid line in the V direction, inside the surface.</summary>
    V,

    /// <summary>Across a cell.</summary>
    Diagonal,

    /// <summary>
    /// Along the boundary of the surface. Apart from <see cref="U"/> and
    /// <see cref="V"/> because an edge beam carries half the width and
    /// everything that hangs off the edge, and is sized accordingly.
    /// </summary>
    Edge,
}

public static class GridMemberRoles
{
    public static readonly IReadOnlyList<GridMemberRole> All = new[]
    {
        GridMemberRole.U, GridMemberRole.V, GridMemberRole.Diagonal, GridMemberRole.Edge,
    };

    /// <summary>The one spelling both front-ends use: a layer name in Rhino, a port in Grasshopper.</summary>
    public static string DisplayName(this GridMemberRole role) => role switch
    {
        GridMemberRole.U => "U member",
        GridMemberRole.V => "V member",
        _ => Naming.Humanise(role),
    };
}

/// <summary>
/// One straight member of a grid.
/// </summary>
/// <param name="StartNode">Index into <see cref="Lattice.Nodes"/>.</param>
/// <param name="EndNode">Index into <see cref="Lattice.Nodes"/>.</param>
/// <param name="AlongU">
/// True for a member running in the U direction. The role alone does not say:
/// an <see cref="GridMemberRole.Edge"/> member runs one way or the other.
/// </param>
/// <param name="GridLine">
/// Which grid line the member lies along: row <c>j</c> for one running in U,
/// column <c>i</c> for one running in V. -1 for a diagonal. It is what lets a
/// beam be put back together from its segments without comparing coordinates.
/// </param>
public sealed record GridMember(Line Line, GridMemberRole Role, int StartNode, int EndNode, bool AlongU, int GridLine)
{
    public double Length => Line.Length;
}

/// <summary>A generated grid: the <see cref="Lattice"/> of nodes, and the members a pattern drew over it.</summary>
public sealed class SurfaceGrid
{
    internal SurfaceGrid(
        Lattice lattice,
        List<GridMember> members,
        SurfaceGridOptions options,
        bool trimmed,
        int offEdgeSnapPoints,
        int unusedSnapPoints,
        int raisedU,
        int raisedV)
    {
        Lattice = lattice;
        Members = members.AsReadOnly();
        Options = options;
        IsTrimmed = trimmed;
        OffEdgeSnapPoints = offEdgeSnapPoints;
        UnusedSnapPoints = unusedSnapPoints;
        RaisedU = raisedU;
        RaisedV = raisedV;
    }

    public Lattice Lattice { get; }
    public IReadOnlyList<GridMember> Members { get; }
    public SurfaceGridOptions Options { get; }

    public int PanelsU => Lattice.PanelsU;
    public int PanelsV => Lattice.PanelsV;

    /// <summary>
    /// True when the surface was trimmed. The grid covers the whole surface
    /// underneath the trim, not the part left showing — clipping a grid to an
    /// outline is a later tool's job, and a grid drawn past the edge is at
    /// least an obvious thing to be told about.
    /// </summary>
    public bool IsTrimmed { get; }

    /// <summary>Snap points discounted for not lying on an edge of the surface.</summary>
    public int OffEdgeSnapPoints { get; }

    /// <summary>Snap points on an edge that no grid line could reach. See <see cref="FlatTruss.UnusedSnapPoints"/>.</summary>
    public int UnusedSnapPoints { get; }

    /// <summary>
    /// Panels added in U to make a diagrid close, or zero. A diagrid needs an
    /// even count each way; asked for an odd one it is given the next one up
    /// rather than left with half a diamond at the far edge.
    /// </summary>
    public int RaisedU { get; }

    /// <summary>As <see cref="RaisedU"/>, in V.</summary>
    public int RaisedV { get; }

    public IEnumerable<Line> MembersOf(GridMemberRole role)
        => Members.Where(m => m.Role == role).Select(m => m.Line);

    public IEnumerable<Line> MembersU => MembersOf(GridMemberRole.U);
    public IEnumerable<Line> MembersV => MembersOf(GridMemberRole.V);
    public IEnumerable<Line> Diagonals => MembersOf(GridMemberRole.Diagonal);
    public IEnumerable<Line> Edges => MembersOf(GridMemberRole.Edge);

    /// <summary>
    /// The nodes that members actually meet at. A diagrid uses every other node
    /// of its lattice, and a surface drawn to a point stacks a whole row of
    /// them on one spot — which the members already refer to by a single index
    /// — so this is the list to draw and bake; <see cref="Lattice.Nodes"/> is
    /// the one members index into.
    /// </summary>
    public IReadOnlyList<Point3d> UsedNodes => Members
        .SelectMany(m => new[] { m.StartNode, m.EndNode })
        .Distinct()
        .OrderBy(index => index)
        .Select(index => Lattice.Nodes[index])
        .ToArray();

    public double TotalLength => Members.Sum(m => m.Length);

    /// <summary>What is worth telling the user about this grid, if anything.</summary>
    public IReadOnlyList<FormNote> Notes
    {
        get
        {
            var notes = new List<FormNote>(4);

            if (IsTrimmed)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    "The surface is trimmed, and the grid covers the whole surface underneath the trim. "
                    + "Members outside the trimmed edge are yours to cut back."));

            if (OffEdgeSnapPoints > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    (OffEdgeSnapPoints == 1
                        ? "One snap point was discounted for not lying on an edge of the surface. "
                        : $"{OffEdgeSnapPoints} snap points were discounted for not lying on an edge of the surface. ")
                    + "A point on an edge sets one grid line; one out in the middle would have to set two."));

            if (UnusedSnapPoints > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    UnusedSnapPoints == 1
                        ? "One snap point was too far from a grid line to be used. "
                          + "Set strictness to Strict to run a line through it."
                        : $"{UnusedSnapPoints} snap points were too far from a grid line to be used. "
                          + "Set strictness to Strict to run a line through each of them."));

            if (RaisedU > 0 || RaisedV > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    "A diagrid needs an even number of divisions each way to close, so "
                    + (RaisedU > 0 && RaisedV > 0 ? "U and V were each" : RaisedU > 0 ? "U was" : "V was")
                    + $" given one more: {PanelsU} by {PanelsV}."));

            return notes.AsReadOnly();
        }
    }
}
