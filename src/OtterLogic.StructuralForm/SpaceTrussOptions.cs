namespace OtterLogic.StructuralForm;

/// <summary>
/// How the second layer of a space truss sits under the first.
/// <para>
/// Values are explicit and must stay stable: they are what a Grasshopper
/// definition stores on the wire and what the Rhino command persists between
/// sessions. Append new members, never renumber existing ones.
/// </para>
/// </summary>
public enum SpaceTrussType
{
    /// <summary>
    /// The second layer is offset half a cell each way: one node under the
    /// centre of every cell, joined to the cell's four corners, so every cell
    /// carries a pyramid and the two layers are never over each other. The
    /// space frame in its usual form — square on square offset — with no
    /// verticals and no pattern to choose, since a pyramid is the whole web.
    /// Under a diagrid the same rule puts a node under the centre of every
    /// diamond, and the second layer comes out a diagrid too.
    /// </summary>
    Offset = 0,

    /// <summary>
    /// The second layer is the first, offset: a node under every node, and
    /// the same members between them. Every grid line then has a line under
    /// it, and the face between the two is a flat truss — Warren, Pratt,
    /// Howe, any of them — running both ways across the grid. Two-way trusses
    /// on a grid, rather than a space frame.
    /// </summary>
    Aligned = 1,
}

/// <summary>Which way the second layer is offset from the surface. Explicit values, for the same reason.</summary>
public enum DepthDirection
{
    /// <summary>
    /// Along the surface's normal at each node, so the truss is the same depth
    /// everywhere and follows the surface: a dome's second layer is a smaller
    /// dome. The layer is put on the underside where the surface has one — the
    /// side its normals point away from when they point up — and on the side
    /// the surface faces when it stands on end.
    /// </summary>
    SurfaceNormal = 0,

    /// <summary>
    /// Straight down, whichever way the surface faces, so every web member
    /// under a node is plumb and the second layer is the first dropped through
    /// the depth. What a pitched roof wants when the truss has to read as a
    /// depth in section rather than a thickness of the roof. On a surface that
    /// stands on end this puts the second layer in the surface's own plane,
    /// and the truss says so.
    /// </summary>
    Vertical = 1,
}

/// <summary>
/// Inputs to <see cref="SpaceTrussGenerator"/> other than the grid.
/// <para>
/// A behaviour-free record, as every options type here is. The grid itself
/// arrives already built — pattern, divisions, snapping and clipping are
/// <see cref="SurfaceGridOptions"/>'s to say and were said when it was made —
/// so what is here is only what a truss adds to a grid: how deep, how the
/// layers sit, and the web between them.
/// </para>
/// </summary>
public sealed record SpaceTrussOptions
{
    /// <summary>
    /// Distance between the two layers, in model units, measured the way
    /// <see cref="DepthAlong"/> says. Has to be greater than zero: there is no
    /// default, because a depth is a design decision and a made-up one would
    /// look exactly like a considered one.
    /// </summary>
    public double Depth { get; init; }

    /// <summary>
    /// How the second layer sits under the first: offset half a cell, with a
    /// pyramid on every cell, or aligned under it, with a flat truss along
    /// every grid line. Offset by default, because that is what a space truss
    /// usually means.
    /// </summary>
    public SpaceTrussType Type { get; init; } = SpaceTrussType.Offset;

    /// <summary>
    /// Web bracing pattern of an <see cref="SpaceTrussType.Aligned"/> truss,
    /// read the same way a flat truss reads it, along every grid line in turn.
    /// Warren by default, as a flat truss is, since an aligned space truss is
    /// flat trusses. Not read by <see cref="SpaceTrussType.Offset"/>, whose
    /// web is the pyramids.
    /// </summary>
    public TrussType Web { get; init; } = TrussType.Warren;

    /// <summary>
    /// Mirror every web diagonal within its own panel, as
    /// <see cref="FlatTrussOptions.Flip"/> does. Aligned only.
    /// </summary>
    public bool FlipWeb { get; init; }

    /// <summary>
    /// Close an aligned truss with a post wherever a grid line ends: round the
    /// outside of the grid, and at the rim of an opening. Off, the lines are
    /// open-ended there, as a flat truss without end posts is. Offset has no
    /// posts either way, since its layers have no node over another.
    /// </summary>
    public bool GenerateEndPosts { get; init; } = true;

    /// <summary>Which way the second layer is offset. See <see cref="DepthDirection"/>.</summary>
    public DepthDirection DepthAlong { get; init; } = DepthDirection.SurfaceNormal;

    /// <summary>
    /// Put the second layer on the other side of the surface: above a roof
    /// rather than below it, inside a tower rather than out. The default
    /// side is the one <see cref="DepthDirection"/> describes; this is for
    /// when it guessed wrong, and for the surface that has no underside.
    /// </summary>
    public bool FlipDepth { get; init; }
}
