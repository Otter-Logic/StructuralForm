using OtterLogic.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

[Collection(RhinoCollection.Name)]
public class GridColumnsGeneratorTests
{
    private static Curve Gridline(double x0, double y0, double x1, double y1, double z = 0.0)
        => new LineCurve(new Point3d(x0, y0, z), new Point3d(x1, y1, z));

    /// <summary>A rectangular grid: lines at every x running the full depth, and at every y running the full width.</summary>
    private static List<Curve> Grid(double[] xs, double[] ys)
    {
        var lines = new List<Curve>();
        lines.AddRange(xs.Select(x => Gridline(x, ys[0], x, ys[^1])));
        lines.AddRange(ys.Select(y => Gridline(xs[0], y, xs[^1], y)));
        return lines;
    }

    private static readonly GridColumnsOptions Storey = new() { Base = 0.0, Top = 3.5 };

    // ---- the basic case -----------------------------------------------------

    [Fact]
    public void A_column_stands_at_the_crossing_of_two_gridlines()
    {
        GridColumns result = GridColumnsGenerator.Generate(
            new[] { Gridline(-5, 0, 5, 0), Gridline(0, -5, 0, 5) }, Storey);

        Point3d crossing = Assert.Single(result.Crossings);
        Assert.Equal(Point3d.Origin, crossing);

        Line column = Assert.Single(result.Columns);
        Assert.Equal(new Point3d(0, 0, 0.0), column.From);
        Assert.Equal(new Point3d(0, 0, 3.5), column.To);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void A_grid_gets_a_column_at_every_crossing_in_position_order()
    {
        GridColumns result = GridColumnsGenerator.Generate(
            Grid(new[] { 0.0, 6.0, 12.0 }, new[] { 0.0, 8.0, 16.0 }), Storey);

        Assert.Equal(9, result.Columns.Count);
        Assert.Equal(9, result.Crossings.Count);

        // Ordered by X then Y, so the first three are the x = 0 gridline bottom to top.
        Assert.Equal(new Point3d(0, 0, 0), result.Crossings[0]);
        Assert.Equal(new Point3d(0, 8, 0), result.Crossings[1]);
        Assert.Equal(new Point3d(0, 16, 0), result.Crossings[2]);
        Assert.Equal(new Point3d(12, 16, 0), result.Crossings[8]);

        Assert.All(result.Columns, c => Assert.Equal(3.5, c.Length, 9));
        Assert.Equal(9 * 3.5, result.TotalLength, 9);
    }

    [Fact]
    public void Pick_order_does_not_change_the_result()
    {
        List<Curve> grid = Grid(new[] { 0.0, 6.0 }, new[] { 0.0, 8.0 });
        var reversed = Enumerable.Reverse(grid).ToList();

        GridColumns forward = GridColumnsGenerator.Generate(grid, Storey);
        GridColumns backward = GridColumnsGenerator.Generate(reversed, Storey);

        Assert.Equal(forward.Crossings, backward.Crossings);
    }

    // ---- the grid is read in plan -------------------------------------------

    [Fact]
    public void Gridlines_at_different_heights_still_cross()
    {
        // One traced at ground, one off a plan at the third storey.
        GridColumns result = GridColumnsGenerator.Generate(
            new[] { Gridline(-5, 0, 5, 0, z: 0.0), Gridline(0, -5, 0, 5, z: 10.5) },
            new GridColumnsOptions { Base = 1.0, Top = 4.0 });

        Line column = Assert.Single(result.Columns);
        Assert.Equal(new Point3d(0, 0, 1.0), column.From);
        Assert.Equal(new Point3d(0, 0, 4.0), column.To);

        // The plan is what was crossed, and it sits at zero.
        Assert.All(result.Plan, p => Assert.Equal(0.0, p.PointAtStart.Z, 9));
        Assert.All(result.Plan, p => Assert.Equal(0.0, p.PointAtEnd.Z, 9));
    }

    [Fact]
    public void A_vertical_curve_is_ignored_and_said_so()
    {
        var curves = new List<Curve>(Grid(new[] { 0.0, 6.0 }, new[] { 0.0, 8.0 }))
        {
            new LineCurve(new Point3d(3, 4, 0), new Point3d(3, 4, 3)),   // a column already there
        };

        GridColumns result = GridColumnsGenerator.Generate(curves, Storey);

        Assert.Equal(4, result.Columns.Count);
        Assert.Equal(1, result.PlumbCurves);
        Assert.Contains(result.Notes, n => n.Level == FormNoteLevel.Remark && n.Message.Contains("vertical"));
    }

    // ---- crossings are merged -----------------------------------------------

    [Fact]
    public void Three_gridlines_through_one_point_are_one_column()
    {
        GridColumns result = GridColumnsGenerator.Generate(
            new[] { Gridline(-5, 0, 5, 0), Gridline(0, -5, 0, 5), Gridline(-5, -5, 5, 5) }, Storey);

        Assert.Single(result.Columns);
    }

    [Fact]
    public void A_gridline_ending_on_another_is_a_crossing()
    {
        GridColumns result = GridColumnsGenerator.Generate(
            new[] { Gridline(0, 0, 10, 0), Gridline(4, 0, 4, 6) }, Storey);

        Point3d crossing = Assert.Single(result.Crossings);
        Assert.Equal(new Point3d(4, 0, 0), crossing);
    }

    [Fact]
    public void An_arc_crossing_a_line_twice_gets_two_columns()
    {
        var arc = new ArcCurve(new Arc(new Point3d(-6, 0, 0), new Point3d(0, 6, 0), new Point3d(6, 0, 0)));

        GridColumns result = GridColumnsGenerator.Generate(new Curve[] { arc, Gridline(-10, 3, 10, 3) }, Storey);

        Assert.Equal(2, result.Columns.Count);
        Assert.All(result.Crossings, c => Assert.Equal(3.0, c.Y, 6));
        Assert.Equal(-result.Crossings[0].X, result.Crossings[1].X, 6);
    }

    // ---- what is handed back ------------------------------------------------

    [Fact]
    public void Parallel_gridlines_give_nothing_and_a_warning()
    {
        GridColumns result = GridColumnsGenerator.Generate(
            new[] { Gridline(0, 0, 10, 0), Gridline(0, 5, 10, 5) }, Storey);

        Assert.Empty(result.Columns);
        Assert.Empty(result.Crossings);
        Assert.Contains(result.Notes, n => n.Level == FormNoteLevel.Warning && n.Message.Contains("No crossings"));
    }

    [Fact]
    public void A_gridline_picked_twice_is_counted_and_not_stood_on()
    {
        var curves = new List<Curve>(Grid(new[] { 0.0, 6.0 }, new[] { 0.0, 8.0 }))
        {
            Gridline(0, 0, 0, 8),   // the x = 0 gridline again
        };

        GridColumns result = GridColumnsGenerator.Generate(curves, Storey);

        Assert.Equal(4, result.Columns.Count);
        Assert.Equal(1, result.OverlappingPairs);
        Assert.Contains(result.Notes, n => n.Level == FormNoteLevel.Warning && n.Message.Contains("same line"));
    }

    [Fact]
    public void Equal_heights_find_the_crossings_and_place_nothing()
    {
        GridColumns result = GridColumnsGenerator.Generate(
            Grid(new[] { 0.0, 6.0 }, new[] { 0.0, 8.0 }), new GridColumnsOptions { Base = 2.0, Top = 2.0 });

        Assert.Equal(4, result.Crossings.Count);
        Assert.Empty(result.Columns);
        Assert.Contains(result.Notes, n => n.Message.Contains("same height"));
    }

    [Fact]
    public void A_top_below_the_base_is_a_column_drawn_downward()
    {
        GridColumns result = GridColumnsGenerator.Generate(
            new[] { Gridline(-5, 0, 5, 0), Gridline(0, -5, 0, 5) },
            new GridColumnsOptions { Base = 0.0, Top = -12.0 });

        Line pile = Assert.Single(result.Columns);
        Assert.Equal(0.0, pile.From.Z, 9);
        Assert.Equal(-12.0, pile.To.Z, 9);
        Assert.Empty(result.Notes);
    }

    // ---- validation ---------------------------------------------------------

    [Fact]
    public void Tolerance_has_to_be_positive()
    {
        Assert.Throws<ArgumentException>(() => GridColumnsGenerator.Generate(
            Grid(new[] { 0.0, 6.0 }, new[] { 0.0, 8.0 }), new GridColumnsOptions { Top = 3.0, Tolerance = 0.0 }));
    }

    [Fact]
    public void Heights_have_to_be_finite()
    {
        Assert.Throws<ArgumentException>(() => GridColumnsGenerator.Generate(
            Grid(new[] { 0.0, 6.0 }, new[] { 0.0, 8.0 }), new GridColumnsOptions { Top = double.NaN }));
    }

    [Fact]
    public void A_null_gridline_is_refused()
    {
        Assert.Throws<ArgumentException>(() => GridColumnsGenerator.Generate(
            new Curve?[] { Gridline(0, 0, 1, 0), null }!, Storey));
    }

    [Fact]
    public void Results_are_read_only()
    {
        GridColumns result = GridColumnsGenerator.Generate(
            Grid(new[] { 0.0, 6.0 }, new[] { 0.0, 8.0 }), Storey);

        Assert.Throws<NotSupportedException>(() => ((IList<Line>)result.Columns).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Point3d>)result.Crossings).Clear());
    }
}
