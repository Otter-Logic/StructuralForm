using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>One cell of a <see cref="Lattice"/>: its position, and its corners in order round it.</summary>
/// <param name="I">Column of the cell, counted in the U direction.</param>
/// <param name="J">Row of the cell, counted in the V direction.</param>
/// <param name="Corners">
/// Node indices of (I, J), (I+1, J), (I+1, J+1) and (I, J+1) — round the cell,
/// so consecutive corners share a side and opposite ones a diagonal.
/// </param>
public readonly record struct LatticeCell(int I, int J, IReadOnlyList<int> Corners);

/// <summary>
/// A structured grid of nodes: rows and columns, addressed by position.
/// <para>
/// The engine under every gridded layout here, and deliberately not a mesh. A
/// mesh says which nodes are joined; a lattice also says <em>where in the grid
/// each one is</em>, and that is what the things built on it depend on. A
/// pattern — quad, triangulated, diagrid — is index arithmetic over positions,
/// the same trick the truss webs play along one axis, played along two. A
/// grillage is its lines: beam <c>k</c> is <see cref="LineU"/> <c>k</c>, already
/// whole and in order, not a heap of segments to chain back together. A load
/// take-down is its <see cref="Cells"/>: each has an area and four corners to
/// share it between.
/// </para>
/// <para>
/// It knows nothing about where its nodes came from. A surface's own directions
/// place them today; two directions across a floor plate will place them for a
/// grillage, and everything above this class is meant to work unchanged. That
/// is also what <see cref="IsPresent(int, int)"/> is for. Nothing is absent
/// from a grid over a whole surface; a grid clipped to a trimmed surface has
/// positions that fall in an opening or outside the trimmed edge, and those are
/// simply not there — no node, and no member to or from one.
/// </para>
/// </summary>
public sealed class Lattice
{
    private readonly Point3d[] _nodes;
    private readonly bool[] _present;

    /// <param name="present">
    /// Which positions have a node, or null for all of them. Absent positions
    /// keep their place in <see cref="Nodes"/> — a row is still a row — so
    /// nothing indexed by position has to be renumbered around a hole.
    /// </param>
    internal Lattice(Point3d[] nodes, int countU, int countV, bool wrapU, bool wrapV, bool[]? present = null)
    {
        _nodes = nodes;
        _present = present ?? Enumerable.Repeat(true, nodes.Length).ToArray();

        CountU = countU;
        CountV = countV;
        WrapU = wrapU;
        WrapV = wrapV;

        Nodes = Array.AsReadOnly(_nodes);
    }

    /// <summary>Distinct columns of nodes. One more than <see cref="PanelsU"/>, unless the grid wraps.</summary>
    public int CountU { get; }

    /// <summary>Distinct rows of nodes. One more than <see cref="PanelsV"/>, unless the grid wraps.</summary>
    public int CountV { get; }

    /// <summary>
    /// True when the grid closes on itself in U — round a tower, say — so the
    /// column after the last <em>is</em> the first. There is then no edge on
    /// either side in that direction, and no second copy of the seam's nodes.
    /// </summary>
    public bool WrapU { get; }

    /// <summary>As <see cref="WrapU"/>, in V.</summary>
    public bool WrapV { get; }

    public int PanelsU => WrapU ? CountU : CountU - 1;
    public int PanelsV => WrapV ? CountV : CountV - 1;

    /// <summary>
    /// Every node, row by row: the node at (i, j) is item <see cref="Index"/>
    /// (i, j). Members refer to nodes by these indices.
    /// </summary>
    public IReadOnlyList<Point3d> Nodes { get; }

    /// <summary>
    /// Index of the node at column <paramref name="i"/>, row
    /// <paramref name="j"/>. In a direction that wraps, a position past the end
    /// comes back round, so a pattern can step to <c>i + 1</c> without asking
    /// whether it has reached the seam.
    /// </summary>
    public int Index(int i, int j)
    {
        if (WrapU) i = ((i % CountU) + CountU) % CountU;
        if (WrapV) j = ((j % CountV) + CountV) % CountV;

        if (i < 0 || i >= CountU) throw new ArgumentOutOfRangeException(nameof(i));
        if (j < 0 || j >= CountV) throw new ArgumentOutOfRangeException(nameof(j));

        return j * CountU + i;
    }

    public Point3d Node(int i, int j) => _nodes[Index(i, j)];

    /// <summary>Whether there is a node at this position. Always, for a grid over a whole surface.</summary>
    public bool IsPresent(int i, int j) => _present[Index(i, j)];

    /// <summary>As <see cref="IsPresent(int, int)"/>, by index into <see cref="Nodes"/>.</summary>
    public bool IsPresent(int index) => _present[index];

    /// <summary>How many positions have no node: zero unless the grid was clipped.</summary>
    public int AbsentCount => _present.Count(p => !p);

    /// <summary>
    /// Node indices along grid line <paramref name="j"/> in the U direction, in
    /// order. Where the grid wraps, the first index is repeated at the end, so
    /// consecutive pairs are always the line's segments.
    /// </summary>
    public IReadOnlyList<int> LineU(int j)
        => Enumerable.Range(0, PanelsU + 1).Select(i => Index(i, j)).ToArray();

    /// <summary>As <see cref="LineU"/>, for grid line <paramref name="i"/> in the V direction.</summary>
    public IReadOnlyList<int> LineV(int i)
        => Enumerable.Range(0, PanelsV + 1).Select(j => Index(i, j)).ToArray();

    /// <summary>Every cell with all four corners present, row by row.</summary>
    public IEnumerable<LatticeCell> Cells
    {
        get
        {
            for (int j = 0; j < PanelsV; j++)
                for (int i = 0; i < PanelsU; i++)
                {
                    if (!IsPresent(i, j) || !IsPresent(i + 1, j)
                        || !IsPresent(i + 1, j + 1) || !IsPresent(i, j + 1))
                        continue;

                    yield return new LatticeCell(i, j, new[]
                    {
                        Index(i, j), Index(i + 1, j), Index(i + 1, j + 1), Index(i, j + 1),
                    });
                }
        }
    }

    /// <summary>
    /// Area of a cell, as the two triangles either side of its first diagonal.
    /// Exact for a flat cell; for a warped one it is the area of that fold,
    /// which is as much as four points can say.
    /// </summary>
    public double Area(LatticeCell cell)
    {
        Point3d a = _nodes[cell.Corners[0]], b = _nodes[cell.Corners[1]];
        Point3d c = _nodes[cell.Corners[2]], d = _nodes[cell.Corners[3]];

        return 0.5 * (Vector3d.CrossProduct(b - a, c - a).Length + Vector3d.CrossProduct(c - a, d - a).Length);
    }
}
