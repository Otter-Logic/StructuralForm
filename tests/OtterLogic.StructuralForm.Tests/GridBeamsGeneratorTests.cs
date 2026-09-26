using OtterLogic.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

[Collection(RhinoCollection.Name)]
public class GridBeamsGeneratorTests
{
    private static Curve Gridline(double x0, double y0, double x1, double y1, double z = 0.0)
        => new LineCurve(new Point3d(x0, y0, z), new Point3d(x1, y1, z));

    private static Curve Column(double x, double y, double from = 0.0, double to = 3.5)
        => new LineCurve(new Point3d(x, y, from), new Point3d(x, y, to));

    /// <summary>A rectangular grid: lines at every x running the full depth, and at every y running the full width.</summary>
    private static List<Curve> Grid(double[] xs, double[] ys)
    {
        var lines = new List<Curve>();
        lines.AddRange(xs.Select(x => Gridline(x, ys[0], x, ys[^1])));
        lines.AddRange(ys.Select(y => Gridline(xs[0], y, xs[^1], y)));
        return lines;
    }

    private static readonly double[] Xs = { 0.0, 6.0, 12.0 };
    private static readonly double[] Ys = { 0.0, 8.0 };

    /// <summary>A column at every crossing of the 3 by 2 grid: six of them, of the given height.</summary>
    private static List<Curve> AllColumns(double to = 3.5)
        => Xs.SelectMany(x => Ys.Select(y => Column(x, y, 0.0, to))).ToList();

    private static readonly GridBeamsOptions AtTop = new() { Level = 3.5 };

    private static void AssertPoint(Point3d expected, Point3d actual)
        => Assert.True(expected.DistanceTo(actual) < 1e-6, $"expected {expected}, got {actual}");

    // ---- the basic case -----------------------------------------------------

    [Fact]
    public void A_beam_runs_along_a_gridline_between_two_columns_at_the_level()
    {
        GridBeams result = GridBeamsGenerator.Generate(
            new[] { Column(0, 0), Column(6, 0) }, new[] { Gridline(-2, 0, 20, 0) }, AtTop);

        Curve beam = Assert.Single(result.Beams);
        AssertPoint(new Point3d(0, 0, 3.5), beam.PointAtStart);
        AssertPoint(new Point3d(6, 0, 3.5), beam.PointAtEnd);

        // No cantilever past the last column, and no beam to the gridline's start.
        Assert.Equal(2, result.Nodes.Count);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void A_full_grid_gets_a_beam_between_every_pair_of_neighbouring_columns()
    {
        GridBeams result = GridBeamsGenerator.Generate(AllColumns(), Grid(Xs, Ys), AtTop);

        // Three X gridlines with two columns each: one beam apiece. Two Y
        // gridlines with three columns each: two beams apiece.
        Assert.Equal(3 + 4, result.Beams.Count);
        Assert.Equal(5, result.ByGridline.Count);
        Assert.Equal(new[] { 1, 1, 1, 2, 2 }, result.ByGridline.Select(g => g.Count));
        Assert.Equal(6, result.Nodes.Count);
        Assert.All(result.Beams, b => Assert.Equal(3.5, b.PointAtStart.Z, 9));
        Assert.All(result.Nodes, n => Assert.Equal(3.5, n.Z, 9));
        Assert.Empty(result.Notes);

        // Along the line in its own direction: the first Y gridline runs 0 to 12.
        AssertPoint(new Point3d(0, 0, 3.5), result.ByGridline[3][0].PointAtStart);
        AssertPoint(new Point3d(6, 0, 3.5), result.ByGridline[3][0].PointAtEnd);
        AssertPoint(new Point3d(12, 0, 3.5), result.ByGridline[3][1].PointAtEnd);
    }

    [Fact]
    public void A_crossing_with_no_column_gets_no_beam_through_it()
    {
        // The middle columns are missing, as round an atrium: the Y gridlines
        // then run one beam straight across from 0 to 12, and the middle X
        // gridline has nothing on it.
        List<Curve> columns = AllColumns().Where(c => Math.Abs(c.PointAtStart.X - 6) > 1e-9).ToList();

        GridBeams result = GridBeamsGenerator.Generate(columns, Grid(Xs, Ys), AtTop);

        Assert.Equal(2 + 2, result.Beams.Count);
        Assert.Empty(result.ByGridline[1]);
        Assert.Equal(12.0, result.ByGridline[3][0].GetLength(), 9);
        Assert.Equal(1, result.GridlinesWithOneColumn);
        Assert.Contains(result.Notes, n => n.Message.Contains("fewer than two columns"));
    }

    [Fact]
    public void Nodes_are_ordered_by_position_and_a_column_on_a_crossing_is_one_node()
    {
        GridBeams result = GridBeamsGenerator.Generate(AllColumns().AsEnumerable().Reverse(), Grid(Xs, Ys), AtTop);

        AssertPoint(new Point3d(0, 0, 3.5), result.Nodes[0]);
        AssertPoint(new Point3d(0, 8, 3.5), result.Nodes[1]);
        AssertPoint(new Point3d(12, 8, 3.5), result.Nodes[5]);
    }

    // ---- what a column is ---------------------------------------------------

    [Fact]
    public void Stacked_columns_are_one_position_and_a_column_short_of_the_level_is_said()
    {
        // Two storeys of columns picked together, with beams asked for at the
        // top of the second storey. The lower storey stops short; the upper
        // reaches. Neither position is short, because one column of each
        // stack reaches.
        var columns = new List<Curve>
        {
            Column(0, 0, 0, 3.5), Column(0, 0, 3.5, 7.0),
            Column(6, 0, 0, 3.5), Column(6, 0, 3.5, 7.0),
        };

        GridBeams result = GridBeamsGenerator.Generate(columns, new[] { Gridline(0, 0, 6, 0) }, new GridBeamsOptions { Level = 7.0 });

        Assert.Single(result.Beams);
        Assert.Equal(0, result.ColumnsShortOfLevel);

        // The lower storey alone: the beams are drawn at 7 anyway, and it is said.
        GridBeams lower = GridBeamsGenerator.Generate(
            new[] { Column(0, 0, 0, 3.5), Column(6, 0, 0, 3.5) }, new[] { Gridline(0, 0, 6, 0) }, new GridBeamsOptions { Level = 7.0 });

        Assert.Single(lower.Beams);
        Assert.Equal(7.0, lower.Beams[0].PointAtStart.Z, 9);
        Assert.Equal(2, lower.ColumnsShortOfLevel);
        Assert.Contains(lower.Notes, n => n.Level == FormNoteLevel.Warning && n.Message.Contains("not reach the level"));
    }

    [Fact]
    public void A_leaning_column_is_read_where_it_crosses_the_level()
    {
        // Leans 1 in X over its 3.5 rise: at 3.5 it is at x = 1.
        var leaning = new LineCurve(new Point3d(0, 0, 0), new Point3d(1, 0, 3.5));

        GridBeams result = GridBeamsGenerator.Generate(
            new[] { leaning, Column(6, 0) }, new[] { Gridline(-2, 0, 20, 0) }, AtTop);

        Curve beam = Assert.Single(result.Beams);
        AssertPoint(new Point3d(1, 0, 3.5), beam.PointAtStart);
        Assert.Equal(5.0, beam.GetLength(), 9);
    }

    [Fact]
    public void A_beam_or_brace_picked_as_a_column_is_left_out_and_said()
    {
        var beamNotColumn = new LineCurve(new Point3d(0, 0, 3.5), new Point3d(6, 0, 3.5));

        GridBeams result = GridBeamsGenerator.Generate(
            new[] { Column(0, 0), Column(6, 0), beamNotColumn }, new[] { Gridline(0, 0, 6, 0) }, AtTop);

        Assert.Single(result.Beams);
        Assert.Equal(1, result.NotColumns);
        Assert.Contains(result.Notes, n => n.Message.Contains("not a column"));
    }

    // ---- columns and gridlines that do not meet -------------------------------

    [Fact]
    public void A_column_on_no_gridline_is_counted_and_the_reach_can_pick_it_up()
    {
        // The second column sits 0.02 off the line: beyond the tolerance,
        // within a reach of 0.05.
        var columns = new[] { Column(0, 0), Column(6, 0.02) };
        var gridline = new[] { Gridline(0, 0, 6, 0) };

        GridBeams strict = GridBeamsGenerator.Generate(columns, gridline, AtTop);
        Assert.Empty(strict.Beams);
        Assert.Equal(1, strict.ColumnsOffGrid);
        Assert.Contains(strict.Notes, n => n.Message.Contains("none of the gridlines"));
        Assert.Contains(strict.Notes, n => n.Message.Contains("No beams were drawn"));

        GridBeams reached = GridBeamsGenerator.Generate(columns, gridline, AtTop with { Reach = 0.05 });
        Curve beam = Assert.Single(reached.Beams);
        Assert.Equal(0, reached.ColumnsOffGrid);

        // The beam and its node are on the gridline, not at the column: the
        // gridline is the truth of where the beam runs.
        AssertPoint(new Point3d(6, 0, 3.5), beam.PointAtEnd);
    }

    [Fact]
    public void Gridlines_at_any_height_are_read_in_plan_and_a_vertical_one_is_ignored()
    {
        var gridlines = new[] { Gridline(0, 0, 6, 0, z: 12.0), Column(3, 3) };

        GridBeams result = GridBeamsGenerator.Generate(new[] { Column(0, 0), Column(6, 0) }, gridlines, AtTop);

        Assert.Single(result.Beams);
        Assert.Single(result.Plan);
        Assert.Equal(1, result.PlumbGridlines);
    }

    [Fact]
    public void An_arc_gridline_gives_arc_beams()
    {
        var ring = new ArcCurve(new Circle(Plane.WorldXY, 10.0));
        var columns = new[] { Column(10, 0), Column(0, 10), Column(-10, 0) };

        GridBeams result = GridBeamsGenerator.Generate(columns, new Curve[] { ring }, AtTop);

        Assert.Equal(2, result.Beams.Count);
        Assert.All(result.Beams, b =>
        {
            Assert.True(b.IsArc());
            Assert.Equal(Math.PI * 10 / 2, b.GetLength(), 6);
            Assert.Equal(3.5, b.PointAtStart.Z, 9);
        });
    }

    // ---- on a surface -------------------------------------------------------

    [Fact]
    public void Beams_are_projected_onto_a_surface_and_follow_it_between_columns()
    {
        // A roof rising at 1 in 6 along X, z = 4 + x / 6, overhanging the
        // grid a little each way so no beam end sits on its edge.
        Brep roof = NurbsSurface.CreateFromCorners(
            new Point3d(-1, -1, 4 - 1.0 / 6), new Point3d(13, -1, 4 + 13.0 / 6),
            new Point3d(13, 9, 4 + 13.0 / 6), new Point3d(-1, 9, 4 - 1.0 / 6)).ToBrep();

        // Columns tall enough to pass through the roof, so each is read
        // where it crosses it.
        GridBeams result = GridBeamsGenerator.Generate(AllColumns(to: 7.0), Grid(Xs, Ys), new GridBeamsOptions { Surface = roof, Level = 99.0 });

        Assert.Equal(7, result.Beams.Count);
        Assert.Empty(result.Notes);

        // A beam along y = 0 from x = 0 to x = 6 climbs from 4 to 5.
        Curve beam = result.ByGridline[3][0];
        AssertPoint(new Point3d(0, 0, 4), beam.PointAtStart);
        AssertPoint(new Point3d(6, 0, 5), beam.PointAtEnd);

        // Nodes sit on the surface too, and the level was ignored.
        Assert.Equal(6, result.Nodes.Count);
        AssertPoint(new Point3d(12, 8, 6), result.Nodes[5]);
        Assert.All(result.Nodes, n => Assert.InRange(n.Z, 4 - 1e-6, 6 + 1e-6));
    }

    [Fact]
    public void A_beam_the_surface_does_not_cover_is_left_out_and_said()
    {
        // A roof over the left bay only.
        Brep half = NurbsSurface.CreateFromCorners(
            new Point3d(-1, -1, 5), new Point3d(6, -1, 5), new Point3d(6, 9, 5), new Point3d(-1, 9, 5)).ToBrep();

        GridBeams result = GridBeamsGenerator.Generate(AllColumns(to: 7.0), Grid(Xs, Ys), new GridBeamsOptions { Surface = half });

        // Only the X gridlines at 0 and 6 and the left halves of the Y
        // gridlines lie under it: 2 + 2 beams. The projection of a piece
        // half under the roof would be the covered half; here the pieces
        // between columns are whole bays, so the right bay is whole too.
        Assert.Equal(4, result.Beams.Count);
        Assert.Equal(3, result.BeamsOffSurface);
        Assert.Contains(result.Notes, n => n.Message.Contains("outside the surface"));
    }

    // ---- refusals -----------------------------------------------------------

    [Fact]
    public void Nonsense_options_are_refused()
    {
        var columns = new[] { Column(0, 0) };
        var gridlines = new[] { Gridline(0, 0, 6, 0) };

        Assert.Throws<ArgumentException>(() => GridBeamsGenerator.Generate(columns, gridlines, new GridBeamsOptions { Tolerance = 0 }));
        Assert.Throws<ArgumentException>(() => GridBeamsGenerator.Generate(columns, gridlines, new GridBeamsOptions { Reach = -1 }));
        Assert.Throws<ArgumentException>(() => GridBeamsGenerator.Generate(columns, gridlines, new GridBeamsOptions { Level = double.NaN }));
        Assert.Throws<ArgumentNullException>(() => GridBeamsGenerator.Generate(null!, gridlines));
        Assert.Throws<ArgumentNullException>(() => GridBeamsGenerator.Generate(columns, null!));
    }
}
