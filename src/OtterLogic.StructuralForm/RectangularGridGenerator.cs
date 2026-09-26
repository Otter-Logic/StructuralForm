using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Sets out a rectangular structural grid from its bay spacings.
/// <para>
/// The whole engine. Both the Grasshopper component and the Rhino command call
/// <see cref="Generate"/> and nothing else, so the two front-ends cannot drift.
/// </para>
/// <para>
/// A grid is the running total of the spacings each way, a line at every total
/// running the full extent the other way, and a node wherever two lines cross.
/// That is a few lines of arithmetic, and the reason it is a tool at all is
/// that Rhino has no way to type <c>3x6000, 8000</c> and get the lines: it is
/// one <c>Line</c> per gridline, or an <c>Array</c> that can only repeat one
/// spacing. Grasshopper's own Rectangular grid repeats one spacing too, and
/// gives cells rather than gridlines.
/// </para>
/// <para>
/// No labels. A gridline is a line the user can name however their office
/// does; a grid object that carried its own labels would have to be kept in
/// step with every drawing that showed them.
/// </para>
/// </summary>
public static class RectangularGridGenerator
{
    public static RectangularGrid Generate(RectangularGridOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        if (!options.Plane.IsValid)
            throw new ArgumentException("The plane has to be valid.", nameof(options));
        if (options.Tolerance <= 0.0)
            throw new ArgumentException("Tolerance has to be greater than zero.", nameof(options));
        if (!double.IsFinite(options.Overhang) || options.Overhang < 0.0)
            throw new ArgumentException("Overhang has to be zero or more.", nameof(options));

        CheckSpacings(options.XSpacings, "X", options.Tolerance);
        CheckSpacings(options.YSpacings, "Y", options.Tolerance);

        List<double> xOffsets = Spacings.Offsets(options.XSpacings);
        List<double> yOffsets = Spacings.Offsets(options.YSpacings);

        double width = xOffsets[^1];
        double depth = yOffsets[^1];
        double over = options.Overhang;
        Plane plane = options.Plane;

        var xGridlines = new List<Line>(xOffsets.Count);
        foreach (double x in xOffsets)
            xGridlines.Add(new Line(plane.PointAt(x, -over), plane.PointAt(x, depth + over)));

        var yGridlines = new List<Line>(yOffsets.Count);
        foreach (double y in yOffsets)
            yGridlines.Add(new Line(plane.PointAt(-over, y), plane.PointAt(width + over, y)));

        var rows = new List<IReadOnlyList<Point3d>>(xOffsets.Count);
        foreach (double x in xOffsets)
        {
            var row = new List<Point3d>(yOffsets.Count);
            foreach (double y in yOffsets)
                row.Add(plane.PointAt(x, y));
            rows.Add(row.AsReadOnly());
        }

        return new RectangularGrid(xGridlines, yGridlines, rows, xOffsets, yOffsets, options);
    }

    /// <summary>
    /// Shared with the radial grid, whose rings are spacings too. A zero or
    /// negative bay is two gridlines in one place, and one under the tolerance
    /// is the same thing to the document.
    /// </summary>
    internal static void CheckSpacings(IReadOnlyList<double> spacings, string which, double tolerance)
    {
        if (spacings is null || spacings.Count == 0)
            throw new ArgumentException($"At least one {which} spacing is needed: a grid has to have a bay each way.", nameof(spacings));

        foreach (double spacing in spacings)
            if (!double.IsFinite(spacing) || spacing <= tolerance)
                throw new ArgumentException($"Every {which} spacing has to be greater than the tolerance ({tolerance:0.###}); {spacing:0.###} is not.", nameof(spacings));
    }
}
