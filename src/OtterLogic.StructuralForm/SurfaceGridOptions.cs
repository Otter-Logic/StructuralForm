using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Which members a grid is drawn with.
/// <para>
/// Values are explicit and must stay stable: they are what a Grasshopper
/// definition stores on the wire and what the Rhino command persists between
/// sessions. Append new members, never renumber existing ones.
/// </para>
/// </summary>
public enum GridPattern
{
    /// <summary>Members along every grid line, both ways. A grillage, a gridshell of quads.</summary>
    Quad = 0,

    /// <summary>
    /// <see cref="Quad"/>, with one diagonal across every cell. Which one is
    /// <see cref="DiagonalRule"/>'s to say.
    /// </summary>
    Triangulated = 1,

    /// <summary>
    /// Diagonals only, and only between every other node, chequer-board
    /// fashion, closed round the outside by edge members.
    /// <para>
    /// Every other node because the obvious alternative — both diagonals of
    /// every cell — has each pair crossing in mid-air with no node at the
    /// crossing, and an analysis model made of that is two separate structures
    /// that happen to overlap. Taken this way, members only ever meet at nodes.
    /// It costs one thing: the chequer-board has to close, so each direction
    /// needs an even number of divisions, and is given one more when it is not.
    /// </para>
    /// </summary>
    Diagrid = 2,
}

/// <summary>Which diagonal a <see cref="GridPattern.Triangulated"/> cell is given. Explicit values, for the same reason.</summary>
public enum DiagonalRule
{
    /// <summary>The same one in every cell.</summary>
    OneWay = 0,

    /// <summary>
    /// Turn about from cell to cell, chequer-board fashion, so the diagonals
    /// join up into diamonds. The layout a diagrid has, with the grid lines
    /// kept.
    /// </summary>
    Alternating = 1,

    /// <summary>
    /// Whichever is shorter, cell by cell. On a warped or sheared surface this
    /// is the one that folds the cell least, and the stiffer triangle pair.
    /// </summary>
    Shorter = 2,
}

/// <summary>
/// Inputs to <see cref="SurfaceGridGenerator"/> other than the surface.
/// <para>
/// A behaviour-free record, as every options type here is. Each direction is
/// divided exactly as a truss is — a count, or a spacing to derive one from,
/// then snapped — so the pairs below mean what <see cref="FlatTrussOptions"/>
/// says they mean, once for U and once for V.
/// </para>
/// </summary>
public sealed record SurfaceGridOptions
{
    public GridPattern Pattern { get; init; } = GridPattern.Quad;

    /// <summary>
    /// Panels across the surface in its U direction. Overrides
    /// <see cref="SpacingU"/>. Zero from both leaves the count to the geometry:
    /// a grid line through every kink in the two edges that run this way and
    /// every snap point on them, which for two smooth edges is a single panel.
    /// </summary>
    public int DivisionsU { get; init; }

    /// <summary>As <see cref="DivisionsU"/>, in V.</summary>
    public int DivisionsV { get; init; }

    /// <summary>
    /// Target panel width in U, in model units, measured along the longer of
    /// the two edges that run that way and rounded to whole panels.
    /// </summary>
    public double SpacingU { get; init; }

    /// <summary>As <see cref="SpacingU"/>, in V.</summary>
    public double SpacingV { get; init; }

    /// <summary>Which diagonal each cell takes. Only read by <see cref="GridPattern.Triangulated"/>.</summary>
    public DiagonalRule Diagonals { get; init; } = DiagonalRule.OneWay;

    /// <summary>
    /// Take the other diagonal in every cell. For a diagrid that shifts the
    /// whole chequer-board by one node, which moves its nodes off the corners
    /// of the surface and onto the first division in from them.
    /// </summary>
    public bool Flip { get; init; }

    /// <summary>
    /// Points to run a grid line through: a column under a roof, a support on a
    /// façade. Each has to lie on an <em>edge</em> of the surface, within
    /// <see cref="Tolerance"/>, and sets a line in whichever direction crosses
    /// that edge. One out in the middle of the surface would have to move a
    /// line both ways at once, and there is no single honest answer to which —
    /// so, as with a truss, it is counted and reported rather than guessed at.
    /// </summary>
    public IReadOnlyList<Point3d> SnapPoints { get; init; } = Array.Empty<Point3d>();

    /// <summary>What a division owes the snap points. See <see cref="SnapStrictness"/>.</summary>
    public SnapStrictness Strictness { get; init; } = SnapStrictness.Relaxed;

    /// <summary>
    /// Clip the grid to a trimmed surface: a node that falls in an opening, or
    /// outside the trimmed edge, is left out, and so is every member that ran
    /// to it and any member whose middle crosses an opening.
    /// <para>
    /// Off by default, because that is what happened before there was a
    /// choice: a trimmed surface is gridded whole, over the surface underneath
    /// the trim, with a warning. On, the openings in a roof — a rooflight, a
    /// stair, a plant well — come out as holes in the grid, and a surface
    /// trimmed to an outline gives a grid that stops at the outline. Rows and
    /// columns are unchanged and the positions are simply empty, so a tool
    /// reading the grid by position still can. Members are left whole rather
    /// than cut at the rim of an opening, because a member cut there would end
    /// where there is no node; one that crosses an opening is left out.
    /// </para>
    /// </summary>
    public bool ClipToTrim { get; init; }

    /// <summary>
    /// Distance below which two nodes are the same node, and how near a snap
    /// point has to be to an edge to count as on it. Both front-ends set this
    /// from the document tolerance.
    /// </summary>
    public double Tolerance { get; init; } = 0.01;
}
