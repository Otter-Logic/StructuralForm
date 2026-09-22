using OtterLogic.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

/// <summary>
/// Snap points belong to the chord they were picked on, and the chords' points
/// pair up into panel points rather than each demanding one of its own.
/// <para>
/// The case these exist for: seven points set out along the top chord and seven
/// along the bottom, not quite above each other. That is seven panel points
/// with a leaning vertical at each - not fourteen, which is what pooling them
/// onto one shared station list produced.
/// </para>
/// </summary>
[Collection(RhinoCollection.Name)]
public class PairedStationTests
{
    private const double Span = 12.0;
    private const double Depth = 2.0;

    private static Curve Straight(double z)
        => new LineCurve(new Point3d(0, 0, z), new Point3d(Span, 0, z));

    // Seven along each chord, offset from each other the way two hand-picked
    // sets are: never aligned, never further apart than their own spacing.
    private static readonly double[] TopX = { 0.0, 2.2, 5.1, 6.9, 8.5, 9.7, 12.0 };
    private static readonly double[] BottomX = { 0.0, 3.1, 4.7, 6.2, 7.9, 9.4, 12.0 };

    private static Point3d[] BothSets()
        => TopX.Select(x => new Point3d(x, 0, Depth))
            .Concat(BottomX.Select(x => new Point3d(x, 0, 0)))
            .ToArray();

    private static FlatTruss Build(
        SnapStrictness strictness, int divisions, Point3d[] points, bool onPlan = false)
        => FlatTrussGenerator.Generate(Straight(Depth), Straight(0), new FlatTrussOptions
        {
            Divisions = divisions,
            Strictness = strictness,
            Type = TrussType.WarrenWithVerticals,
            MeasureOnPlan = onPlan,
            AdditionalSnapPoints = points,
        });

    [Theory]
    [InlineData(SnapStrictness.Relaxed)]
    [InlineData(SnapStrictness.Strict)]
    public void Seven_points_on_each_chord_make_seven_panel_points(SnapStrictness strictness)
    {
        FlatTruss truss = Build(strictness, 6, BothSets());

        // Seven rings, six panels - not fourteen and thirteen.
        Assert.Equal(6, truss.PanelCount);
        Assert.Equal(7, truss.TopNodes.Count);
        Assert.Equal(7, truss.BottomNodes.Count);

        // And each chord sits on its own points, not on the other's.
        for (int i = 0; i < TopX.Length; i++)
        {
            Assert.Equal(TopX[i], truss.TopNodes[i].X, 6);
            Assert.Equal(BottomX[i], truss.BottomNodes[i].X, 6);
        }

        Assert.Equal(0, truss.UnusedSnapPoints);
    }

    /// <summary>
    /// The verticals are what the user sees. One per pair, leaning to join the
    /// two points - not two upright ones a hand's width apart.
    /// </summary>
    [Theory]
    [InlineData(SnapStrictness.Relaxed)]
    [InlineData(SnapStrictness.Strict)]
    public void One_leaning_vertical_per_pair(SnapStrictness strictness)
    {
        FlatTruss truss = Build(strictness, 6, BothSets());

        var verticals = truss.Verticals.ToList();

        Assert.Equal(5, verticals.Count);                  // interior rings only
        Assert.All(verticals, line => Assert.NotEqual(0.0, Math.Round(line.Direction.X, 9)));

        // Each one runs from its top point to its bottom point.
        for (int i = 0; i < verticals.Count; i++)
        {
            Assert.Equal(TopX[i + 1], verticals[i].From.X, 6);
            Assert.Equal(BottomX[i + 1], verticals[i].To.X, 6);
        }
    }

    /// <summary>
    /// The regression this must not cause. A point on one chord alone leaves
    /// every other chord at the same station, so the vertical under it still
    /// stands up straight - which is what a lone vertex on a pitched chord has
    /// always done, and has to keep doing.
    /// </summary>
    [Theory]
    [InlineData(SnapStrictness.Relaxed)]
    [InlineData(SnapStrictness.Strict)]
    public void A_point_on_one_chord_alone_still_squares_the_other(SnapStrictness strictness)
    {
        FlatTruss truss = Build(strictness, 4, new[] { new Point3d(5.0, 0, Depth) });

        Assert.Contains(truss.TopNodes, n => Math.Abs(n.X - 5.0) < 1e-6);
        Assert.Contains(truss.BottomNodes, n => Math.Abs(n.X - 5.0) < 1e-6);   // followed the top
        Assert.All(truss.Verticals, line => Assert.Equal(0.0, line.Direction.X, 6));
    }

    [Fact]
    public void A_chord_vertex_still_squares_the_other_chord()
    {
        var top = new PolylineCurve(new[]
        {
            new Point3d(0, 0, Depth), new Point3d(5.0, 0, Depth + 1), new Point3d(Span, 0, Depth),
        });

        FlatTruss truss = FlatTrussGenerator.Generate(top, Straight(0), new FlatTrussOptions
        {
            Divisions = 4,
            Type = TrussType.Vierendeel,

            // On plan, because the assertions below are plan positions. Along
            // the chord, the kinked top chord is longer than the flat bottom
            // one, so the same station is a different x on each - which is the
            // whole reason MeasureOnPlan exists.
            MeasureOnPlan = true,
        });

        Assert.Contains(truss.TopNodes, n => Math.Abs(n.X - 5.0) < 1e-6);
        Assert.Contains(truss.BottomNodes, n => Math.Abs(n.X - 5.0) < 1e-6);
        Assert.All(truss.Verticals, line => Assert.Equal(0.0, line.Direction.X, 6));
    }

    /// <summary>
    /// Pairing is a local judgement - each point nearer to its partner than to
    /// the next point along its own chord - so it does not change with the
    /// division count. A rule keyed to half a panel would pair these at six
    /// divisions and not at twelve, which is no way to set a truss out.
    /// </summary>
    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    public void Pairing_does_not_depend_on_the_division_count(int divisions)
    {
        FlatTruss truss = Build(SnapStrictness.Strict, divisions, BothSets());

        // The count still grows with the divisions asked for - Strict fills
        // between the pinned rings. What must not change is which points pair:
        // every one of them is still a node on its own chord.
        Assert.Equal(divisions, truss.PanelCount);
        Assert.Equal(0, truss.UnusedSnapPoints);

        foreach (double x in TopX)
            Assert.Contains(truss.TopNodes, n => Math.Abs(n.X - x) < 1e-6);

        foreach (double x in BottomX)
            Assert.Contains(truss.BottomNodes, n => Math.Abs(n.X - x) < 1e-6);

        // And no point dragged the other chord to it: at six divisions the
        // pairs are exactly the seven rings, so the misalignment shows as
        // leaning verticals rather than extra panel points.
        Assert.Equal(divisions + 1, truss.TopNodes.Count);
    }

    /// <summary>
    /// Two lone points at opposite ends are two panel points, not one pair with
    /// a vertical lying almost flat across the truss.
    /// </summary>
    [Fact]
    public void Points_far_apart_are_not_read_as_a_pair()
    {
        FlatTruss truss = Build(SnapStrictness.Strict, 4, new[]
        {
            new Point3d(1.2, 0, Depth),      // near the start, on the top chord
            new Point3d(10.8, 0, 0),         // near the end, on the bottom chord
        });

        Assert.Contains(truss.TopNodes, n => Math.Abs(n.X - 1.2) < 1e-6);
        Assert.Contains(truss.BottomNodes, n => Math.Abs(n.X - 10.8) < 1e-6);

        // Each squared the other chord rather than pairing with it.
        Assert.Contains(truss.BottomNodes, n => Math.Abs(n.X - 1.2) < 1e-6);
        Assert.Contains(truss.TopNodes, n => Math.Abs(n.X - 10.8) < 1e-6);
    }

    [Fact]
    public void Unequal_sets_pair_what_corresponds_and_square_the_rest()
    {
        // Three on top, two on the bottom sitting under the outer two.
        FlatTruss truss = Build(SnapStrictness.Strict, 4, new[]
        {
            new Point3d(3.0, 0, Depth), new Point3d(6.0, 0, Depth), new Point3d(9.0, 0, Depth),
            new Point3d(3.2, 0, 0), new Point3d(8.8, 0, 0),
        });

        Assert.Equal(0, truss.UnusedSnapPoints);

        // The pairs keep their own points...
        Assert.Contains(truss.TopNodes, n => Math.Abs(n.X - 3.0) < 1e-6);
        Assert.Contains(truss.BottomNodes, n => Math.Abs(n.X - 3.2) < 1e-6);
        Assert.Contains(truss.TopNodes, n => Math.Abs(n.X - 9.0) < 1e-6);
        Assert.Contains(truss.BottomNodes, n => Math.Abs(n.X - 8.8) < 1e-6);

        // ...and the unpaired one squares the chord under it.
        Assert.Contains(truss.TopNodes, n => Math.Abs(n.X - 6.0) < 1e-6);
        Assert.Contains(truss.BottomNodes, n => Math.Abs(n.X - 6.0) < 1e-6);
    }

    /// <summary>
    /// What measuring on plan changes, and all it changes: where the rings
    /// nothing pinned end up. A pinned ring sits on its own point either way.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_pinned_ring_sits_on_its_point_on_plan_or_not(bool onPlan)
    {
        FlatTruss truss = Build(SnapStrictness.Strict, 6, BothSets(), onPlan);

        for (int i = 0; i < TopX.Length; i++)
        {
            Assert.Equal(TopX[i], truss.TopNodes[i].X, 6);
            Assert.Equal(BottomX[i], truss.BottomNodes[i].X, 6);
        }
    }

    [Fact]
    public void Nodes_stay_in_order_along_both_chords()
    {
        FlatTruss truss = Build(SnapStrictness.Strict, 9, BothSets());

        for (int i = 1; i < truss.TopNodes.Count; i++)
        {
            Assert.True(truss.TopNodes[i].X > truss.TopNodes[i - 1].X);
            Assert.True(truss.BottomNodes[i].X > truss.BottomNodes[i - 1].X);
        }
    }

    // ---- and the same for a box truss --------------------------------------

    [Theory]
    [InlineData(SnapStrictness.Relaxed)]
    [InlineData(SnapStrictness.Strict)]
    public void A_box_truss_pairs_its_chords_the_same_way(SnapStrictness strictness)
    {
        const double Width = 1.5;

        Curve Chord(double y, double z)
            => new LineCurve(new Point3d(0, y, z), new Point3d(Span, y, z));

        // The same two sets, picked on one top chord and one bottom chord.
        var points = TopX.Select(x => new Point3d(x, 0, Depth))
            .Concat(BottomX.Select(x => new Point3d(x, 0, 0)))
            .ToArray();

        BoxTruss truss = BoxTrussGenerator.Generate(
            new[] { Chord(0, Depth), Chord(Width, Depth) },
            new[] { Chord(0, 0), Chord(Width, 0) },
            new BoxTrussOptions
            {
                Sides = new FlatTrussOptions
                {
                    Divisions = 6,
                    Strictness = strictness,
                    Type = TrussType.WarrenWithVerticals,
                    AdditionalSnapPoints = points,
                },
            });

        Assert.Equal(6, truss.PanelCount);
        Assert.Equal(0, truss.UnusedSnapPoints);
    }
}
