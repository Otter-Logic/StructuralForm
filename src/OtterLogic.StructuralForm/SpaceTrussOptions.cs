namespace OtterLogic.StructuralForm;

/// <summary>
/// What runs between the two layers of a space truss, which is the one
/// decision a space truss adds to the grid it stands on.
/// <para>
/// Two families in one list, on purpose. <see cref="Pyramid"/> offsets the
/// second layer half a cell and joins the layers with a pyramid per cell; the
/// rest align the second layer under the first and run a flat truss of that
/// pattern along every grid line. They were once two settings, a layer type
/// and a web pattern, and the web pattern was dead whenever the type was
/// offset; asked as one question there is nothing to set that is not read.
/// </para>
/// <para>
/// Values are explicit and must stay stable: they are what a Grasshopper
/// definition stores on the wire and what the Rhino command persists between
/// sessions. Append new members, never renumber existing ones. 0 and 1 are
/// what the two-member list stored, and mean what they meant: 0 was the
/// offset layers, 1 the aligned ones with their default Warren web.
/// </para>
/// </summary>
public enum SpaceTrussType
{
    /// <summary>
    /// The second layer is offset half a cell each way: one node under the
    /// centre of every cell, joined to the cell's four corners, so every cell
    /// carries a pyramid and the two layers are never over each other. The
    /// space frame in its usual form, square on square offset, with no
    /// verticals and no pattern to choose, since a pyramid is the whole web.
    /// Under a diagrid the same rule puts a node under the centre of every
    /// diamond, and the second layer comes out a diagrid too.
    /// </summary>
    Pyramid = 0,

    /// <summary>
    /// The second layer is the first, dropped: a node under every node and
    /// the same members between them, with a Warren truss, a continuous
    /// zigzag of diagonals and no verticals, along every grid line. Two-way
    /// trusses on a grid, rather than a space frame.
    /// </summary>
    Warren = 1,

    /// <summary>Aligned layers with a Warren zigzag and a vertical at every interior node.</summary>
    WarrenWithVerticals = 2,

    /// <summary>Aligned layers with verticals throughout and diagonals sloping down toward mid-span.</summary>
    Pratt = 3,

    /// <summary>Aligned layers with verticals throughout and diagonals sloping up toward mid-span: Pratt mirrored.</summary>
    Howe = 4,

    /// <summary>
    /// Aligned layers with posts only, no diagonals: load carried through
    /// rigid joints and bending, and every panel between the layers open.
    /// </summary>
    Vierendeel = 5,

    /// <summary>Aligned layers with both diagonals in every panel and a vertical at each interior node.</summary>
    CrossBraced = 6,
}

public static class SpaceTrussTypes
{
    /// <summary>True for the offset layers with a pyramid per cell; false for every aligned pattern.</summary>
    public static bool IsPyramid(this SpaceTrussType type) => type == SpaceTrussType.Pyramid;

    /// <summary>
    /// The flat-truss pattern an aligned type runs along every grid line, or
    /// null for <see cref="SpaceTrussType.Pyramid"/>, whose web is the
    /// pyramids. Spelled out rather than computed from the values so that a
    /// pattern added to <see cref="TrussType"/> and forgotten here fails a
    /// test instead of mapping to the wrong one.
    /// </summary>
    public static TrussType? Web(this SpaceTrussType type) => type switch
    {
        SpaceTrussType.Pyramid => null,
        SpaceTrussType.Warren => TrussType.Warren,
        SpaceTrussType.WarrenWithVerticals => TrussType.WarrenWithVerticals,
        SpaceTrussType.Pratt => TrussType.Pratt,
        SpaceTrussType.Howe => TrussType.Howe,
        SpaceTrussType.Vierendeel => TrussType.Vierendeel,
        SpaceTrussType.CrossBraced => TrussType.CrossBraced,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unhandled space truss type."),
    };
}

/// <summary>
/// Inputs to <see cref="SpaceTrussGenerator"/> other than the grid.
/// <para>
/// A behaviour-free record, as every options type here is. The grid itself
/// arrives already built: pattern, divisions, snapping and clipping are
/// <see cref="SurfaceGridOptions"/>'s to say and were said when it was made.
/// So what is here is only what a truss adds to a grid: how deep, what runs
/// between the layers, and which side.
/// </para>
/// <para>
/// Three settings, down from seven. The web pattern, its flip and its end
/// posts folded into <see cref="Type"/>: the flip was Howe by another name,
/// and a two-way truss with no post at its supported edge is the case nobody
/// asks for. A vertical depth went too. Measured plumb, a truss on a curved
/// roof thins toward its sides and on a tower collapses into the surface,
/// and a space truss is a constant depth or it is something else.
/// </para>
/// </summary>
public sealed record SpaceTrussOptions
{
    /// <summary>
    /// Distance between the two layers, in model units, measured along the
    /// surface normal at every node so the truss is the same depth everywhere
    /// and follows the surface: a dome's second layer is a smaller dome. Has
    /// to be greater than zero: there is no default, because a depth is a
    /// design decision and a made-up one would look exactly like a considered
    /// one.
    /// </summary>
    public double Depth { get; init; }

    /// <summary>
    /// What runs between the layers. Pyramid by default, because that is what
    /// a space truss usually means. See <see cref="SpaceTrussType"/>.
    /// </summary>
    public SpaceTrussType Type { get; init; } = SpaceTrussType.Pyramid;

    /// <summary>
    /// Put the second layer on the other side of the surface: above a roof
    /// rather than below it, inside a tower rather than out. The default side
    /// is the underside where the surface has one, judged once for the whole
    /// surface from the mean of its normals, and the side it faces when it
    /// stands on end. This is for when that guessed wrong, and for the
    /// surface that has no underside.
    /// </summary>
    public bool FlipDepth { get; init; }
}
