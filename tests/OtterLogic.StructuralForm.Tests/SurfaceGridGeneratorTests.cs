using OtterLogic.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

[Collection(RhinoCollection.Name)]
public class SurfaceGridGeneratorTests
{
    /// <summary>A flat 12 by 8 rectangle: U runs along X, V along Y.</summary>
    private static Surface Flat(double width = 12.0, double depth = 8.0)
        => NurbsSurface.CreateFromCorners(
            new Point3d(0, 0, 0), new Point3d(width, 0, 0), new Point3d(width, depth, 0), new Point3d(0, depth, 0));

    private static Surface Tower(double radius = 5.0, double height = 20.0)
        => new Cylinder(new Circle(Plane.WorldXY, radius), height).ToNurbsSurface();

    private static SurfaceGridOptions Options(
        GridPattern pattern, int u = 4, int v = 2, DiagonalRule rule = DiagonalRule.OneWay, bool flip = false)
        => new() { Pattern = pattern, DivisionsU = u, DivisionsV = v, Diagonals = rule, Flip = flip };

    /// <summary>Every member must start and end on a node another member could share.</summary>
    private static void AssertMembersMeetOnlyAtNodes(SurfaceGrid grid)
    {
        var lines = grid.Members.Select(m => m.Line).ToList();

        for (int a = 0; a < lines.Count; a++)
            for (int b = a + 1; b < lines.Count; b++)
            {
                if (!Rhino.Geometry.Intersect.Intersection.LineLine(
                        lines[a], lines[b], out double ta, out double tb, 1e-6, finiteSegments: true))
                    continue;

                bool atEndOfA = ta < 1e-6 || ta > 1 - 1e-6;
                bool atEndOfB = tb < 1e-6 || tb > 1 - 1e-6;

                Assert.True(atEndOfA && atEndOfB, $"Members {a} and {b} cross away from their ends.");
            }
    }

    // ---- quad -----------------------------------------------------------------

    [Fact]
    public void A_quad_grid_has_a_member_along_every_grid_line()
    {
        SurfaceGrid grid = SurfaceGridGenerator.Generate(Flat(), Options(GridPattern.Quad));

        Assert.Equal(4, grid.PanelsU);
        Assert.Equal(2, grid.PanelsV);
        Assert.Equal(15, grid.Lattice.Nodes.Count);        // 5 by 3

        Assert.Equal(4, grid.MembersU.Count());            // one interior row of four
        Assert.Equal(6, grid.MembersV.Count());            // three interior columns of two
        Assert.Equal(12, grid.Edges.Count());              // 4 + 4 + 2 + 2
        Assert.Empty(grid.Diagonals);
    }

    [Fact]
    public void Nodes_are_addressed_by_position()
    {
        Lattice lattice = SurfaceGridGenerator.Generate(Flat(), Options(GridPattern.Quad)).Lattice;

        for (int j = 0; j <= 2; j++)
            for (int i = 0; i <= 4; i++)
            {
                Assert.Equal(i * 3.0, lattice.Node(i, j).X, 6);
                Assert.Equal(j * 4.0, lattice.Node(i, j).Y, 6);
            }
    }

    [Fact]
    public void Members_carry_indices_that_resolve_and_the_grid_line_they_lie_on()
    {
        SurfaceGrid grid = SurfaceGridGenerator.Generate(Flat(), Options(GridPattern.Quad));

        Assert.All(grid.Members, m =>
        {
            Assert.Equal(grid.Lattice.Nodes[m.StartNode], m.Line.From);
            Assert.Equal(grid.Lattice.Nodes[m.EndNode], m.Line.To);
        });

        // Row 1 put back together from its segments is the whole width.
        var row = grid.Members.Where(m => m.AlongU && m.GridLine == 1).ToList();

        Assert.Equal(4, row.Count);
        Assert.Equal(12.0, row.Sum(m => m.Length), 6);
        Assert.Equal(grid.Lattice.LineU(1), row.Select(m => m.StartNode).Append(row[^1].EndNode));
    }

    [Fact]
    public void Cells_have_corners_and_an_area_to_share_between_them()
    {
        Lattice lattice = SurfaceGridGenerator.Generate(Flat(), Options(GridPattern.Quad)).Lattice;

        Assert.Equal(8, lattice.Cells.Count());
        Assert.All(lattice.Cells, cell => Assert.Equal(12.0, lattice.Area(cell), 6));     // 3 by 4
        Assert.Equal(96.0, lattice.Cells.Sum(lattice.Area), 6);
    }

    [Fact]
    public void Spacing_sets_each_direction_on_its_own()
    {
        SurfaceGrid grid = SurfaceGridGenerator.Generate(
            Flat(), new SurfaceGridOptions { SpacingU = 2.0, SpacingV = 4.0 });

        Assert.Equal(6, grid.PanelsU);
        Assert.Equal(2, grid.PanelsV);
    }

    // ---- triangulated -------------------------------------------------------

    [Fact]
    public void Triangulated_is_the_quad_grid_with_a_diagonal_in_every_cell()
    {
        SurfaceGrid quad = SurfaceGridGenerator.Generate(Flat(), Options(GridPattern.Quad));
        SurfaceGrid tri = SurfaceGridGenerator.Generate(Flat(), Options(GridPattern.Triangulated));

        Assert.Equal(8, tri.Diagonals.Count());
        Assert.Equal(quad.Members.Count + 8, tri.Members.Count);

        // One way: every diagonal rises the same way across its cell.
        Assert.All(tri.Diagonals, d => Assert.True(d.Direction.X * d.Direction.Y > 0));
    }

    [Fact]
    public void Flip_takes_the_other_diagonal()
    {
        SurfaceGrid tri = SurfaceGridGenerator.Generate(Flat(), Options(GridPattern.Triangulated, flip: true));

        Assert.All(tri.Diagonals, d => Assert.True(d.Direction.X * d.Direction.Y < 0));
    }

    [Fact]
    public void Alternating_diagonals_turn_about_from_cell_to_cell()
    {
        SurfaceGrid tri = SurfaceGridGenerator.Generate(
            Flat(), Options(GridPattern.Triangulated, rule: DiagonalRule.Alternating));

        Assert.Equal(4, tri.Diagonals.Count(d => d.Direction.X * d.Direction.Y > 0));
        Assert.Equal(4, tri.Diagonals.Count(d => d.Direction.X * d.Direction.Y < 0));
    }

    [Fact]
    public void Shorter_picks_the_diagonal_that_folds_a_sheared_cell_least()
    {
        // A parallelogram leaning to the right: the back diagonal stands
        // upright, and is the short one.
        Surface sheared = NurbsSurface.CreateFromCorners(
            new Point3d(0, 0, 0), new Point3d(12, 0, 0), new Point3d(18, 8, 0), new Point3d(6, 8, 0));

        SurfaceGrid grid = SurfaceGridGenerator.Generate(
            sheared, Options(GridPattern.Triangulated, rule: DiagonalRule.Shorter));

        double shortest = grid.Diagonals.Min(d => d.Length);
        Assert.All(grid.Diagonals, d => Assert.Equal(shortest, d.Length, 6));
        Assert.All(grid.Diagonals, d => Assert.Equal(0.0, d.Direction.X, 6));
        Assert.Equal(4.0, shortest, 6);
    }

    // ---- diagrid --------------------------------------------------------------

    [Fact]
    public void A_diagrid_stands_on_every_other_node_and_its_members_never_cross()
    {
        SurfaceGrid grid = SurfaceGridGenerator.Generate(Flat(), Options(GridPattern.Diagrid));

        Assert.Equal(8, grid.Diagonals.Count());           // one per cell
        Assert.Empty(grid.MembersU);
        Assert.Empty(grid.MembersV);
        Assert.Equal(6, grid.Edges.Count());               // 2 + 2 along U, 1 + 1 along V

        Assert.Equal(8, grid.UsedNodes.Count);             // of the lattice's fifteen
        Assert.Contains(new Point3d(0, 0, 0), grid.UsedNodes);

        AssertMembersMeetOnlyAtNodes(grid);
    }

    [Fact]
    public void An_odd_count_is_raised_so_the_diagrid_closes()
    {
        SurfaceGrid grid = SurfaceGridGenerator.Generate(Flat(), Options(GridPattern.Diagrid, u: 5, v: 2));

        Assert.Equal(6, grid.PanelsU);
        Assert.Equal(2, grid.PanelsV);
        Assert.Equal(1, grid.RaisedU);
        Assert.Equal(0, grid.RaisedV);
        Assert.Contains(grid.Notes, n => n.Message.Contains("even number"));
    }

    [Fact]
    public void A_flipped_diagrid_moves_off_the_corners()
    {
        SurfaceGrid grid = SurfaceGridGenerator.Generate(Flat(), Options(GridPattern.Diagrid, flip: true));

        Assert.DoesNotContain(new Point3d(0, 0, 0), grid.UsedNodes);
        Assert.Equal(7, grid.UsedNodes.Count);
        AssertMembersMeetOnlyAtNodes(grid);
    }

    // ---- closed surfaces ----------------------------------------------------

    /// <summary>Round a tower there is a seam in the surface and none in the structure.</summary>
    [Fact]
    public void A_tower_wraps_with_no_edge_and_no_second_set_of_nodes_at_the_seam()
    {
        SurfaceGrid grid = SurfaceGridGenerator.Generate(Tower(), Options(GridPattern.Diagrid, u: 8, v: 4));

        Lattice lattice = grid.Lattice;
        bool roundU = lattice.WrapU;

        Assert.True(lattice.WrapU ^ lattice.WrapV);
        Assert.Equal(8 * 5, lattice.Nodes.Count);          // eight round, five rings, no doubled seam

        int round = roundU ? lattice.PanelsU : lattice.PanelsV;
        int up = roundU ? lattice.PanelsV : lattice.PanelsU;

        Assert.Equal(round * up, grid.Diagonals.Count());  // every cell, including the one over the seam
        Assert.Equal(8, grid.Edges.Count());               // top and bottom rings only: 4 + 4
        Assert.All(grid.Edges, e => Assert.Equal(0.0, e.Direction.Z, 6));

        // Every node has the same number of members whichever side of the seam it is on.
        var valence = grid.Members
            .SelectMany(m => new[] { m.StartNode, m.EndNode })
            .GroupBy(n => n)
            .Where(g => lattice.Nodes[g.Key].Z is > 1 and < 19)
            .Select(g => g.Count())
            .Distinct();

        Assert.Equal(new[] { 4 }, valence);
    }

    [Fact]
    public void Divisions_round_a_tower_are_even_in_length()
    {
        SurfaceGrid grid = SurfaceGridGenerator.Generate(Tower(), Options(GridPattern.Quad, u: 8, v: 8));

        var ring = grid.Members.Where(m => Math.Abs(m.Line.Direction.Z) < 1e-6).ToList();

        Assert.NotEmpty(ring);
        Assert.All(ring, m => Assert.Equal(ring[0].Length, m.Length, 5));
        Assert.All(grid.Lattice.Nodes, n => Assert.Equal(5.0, Math.Sqrt(n.X * n.X + n.Y * n.Y), 5));
    }

    // ---- poles ------------------------------------------------------------------

    /// <summary>A fan: one edge collapsed to a point, like the apex of a dome.</summary>
    [Fact]
    public void A_surface_drawn_to_a_point_gets_one_node_there_and_no_member_twice()
    {
        Surface fan = NurbsSurface.CreateFromCorners(
            new Point3d(0, 0, 0), new Point3d(12, 0, 0), new Point3d(6, 8, 0), new Point3d(6, 8, 0));

        SurfaceGrid grid = SurfaceGridGenerator.Generate(fan, Options(GridPattern.Triangulated, u: 4, v: 2));

        Assert.Single(grid.UsedNodes, n => n.DistanceTo(new Point3d(6, 8, 0)) < 1e-6);

        var keys = grid.Members
            .Select(m => (m.Line.From.DistanceTo(m.Line.To) > 0, Math.Min(m.StartNode, m.EndNode), Math.Max(m.StartNode, m.EndNode)))
            .ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());

        var lines = grid.Members.Select(m => m.Line).ToList();
        for (int a = 0; a < lines.Count; a++)
            for (int b = a + 1; b < lines.Count; b++)
                Assert.False(
                    (lines[a].From.DistanceTo(lines[b].From) < 1e-6 && lines[a].To.DistanceTo(lines[b].To) < 1e-6)
                    || (lines[a].From.DistanceTo(lines[b].To) < 1e-6 && lines[a].To.DistanceTo(lines[b].From) < 1e-6),
                    "The same member was drawn twice.");
    }

    // ---- measured by length, steered by the edges ------------------------

    /// <summary>
    /// The reason stations come from the edges: a surface built unevenly has
    /// parameters that bunch, and an even grid should not inherit that.
    /// </summary>
    [Fact]
    public void Divisions_are_even_in_length_however_the_surface_is_parameterised()
    {
        // A lofted strip whose control points crowd towards one end: straight
        // rails, but half way along in parameter is nowhere near half way along.
        var rail = new[] { 0.0, 0.5, 1.0, 2.0, 12.0 };
        Curve near = NurbsCurve.Create(false, 3, rail.Select(x => new Point3d(x, 0, 0)));
        Curve far = NurbsCurve.Create(false, 3, rail.Select(x => new Point3d(x, 8, 0)));

        Assert.NotEqual(6.0, near.PointAt(near.Domain.Mid).X, 1);

        Brep loft = Brep.CreateFromLoft(new[] { near, far }, Point3d.Unset, Point3d.Unset, LoftType.Straight, false)[0];

        SurfaceGrid grid = SurfaceGridGenerator.Generate(loft, Options(GridPattern.Quad, u: 4, v: 2));

        // Whichever way the loft ran its directions, the 12 m way is in quarters.
        var xs = grid.Lattice.Nodes.Select(n => Math.Round(n.X, 4)).Distinct().OrderBy(x => x).ToArray();
        var ys = grid.Lattice.Nodes.Select(n => Math.Round(n.Y, 4)).Distinct().OrderBy(y => y).ToArray();

        double[] long4 = { 0, 3, 6, 9, 12 };
        double[] long2 = { 0, 6, 12 };

        Assert.True(xs.SequenceEqual(long4) || xs.SequenceEqual(long2), string.Join(", ", xs));
        Assert.True(ys.Length is 3 or 5);
    }

    [Fact]
    public void A_snap_point_on_an_edge_runs_a_grid_line_through_it()
    {
        var options = Options(GridPattern.Quad) with
        {
            Strictness = SnapStrictness.Strict,
            SnapPoints = new[] { new Point3d(5.0, 0, 0), new Point3d(12, 1.0, 0), new Point3d(6, 4, 0) },
        };

        SurfaceGrid grid = SurfaceGridGenerator.Generate(Flat(), options);

        Assert.Contains(grid.Lattice.Nodes, n => Math.Abs(n.X - 5.0) < 1e-6 && Math.Abs(n.Y - 8.0) < 1e-6);
        Assert.Contains(grid.Lattice.Nodes, n => Math.Abs(n.Y - 1.0) < 1e-6 && Math.Abs(n.X) < 1e-6);

        Assert.Equal(1, grid.OffEdgeSnapPoints);           // the one in the middle
        Assert.Contains(grid.Notes, n => n.Message.Contains("edge of the surface"));
    }

    [Fact]
    public void A_kink_in_an_edge_is_a_grid_line_when_the_geometry_drives()
    {
        var kinked = new PolylineCurve(new[] { new Point3d(0, 0, 0), new Point3d(5, 0, -1), new Point3d(12, 0, 0) });
        Curve straight = new LineCurve(new Point3d(0, 8, 0), new Point3d(12, 8, 0));

        Brep loft = Brep.CreateFromLoft(new[] { kinked, straight }, Point3d.Unset, Point3d.Unset, LoftType.Straight, false)[0];

        SurfaceGrid grid = SurfaceGridGenerator.Generate(loft, new SurfaceGridOptions());

        Assert.Contains(grid.Lattice.Nodes, n => n.DistanceTo(new Point3d(5, 0, -1)) < 1e-6);
        Assert.Equal(2, Math.Max(grid.PanelsU, grid.PanelsV));
    }

    // ---- the other ways in ---------------------------------------------------

    [Fact]
    public void Four_curves_round_the_outside_will_do_instead_of_a_surface()
    {
        // In no particular order, and not all drawn the same way round.
        var edges = new Curve[]
        {
            new LineCurve(new Point3d(12, 8, 0), new Point3d(0, 8, 0)),
            new LineCurve(new Point3d(0, 0, 0), new Point3d(12, 0, 0)),
            new LineCurve(new Point3d(0, 8, 0), new Point3d(0, 0, 0)),
            new ArcCurve(new Arc(new Point3d(12, 0, 0), new Point3d(13, 4, 0), new Point3d(12, 8, 0))),
        };

        SurfaceGrid grid = SurfaceGridGenerator.Generate(edges, Options(GridPattern.Quad, u: 4, v: 4));

        Assert.Equal(25, grid.Lattice.Nodes.Count);
        Assert.Contains(grid.Lattice.Nodes, n => n.DistanceTo(new Point3d(13, 4, 0)) < 1e-4);   // on the arc
        Assert.False(grid.IsTrimmed);
    }

    [Fact]
    public void Curves_that_do_not_close_are_refused()
    {
        var edges = new Curve[]
        {
            new LineCurve(new Point3d(0, 0, 0), new Point3d(12, 0, 0)),
            new LineCurve(new Point3d(20, 0, 0), new Point3d(20, 8, 0)),
            new LineCurve(new Point3d(30, 9, 0), new Point3d(40, 9, 5)),
        };

        Assert.Throws<ArgumentException>(() => SurfaceGridGenerator.Generate(edges, Options(GridPattern.Quad)));
    }

    [Fact]
    public void A_trimmed_surface_is_gridded_whole_and_says_so()
    {
        Brep plate = Brep.CreatePlanarBreps(new Circle(Plane.WorldXY, 5.0).ToNurbsCurve(), 0.001)[0];

        SurfaceGrid grid = SurfaceGridGenerator.Generate(plate, Options(GridPattern.Quad));

        Assert.True(grid.IsTrimmed);
        Assert.Contains(grid.Notes, n => n.Level == FormNoteLevel.Warning && n.Message.Contains("trimmed"));
    }

    [Fact]
    public void A_trimmed_surface_is_clipped_to_its_outline_when_asked()
    {
        Brep plate = Brep.CreatePlanarBreps(new Circle(Plane.WorldXY, 5.0).ToNurbsCurve(), 0.001)[0];

        SurfaceGrid whole = SurfaceGridGenerator.Generate(plate, Options(GridPattern.Quad, u: 10, v: 10));
        SurfaceGrid clipped = SurfaceGridGenerator.Generate(
            plate, Options(GridPattern.Quad, u: 10, v: 10) with { ClipToTrim = true });

        Assert.True(clipped.IsClipped);
        Assert.False(whole.IsClipped);
        Assert.Equal(whole.Lattice.Nodes.Count, clipped.Lattice.Nodes.Count);       // positions are kept
        Assert.True(clipped.ClippedNodes > 0);
        Assert.Equal(clipped.ClippedNodes, clipped.Lattice.AbsentCount);

        // Nothing left touches a node outside the circle, and the rows still read by position.
        Assert.All(clipped.UsedNodes, n => Assert.True(n.DistanceTo(Point3d.Origin) <= 5.0 + 1e-6));
        Assert.All(clipped.Members, m => Assert.True(m.Line.PointAt(0.5).DistanceTo(Point3d.Origin) <= 5.0 + 1e-6));
        Assert.True(clipped.Members.Count < whole.Members.Count);
        Assert.Contains(clipped.Notes, n => n.Level == FormNoteLevel.Remark && n.Message.Contains("Clipped"));
        Assert.DoesNotContain(clipped.Notes, n => n.Level == FormNoteLevel.Warning);
    }

    [Fact]
    public void An_opening_takes_out_the_nodes_in_it_and_the_members_across_it()
    {
        // A 12 by 8 plate with a 2 by 2 hole in the middle. At 1 m cells the
        // node at (6, 4) is inside the hole and goes, with the four members
        // that ran to it. At 4 m cells no node is inside it, but the member
        // from (4, 4) to (8, 4) crosses it, and goes for that.
        Brep plate = Brep.CreatePlanarBreps(
            new[]
            {
                new Rectangle3d(Plane.WorldXY, new Interval(0, 12), new Interval(0, 8)).ToNurbsCurve(),
                new Rectangle3d(Plane.WorldXY, new Interval(5, 7), new Interval(3, 5)).ToNurbsCurve(),
            },
            0.001)[0];

        SurfaceGrid fine = SurfaceGridGenerator.Generate(
            plate, Options(GridPattern.Quad, u: 12, v: 8) with { ClipToTrim = true });

        Assert.Equal(1, fine.ClippedNodes);
        Assert.False(fine.Lattice.IsPresent(6, 4));
        Assert.DoesNotContain(fine.Members, m => m.StartNode == fine.Lattice.Index(6, 4) || m.EndNode == fine.Lattice.Index(6, 4));
        Assert.Equal(13 * 8 + 12 * 9 - 4, fine.Members.Count);                  // four members ran to the lost node

        SurfaceGrid coarse = SurfaceGridGenerator.Generate(
            plate, Options(GridPattern.Triangulated, u: 3, v: 2) with { ClipToTrim = true });

        Assert.Equal(0, coarse.ClippedNodes);
        Assert.Equal(1, coarse.ClippedMembers);
        Assert.All(coarse.Members, m =>
        {
            Point3d middle = m.Line.PointAt(0.5);
            Assert.False(middle.X > 5 && middle.X < 7 && middle.Y > 3 && middle.Y < 5, $"{m.Role} crosses the hole.");
        });
        Assert.Contains(coarse.Notes, n => n.Message.Contains("crossing an opening"));
    }

    [Fact]
    public void Normals_are_read_at_every_node()
    {
        SurfaceGrid grid = SurfaceGridGenerator.Generate(Tower(), Options(GridPattern.Quad, u: 8, v: 2));

        Assert.Equal(grid.Lattice.Nodes.Count, grid.Normals.Count);
        Assert.All(grid.Normals, n => Assert.Equal(1.0, n.Length, 9));
        Assert.All(grid.Normals, n => Assert.Equal(0.0, n.Z, 9));
    }

    [Fact]
    public void A_polysurface_is_refused()
    {
        Brep box = new Box(Plane.WorldXY, new Interval(0, 1), new Interval(0, 1), new Interval(0, 1)).ToBrep();

        var error = Assert.Throws<ArgumentException>(() => SurfaceGridGenerator.Generate(box, Options(GridPattern.Quad)));
        Assert.Contains("single surface", error.Message);
    }

    [Fact]
    public void Nonsense_options_are_refused()
    {
        Assert.Throws<ArgumentException>(
            () => SurfaceGridGenerator.Generate(Flat(), new SurfaceGridOptions { DivisionsU = -1 }));
        Assert.Throws<ArgumentException>(
            () => SurfaceGridGenerator.Generate(Flat(), new SurfaceGridOptions { Pattern = (GridPattern)9 }));
    }
}
