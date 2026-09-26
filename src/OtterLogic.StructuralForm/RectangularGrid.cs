using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// A rectangular structural grid: gridlines both ways and a node at every
/// crossing.
/// <para>
/// No <c>Notes</c>, unlike the other results here. Those say what quietly did
/// not work, and nothing here can: the inputs are a plane and some numbers,
/// every one is checked up front, and a grid that passes the checks is drawn
/// in full.
/// </para>
/// </summary>
public sealed class RectangularGrid
{
    internal RectangularGrid(
        List<Line> xGridlines,
        List<Line> yGridlines,
        List<IReadOnlyList<Point3d>> rows,
        List<double> xOffsets,
        List<double> yOffsets,
        RectangularGridOptions options)
    {
        XGridlines = xGridlines.AsReadOnly();
        YGridlines = yGridlines.AsReadOnly();
        Rows = rows.AsReadOnly();
        XOffsets = xOffsets.AsReadOnly();
        YOffsets = yOffsets.AsReadOnly();
        Options = options;

        var gridlines = new List<Line>(xGridlines.Count + yGridlines.Count);
        gridlines.AddRange(xGridlines);
        gridlines.AddRange(yGridlines);
        Gridlines = gridlines.AsReadOnly();

        var nodes = new List<Point3d>(xOffsets.Count * yOffsets.Count);
        foreach (IReadOnlyList<Point3d> row in rows)
            nodes.AddRange(row);
        Nodes = nodes.AsReadOnly();
    }

    /// <summary>
    /// The gridlines set out along X, one at each entry of
    /// <see cref="XOffsets"/>, each running the depth of the grid in Y from
    /// the first Y gridline to the last plus the overhang each end. Drawn
    /// from low Y to high Y.
    /// </summary>
    public IReadOnlyList<Line> XGridlines { get; }

    /// <summary>
    /// The gridlines set out along Y, each running the width of the grid in X.
    /// Drawn from low X to high X.
    /// </summary>
    public IReadOnlyList<Line> YGridlines { get; }

    /// <summary>
    /// Every gridline: <see cref="XGridlines"/> then <see cref="YGridlines"/>.
    /// For a front-end that wants them on one layer.
    /// </summary>
    public IReadOnlyList<Line> Gridlines { get; }

    /// <summary>
    /// The nodes, one list per X gridline: <c>Rows[i][j]</c> is where the
    /// <c>i</c>-th X gridline crosses the <c>j</c>-th Y gridline. The place
    /// in the grid is the place in the list, which is what a downstream tool
    /// setting out columns or labelling gridlines needs.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Point3d>> Rows { get; }

    /// <summary>
    /// Every node, <see cref="Rows"/> flattened: ordered by X gridline and
    /// then along it. The same order <see cref="GridColumns.Crossings"/> uses,
    /// so a grid fed straight to Grid Columns gives columns in this order.
    /// </summary>
    public IReadOnlyList<Point3d> Nodes { get; }

    /// <summary>
    /// Where each X gridline sits along the plane's X axis, from zero at the
    /// origin: the running total of <see cref="RectangularGridOptions.XSpacings"/>.
    /// </summary>
    public IReadOnlyList<double> XOffsets { get; }

    /// <summary>Where each Y gridline sits along the plane's Y axis.</summary>
    public IReadOnlyList<double> YOffsets { get; }

    /// <summary>The distance from the first X gridline to the last.</summary>
    public double Width => XOffsets[^1];

    /// <summary>The distance from the first Y gridline to the last.</summary>
    public double Depth => YOffsets[^1];

    public RectangularGridOptions Options { get; }
}
