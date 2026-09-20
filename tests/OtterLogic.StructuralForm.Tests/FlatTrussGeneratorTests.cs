using OtterLogic.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

[Collection(RhinoCollection.Name)]
public class FlatTrussGeneratorTests
{
    private const double Span = 12.0;
    private const double Depth = 2.0;

    private static Curve StraightChord(double z)
        => new LineCurve(new Point3d(0, 0, z), new Point3d(Span, 0, z));

    private static Curve PolylineChord(double z, params double[] interiorX)
    {
        var points = new List<Point3d> { new(0, 0, z) };
        points.AddRange(interiorX.Select(x => new Point3d(x, 0, z)));
        points.Add(new Point3d(Span, 0, z));
        return new PolylineCurve(points);
    }

    private static FlatTruss Build(TrussType type, double spacing = 2.0, bool endPosts = true)
        => FlatTrussGenerator.Generate(
            StraightChord(Depth),
            StraightChord(0),
            new FlatTrussOptions { Type = type, Spacing = spacing, GenerateEndPosts = endPosts });

    [Fact]
    public void Spacing_sets_the_panel_count()
    {
        FlatTruss truss = Build(TrussType.Warren, spacing: 2.0);

        Assert.Equal(6, truss.PanelCount);                 // 12 / 2
        Assert.Equal(7, truss.TopNodes.Count);
        Assert.Equal(7, truss.BottomNodes.Count);
    }

    [Fact]
    public void Polyline_vertices_become_nodes_without_any_spacing()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            PolylineChord(Depth, 3.0, 7.0),
            StraightChord(0),
            new FlatTrussOptions { Spacing = 0.0 });

        Assert.Equal(4, truss.TopNodes.Count);             // ends plus the two kinks
        Assert.Equal(3.0, truss.TopNodes[1].X, 6);
        Assert.Equal(7.0, truss.TopNodes[2].X, 6);
    }

    [Fact]
    public void A_vertex_on_either_chord_creates_a_node_on_both_when_geometry_drives()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth),
            PolylineChord(0, 5.0),
            new FlatTrussOptions { Spacing = 0.0 });

        Assert.Equal(3, truss.TopNodes.Count);
        Assert.Equal(5.0, truss.TopNodes[1].X, 6);         // induced on the straight top chord
        Assert.Equal(5.0, truss.BottomNodes[1].X, 6);
    }

    [Fact]
    public void Additional_points_are_pulled_onto_the_nearer_chord()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth),
            StraightChord(0),
            new FlatTrussOptions
            {
                Spacing = 0.0,
                AdditionalSnapPoints = new[] { new Point3d(4.0, 0, Depth) },   // on the top chord
            });

        Assert.Equal(3, truss.TopNodes.Count);
        Assert.Equal(4.0, truss.TopNodes[1].X, 6);
    }

    [Fact]
    public void A_bare_line_with_no_spacing_gives_a_single_panel()
    {
        FlatTruss truss = Build(TrussType.Warren, spacing: 0.0);
        Assert.Equal(1, truss.PanelCount);
    }

    [Theory]
    [InlineData(TrussType.Warren, 6)]                 // one diagonal per panel
    [InlineData(TrussType.WarrenWithVerticals, 11)]   // plus 5 interior verticals
    [InlineData(TrussType.Pratt, 11)]
    [InlineData(TrussType.Howe, 11)]
    [InlineData(TrussType.Vierendeel, 5)]             // interior verticals only
    [InlineData(TrussType.CrossBraced, 17)]           // both diagonals per panel, plus 5 verticals
    public void Web_member_counts_match_the_pattern(TrussType type, int expected)
    {
        FlatTruss truss = Build(type);
        Assert.Equal(expected, truss.Web.Count());
    }

    [Fact]
    public void Chords_are_one_member_per_panel()
    {
        FlatTruss truss = Build(TrussType.Warren);

        Assert.Equal(truss.PanelCount, truss.TopChord.Count());
        Assert.Equal(truss.PanelCount, truss.BottomChord.Count());
    }

    [Fact]
    public void End_posts_are_generated_only_when_asked_for()
    {
        Assert.Equal(2, Build(TrussType.Warren, endPosts: true).EndPosts.Count());
        Assert.Empty(Build(TrussType.Warren, endPosts: false).EndPosts);
    }

    [Fact]
    public void Pratt_and_Howe_are_mirror_images()
    {
        var pratt = Build(TrussType.Pratt).Web.Where(l => !IsVertical(l)).ToList();
        var howe = Build(TrussType.Howe).Web.Where(l => !IsVertical(l)).ToList();

        Assert.Equal(pratt.Count, howe.Count);

        // Every Pratt diagonal rises where the matching Howe diagonal falls.
        for (int i = 0; i < pratt.Count; i++)
            Assert.NotEqual(Math.Sign(pratt[i].Direction.Z), Math.Sign(howe[i].Direction.Z));

        static bool IsVertical(Line line) => Math.Abs(line.Direction.X) < 1e-9;
    }

    [Fact]
    public void A_reversed_bottom_chord_does_not_cross_the_truss()
    {
        Curve reversed = StraightChord(0);
        reversed.Reverse();

        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), reversed, new FlatTrussOptions { Spacing = 2.0 });

        // Aligned chords mean every end post is vertical rather than diagonal.
        Assert.All(truss.EndPosts, post => Assert.Equal(0.0, post.Direction.X, 6));
    }

    [Fact]
    public void Members_carry_node_indices_that_resolve_against_Nodes()
    {
        FlatTruss truss = Build(TrussType.Pratt);
        var nodes = truss.Nodes;

        Assert.All(truss.Members, member =>
        {
            Assert.Equal(nodes[member.StartNode], member.Line.From);
            Assert.Equal(nodes[member.EndNode], member.Line.To);
        });
    }

    [Fact]
    public void Coplanar_chords_report_as_planar()
    {
        Assert.True(Build(TrussType.Warren).IsPlanar);
    }

    [Fact]
    public void Chords_in_different_planes_report_as_non_planar()
    {
        Curve twisted = new LineCurve(new Point3d(0, 0, 0), new Point3d(Span, 5, 0));

        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), twisted, new FlatTrussOptions { Spacing = 2.0 });

        Assert.False(truss.IsPlanar);
    }

    // ---- divisions and snapping -------------------------------------------

    [Fact]
    public void Divisions_set_the_panel_count()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0), new FlatTrussOptions { Divisions = 5 });

        Assert.Equal(5, truss.PanelCount);
        Assert.Equal(6, truss.TopNodes.Count);
    }

    [Fact]
    public void Divisions_win_over_spacing()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new FlatTrussOptions { Divisions = 3, Spacing = 1.0 });   // spacing alone would give 12

        Assert.Equal(3, truss.PanelCount);
    }

    [Fact]
    public void Divisions_move_onto_nearby_points_rather_than_adding_to_them()
    {
        // Four even stations would sit at x = 3, 6, 9. The vertex at 3.4 is
        // within half a panel of the first, so that station moves onto it.
        FlatTruss truss = FlatTrussGenerator.Generate(
            PolylineChord(Depth, 3.4), StraightChord(0), new FlatTrussOptions { Divisions = 4 });

        Assert.Equal(4, truss.PanelCount);                 // count is unchanged
        Assert.Equal(3.4, truss.TopNodes[1].X, 6);         // but the station moved
        Assert.Equal(3.4, truss.BottomNodes[1].X, 6);      // and the truss steps with it

        // What is left of the chord is divided evenly between the vertex and
        // the end, rather than the two panels around it going short and long.
        Assert.Equal(3.4 + (Span - 3.4) / 3.0, truss.TopNodes[2].X, 6);
        Assert.Equal(3.4 + (Span - 3.4) * 2.0 / 3.0, truss.TopNodes[3].X, 6);
    }

    /// <summary>
    /// The point of spreading: a snapped node is a fixed point, and what lies
    /// between two fixed points is divided evenly. Anything else leaves one
    /// short panel and one long one against an otherwise regular truss.
    /// </summary>
    [Fact]
    public void Everything_after_a_snapped_node_is_divided_evenly_again()
    {
        // Six panels put stations at x = 2, 4, 6, 8, 10. The vertex at 2.5 is
        // within reach of the first, so it anchors there.
        FlatTruss truss = FlatTrussGenerator.Generate(
            PolylineChord(Depth, 2.5), StraightChord(0), new FlatTrussOptions { Divisions = 6 });

        Assert.Equal(6, truss.PanelCount);
        Assert.Equal(2.5, truss.TopNodes[1].X, 6);

        // 2.5 to 12 in five equal panels of 1.9.
        for (int i = 1; i <= 6; i++)
            Assert.Equal(2.5 + 1.9 * (i - 1), truss.TopNodes[i].X, 6);
    }

    [Fact]
    public void A_chord_with_nothing_to_snap_to_is_spread_evenly_end_to_end()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0), new FlatTrussOptions { Divisions = 4 });

        for (int i = 0; i <= 4; i++)
            Assert.Equal(i * Span / 4.0, truss.TopNodes[i].X, 6);
    }

    [Fact]
    public void A_point_further_than_half_a_panel_away_is_left_alone()
    {
        // Stations at x = 6 only. The vertex at 1.0 is 5 units away, well beyond
        // the 3-unit reach, so nothing snaps.
        FlatTruss truss = FlatTrussGenerator.Generate(
            PolylineChord(Depth, 1.0), StraightChord(0), new FlatTrussOptions { Divisions = 2 });

        Assert.Equal(2, truss.PanelCount);
        Assert.Equal(6.0, truss.TopNodes[1].X, 6);
    }

    [Fact]
    public void Two_stations_never_collapse_onto_the_same_point()
    {
        // Two vertices crowded around the single interior station at x = 6.
        FlatTruss truss = FlatTrussGenerator.Generate(
            PolylineChord(Depth, 5.8, 6.2), StraightChord(0), new FlatTrussOptions { Divisions = 2 });

        Assert.Equal(2, truss.PanelCount);
        Assert.Equal(3, truss.TopNodes.Count);
        Assert.Equal(5.8, truss.TopNodes[1].X, 6);         // nearest wins, the other is ignored
    }

    [Fact]
    public void Additional_points_snap_the_divisions_too()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new FlatTrussOptions
            {
                Divisions = 2,
                AdditionalSnapPoints = new[] { new Point3d(5.0, 0, Depth) },
            });

        Assert.Equal(2, truss.PanelCount);
        Assert.Equal(5.0, truss.TopNodes[1].X, 6);
    }

    [Fact]
    public void Negative_divisions_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0), new FlatTrussOptions { Divisions = -1 }));
    }

    // ---- the two chords step together --------------------------------------

    /// <summary>
    /// A panel point is where the whole truss steps, so a point belonging to
    /// either chord gives both of them a node. Dividing each chord against only
    /// its own points would put node <c>i</c> at a different plan position on
    /// each and leave the member between them leaning.
    /// </summary>
    [Fact]
    public void Both_chords_step_at_every_snap_point()
    {
        // Four panels put both chords at x = 0, 3, 6, 9, 12. The top chord has a
        // vertex near its second station, the bottom chord near its fourth.
        FlatTruss truss = FlatTrussGenerator.Generate(
            PolylineChord(Depth, 3.4), PolylineChord(0, 8.0),
            new FlatTrussOptions { Divisions = 4, Type = TrussType.Vierendeel });

        Assert.Equal(4, truss.PanelCount);

        // Both vertices anchor both chords, and the one station left over is
        // spread between them.
        double[] expected = { 0.0, 3.4, (3.4 + 8.0) / 2.0, 8.0, Span };

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], truss.TopNodes[i].X, 6);
            Assert.Equal(expected[i], truss.BottomNodes[i].X, 6);
        }

        Assert.All(truss.Verticals, line => Assert.Equal(0.0, line.Direction.X, 6));
    }

    [Fact]
    public void A_point_beside_one_chord_is_measured_there_but_moves_both()
    {
        // The point sits on the bottom chord, so the bottom chord is
        // what it is measured against — but the station it lands on is a plan
        // position, and the truss steps there as a whole.
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new FlatTrussOptions
            {
                Divisions = 2,
                AdditionalSnapPoints = new[] { new Point3d(5.0, 0, 0.0) },
            });

        Assert.Equal(5.0, truss.BottomNodes[1].X, 6);
        Assert.Equal(5.0, truss.TopNodes[1].X, 6);
    }

    [Fact]
    public void Chords_keep_matching_node_counts_however_much_they_snap()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            PolylineChord(Depth, 2.6, 5.2, 9.4),
            PolylineChord(0, 3.3, 6.4),
            new FlatTrussOptions { Divisions = 5 });

        Assert.Equal(truss.TopNodes.Count, truss.BottomNodes.Count);
        Assert.Equal(6, truss.TopNodes.Count);
    }

    [Fact]
    public void Snapped_chords_still_pair_by_index()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            PolylineChord(Depth, 3.4), PolylineChord(0, 8.0),
            new FlatTrussOptions { Divisions = 4, Type = TrussType.Vierendeel });

        var nodes = truss.Nodes;

        Assert.All(truss.Members, member =>
        {
            Assert.Equal(nodes[member.StartNode], member.Line.From);
            Assert.Equal(nodes[member.EndNode], member.Line.To);
        });
    }

    // ---- flip --------------------------------------------------------------

    /// <summary>
    /// Order-independent key for a member, so the same brace drawn either way
    /// round compares equal.
    /// </summary>
    private static string Key(Line line)
    {
        static string Corner(Point3d p) => $"{p.X:F4},{p.Y:F4},{p.Z:F4}";

        string from = Corner(line.From);
        string to = Corner(line.To);
        return string.CompareOrdinal(from, to) <= 0 ? from + "|" + to : to + "|" + from;
    }

    private static HashSet<string> WebKeys(TrussType type, bool flip)
        => FlatTrussGenerator.Generate(
                StraightChord(Depth), StraightChord(0),
                new FlatTrussOptions { Type = type, Divisions = 6, Flip = flip })
            .Web.Select(Key)
            .ToHashSet();

    [Fact]
    public void Flipping_Pratt_gives_Howe()
    {
        Assert.True(WebKeys(TrussType.Pratt, flip: true).SetEquals(WebKeys(TrussType.Howe, flip: false)));
    }

    [Fact]
    public void Flipping_Howe_gives_Pratt()
    {
        Assert.True(WebKeys(TrussType.Howe, flip: true).SetEquals(WebKeys(TrussType.Pratt, flip: false)));
    }

    [Fact]
    public void Flipping_Warren_changes_the_bracing_without_changing_the_count()
    {
        var upright = WebKeys(TrussType.Warren, flip: false);
        var flipped = WebKeys(TrussType.Warren, flip: true);

        Assert.Equal(upright.Count, flipped.Count);
        Assert.False(upright.SetEquals(flipped));
    }

    [Theory]
    [InlineData(TrussType.CrossBraced)]   // both diagonals already drawn
    [InlineData(TrussType.Vierendeel)]    // no diagonals to mirror
    public void Flip_is_a_no_op_for_symmetric_patterns(TrussType type)
    {
        Assert.True(WebKeys(type, flip: false).SetEquals(WebKeys(type, flip: true)));
    }

    [Fact]
    public void Cross_bracing_includes_the_interior_verticals()
    {
        FlatTruss truss = Build(TrussType.CrossBraced);

        Assert.Equal(truss.PanelCount - 1, truss.Verticals.Count());
        Assert.Equal(2 * truss.PanelCount, truss.Diagonals.Count());
    }

    /// <summary>
    /// The roles partition the web, and they agree with the geometry: on a
    /// parallel-chord truss a vertical member is the one with no run.
    /// </summary>
    [Theory]
    [InlineData(TrussType.Warren)]
    [InlineData(TrussType.WarrenWithVerticals)]
    [InlineData(TrussType.Pratt)]
    [InlineData(TrussType.Howe)]
    [InlineData(TrussType.Vierendeel)]
    [InlineData(TrussType.CrossBraced)]
    public void Verticals_and_diagonals_partition_the_web(TrussType type)
    {
        FlatTruss truss = Build(type);

        Assert.Equal(truss.Web.Count(), truss.Verticals.Count() + truss.Diagonals.Count());
        Assert.All(truss.Verticals, l => Assert.Equal(0.0, l.Direction.X, 9));
        Assert.All(truss.Diagonals, l => Assert.NotEqual(0.0, Math.Round(l.Direction.X, 9)));
    }

    // ---- what the front-ends read off the result ---------------------------

    [Theory]
    [InlineData(TrussMemberRole.TopChord, "Top chord")]
    [InlineData(TrussMemberRole.BottomChord, "Bottom chord")]
    [InlineData(TrussMemberRole.Vertical, "Vertical")]
    [InlineData(TrussMemberRole.Diagonal, "Diagonal")]
    [InlineData(TrussMemberRole.EndPost, "End post")]
    public void Roles_name_themselves_the_same_way_for_every_front_end(TrussMemberRole role, string expected)
    {
        Assert.Equal(expected, role.DisplayName());
    }

    [Fact]
    public void A_parallel_chord_truss_has_a_distinct_node_for_every_node()
    {
        FlatTruss truss = Build(TrussType.Warren);
        Assert.Equal(truss.Nodes.Count, truss.DistinctNodes.Count);
    }

    /// <summary>
    /// A generated truss is a result, and a result nobody can edit behind your
    /// back. Declaring the collections read-only is not enough on its own — an
    /// <c>IReadOnlyList</c> over a live array or list is one cast away from
    /// being writable — so this checks the wrapping, not just the signature.
    /// </summary>
    [Fact]
    public void Nothing_can_reach_into_a_generated_truss_and_edit_it()
    {
        FlatTruss truss = Build(TrussType.Warren);
        TrussMember member = truss.Members[0];

        Assert.Throws<NotSupportedException>(() => ((IList<TrussMember>)truss.Members).Add(member));
        Assert.Throws<NotSupportedException>(() => ((IList<Point3d>)truss.TopNodes).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Point3d>)truss.BottomNodes).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Point3d>)truss.Nodes).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Point3d>)truss.DistinctNodes).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<FormNote>)truss.Notes).Clear());

        // The array the generator built is not the array handed out.
        Assert.Null(truss.TopNodes as Point3d[]);
        Assert.Null(truss.Members as List<TrussMember>);
    }

    [Fact]
    public void The_truss_says_nothing_when_there_is_nothing_to_say()
    {
        Assert.Empty(Build(TrussType.Warren).Notes);
    }

    [Fact]
    public void A_warped_truss_says_so()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth),
            new LineCurve(new Point3d(0, 5, 0), new Point3d(Span, 0, 0)),   // out of the top chord's plane
            new FlatTrussOptions { Divisions = 4 });

        Assert.False(truss.IsPlanar);
        Assert.Contains(truss.Notes, n => n.Level == FormNoteLevel.Warning);
    }

    [Fact]
    public void An_undefined_truss_type_is_rejected_before_anything_is_built()
    {
        var options = new FlatTrussOptions { Type = (TrussType)99, Divisions = 4 };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => FlatTrussGenerator.Generate(StraightChord(Depth), StraightChord(0), options));

        Assert.Contains("99", error.Message);
    }

    // ---- set out on plan ---------------------------------------------------

    /// <summary>
    /// The reason panels are measured in plan: a pitched chord is longer than
    /// the level one below it, so dividing each along its own length staggers
    /// the pairs and every vertical comes out leaning.
    /// </summary>
    [Fact]
    public void Panels_are_set_out_on_plan_so_the_verticals_stand_up()
    {
        Curve top = new LineCurve(new Point3d(0, 0, 1), new Point3d(Span, 0, 5));

        FlatTruss truss = FlatTrussGenerator.Generate(
            top, StraightChord(0),
            new FlatTrussOptions { Divisions = 6, Type = TrussType.Vierendeel, MeasureOnPlan = true });

        for (int i = 0; i < truss.TopNodes.Count; i++)
        {
            Assert.Equal(i * Span / 6.0, truss.TopNodes[i].X, 6);       // even on plan
            Assert.Equal(truss.TopNodes[i].X, truss.BottomNodes[i].X, 6);
        }

        // Which is the same as saying every vertical is vertical.
        Assert.All(truss.Verticals, line => Assert.Equal(0.0, line.Direction.X, 6));
    }

    [Fact]
    public void A_curved_chord_is_still_divided_evenly_on_plan()
    {
        var arc = new ArcCurve(new Arc(
            new Point3d(0, 0, 1), new Point3d(Span / 2, 0, 4), new Point3d(Span, 0, 1)));

        FlatTruss truss = FlatTrussGenerator.Generate(
            arc, StraightChord(0),
            new FlatTrussOptions { Divisions = 6, Type = TrussType.Vierendeel, MeasureOnPlan = true });

        for (int i = 0; i < truss.TopNodes.Count; i++)
            Assert.Equal(i * Span / 6.0, truss.TopNodes[i].X, 6);

        Assert.All(truss.Verticals, line => Assert.Equal(0.0, line.Direction.X, 6));
    }

    /// <summary>
    /// The default: each chord divided by its own length. On an arc that is
    /// equal steps round the curve, which on plan are anything but equal —
    /// and is the only division an organic chord can be said to have.
    /// </summary>
    [Fact]
    public void By_default_a_curved_chord_is_divided_along_itself()
    {
        var arc = new ArcCurve(new Arc(
            new Point3d(0, 0, 1), new Point3d(Span / 2, 0, 4), new Point3d(Span, 0, 1)));

        FlatTruss truss = FlatTrussGenerator.Generate(
            arc, StraightChord(0), new FlatTrussOptions { Divisions = 6, Type = TrussType.Vierendeel });

        for (int i = 0; i < truss.TopNodes.Count; i++)
        {
            Assert.True(truss.TopNodes[i].DistanceTo(arc.PointAtNormalizedLength(i / 6.0)) < 1e-6);
            Assert.Equal(i * Span / 6.0, truss.BottomNodes[i].X, 6);
        }

        // Not the plan answer: the arc is steeper at its ends, so an equal step
        // along it covers less ground there.
        Assert.True(truss.TopNodes[1].X < Span / 6.0 - 0.01);
    }

    /// <summary>
    /// What the default is for. Two chords standing on end have no plan length,
    /// and one longer than the other still has to be divided in proportion.
    /// </summary>
    [Fact]
    public void By_default_spacing_is_measured_along_the_chord()
    {
        // 3-4-5: 15 long over a plan run of 9.
        Curve top = new LineCurve(new Point3d(0, 0, 2), new Point3d(9, 0, 14));
        Curve bottom = new LineCurve(new Point3d(0, 0, 0), new Point3d(9, 0, 12));

        var along = FlatTrussGenerator.Generate(top, bottom, new FlatTrussOptions { Spacing = 3.0 });
        var onPlan = FlatTrussGenerator.Generate(
            top, bottom, new FlatTrussOptions { Spacing = 3.0, MeasureOnPlan = true });

        Assert.Equal(5, along.PanelCount);      // 15 / 3
        Assert.Equal(3, onPlan.PanelCount);     //  9 / 3
    }

    /// <summary>
    /// A truss standing in a plane the plan view looks along has no plan length
    /// to divide, so it falls back to measuring along the chords themselves
    /// rather than dividing by zero.
    /// </summary>
    [Fact]
    public void A_truss_edge_on_in_plan_still_generates()
    {
        Curve top = new LineCurve(new Point3d(3, 4, 2), new Point3d(3, 4, 14));
        Curve bottom = new LineCurve(new Point3d(5, 7, 2), new Point3d(5, 7, 14));

        FlatTruss truss = FlatTrussGenerator.Generate(
            top, bottom, new FlatTrussOptions { Divisions = 4, MeasureOnPlan = true });

        Assert.Equal(4, truss.PanelCount);
        Assert.All(truss.TopNodes, n => Assert.True(n.IsValid));

        for (int i = 0; i <= 4; i++)
            Assert.Equal(2.0 + i * 3.0, truss.TopNodes[i].Z, 6);
    }

    // ---- chords that meet at an end ---------------------------------------

    /// <summary>A chord running from a shared apex out to the far end.</summary>
    private static Curve TaperedChord(Point3d apex, double z)
        => new LineCurve(apex, new Point3d(Span, 0, z));

    [Fact]
    public void No_end_post_where_the_chords_meet()
    {
        Point3d apex = new(0, 0, 1);

        FlatTruss truss = FlatTrussGenerator.Generate(
            TaperedChord(apex, Depth), TaperedChord(apex, 0),
            new FlatTrussOptions { Divisions = 4, GenerateEndPosts = true });

        Assert.True(truss.ChordsMeetAtStart);
        Assert.False(truss.ChordsMeetAtEnd);
        Assert.Single(truss.EndPosts);                     // only the open end is closed
    }

    [Fact]
    public void No_end_posts_at_all_when_both_ends_meet()
    {
        Point3d left = new(0, 0, 1);
        Point3d right = new(Span, 0, 1);

        var top = new PolylineCurve(new[] { left, new Point3d(Span / 2, 0, Depth), right });
        var bottom = new PolylineCurve(new[] { left, new Point3d(Span / 2, 0, 0), right });

        FlatTruss truss = FlatTrussGenerator.Generate(
            top, bottom, new FlatTrussOptions { Divisions = 6, GenerateEndPosts = true });

        Assert.True(truss.ChordsMeetAtStart);
        Assert.True(truss.ChordsMeetAtEnd);
        Assert.Empty(truss.EndPosts);

        // The two apexes are one point each, however many nodes index them.
        Assert.Equal(truss.Nodes.Count - 2, truss.DistinctNodes.Count);

        // And that is worth saying out loud, since end posts were asked for.
        FormNote note = Assert.Single(truss.Notes);
        Assert.Equal(FormNoteLevel.Remark, note.Level);
        Assert.Contains("both ends", note.Message);
    }

    [Fact]
    public void Chords_that_meet_say_nothing_when_no_end_posts_were_wanted()
    {
        Point3d apex = new(0, 0, 1);

        FlatTruss truss = FlatTrussGenerator.Generate(
            TaperedChord(apex, Depth), TaperedChord(apex, 0),
            new FlatTrussOptions { Divisions = 4, GenerateEndPosts = false });

        Assert.True(truss.ChordsMeetAtStart);
        Assert.Empty(truss.Notes);
    }

    [Fact]
    public void Separated_chords_still_report_as_not_meeting()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0), new FlatTrussOptions { Divisions = 4 });

        Assert.False(truss.ChordsMeetAtStart);
        Assert.False(truss.ChordsMeetAtEnd);
        Assert.Equal(2, truss.EndPosts.Count());
    }

    [Fact]
    public void A_null_chord_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => FlatTrussGenerator.Generate(null!, StraightChord(0)));
    }

    // ---- which snap points win --------------------------------------------

    /// <summary>
    /// The chords' own points come first. A truss whose node sits anywhere but
    /// a kink has a chord member cutting that corner, so a vertex outranks a
    /// picked point even when the picked point is nearer.
    /// </summary>
    [Fact]
    public void A_chord_vertex_beats_a_picked_point_that_is_closer()
    {
        // Two panels put the one interior station at x = 6, reaching 3 either
        // way. The picked point is 0.2 from it; the vertex is 0.5.
        FlatTruss truss = FlatTrussGenerator.Generate(
            PolylineChord(Depth, 6.5), StraightChord(0),
            new FlatTrussOptions
            {
                Divisions = 2,
                AdditionalSnapPoints = new[] { new Point3d(5.8, 0, Depth) },
            });

        Assert.Equal(2, truss.PanelCount);
        Assert.Equal(6.5, truss.TopNodes[1].X, 6);
    }

    /// <summary>
    /// Once the vertices have taken what they can reach, the picked points are
    /// offered whatever is left — they are not thrown away.
    /// </summary>
    [Fact]
    public void A_picked_point_still_takes_a_station_no_vertex_wanted()
    {
        // Four panels: stations at x = 3, 6, 9. The vertex reaches the first,
        // the picked point the second, and neither competes for the other.
        FlatTruss truss = FlatTrussGenerator.Generate(
            PolylineChord(Depth, 3.4), StraightChord(0),
            new FlatTrussOptions
            {
                Divisions = 4,
                Type = TrussType.Vierendeel,
                AdditionalSnapPoints = new[] { new Point3d(6.4, 0, Depth) },
            });

        Assert.Equal(4, truss.PanelCount);
        Assert.Equal(3.4, truss.TopNodes[1].X, 6);
        Assert.Equal(6.4, truss.TopNodes[2].X, 6);
    }

    [Fact]
    public void Only_the_nearest_of_several_picked_points_is_used()
    {
        // Both are within reach of the single station at x = 6: one a unit
        // away, the other 0.4.
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new FlatTrussOptions
            {
                Divisions = 2,
                AdditionalSnapPoints = new[]
                {
                    new Point3d(5.0, 0, Depth),
                    new Point3d(6.4, 0, Depth),
                },
            });

        Assert.Equal(2, truss.PanelCount);
        Assert.Equal(3, truss.TopNodes.Count);
        Assert.Equal(6.4, truss.TopNodes[1].X, 6);
    }

    /// <summary>
    /// Reach is capped at half a panel, so a point never moves a node past
    /// its neighbour and folds the truss over.
    /// </summary>
    [Fact]
    public void A_distant_point_cannot_reorder_the_stations()
    {
        // Six panels put stations every 2 units and let each move 1 either way,
        // so a point at 6.5 is within reach of the station at 6 and no other.
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new FlatTrussOptions
            {
                Divisions = 6,
                AdditionalSnapPoints = new[] { new Point3d(6.5, 0, Depth) },
            });

        Assert.Equal(6, truss.PanelCount);
        Assert.Equal(6.5, truss.TopNodes[3].X, 6);

        for (int i = 1; i < truss.TopNodes.Count; i++)
            Assert.True(truss.TopNodes[i].X > truss.TopNodes[i - 1].X);
    }

    [Fact]
    public void Picked_points_are_stations_in_their_own_right_without_divisions()
    {
        // No division driver, so nothing is competing for a fixed node count
        // and the priority rule has nothing to decide.
        FlatTruss truss = FlatTrussGenerator.Generate(
            PolylineChord(Depth, 4.0), StraightChord(0),
            new FlatTrussOptions
            {
                Divisions = 0,
                AdditionalSnapPoints = new[] { new Point3d(9.0, 0, Depth) },
            });

        Assert.Equal(4, truss.TopNodes.Count);
        Assert.Equal(4.0, truss.TopNodes[1].X, 6);
        Assert.Equal(9.0, truss.TopNodes[2].X, 6);
    }

    // ---- no diagonal where the chords meet --------------------------------

    /// <summary>No member is drawn twice, whatever it is called.</summary>
    private static void AssertNoMemberIsDrawnTwice(FlatTruss truss)
    {
        var keys = truss.Members.Select(m => Key(m.Line)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void No_end_diagonal_where_the_chords_meet_at_the_start()
    {
        Point3d apex = new(0, 0, 1);

        FlatTruss truss = FlatTrussGenerator.Generate(
            TaperedChord(apex, Depth), TaperedChord(apex, 0),
            new FlatTrussOptions { Divisions = 4, Type = TrussType.Warren });

        Assert.True(truss.ChordsMeetAtStart);
        Assert.Equal(4, truss.PanelCount);

        // Four panels, but the one at the apex has no room for a diagonal: it
        // would run from the shared point along a chord and double it.
        Assert.Equal(3, truss.Diagonals.Count());
        AssertNoMemberIsDrawnTwice(truss);
    }

    [Fact]
    public void No_end_diagonal_where_the_chords_meet_at_the_end()
    {
        Point3d apex = new(Span, 0, 1);

        Curve top = new LineCurve(new Point3d(0, 0, Depth), apex);
        Curve bottom = new LineCurve(new Point3d(0, 0, 0), apex);

        FlatTruss truss = FlatTrussGenerator.Generate(
            top, bottom, new FlatTrussOptions { Divisions = 4, Type = TrussType.Warren });

        Assert.True(truss.ChordsMeetAtEnd);
        Assert.False(truss.ChordsMeetAtStart);
        Assert.Equal(3, truss.Diagonals.Count());
        AssertNoMemberIsDrawnTwice(truss);
    }

    [Fact]
    public void No_end_diagonals_at_either_end_when_both_ends_meet()
    {
        Point3d left = new(0, 0, 1);
        Point3d right = new(Span, 0, 1);

        var top = new PolylineCurve(new[] { left, new Point3d(Span / 2, 0, Depth), right });
        var bottom = new PolylineCurve(new[] { left, new Point3d(Span / 2, 0, 0), right });

        FlatTruss truss = FlatTrussGenerator.Generate(
            top, bottom, new FlatTrussOptions { Divisions = 6, Type = TrussType.Warren });

        Assert.True(truss.ChordsMeetAtStart);
        Assert.True(truss.ChordsMeetAtEnd);
        Assert.Equal(6, truss.PanelCount);
        Assert.Equal(4, truss.Diagonals.Count());   // six panels, both ends dropped
        AssertNoMemberIsDrawnTwice(truss);
    }

    /// <summary>
    /// Cross bracing draws both diagonals of a panel, and at a meeting end both
    /// of them double a chord, so both go.
    /// </summary>
    [Fact]
    public void Both_cross_braces_go_at_a_meeting_end()
    {
        Point3d apex = new(0, 0, 1);

        FlatTruss truss = FlatTrussGenerator.Generate(
            TaperedChord(apex, Depth), TaperedChord(apex, 0),
            new FlatTrussOptions { Divisions = 4, Type = TrussType.CrossBraced });

        Assert.Equal(6, truss.Diagonals.Count());   // two per panel, less the apex panel
        AssertNoMemberIsDrawnTwice(truss);
    }

    /// <summary>
    /// Only the panel at the meeting end loses its diagonal. Verticals were
    /// never drawn at the ends, and the chords themselves are untouched.
    /// </summary>
    [Fact]
    public void Nothing_but_the_end_diagonal_is_dropped()
    {
        Point3d apex = new(0, 0, 1);

        FlatTruss truss = FlatTrussGenerator.Generate(
            TaperedChord(apex, Depth), TaperedChord(apex, 0),
            new FlatTrussOptions { Divisions = 4, Type = TrussType.Pratt });

        Assert.Equal(4, truss.TopChord.Count());
        Assert.Equal(4, truss.BottomChord.Count());
        Assert.Equal(3, truss.Verticals.Count());   // interior only, as always
        Assert.Single(truss.EndPosts);              // the open end
    }

    [Fact]
    public void Separated_chords_keep_every_diagonal()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new FlatTrussOptions { Divisions = 4, Type = TrussType.Warren });

        Assert.Equal(4, truss.Diagonals.Count());
        AssertNoMemberIsDrawnTwice(truss);
    }

    // ---- strictness --------------------------------------------------------

    private static FlatTruss Snapped(SnapStrictness strictness, int divisions, params double[] xs)
        => FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new FlatTrussOptions
            {
                Divisions = divisions,
                Strictness = strictness,
                AdditionalSnapPoints = xs.Select(x => new Point3d(x, 0, Depth)).ToArray(),
            });

    [Fact]
    public void Relaxed_is_the_default()
    {
        Assert.Equal(SnapStrictness.Relaxed, new FlatTrussOptions().Strictness);
    }

    /// <summary>
    /// The case the whole option exists for. Four panels put stations every
    /// 3 units and let each reach 1.5; a point at 0.6 is 2.4 from the nearest,
    /// so the regular layout cannot honour it.
    /// </summary>
    [Fact]
    public void Relaxed_leaves_an_unreachable_point_alone_and_says_so()
    {
        FlatTruss truss = Snapped(SnapStrictness.Relaxed, 4, 0.6);

        Assert.Equal(4, truss.PanelCount);
        Assert.Equal(1, truss.UnusedSnapPoints);
        Assert.DoesNotContain(truss.TopNodes, n => Math.Abs(n.X - 0.6) < 1e-6);

        // Invisible on the geometry, so the truss has to say it out loud.
        Assert.Contains(truss.Notes, n => n.Message.Contains("Strict"));
    }

    [Fact]
    public void Strict_places_a_node_on_the_same_point()
    {
        FlatTruss truss = Snapped(SnapStrictness.Strict, 4, 0.6);

        Assert.Equal(4, truss.PanelCount);          // the count still holds
        Assert.Equal(0, truss.UnusedSnapPoints);
        Assert.Equal(0.6, truss.TopNodes[1].X, 6);

        // What is left of the chord is divided evenly among the panels left.
        for (int i = 2; i < truss.TopNodes.Count; i++)
            Assert.Equal(0.6 + (Span - 0.6) * (i - 1) / 3.0, truss.TopNodes[i].X, 6);
    }

    [Fact]
    public void Strict_honours_every_point_where_relaxed_honours_what_it_can_reach()
    {
        double[] points = { 1.0, 5.5, 11.4 };

        FlatTruss relaxed = Snapped(SnapStrictness.Relaxed, 6, points);
        FlatTruss strict = Snapped(SnapStrictness.Strict, 6, points);

        Assert.Equal(2, relaxed.UnusedSnapPoints);
        Assert.Equal(0, strict.UnusedSnapPoints);

        Assert.All(points, x => Assert.Contains(strict.TopNodes, n => Math.Abs(n.X - x) < 1e-6));

        // Neither one changed how many members there are.
        Assert.Equal(6, relaxed.PanelCount);
        Assert.Equal(6, strict.PanelCount);
    }

    /// <summary>
    /// Strict shares the panels between the fixed points rather than across the
    /// whole truss, so each bay is regular on its own.
    /// </summary>
    [Fact]
    public void Strict_divides_each_bay_evenly_within_itself()
    {
        // One point at mid-span: two bays of equal width, three panels each.
        FlatTruss truss = Snapped(SnapStrictness.Strict, 6, 6.0);

        Assert.Equal(6, truss.PanelCount);

        for (int i = 0; i <= 6; i++)
            Assert.Equal(i * 2.0, truss.TopNodes[i].X, 6);
    }

    [Fact]
    public void Strict_grows_the_panel_count_rather_than_drop_a_point()
    {
        FlatTruss truss = Snapped(SnapStrictness.Strict, 2, 1.0, 4.0, 7.0, 10.0);

        Assert.Equal(0, truss.UnusedSnapPoints);
        Assert.Equal(5, truss.PanelCount);          // four points plus both ends
        Assert.Contains(truss.Notes, n => n.Message.Contains("5 panels"));
    }

    [Fact]
    public void Strict_still_honours_the_chords_own_vertices()
    {
        var top = new PolylineCurve(new[]
        {
            new Point3d(0, 0, Depth), new Point3d(4.3, 0, Depth + 1), new Point3d(Span, 0, Depth),
        });

        FlatTruss truss = FlatTrussGenerator.Generate(top, StraightChord(0), new FlatTrussOptions
        {
            Divisions = 5,
            Strictness = SnapStrictness.Strict,
            // On the sloped run of the chord, so projecting it onto that chord
            // does not move it: a point off to the side lands at the foot of
            // the perpendicular, which on a slope is not the plan position it
            // was picked at.
            AdditionalSnapPoints = new[] { new Point3d(2.15, 0, Depth + 0.5) },
        });

        // The kink and the picked point are both nodes; a chord member across
        // the kink would cut the corner off the truss.
        Assert.Contains(truss.TopNodes, n => Math.Abs(n.X - 4.3) < 1e-6);
        Assert.Contains(truss.TopNodes, n => Math.Abs(n.X - 2.15) < 1e-6);
        Assert.Equal(5, truss.PanelCount);
    }

    /// <summary>
    /// The on-chord rule is not a strictness setting. Strict decides what the
    /// division owes a point it can see; a point off the chords is not one it
    /// can see at all.
    /// </summary>
    [Theory]
    [InlineData(SnapStrictness.Relaxed)]
    [InlineData(SnapStrictness.Strict)]
    public void A_point_off_both_chords_is_discounted_under_either_rule(SnapStrictness strictness)
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new FlatTrussOptions
            {
                Divisions = 4,
                Strictness = strictness,
                AdditionalSnapPoints = new[] { new Point3d(6.0, 5.0, Depth) },   // 5 units aside
            });

        Assert.Equal(1, truss.OffChordSnapPoints);
        Assert.Equal(0, truss.UnusedSnapPoints);       // it never got as far as being unused
        Assert.Equal(4, truss.PanelCount);

        // Loud, because the truss looks perfectly reasonable without it.
        FormNote note = Assert.Single(truss.Notes);
        Assert.Equal(FormNoteLevel.Warning, note.Level);
        Assert.Contains("either chord", note.Message);
    }

    [Fact]
    public void A_point_on_the_bottom_chord_counts_just_as_well_as_one_on_the_top()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new FlatTrussOptions
            {
                Divisions = 4,
                Strictness = SnapStrictness.Strict,
                AdditionalSnapPoints = new[] { new Point3d(0.6, 0, 0.0) },   // on the bottom chord
            });

        Assert.Equal(0, truss.OffChordSnapPoints);
        Assert.Equal(0.6, truss.TopNodes[1].X, 6);
        Assert.Equal(0.6, truss.BottomNodes[1].X, 6);
    }

    /// <summary>
    /// On the curve means on it within the document tolerance, which is what
    /// Rhino's own On-Curve osnap lands inside. Float noise from a computed
    /// point must not disqualify it.
    /// </summary>
    [Fact]
    public void A_point_a_hair_off_the_chord_still_counts()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new FlatTrussOptions
            {
                Divisions = 4,
                SnapTolerance = 0.01,
                Strictness = SnapStrictness.Strict,
                AdditionalSnapPoints = new[] { new Point3d(0.6, 0, Depth - 0.005) },
            });

        Assert.Equal(0, truss.OffChordSnapPoints);
        Assert.Equal(0.6, truss.TopNodes[1].X, 6);
    }

    /// <summary>
    /// The case the on-chord rule exists for. A point beside a curved chord
    /// projects to the foot of the perpendicular, which is not the plan
    /// position it was picked at; taken off the curve itself there is nothing
    /// to drift.
    /// </summary>
    [Fact]
    public void A_curved_chord_takes_a_point_that_lies_on_it()
    {
        var arc = new ArcCurve(new Arc(
            new Point3d(0, 0, 1), new Point3d(Span / 2, 0, 4), new Point3d(Span, 0, 1)));

        Point3d onArc = arc.PointAtNormalizedLength(0.25);

        FlatTruss truss = FlatTrussGenerator.Generate(
            arc, StraightChord(0), new FlatTrussOptions
            {
                Divisions = 5,
                MeasureOnPlan = true,
                Strictness = SnapStrictness.Strict,
                Type = TrussType.Vierendeel,
                AdditionalSnapPoints = new[] { onArc },
            });

        Assert.Equal(0, truss.OffChordSnapPoints);
        Assert.Contains(truss.TopNodes, n => n.DistanceTo(onArc) < 1e-6);

        // And the pair still steps together, which is what plan stations exist
        // to protect.
        Assert.All(truss.Verticals, line => Assert.Equal(0.0, line.Direction.X, 6));
    }

    [Fact]
    public void Strictness_is_moot_when_the_geometry_drives()
    {
        // No division to argue with, so both rules put a node on every point.
        foreach (SnapStrictness mode in Enum.GetValues<SnapStrictness>())
        {
            FlatTruss truss = Snapped(mode, 0, 0.6, 5.5);

            Assert.Equal(0, truss.UnusedSnapPoints);
            Assert.Equal(0.6, truss.TopNodes[1].X, 6);
            Assert.Equal(5.5, truss.TopNodes[2].X, 6);
        }
    }

    [Fact]
    public void Strict_keeps_the_nodes_in_order()
    {
        FlatTruss truss = Snapped(SnapStrictness.Strict, 7, 0.4, 0.9, 11.6);

        for (int i = 1; i < truss.TopNodes.Count; i++)
            Assert.True(truss.TopNodes[i].X > truss.TopNodes[i - 1].X);
    }

    [Fact]
    public void An_undefined_strictness_is_rejected()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => FlatTrussGenerator.Generate(
                StraightChord(Depth), StraightChord(0),
                new FlatTrussOptions { Divisions = 4, Strictness = (SnapStrictness)42 }));

        Assert.Contains("42", error.Message);
    }

    [Fact]
    public void A_truss_with_nothing_unused_says_nothing_about_snap_points()
    {
        FlatTruss truss = Snapped(SnapStrictness.Relaxed, 4, 3.4);

        Assert.Equal(0, truss.UnusedSnapPoints);
        Assert.Empty(truss.Notes);
    }

    // ---- spacing -----------------------------------------------------------

    [Fact]
    public void Spacing_is_overridden_by_divisions()
    {
        FlatTruss truss = FlatTrussGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new FlatTrussOptions { Divisions = 3, Spacing = 1.0 });

        Assert.Equal(3, truss.PanelCount);
    }

    [Fact]
    public void A_negative_spacing_is_rejected()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => FlatTrussGenerator.Generate(
                StraightChord(Depth), StraightChord(0), new FlatTrussOptions { Spacing = -1.0 }));

        Assert.Contains("Spacing", error.Message);
    }
}
