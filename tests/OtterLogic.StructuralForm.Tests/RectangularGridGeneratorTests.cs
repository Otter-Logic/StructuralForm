using OtterLogic.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

[Collection(RhinoCollection.Name)]
public class RectangularGridGeneratorTests
{
    private static readonly RectangularGridOptions ThreeByTwo = new()
    {
        XSpacings = new[] { 6.0, 6.0, 8.0 },
        YSpacings = new[] { 7.0, 9.0 },
    };

    private static void AssertPoint(Point3d expected, Point3d actual)
        => Assert.True(expected.DistanceTo(actual) < 1e-9, $"expected {expected}, got {actual}");

    // ---- the basic case -----------------------------------------------------

    [Fact]
    public void One_more_gridline_than_bays_each_way_and_a_node_at_every_crossing()
    {
        RectangularGrid grid = RectangularGridGenerator.Generate(ThreeByTwo);

        Assert.Equal(4, grid.XGridlines.Count);
        Assert.Equal(3, grid.YGridlines.Count);
        Assert.Equal(7, grid.Gridlines.Count);
        Assert.Equal(12, grid.Nodes.Count);
        Assert.Equal(4, grid.Rows.Count);
        Assert.All(grid.Rows, row => Assert.Equal(3, row.Count));
    }

    [Fact]
    public void Offsets_are_the_running_total_of_the_spacings()
    {
        RectangularGrid grid = RectangularGridGenerator.Generate(ThreeByTwo);

        Assert.Equal(new[] { 0.0, 6.0, 12.0, 20.0 }, grid.XOffsets);
        Assert.Equal(new[] { 0.0, 7.0, 16.0 }, grid.YOffsets);
        Assert.Equal(20.0, grid.Width);
        Assert.Equal(16.0, grid.Depth);
    }

    [Fact]
    public void X_gridlines_run_the_depth_in_Y_and_Y_gridlines_run_the_width_in_X()
    {
        RectangularGrid grid = RectangularGridGenerator.Generate(ThreeByTwo);

        // The third X gridline sits at x = 12 and runs from y = 0 to y = 16.
        AssertPoint(new Point3d(12, 0, 0), grid.XGridlines[2].From);
        AssertPoint(new Point3d(12, 16, 0), grid.XGridlines[2].To);

        // The second Y gridline sits at y = 7 and runs from x = 0 to x = 20.
        AssertPoint(new Point3d(0, 7, 0), grid.YGridlines[1].From);
        AssertPoint(new Point3d(20, 7, 0), grid.YGridlines[1].To);
    }

    [Fact]
    public void Rows_index_the_grid_and_nodes_flatten_them_by_X_then_Y()
    {
        RectangularGrid grid = RectangularGridGenerator.Generate(ThreeByTwo);

        AssertPoint(new Point3d(12, 7, 0), grid.Rows[2][1]);

        // Same order Grid Columns sorts its crossings into.
        AssertPoint(new Point3d(0, 0, 0), grid.Nodes[0]);
        AssertPoint(new Point3d(0, 7, 0), grid.Nodes[1]);
        AssertPoint(new Point3d(0, 16, 0), grid.Nodes[2]);
        AssertPoint(new Point3d(6, 0, 0), grid.Nodes[3]);
        AssertPoint(new Point3d(20, 16, 0), grid.Nodes[11]);
    }

    [Fact]
    public void The_nodes_are_where_the_gridlines_cross()
    {
        RectangularGrid grid = RectangularGridGenerator.Generate(ThreeByTwo);

        // Grid Columns reads gridlines as curves and finds the crossings for
        // itself; it has to find exactly the nodes this grid says it has.
        GridColumns columns = GridColumnsGenerator.Generate(
            grid.Gridlines.Select(line => (Curve)new LineCurve(line)),
            new GridColumnsOptions { Base = 0, Top = 3 });

        Assert.Equal(grid.Nodes.Count, columns.Crossings.Count);
        for (int i = 0; i < grid.Nodes.Count; i++)
            AssertPoint(grid.Nodes[i], columns.Crossings[i]);
    }

    // ---- overhang and plane -------------------------------------------------

    [Fact]
    public void Overhang_extends_every_gridline_past_the_outer_ones_but_moves_no_node()
    {
        RectangularGrid plain = RectangularGridGenerator.Generate(ThreeByTwo);
        RectangularGrid grid = RectangularGridGenerator.Generate(ThreeByTwo with { Overhang = 1.5 });

        AssertPoint(new Point3d(12, -1.5, 0), grid.XGridlines[2].From);
        AssertPoint(new Point3d(12, 17.5, 0), grid.XGridlines[2].To);
        AssertPoint(new Point3d(-1.5, 7, 0), grid.YGridlines[1].From);
        AssertPoint(new Point3d(21.5, 7, 0), grid.YGridlines[1].To);

        Assert.Equal(plain.Nodes.Count, grid.Nodes.Count);
        for (int i = 0; i < plain.Nodes.Count; i++)
            AssertPoint(plain.Nodes[i], grid.Nodes[i]);
    }

    [Fact]
    public void The_grid_follows_the_plane()
    {
        // Origin at (10, 20, 5), X axis turned 90 degrees to world Y.
        var plane = new Plane(new Point3d(10, 20, 5), Vector3d.YAxis, -Vector3d.XAxis);

        RectangularGrid grid = RectangularGridGenerator.Generate(ThreeByTwo with { Plane = plane });

        AssertPoint(new Point3d(10, 20, 5), grid.Rows[0][0]);
        AssertPoint(new Point3d(10, 26, 5), grid.Rows[1][0]);    // 6 along plane X = world +Y
        AssertPoint(new Point3d(3, 20, 5), grid.Rows[0][1]);     // 7 along plane Y = world -X
    }

    [Fact]
    public void A_single_bay_each_way_is_a_rectangle()
    {
        RectangularGrid grid = RectangularGridGenerator.Generate(new RectangularGridOptions
        {
            XSpacings = new[] { 5.0 },
            YSpacings = new[] { 3.0 },
        });

        Assert.Equal(4, grid.Gridlines.Count);
        Assert.Equal(4, grid.Nodes.Count);
    }

    // ---- refusals -----------------------------------------------------------

    [Fact]
    public void A_grid_needs_a_bay_each_way()
    {
        Assert.Throws<ArgumentException>(() => RectangularGridGenerator.Generate(
            ThreeByTwo with { YSpacings = Array.Empty<double>() }));
        Assert.Throws<ArgumentException>(() => RectangularGridGenerator.Generate(
            ThreeByTwo with { XSpacings = Array.Empty<double>() }));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-6.0)]
    [InlineData(0.005)]
    [InlineData(double.NaN)]
    public void A_bay_no_wider_than_the_tolerance_is_refused(double bad)
    {
        Assert.Throws<ArgumentException>(() => RectangularGridGenerator.Generate(
            ThreeByTwo with { XSpacings = new[] { 6.0, bad } }));
    }

    [Fact]
    public void A_negative_overhang_is_refused()
    {
        Assert.Throws<ArgumentException>(() => RectangularGridGenerator.Generate(
            ThreeByTwo with { Overhang = -1.0 }));
    }
}
