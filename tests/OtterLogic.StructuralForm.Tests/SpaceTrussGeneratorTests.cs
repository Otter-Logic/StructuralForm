using OtterLogic.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

[Collection(RhinoCollection.Name)]
public class SpaceTrussGeneratorTests
{
    private const double Depth = 1.5;

    /// <summary>A flat 12 by 8 rectangle at z = 0: U runs along X, V along Y, and the normal points up.</summary>
    private static Surface Flat(double width = 12.0, double depth = 8.0)
        => NurbsSurface.CreateFromCorners(
            new Point3d(0, 0, 0), new Point3d(width, 0, 0), new Point3d(width, depth, 0), new Point3d(0, depth, 0));

    private static Surface Tower(double radius = 5.0, double height = 20.0)
        => new Cylinder(new Circle(Plane.WorldXY, radius), height).ToNurbsSurface();

    /// <summary>A 4 by 2 grid over the flat rectangle: 5 by 3 nodes, 3 m by 4 m cells.</summary>
    private static SurfaceGrid Grid(GridPattern pattern = GridPattern.Quad, int u = 4, int v = 2, bool flip = false)
        => SurfaceGridGenerator.Generate(Flat(), new SurfaceGridOptions { Pattern = pattern, DivisionsU = u, DivisionsV = v, Flip = flip });

    private static SpaceTruss Build(SurfaceGrid grid, SpaceTrussType type)
        => SpaceTrussGenerator.Generate(grid, new SpaceTrussOptions { Depth = Depth, Type = type });

    private static void AssertIndicesResolve(SpaceTruss truss)
    {
        IReadOnlyList<Point3d> nodes = truss.Nodes;

        Assert.All(truss.Members, m =>
        {
            Assert.Equal(nodes[m.StartNode], m.Line.From);
            Assert.Equal(nodes[m.EndNode], m.Line.To);
        });

        var keys = truss.Members.Select(m => (Math.Min(m.StartNode, m.EndNode), Math.Max(m.StartNode, m.EndNode))).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // ---- the one setting ----------------------------------------------------

    [Fact]
    public void Every_flat_truss_pattern_is_a_space_truss_type_and_pyramid_is_the_only_other()
    {
        // A pattern added to TrussType has to be added here by name, or the
        // space truss silently cannot offer it.
        foreach (TrussType pattern in Enum.GetValues<TrussType>())
        {
            var type = Enum.Parse<SpaceTrussType>(pattern.ToString());
            Assert.Equal(pattern, type.Web());
            Assert.False(type.IsPyramid());
        }

        Assert.True(SpaceTrussType.Pyramid.IsPyramid());
        Assert.Null(SpaceTrussType.Pyramid.Web());
        Assert.Equal(Enum.GetValues<TrussType>().Length + 1, Enum.GetValues<SpaceTrussType>().Length);
    }

    [Fact]
    public void The_stored_values_still_mean_what_the_two_member_list_meant()
    {
        // 0 was the offset layers, 1 the aligned ones with their default
        // Warren web: a saved definition reads the same either way.
        Assert.Equal(SpaceTrussType.Pyramid, (SpaceTrussType)0);
        Assert.Equal(SpaceTrussType.Warren, (SpaceTrussType)1);
        Assert.Equal(SpaceTrussType.Pyramid, new SpaceTrussOptions().Type);
    }

    // ---- pyramids -------------------------------------------------------------

    [Fact]
    public void Pyramid_puts_a_pyramid_on_every_cell_and_a_quad_grid_between_the_apexes()
    {
        SpaceTruss truss = Build(Grid(), SpaceTrussType.Pyramid);

        Assert.True(truss.IsPyramid);
        Assert.Equal(8, truss.BottomLattice.Nodes.Count);              // one apex per cell: 4 by 2
        Assert.Equal(4, truss.BottomLattice.CountU);
        Assert.Equal(2, truss.BottomLattice.CountV);

        Assert.Equal(22, truss.TopChord.Count());                      // the grid, as it was
        Assert.Equal(10, truss.BottomChord.Count());                   // 3 + 3 along, 4 across
        Assert.Equal(32, truss.Diagonals.Count());                     // four per pyramid
        Assert.Empty(truss.Verticals);
        Assert.Empty(truss.EndPosts);
        Assert.Empty(truss.Notes);

        AssertIndicesResolve(truss);
    }

    [Fact]
    public void Pyramid_apexes_sit_under_the_cell_centres_at_the_depth()
    {
        SpaceTruss truss = Build(Grid(), SpaceTrussType.Pyramid);

        Assert.Equal(new Point3d(1.5, 2.0, -Depth), truss.BottomLattice.Node(0, 0));
        Assert.Equal(new Point3d(10.5, 6.0, -Depth), truss.BottomLattice.Node(3, 1));
        Assert.All(truss.BottomNodes, n => Assert.Equal(-Depth, n.Z, 9));
    }

    [Fact]
    public void Depth_goes_below_a_roof_whichever_way_the_surface_was_built()
    {
        // The same rectangle drawn the other way round, so its normal points down.
        Surface upsideDown = NurbsSurface.CreateFromCorners(
            new Point3d(0, 0, 0), new Point3d(0, 8, 0), new Point3d(12, 8, 0), new Point3d(12, 0, 0));

        SurfaceGrid grid = SurfaceGridGenerator.Generate(upsideDown, new SurfaceGridOptions { DivisionsU = 4, DivisionsV = 2 });
        Assert.True(grid.Normals[0].Z < 0);

        SpaceTruss truss = Build(grid, SpaceTrussType.Pyramid);

        Assert.All(truss.BottomNodes, n => Assert.Equal(-Depth, n.Z, 9));
    }

    [Fact]
    public void Flip_depth_puts_the_second_layer_on_the_other_side()
    {
        SpaceTruss truss = SpaceTrussGenerator.Generate(Grid(), new SpaceTrussOptions { Depth = Depth, FlipDepth = true });

        Assert.All(truss.BottomNodes, n => Assert.Equal(Depth, n.Z, 9));
    }

    [Fact]
    public void Pyramids_under_a_diagrid_sit_under_every_diamond_with_a_diagrid_between_them()
    {
        SpaceTruss truss = Build(Grid(GridPattern.Diagrid, u: 6, v: 4), SpaceTrussType.Pyramid);

        // 7 by 5 positions; the diagrid stands on the even ones. A diamond
        // centre is an odd interior position with a diagrid node each side of
        // it: seven of them, in a chequer over the three interior rows.
        Lattice bottom = truss.BottomLattice;
        Assert.Equal(35, bottom.Nodes.Count);
        Assert.Equal(35 - 7, bottom.AbsentCount);
        Assert.True(bottom.IsPresent(2, 1));
        Assert.True(bottom.IsPresent(3, 2));
        Assert.False(bottom.IsPresent(1, 1));                          // a diagrid node, not a centre
        Assert.False(bottom.IsPresent(1, 0));                          // on the edge: three neighbours only

        Assert.Equal(28, truss.Diagonals.Count());                     // four per apex
        Assert.Equal(8, truss.BottomChord.Count());                    // the apexes as a diagrid of their own
        Assert.All(truss.BottomChord, c => Assert.Equal(-Depth, c.From.Z, 9));
        Assert.All(truss.Diagonals, d => Assert.True(Math.Abs(d.From.Z - d.To.Z) > 1));

        AssertIndicesResolve(truss);
    }

    [Fact]
    public void Pyramids_under_a_triangulated_grid_are_drawn_and_the_doubled_bracing_is_said()
    {
        SpaceTruss truss = Build(Grid(GridPattern.Triangulated), SpaceTrussType.Pyramid);

        Assert.True(truss.DoublesTheTopBracing);
        Assert.Equal(8, truss.ChordsOf(TrussMemberRole.TopChord, GridMemberRole.Diagonal).Count());
        Assert.Equal(32, truss.Diagonals.Count());

        FormNote note = Assert.Single(truss.Notes);
        Assert.Equal(FormNoteLevel.Remark, note.Level);
        Assert.Contains("braced twice", note.Message);

        Assert.False(Build(Grid(GridPattern.Triangulated), SpaceTrussType.Warren).DoublesTheTopBracing);
    }

    // ---- two-way trusses ----------------------------------------------------

    [Fact]
    public void Warren_drops_the_grid_and_runs_a_flat_truss_along_every_line_with_posts_at_the_ends()
    {
        SpaceTruss truss = Build(Grid(), SpaceTrussType.Warren);

        Assert.False(truss.IsPyramid);
        Assert.Equal(15, truss.BottomLattice.Nodes.Count);
        Assert.All(truss.BottomNodes, n => Assert.Equal(-Depth, n.Z, 9));

        Assert.Equal(22, truss.TopChord.Count());
        Assert.Equal(22, truss.BottomChord.Count());                   // the same pattern, dropped
        Assert.Equal(3 * 4 + 5 * 2, truss.Diagonals.Count());          // Warren: one per panel per line
        Assert.Empty(truss.Verticals);                                 // Warren has none
        Assert.Equal(12, truss.EndPosts.Count());                      // every perimeter node, always

        AssertIndicesResolve(truss);
    }

    [Theory]
    [InlineData(SpaceTrussType.WarrenWithVerticals, 3)]
    [InlineData(SpaceTrussType.Pratt, 3)]
    [InlineData(SpaceTrussType.Vierendeel, 3)]
    public void Verticals_stand_at_the_interior_nodes_only(SpaceTrussType type, int verticals)
    {
        SpaceTruss truss = Build(Grid(), type);

        Assert.Equal(verticals, truss.Verticals.Count());
        Assert.Equal(12, truss.EndPosts.Count());
        Assert.All(truss.Verticals, v => Assert.True(v.From.X is > 0 and < 12 && v.From.Y is > 0 and < 8));
    }

    [Fact]
    public void Each_two_way_type_is_the_flat_truss_of_that_name_along_every_line()
    {
        // Along the middle row of a 4 by 2 grid, the diagonals of the space
        // truss are exactly what a flat truss of the same pattern draws
        // between the same two chords: Pratt here means what Pratt means there.
        SurfaceGrid grid = Grid();
        var top = new LineCurve(new Point3d(0, 4, 0), new Point3d(12, 4, 0));
        var bottom = new LineCurve(new Point3d(0, 4, -Depth), new Point3d(12, 4, -Depth));

        foreach (SpaceTrussType type in Enum.GetValues<SpaceTrussType>())
        {
            if (type.IsPyramid()) continue;

            FlatTruss flat = FlatTrussGenerator.Generate(top, bottom, new FlatTrussOptions { Type = type.Web()!.Value, Divisions = 4 });
            SpaceTruss space = Build(grid, type);

            var expected = flat.Diagonals.Select(Key).OrderBy(k => k).ToList();
            var actual = space.Diagonals.Where(d => Math.Abs(d.From.Y - 4) < 1e-6 && Math.Abs(d.To.Y - 4) < 1e-6)
                .Select(Key).OrderBy(k => k).ToList();

            Assert.Equal(expected, actual);
        }

        static string Key(Line line)
        {
            Point3d a = line.From, b = line.To;
            if (a.X > b.X || (a.X == b.X && a.Z > b.Z)) (a, b) = (b, a);
            return $"{a.X:0.###},{a.Z:0.###}-{b.X:0.###},{b.Z:0.###}";
        }
    }

    [Fact]
    public void Two_way_trusses_under_a_diagrid_run_along_the_diagonal_lines()
    {
        SpaceTruss truss = Build(Grid(GridPattern.Diagrid), SpaceTrussType.Warren);

        // Eight cells, one diagrid diagonal each, top and bottom.
        Assert.Equal(8, truss.ChordsOf(TrussMemberRole.TopChord, GridMemberRole.Diagonal).Count());
        Assert.Equal(8, truss.ChordsOf(TrussMemberRole.BottomChord, GridMemberRole.Diagonal).Count());

        // Every diagrid diagonal is a panel of some line, so a Warren web has
        // one member per panel.
        Assert.Equal(8, truss.Diagonals.Count());
        Assert.False(truss.HasNoLines);
        Assert.NotEmpty(truss.EndPosts);

        AssertIndicesResolve(truss);
    }

    // ---- surfaces that are not flat ---------------------------------------

    [Fact]
    public void Round_a_tower_the_second_layer_is_a_ring_inside_or_out_and_nothing_doubles_at_the_seam()
    {
        SurfaceGrid grid = SurfaceGridGenerator.Generate(Tower(), new SurfaceGridOptions { DivisionsU = 8, DivisionsV = 4 });

        SpaceTruss truss = Build(grid, SpaceTrussType.WarrenWithVerticals);

        Assert.True(truss.FacesSideways);
        Assert.Contains(truss.Notes, n => n.Message.Contains("stands on end"));

        // Every bottom node is a constant radius from the axis: the depth is
        // along the normal, which is horizontal here.
        Assert.All(truss.BottomNodes, n => Assert.Equal(5.0 + Depth, Math.Sqrt(n.X * n.X + n.Y * n.Y), 6));

        // Rings are closed runs: a vertical at every node, no end posts round them.
        Assert.Equal(8 * 5, truss.Verticals.Count() + truss.EndPosts.Count());
        Assert.Equal(8 * 2, truss.EndPosts.Count());                   // top and bottom rings only

        AssertIndicesResolve(truss);
    }

    [Fact]
    public void Flip_depth_on_a_tower_puts_the_second_layer_inside()
    {
        SurfaceGrid grid = SurfaceGridGenerator.Generate(Tower(), new SurfaceGridOptions { DivisionsU = 8, DivisionsV = 4 });

        SpaceTruss truss = SpaceTrussGenerator.Generate(grid, new SpaceTrussOptions { Depth = Depth, FlipDepth = true });

        Assert.All(truss.BottomNodes, n => Assert.Equal(5.0 - Depth, Math.Sqrt(n.X * n.X + n.Y * n.Y), 6));
    }

    [Fact]
    public void A_fan_drawn_to_a_point_gets_one_apex_row_and_no_member_twice()
    {
        Surface fan = NurbsSurface.CreateFromCorners(
            new Point3d(0, 0, 0), new Point3d(12, 0, 0), new Point3d(6, 8, 0), new Point3d(6, 8, 0));

        SurfaceGrid grid = SurfaceGridGenerator.Generate(fan, new SurfaceGridOptions { DivisionsU = 4, DivisionsV = 2 });

        foreach (SpaceTrussType type in new[] { SpaceTrussType.Pyramid, SpaceTrussType.Pratt })
        {
            SpaceTruss truss = Build(grid, type);

            AssertIndicesResolve(truss);

            var lines = truss.Members.Select(m => m.Line).ToList();
            for (int a = 0; a < lines.Count; a++)
                for (int b = a + 1; b < lines.Count; b++)
                    Assert.False(
                        (lines[a].From.DistanceTo(lines[b].From) < 1e-6 && lines[a].To.DistanceTo(lines[b].To) < 1e-6)
                        || (lines[a].From.DistanceTo(lines[b].To) < 1e-6 && lines[a].To.DistanceTo(lines[b].From) < 1e-6),
                        $"{type}: the same member was drawn twice.");
        }
    }

    // ---- openings -------------------------------------------------------------

    /// <summary>The flat rectangle with a 2 by 2 hole cut through it, in the middle unless told otherwise.</summary>
    private static Brep PlateWithHole(double x = 5.0, double y = 3.0)
    {
        Brep plate = Brep.CreatePlanarBreps(
            new[]
            {
                new Rectangle3d(Plane.WorldXY, new Interval(0, 12), new Interval(0, 8)).ToNurbsCurve(),
                new Rectangle3d(Plane.WorldXY, new Interval(x, x + 2), new Interval(y, y + 2)).ToNurbsCurve(),
            },
            0.001)[0];

        Assert.Single(plate.Faces);
        Assert.False(plate.Faces[0].IsSurface);
        return plate;
    }

    [Fact]
    public void A_pyramid_over_an_opening_is_left_out_of_a_clipped_truss()
    {
        // 3 by 2: 4 m cells, and a hole in the middle of cell (1, 0) that no
        // node and no top member touches — only the apex would sit over it.
        SurfaceGrid grid = SurfaceGridGenerator.Generate(
            PlateWithHole(x: 5.0, y: 1.0), new SurfaceGridOptions { DivisionsU = 3, DivisionsV = 2, ClipToTrim = true });

        Assert.True(grid.IsClipped);
        Assert.Equal(0, grid.ClippedNodes);
        Assert.Equal(0, grid.ClippedMembers);

        SpaceTruss truss = Build(grid, SpaceTrussType.Pyramid);

        Assert.False(truss.BottomLattice.IsPresent(1, 0));
        Assert.Equal(6 - 1, truss.BottomLattice.Nodes.Count - truss.BottomLattice.AbsentCount);
        Assert.Equal(4 * 5, truss.Diagonals.Count());
        Assert.Equal(4, truss.BottomChord.Count());                    // the three chords into the lost apex are gone
        Assert.DoesNotContain(truss.UsedNodes, n => n.DistanceTo(new Point3d(6, 2, -Depth)) < 1e-6);

        AssertIndicesResolve(truss);
    }

    [Fact]
    public void A_two_way_truss_stops_at_the_rim_of_an_opening_with_posts_there()
    {
        // 12 by 8: 1 m cells, so the hole swallows the node at (6, 4) and its
        // members, and the four lines through it are each cut in two.
        SurfaceGrid grid = SurfaceGridGenerator.Generate(
            PlateWithHole(), new SurfaceGridOptions { DivisionsU = 12, DivisionsV = 8, ClipToTrim = true });

        Assert.Equal(1, grid.ClippedNodes);

        SpaceTruss truss = Build(grid, SpaceTrussType.Pratt);

        Assert.False(truss.BottomLattice.IsPresent(6, 4));
        Assert.DoesNotContain(truss.Members, m => m.Line.From.DistanceTo(new Point3d(6, 4, 0)) < 1e-6
                                                 || m.Line.To.DistanceTo(new Point3d(6, 4, 0)) < 1e-6);

        // The rim nodes (5,4), (7,4), (6,3), (6,5) are ends of a cut line, so they carry posts.
        int rimPosts = truss.EndPosts.Count(p => p.From.DistanceTo(new Point3d(6, 4, 0)) < 1.0 + 1e-6);
        Assert.Equal(4, rimPosts);
        Assert.Equal(2 * 13 + 2 * 9 - 4 + 4, truss.EndPosts.Count());

        AssertIndicesResolve(truss);
    }

    // ---- refusals -------------------------------------------------------------

    [Fact]
    public void Nonsense_options_are_refused()
    {
        SurfaceGrid grid = Grid();

        var error = Assert.Throws<ArgumentException>(() => SpaceTrussGenerator.Generate(grid, new SpaceTrussOptions()));
        Assert.Contains("Depth", error.Message);

        Assert.Throws<ArgumentException>(
            () => SpaceTrussGenerator.Generate(grid, new SpaceTrussOptions { Depth = -1 }));
        Assert.Throws<ArgumentException>(
            () => SpaceTrussGenerator.Generate(grid, new SpaceTrussOptions { Depth = 1, Type = (SpaceTrussType)99 }));
        Assert.Throws<ArgumentNullException>(
            () => SpaceTrussGenerator.Generate(null!, new SpaceTrussOptions { Depth = 1 }));
    }
}
