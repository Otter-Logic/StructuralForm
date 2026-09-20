using OtterLogic.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

[Collection(RhinoCollection.Name)]
public class BeamInfillGeneratorTests
{
    private static Curve Beam(double x0, double y0, double x1, double y1, double z = 0.0)
        => new LineCurve(new Point3d(x0, y0, z), new Point3d(x1, y1, z));

    private static Curve Beam(Point3d from, Point3d to) => new LineCurve(from, to);

    /// <summary>Four beams round a bay, corner to corner.</summary>
    private static List<Curve> Bay(double width, double depth) => new()
    {
        Beam(0, 0, width, 0),
        Beam(width, 0, width, depth),
        Beam(width, depth, 0, depth),
        Beam(0, depth, 0, 0),
    };

    /// <summary>
    /// A floor the way it is usually drawn: gridlines running the full length,
    /// crossing each other, with nothing split at the intersections.
    /// </summary>
    private static List<Curve> Grid(double[] xs, double[] ys)
    {
        var beams = new List<Curve>();
        beams.AddRange(xs.Select(x => Beam(x, ys[0], x, ys[^1])));
        beams.AddRange(ys.Select(y => Beam(xs[0], y, xs[^1], y)));
        return beams;
    }

    private static bool RunsAlongX(Line line) => Math.Abs(line.Direction.Y) < 1e-6;
    private static bool RunsAlongY(Line line) => Math.Abs(line.Direction.X) < 1e-6;

    // ---- one panel ----------------------------------------------------------

    [Fact]
    public void Members_run_the_long_way_across_a_panel()
    {
        BeamInfill infill = BeamInfillGenerator.Generate(Bay(9, 6), new BeamInfillOptions { Divisions = 3 });

        BeamPanel panel = Assert.Single(infill.Panels);
        Assert.Equal(2, panel.Members.Count);                     // three bays, two members
        Assert.True(panel.AlongLongerSide);

        Assert.All(panel.Members, m => Assert.True(RunsAlongX(m)));
        Assert.All(panel.Members, m => Assert.Equal(9.0, m.Length, 6));

        var ys = panel.Members.Select(m => m.From.Y).OrderBy(y => y).ToArray();
        Assert.Equal(2.0, ys[0], 6);
        Assert.Equal(4.0, ys[1], 6);
    }

    [Fact]
    public void Flip_runs_them_the_short_way()
    {
        BeamInfill infill = BeamInfillGenerator.Generate(
            Bay(9, 6), new BeamInfillOptions { Divisions = 3, Flip = true });

        BeamPanel panel = Assert.Single(infill.Panels);
        Assert.False(panel.AlongLongerSide);

        Assert.All(panel.Members, m => Assert.True(RunsAlongY(m)));
        Assert.All(panel.Members, m => Assert.Equal(6.0, m.Length, 6));

        var xs = panel.Members.Select(m => m.From.X).OrderBy(x => x).ToArray();
        Assert.Equal(3.0, xs[0], 6);
        Assert.Equal(6.0, xs[1], 6);
    }

    [Fact]
    public void Spacing_divides_the_sides_the_members_land_on()
    {
        // Long way is X, so the members land on the two 6 m sides: 6 / 2 = 3 bays.
        BeamInfill infill = BeamInfillGenerator.Generate(Bay(9, 6), new BeamInfillOptions { Spacing = 2.0 });

        Assert.Equal(3, infill.Panels[0].Divisions);
        Assert.Equal(2, infill.Panels[0].Members.Count);
    }

    [Fact]
    public void Divisions_win_over_spacing()
    {
        BeamInfill infill = BeamInfillGenerator.Generate(
            Bay(9, 6), new BeamInfillOptions { Divisions = 2, Spacing = 0.5 });

        Assert.Single(infill.Panels[0].Members);
    }

    [Fact]
    public void Members_pair_with_their_nodes_by_index()
    {
        BeamPanel panel = BeamInfillGenerator
            .Generate(Bay(9, 6), new BeamInfillOptions { Divisions = 4 }).Panels[0];

        for (int i = 0; i < panel.Members.Count; i++)
        {
            Assert.Equal(panel.Members[i].From, panel.StartNodes[i]);
            Assert.Equal(panel.Members[i].To, panel.EndNodes[i]);
        }
    }

    [Fact]
    public void With_nothing_driving_the_division_panels_are_found_but_left_empty()
    {
        BeamInfill infill = BeamInfillGenerator.Generate(Bay(9, 6));

        Assert.Single(infill.Panels);
        Assert.Empty(infill.Members);
        Assert.Contains(infill.Notes, n => n.Message.Contains("Divisions or Spacing"));
    }

    /// <summary>
    /// Neither way is long in a square, so every square has to fall the same
    /// way or a floor of them comes out chequered.
    /// </summary>
    [Fact]
    public void A_square_panel_runs_along_world_X()
    {
        BeamInfill infill = BeamInfillGenerator.Generate(Bay(6, 6), new BeamInfillOptions { Divisions = 2 });

        Assert.True(RunsAlongX(Assert.Single(infill.Members)));
    }

    // ---- a floor ------------------------------------------------------------

    [Fact]
    public void Crossing_gridlines_make_a_panel_of_every_bay()
    {
        BeamInfill infill = BeamInfillGenerator.Generate(
            Grid(new[] { 0.0, 8.0, 16.0 }, new[] { 0.0, 6.0, 12.0 }),
            new BeamInfillOptions { Divisions = 2 });

        Assert.Equal(4, infill.Panels.Count);
        Assert.Equal(4, infill.Members.Count());
        Assert.All(infill.Members, m => Assert.True(RunsAlongX(m)));
        Assert.All(infill.Members, m => Assert.Equal(8.0, m.Length, 6));
    }

    /// <summary>
    /// Two panels sharing a beam divide it the same way from either side, so
    /// their members arrive at one point along it, not two.
    /// </summary>
    [Fact]
    public void Nodes_on_a_shared_beam_are_merged()
    {
        BeamInfill infill = BeamInfillGenerator.Generate(
            Grid(new[] { 0.0, 8.0, 16.0 }, new[] { 0.0, 6.0, 12.0 }),
            new BeamInfillOptions { Divisions = 2 });

        Assert.Equal(6, infill.Nodes.Count);                       // eight member ends
    }

    [Fact]
    public void Each_panel_decides_its_own_long_way()
    {
        // One bay 9 wide by 6 deep beside one 4 wide by 6 deep.
        BeamInfill infill = BeamInfillGenerator.Generate(
            Grid(new[] { 0.0, 9.0, 13.0 }, new[] { 0.0, 6.0 }),
            new BeamInfillOptions { Divisions = 2 });

        Assert.Equal(2, infill.Panels.Count);
        Assert.Contains(infill.Members, RunsAlongX);
        Assert.Contains(infill.Members, RunsAlongY);
    }

    /// <summary>
    /// A column midway along a side splits the beam there, but two collinear
    /// beams are still one side of the panel.
    /// </summary>
    [Fact]
    public void A_side_made_of_two_beams_in_line_is_one_side()
    {
        var beams = new List<Curve>
        {
            Beam(0, 0, 4, 0),
            Beam(4, 0, 9, 0),
            Beam(9, 0, 9, 6),
            Beam(9, 6, 0, 6),
            Beam(0, 6, 0, 0),
        };

        BeamInfill infill = BeamInfillGenerator.Generate(beams, new BeamInfillOptions { Divisions = 3 });

        Assert.Single(infill.Panels);
        Assert.Equal(0, infill.IrregularPanels);
        Assert.Equal(2, infill.Members.Count());
    }

    [Fact]
    public void A_beam_stopping_short_inside_a_panel_does_not_break_it()
    {
        List<Curve> beams = Bay(9, 6);
        beams.Add(Beam(4, 0, 4, 2));                               // a stub off the bottom beam

        BeamInfill infill = BeamInfillGenerator.Generate(beams, new BeamInfillOptions { Divisions = 3 });

        Assert.Single(infill.Panels);
        Assert.Equal(2, infill.Members.Count());
    }

    // ---- shapes -------------------------------------------------------------

    /// <summary>
    /// A splayed bay, filled across the splay: both beams are divided evenly,
    /// so the members fan rather than staying parallel and none runs into a
    /// side.
    /// </summary>
    [Fact]
    public void Members_fan_in_a_splayed_panel()
    {
        var beams = new List<Curve>
        {
            Beam(0, 0, 12, 0),
            Beam(12, 0, 9, 6),
            Beam(9, 6, 3, 6),
            Beam(3, 6, 0, 0),
        };

        BeamInfill infill = BeamInfillGenerator.Generate(
            beams, new BeamInfillOptions { Divisions = 3, Flip = true });

        var members = Assert.Single(infill.Panels).Members
            .Select(m => m.From.Y < m.To.Y ? m : new Line(m.To, m.From))
            .OrderBy(m => m.From.X)
            .ToArray();

        Assert.Equal(2, members.Length);
        Assert.Equal(4.0, members[0].From.X, 6);                   // thirds of the 12 m beam
        Assert.Equal(5.0, members[0].To.X, 6);                     // thirds of the 6 m beam
        Assert.Equal(8.0, members[1].From.X, 6);
        Assert.Equal(7.0, members[1].To.X, 6);
    }

    /// <summary>
    /// The members land on the beams, not on the plane the panels were found
    /// on: purlins between pitched rafters sit on the rafters.
    /// </summary>
    [Fact]
    public void A_pitched_panel_is_filled_in_space()
    {
        var beams = new List<Curve>
        {
            Beam(new Point3d(0, 0, 0), new Point3d(9, 0, 0)),
            Beam(new Point3d(9, 0, 0), new Point3d(9, 6, 3)),
            Beam(new Point3d(9, 6, 3), new Point3d(0, 6, 3)),
            Beam(new Point3d(0, 6, 3), new Point3d(0, 0, 0)),
        };

        BeamInfill infill = BeamInfillGenerator.Generate(beams, new BeamInfillOptions { Divisions = 2 });

        Line purlin = Assert.Single(infill.Members);
        Assert.Equal(3.0, purlin.From.Y, 6);
        Assert.Equal(1.5, purlin.From.Z, 6);
        Assert.Equal(1.5, purlin.To.Z, 6);
        Assert.Equal(0, infill.LoosePanels);
    }

    [Fact]
    public void A_curved_edge_beam_is_one_side()
    {
        var beams = new List<Curve>
        {
            Beam(0, 0, 9, 0),
            Beam(9, 0, 9, 6),
            new ArcCurve(new Arc(new Point3d(9, 6, 0), new Point3d(4.5, 7, 0), new Point3d(0, 6, 0))),
            Beam(0, 6, 0, 0),
        };

        BeamInfill infill = BeamInfillGenerator.Generate(beams, new BeamInfillOptions { Divisions = 3 });

        Assert.Single(infill.Panels);
        Assert.Equal(2, infill.Members.Count());
    }

    /// <summary>
    /// An edge beam drawn as a polyline round a curve turns a little at every
    /// vertex. Those are not corners, or the panel would have seven sides.
    /// </summary>
    [Fact]
    public void A_faceted_edge_beam_is_one_side()
    {
        var beams = new List<Curve>
        {
            Beam(0, 0, 9, 0),
            Beam(9, 0, 9, 6),
            new PolylineCurve(new[]
            {
                new Point3d(9, 6, 0), new Point3d(6, 6.6, 0), new Point3d(3, 6.6, 0), new Point3d(0, 6, 0),
            }),
            Beam(0, 6, 0, 0),
        };

        BeamInfill infill = BeamInfillGenerator.Generate(beams, new BeamInfillOptions { Divisions = 3 });

        Assert.Single(infill.Panels);
        Assert.Equal(0, infill.IrregularPanels);
    }

    [Fact]
    public void A_panel_with_a_loop_of_beams_inside_it_is_handed_back_empty()
    {
        List<Curve> beams = Bay(12, 9);
        beams.AddRange(new[]
        {
            Beam(4, 3, 8, 3), Beam(8, 3, 8, 6), Beam(8, 6, 4, 6), Beam(4, 6, 4, 3),
        });

        BeamInfill infill = BeamInfillGenerator.Generate(beams, new BeamInfillOptions { Divisions = 3 });

        // The loop itself is a perfectly good panel; the ring round it is not.
        Assert.Single(infill.Panels);
        Assert.Equal(1, infill.PanelsWithOpenings);
        Assert.Single(infill.SkippedPanels);
    }

    [Fact]
    public void A_panel_without_four_sides_is_handed_back_empty()
    {
        var beams = new List<Curve> { Beam(0, 0, 8, 0), Beam(8, 0, 0, 6), Beam(0, 6, 0, 0) };

        BeamInfill infill = BeamInfillGenerator.Generate(beams, new BeamInfillOptions { Divisions = 3 });

        Assert.Empty(infill.Panels);
        Assert.Equal(1, infill.IrregularPanels);
        Assert.Single(infill.SkippedPanels);
        Assert.Contains(infill.Notes, n => n.Message.Contains("four sides"));
    }

    // ---- selections that are not quite a floor -----------------------------

    /// <summary>A window selection across a floor takes the columns with it.</summary>
    [Fact]
    public void Columns_swept_up_in_the_selection_are_ignored()
    {
        List<Curve> beams = Bay(9, 6);
        beams.Add(Beam(new Point3d(0, 0, 0), new Point3d(0, 0, 4)));
        beams.Add(Beam(new Point3d(9, 6, 0), new Point3d(9, 6, 4)));

        BeamInfill infill = BeamInfillGenerator.Generate(beams, new BeamInfillOptions { Divisions = 3 });

        Assert.Single(infill.Panels);
        Assert.Equal(2, infill.EdgeOnBeams);
        Assert.Contains(infill.Notes, n => n.Message.Contains("square to the floor"));
    }

    [Fact]
    public void Beams_that_enclose_nothing_say_so()
    {
        var beams = new List<Curve> { Beam(0, 0, 9, 0), Beam(9, 0, 9, 6), Beam(0, 6, 0, 0) };

        BeamInfill infill = BeamInfillGenerator.Generate(beams, new BeamInfillOptions { Divisions = 3 });

        Assert.Empty(infill.Panels);
        Assert.Contains(infill.Notes, n => n.Level == FormNoteLevel.Warning);
    }

    /// <summary>
    /// Beams that cross when seen from above but pass over one another: the
    /// signature of a selection that took in two levels.
    /// </summary>
    [Fact]
    public void Beams_that_cross_without_touching_are_reported()
    {
        var beams = new List<Curve>
        {
            Beam(-1, 0, 10, 0),
            Beam(-1, 6, 10, 6),
            Beam(0, -1, 0, 7, z: 1.0),
            Beam(9, -1, 9, 7, z: 1.0),
        };

        BeamInfill infill = BeamInfillGenerator.Generate(beams, new BeamInfillOptions { Divisions = 2 });

        Assert.Single(infill.Panels);
        Assert.Equal(1, infill.LoosePanels);
        Assert.Contains(infill.Notes, n => n.Message.Contains("without touching"));
    }

    [Fact]
    public void Nonsense_options_are_refused()
    {
        Assert.Throws<ArgumentException>(
            () => BeamInfillGenerator.Generate(Bay(9, 6), new BeamInfillOptions { Divisions = -1 }));
        Assert.Throws<ArgumentException>(
            () => BeamInfillGenerator.Generate(Bay(9, 6), new BeamInfillOptions { Spacing = -1.0 }));
        Assert.Throws<ArgumentException>(
            () => BeamInfillGenerator.Generate(Bay(9, 6), new BeamInfillOptions { CornerAngle = 0.0 }));
    }
}
