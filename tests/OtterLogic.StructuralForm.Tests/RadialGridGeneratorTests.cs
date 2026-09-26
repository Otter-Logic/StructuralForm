using OtterLogic.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

[Collection(RhinoCollection.Name)]
public class RadialGridGeneratorTests
{
    /// <summary>A quarter grid: three rings, four bays, rays meeting at the centre.</summary>
    private static readonly RadialGridOptions Quarter = new()
    {
        RingSpacings = new[] { 5.0, 5.0, 6.0 },
        Sweep = 90.0,
        Bays = 4,
    };

    /// <summary>A stadium: an oval hole 40 by 25, two rings, the whole way round.</summary>
    private static readonly RadialGridOptions Stadium = new()
    {
        RingSpacings = new[] { 8.0, 6.0 },
        InnerU = 40.0,
        InnerV = 25.0,
        Sweep = 360.0,
        Bays = 24,
    };

    private static void AssertPoint(Point3d expected, Point3d actual)
        => Assert.True(expected.DistanceTo(actual) < 1e-9, $"expected {expected}, got {actual}");

    private static void AssertOn(Curve curve, Point3d point)
    {
        Assert.True(curve.ClosestPoint(point, out double t));
        Assert.True(curve.PointAt(t).DistanceTo(point) < 1e-6, $"{point} is {curve.PointAt(t).DistanceTo(point)} off the curve");
    }

    // ---- a partial sweep from the centre ------------------------------------

    [Fact]
    public void A_partial_sweep_has_one_more_ray_than_bays_and_a_ring_per_spacing()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Quarter);

        Assert.Equal(5, grid.Rays.Count);
        Assert.Equal(3, grid.Rings.Count);
        Assert.Equal(new[] { 5.0, 10.0, 16.0 }, grid.RingOffsets);
        Assert.Equal(new[] { 0.0, 22.5, 45.0, 67.5, 90.0 }, grid.Angles);
        Assert.False(grid.IsFullSweep);
        Assert.False(grid.IsOval);
        Assert.Empty(grid.Notes);
    }

    [Fact]
    public void Rays_run_from_the_centre_to_the_outer_ring()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Quarter);

        AssertPoint(Point3d.Origin, grid.Rays[0].From);
        AssertPoint(new Point3d(16, 0, 0), grid.Rays[0].To);
        AssertPoint(Point3d.Origin, grid.Rays[4].From);
        AssertPoint(new Point3d(0, 16, 0), grid.Rays[4].To);
    }

    [Fact]
    public void Round_rings_are_arcs_from_the_first_ray_to_the_last()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Quarter);

        Curve ring = grid.Rings[1];
        Assert.IsType<ArcCurve>(ring);
        Assert.False(ring.IsClosed);
        AssertPoint(new Point3d(10, 0, 0), ring.PointAtStart);
        AssertPoint(new Point3d(0, 10, 0), ring.PointAtEnd);
        Assert.Equal(Math.PI * 10 / 2, ring.GetLength(), 6);
    }

    [Fact]
    public void The_centre_is_one_node_shared_by_every_ray()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Quarter);

        Assert.True(grid.Centre.HasValue);
        AssertPoint(Point3d.Origin, grid.Centre!.Value);

        // Rows carry the centre as item 0 so item k + 1 is always ring k.
        Assert.Equal(5, grid.Rows.Count);
        Assert.All(grid.Rows, row =>
        {
            Assert.Equal(4, row.Count);
            AssertPoint(Point3d.Origin, row[0]);
        });
        AssertPoint(new Point3d(10 * Math.Cos(Math.PI / 4), 10 * Math.Sin(Math.PI / 4), 0), grid.Rows[2][2]);

        // Nodes carry it once: 1 + 5 rays x 3 rings.
        Assert.Equal(16, grid.Nodes.Count);
        AssertPoint(Point3d.Origin, grid.Nodes[0]);
        Assert.Single(grid.Nodes, node => node.DistanceTo(Point3d.Origin) < 1e-9);
    }

    // ---- a full circle ------------------------------------------------------

    [Fact]
    public void A_full_sweep_draws_bays_rays_not_bays_plus_one_and_closes_its_rings()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Quarter with { Sweep = 360.0, Bays = 8 });

        Assert.True(grid.IsFullSweep);
        Assert.Equal(8, grid.Rays.Count);
        Assert.Equal(45.0, grid.Angles[1]);
        Assert.All(grid.Rings, ring => Assert.True(ring.IsClosed));
        Assert.Equal(2 * Math.PI * 16, grid.Rings[2].GetLength(), 6);
        Assert.Equal(1 + 8 * 3, grid.Nodes.Count);
    }

    // ---- a round hole -------------------------------------------------------

    [Fact]
    public void A_round_hole_has_a_ring_round_it_and_no_centre()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Quarter with { InnerU = 4.0, InnerV = 4.0 });

        Assert.False(grid.Centre.HasValue);
        Assert.False(grid.IsOval);
        Assert.Equal(new[] { 0.0, 5.0, 10.0, 16.0 }, grid.RingOffsets);
        Assert.Equal(4, grid.Rings.Count);
        Assert.All(grid.Rings, ring => Assert.IsType<ArcCurve>(ring));

        AssertPoint(new Point3d(4, 0, 0), grid.Rays[0].From);
        AssertPoint(new Point3d(20, 0, 0), grid.Rays[0].To);

        Assert.All(grid.Rows, row => Assert.Equal(4, row.Count));
        Assert.Equal(5 * 4, grid.Nodes.Count);
    }

    [Fact]
    public void A_hole_within_tolerance_is_the_centre()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Quarter with { InnerU = 0.001, InnerV = 0.001 });

        Assert.True(grid.Centre.HasValue);
        Assert.Equal(3, grid.Rings.Count);
    }

    [Fact]
    public void A_hole_that_is_zero_one_way_and_not_the_other_is_refused()
    {
        Assert.Throws<ArgumentException>(() => RadialGridGenerator.Generate(Quarter with { InnerU = 0.0, InnerV = 5.0 }));
        Assert.Throws<ArgumentException>(() => RadialGridGenerator.Generate(Quarter with { InnerU = 5.0, InnerV = 0.0 }));
    }

    // ---- an oval hole: the stadium ------------------------------------------

    [Fact]
    public void An_oval_hole_makes_every_ring_an_oval_with_the_spacing_added_to_both_axes()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Stadium);

        Assert.True(grid.IsOval);
        Assert.False(grid.Centre.HasValue);
        Assert.Equal(new[] { 0.0, 8.0, 14.0 }, grid.RingOffsets);
        Assert.Equal(3, grid.Rings.Count);

        // Ring 1 is the oval 48 by 33: through (48, 0) and (0, 33), closed,
        // and not a circle.
        Curve ring = grid.Rings[1];
        Assert.True(ring.IsClosed);
        Assert.IsNotType<ArcCurve>(ring);
        AssertOn(ring, new Point3d(48, 0, 0));
        AssertOn(ring, new Point3d(0, 33, 0));
        AssertOn(ring, new Point3d(-48, 0, 0));
        Assert.False(ring.PointAt(ring.Domain.ParameterAt(0.125)).DistanceTo(Point3d.Origin) is > 47.9 and < 48.1);
    }

    [Fact]
    public void Oval_rays_are_straight_and_meet_every_ring_at_the_same_parameter()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Stadium);

        Assert.Equal(24, grid.Rays.Count);

        // Ray 3 is at parameter 45 degrees: it starts on the hole at
        // (40 cos, 25 sin) and runs in the direction (cos, sin), so every
        // node on it is on that line and on its ring.
        double c = Math.Cos(Math.PI / 4), s = Math.Sin(Math.PI / 4);
        Line ray = grid.Rays[3];
        AssertPoint(new Point3d(40 * c, 25 * s, 0), ray.From);
        AssertPoint(new Point3d(54 * c, 39 * s, 0), ray.To);

        IReadOnlyList<Point3d> row = grid.Rows[3];
        Assert.Equal(3, row.Count);
        for (int k = 0; k < 3; k++)
        {
            Assert.True(ray.DistanceTo(row[k], true) < 1e-9);
            AssertOn(grid.Rings[k], row[k]);
        }

        // Along every ray the nodes are exactly the spacing apart, on the axes
        // and between them alike.
        Assert.Equal(8.0, grid.Rows[0][0].DistanceTo(grid.Rows[0][1]), 9);
        Assert.Equal(8.0, grid.Rows[6][0].DistanceTo(grid.Rows[6][1]), 9);
        Assert.Equal(8.0, row[0].DistanceTo(row[1]), 9);

        // What is a little less between the axes is the width between rings
        // measured square to them, because the ray is not quite square to an
        // oval there: the closest point on ring 1 to a ring-0 node is nearer
        // than the ray's own node on ring 1.
        Assert.True(grid.Rings[1].ClosestPoint(row[0], out double t));
        Assert.True(grid.Rings[1].PointAt(t).DistanceTo(row[0]) < 8.0 - 1e-6);
    }

    [Fact]
    public void Oval_bays_are_wider_along_the_long_sides_and_tighter_round_the_ends()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Stadium);

        // Rays 0 and 1 straddle the end of the long axis; rays 5, 6 and 7 the
        // middle of a long side.
        double atEnd = grid.Rows[0][0].DistanceTo(grid.Rows[1][0]);
        double alongSide = grid.Rows[6][0].DistanceTo(grid.Rows[7][0]);

        Assert.True(atEnd < alongSide, $"end bay {atEnd} should be tighter than side bay {alongSide}");
    }

    [Fact]
    public void A_partial_oval_ring_runs_from_the_first_ray_to_the_last_and_no_further()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Stadium with { Sweep = 90.0, Bays = 3, StartAngle = 30.0 });

        Assert.Equal(4, grid.Rays.Count);
        Assert.Empty(grid.Notes);

        Curve ring = grid.Rings[0];
        Assert.False(ring.IsClosed);
        AssertPoint(grid.Rows[0][0], ring.PointAtStart);
        AssertPoint(grid.Rows[3][0], ring.PointAtEnd);

        foreach (IReadOnlyList<Point3d> row in grid.Rows)
            AssertOn(ring, row[0]);

        // The opposite side of the oval is not on the piece.
        var away = new Point3d(-40, 0, 0);
        Assert.True(ring.ClosestPoint(away, out double t));
        Assert.True(ring.PointAt(t).DistanceTo(away) > 1.0);
    }

    // ---- start angle, overhang, plane ---------------------------------------

    [Fact]
    public void The_start_angle_turns_the_whole_grid_including_the_arcs()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Quarter with { StartAngle = 90.0 });

        Assert.Equal(new[] { 90.0, 112.5, 135.0, 157.5, 180.0 }, grid.Angles);
        AssertPoint(new Point3d(0, 16, 0), grid.Rays[0].To);
        AssertPoint(new Point3d(-16, 0, 0), grid.Rays[4].To);
        AssertPoint(new Point3d(0, 5, 0), grid.Rings[0].PointAtStart);
        AssertPoint(new Point3d(-5, 0, 0), grid.Rings[0].PointAtEnd);
    }

    [Fact]
    public void Overhang_runs_the_rays_past_the_outer_ring_and_leaves_the_rings_alone()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Quarter with { Overhang = 2.0 });

        AssertPoint(new Point3d(18, 0, 0), grid.Rays[0].To);
        AssertPoint(new Point3d(16, 0, 0), grid.Rings[2].PointAtStart);
        AssertPoint(new Point3d(16, 0, 0), grid.Rows[0][3]);
    }

    [Fact]
    public void The_grid_follows_the_plane()
    {
        var plane = new Plane(new Point3d(0, 0, 10), Vector3d.XAxis, Vector3d.ZAxis);   // world XZ, lifted

        RadialGrid grid = RadialGridGenerator.Generate(Quarter with { Plane = plane });

        AssertPoint(new Point3d(0, 0, 10), grid.Centre!.Value);
        AssertPoint(new Point3d(16, 0, 10), grid.Rays[0].To);
        AssertPoint(new Point3d(0, 0, 26), grid.Rays[4].To);
    }

    // ---- what is said -------------------------------------------------------

    [Fact]
    public void Bays_narrower_than_the_tolerance_on_the_innermost_ring_are_warned_about()
    {
        RadialGrid grid = RadialGridGenerator.Generate(Quarter with
        {
            RingSpacings = new[] { 0.02, 5.0 },
            Bays = 400,
        });

        FormNote note = Assert.Single(grid.Notes);
        Assert.Equal(FormNoteLevel.Warning, note.Level);
        Assert.Contains("innermost ring", note.Message);
    }

    // ---- refusals -----------------------------------------------------------

    [Theory]
    [InlineData(0.0)]
    [InlineData(-90.0)]
    [InlineData(361.0)]
    [InlineData(double.NaN)]
    public void A_sweep_outside_zero_to_360_is_refused(double sweep)
    {
        Assert.Throws<ArgumentException>(() => RadialGridGenerator.Generate(Quarter with { Sweep = sweep }));
    }

    [Fact]
    public void Fewer_than_one_bay_a_negative_hole_or_overhang_and_no_rings_are_refused()
    {
        Assert.Throws<ArgumentException>(() => RadialGridGenerator.Generate(Quarter with { Bays = 0 }));
        Assert.Throws<ArgumentException>(() => RadialGridGenerator.Generate(Quarter with { InnerU = -1.0, InnerV = 1.0 }));
        Assert.Throws<ArgumentException>(() => RadialGridGenerator.Generate(Quarter with { Overhang = -1.0 }));
        Assert.Throws<ArgumentException>(() => RadialGridGenerator.Generate(Quarter with { RingSpacings = Array.Empty<double>() }));
        Assert.Throws<ArgumentException>(() => RadialGridGenerator.Generate(Quarter with { RingSpacings = new[] { 5.0, 0.0 } }));
    }
}
