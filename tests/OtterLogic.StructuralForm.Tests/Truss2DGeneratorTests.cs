using OtterLogic.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

[Collection(RhinoCollection.Name)]
public class Truss2DGeneratorTests
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

    private static Truss2D Build(TrussType type, double spacing = 2.0, bool endPosts = true)
        => Truss2DGenerator.Generate(
            StraightChord(Depth),
            StraightChord(0),
            new Truss2DOptions { Type = type, SnapSpacing = spacing, GenerateEndPosts = endPosts });

    [Fact]
    public void Spacing_sets_the_panel_count()
    {
        Truss2D truss = Build(TrussType.Warren, spacing: 2.0);

        Assert.Equal(6, truss.PanelCount);                 // 12 / 2
        Assert.Equal(7, truss.TopNodes.Count);
        Assert.Equal(7, truss.BottomNodes.Count);
    }

    [Fact]
    public void Polyline_vertices_become_nodes_without_any_spacing()
    {
        Truss2D truss = Truss2DGenerator.Generate(
            PolylineChord(Depth, 3.0, 7.0),
            StraightChord(0),
            new Truss2DOptions { SnapSpacing = 0.0 });

        Assert.Equal(4, truss.TopNodes.Count);             // ends plus the two kinks
        Assert.Equal(3.0, truss.TopNodes[1].X, 6);
        Assert.Equal(7.0, truss.TopNodes[2].X, 6);
    }

    [Fact]
    public void A_vertex_on_either_chord_creates_a_node_on_both_when_geometry_drives()
    {
        Truss2D truss = Truss2DGenerator.Generate(
            StraightChord(Depth),
            PolylineChord(0, 5.0),
            new Truss2DOptions { SnapSpacing = 0.0 });

        Assert.Equal(3, truss.TopNodes.Count);
        Assert.Equal(5.0, truss.TopNodes[1].X, 6);         // induced on the straight top chord
        Assert.Equal(5.0, truss.BottomNodes[1].X, 6);
    }

    [Fact]
    public void Additional_points_are_pulled_onto_the_nearer_chord()
    {
        Truss2D truss = Truss2DGenerator.Generate(
            StraightChord(Depth),
            StraightChord(0),
            new Truss2DOptions
            {
                SnapSpacing = 0.0,
                AdditionalSnapPoints = new[] { new Point3d(4.0, 0, 1.9) },   // nearest the top chord
            });

        Assert.Equal(3, truss.TopNodes.Count);
        Assert.Equal(4.0, truss.TopNodes[1].X, 6);
    }

    [Fact]
    public void A_bare_line_with_no_spacing_gives_a_single_panel()
    {
        Truss2D truss = Build(TrussType.Warren, spacing: 0.0);
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
        Truss2D truss = Build(type);
        Assert.Equal(expected, truss.Web.Count());
    }

    [Fact]
    public void Chords_are_one_member_per_panel()
    {
        Truss2D truss = Build(TrussType.Warren);

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

        Truss2D truss = Truss2DGenerator.Generate(
            StraightChord(Depth), reversed, new Truss2DOptions { SnapSpacing = 2.0 });

        // Aligned chords mean every end post is vertical rather than diagonal.
        Assert.All(truss.EndPosts, post => Assert.Equal(0.0, post.Direction.X, 6));
    }

    [Fact]
    public void Members_carry_node_indices_that_resolve_against_Nodes()
    {
        Truss2D truss = Build(TrussType.Pratt);
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

        Truss2D truss = Truss2DGenerator.Generate(
            StraightChord(Depth), twisted, new Truss2DOptions { SnapSpacing = 2.0 });

        Assert.False(truss.IsPlanar);
    }

    // ---- divisions and snapping -------------------------------------------

    [Fact]
    public void Divisions_set_the_panel_count()
    {
        Truss2D truss = Truss2DGenerator.Generate(
            StraightChord(Depth), StraightChord(0), new Truss2DOptions { Divisions = 5 });

        Assert.Equal(5, truss.PanelCount);
        Assert.Equal(6, truss.TopNodes.Count);
    }

    [Fact]
    public void Divisions_win_over_spacing()
    {
        Truss2D truss = Truss2DGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new Truss2DOptions { Divisions = 3, SnapSpacing = 1.0 });   // spacing alone would give 12

        Assert.Equal(3, truss.PanelCount);
    }

    [Fact]
    public void Divisions_move_onto_nearby_points_rather_than_adding_to_them()
    {
        // Four even stations would sit at x = 3, 6, 9. The vertex at 3.4 is
        // within half a panel of the first, so that station moves onto it.
        Truss2D truss = Truss2DGenerator.Generate(
            PolylineChord(Depth, 3.4), StraightChord(0), new Truss2DOptions { Divisions = 4 });

        Assert.Equal(4, truss.PanelCount);                 // count is unchanged
        Assert.Equal(3.4, truss.TopNodes[1].X, 6);         // but the station moved
        Assert.Equal(6.0, truss.TopNodes[2].X, 6);         // and its neighbours did not
        Assert.Equal(3.0, truss.BottomNodes[1].X, 6);      // nor did the other chord
    }

    [Fact]
    public void A_point_further_than_half_a_panel_away_is_left_alone()
    {
        // Stations at x = 6 only. The vertex at 1.0 is 5 units away, well beyond
        // the 3-unit reach, so nothing snaps.
        Truss2D truss = Truss2DGenerator.Generate(
            PolylineChord(Depth, 1.0), StraightChord(0), new Truss2DOptions { Divisions = 2 });

        Assert.Equal(2, truss.PanelCount);
        Assert.Equal(6.0, truss.TopNodes[1].X, 6);
    }

    [Fact]
    public void Two_stations_never_collapse_onto_the_same_point()
    {
        // Two vertices crowded around the single interior station at x = 6.
        Truss2D truss = Truss2DGenerator.Generate(
            PolylineChord(Depth, 5.8, 6.2), StraightChord(0), new Truss2DOptions { Divisions = 2 });

        Assert.Equal(2, truss.PanelCount);
        Assert.Equal(3, truss.TopNodes.Count);
        Assert.Equal(5.8, truss.TopNodes[1].X, 6);         // nearest wins, the other is ignored
    }

    [Fact]
    public void Additional_points_snap_the_divisions_too()
    {
        Truss2D truss = Truss2DGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new Truss2DOptions
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
        Assert.Throws<ArgumentException>(() => Truss2DGenerator.Generate(
            StraightChord(Depth), StraightChord(0), new Truss2DOptions { Divisions = -1 }));
    }

    // ---- each chord snaps on its own ---------------------------------------

    [Fact]
    public void Each_chord_snaps_to_its_own_points()
    {
        // Four panels put both chords at x = 0, 3, 6, 9, 12. The top chord has a
        // vertex near its second station, the bottom chord near its fourth.
        Truss2D truss = Truss2DGenerator.Generate(
            PolylineChord(Depth, 3.4), PolylineChord(0, 8.0), new Truss2DOptions { Divisions = 4 });

        Assert.Equal(4, truss.PanelCount);

        Assert.Equal(3.4, truss.TopNodes[1].X, 6);         // top pulled to its vertex
        Assert.Equal(3.0, truss.BottomNodes[1].X, 6);      // bottom unmoved there

        Assert.Equal(9.0, truss.TopNodes[3].X, 6);         // top unmoved here
        Assert.Equal(8.0, truss.BottomNodes[3].X, 6);      // bottom pulled to its vertex
    }

    [Fact]
    public void A_picked_point_beside_one_chord_leaves_the_other_alone()
    {
        // The point sits just above the bottom chord, so it belongs to it.
        Truss2D truss = Truss2DGenerator.Generate(
            StraightChord(Depth), StraightChord(0),
            new Truss2DOptions
            {
                Divisions = 2,
                AdditionalSnapPoints = new[] { new Point3d(5.0, 0, 0.1) },
            });

        Assert.Equal(5.0, truss.BottomNodes[1].X, 6);
        Assert.Equal(6.0, truss.TopNodes[1].X, 6);
    }

    [Fact]
    public void Chords_keep_matching_node_counts_when_they_snap_differently()
    {
        Truss2D truss = Truss2DGenerator.Generate(
            PolylineChord(Depth, 2.6, 5.2, 9.4),
            PolylineChord(0, 3.3, 6.4),
            new Truss2DOptions { Divisions = 5 });

        Assert.Equal(truss.TopNodes.Count, truss.BottomNodes.Count);
        Assert.Equal(6, truss.TopNodes.Count);
    }

    [Fact]
    public void Independently_snapped_chords_still_pair_by_index()
    {
        Truss2D truss = Truss2DGenerator.Generate(
            PolylineChord(Depth, 3.4), PolylineChord(0, 8.0),
            new Truss2DOptions { Divisions = 4, Type = TrussType.Vierendeel });

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
        => Truss2DGenerator.Generate(
                StraightChord(Depth), StraightChord(0),
                new Truss2DOptions { Type = type, Divisions = 6, Flip = flip })
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
        Truss2D truss = Build(TrussType.CrossBraced);

        int verticals = truss.Web.Count(l => Math.Abs(l.Direction.X) < 1e-9);
        Assert.Equal(truss.PanelCount - 1, verticals);
    }

    // ---- chords that meet at an end ---------------------------------------

    /// <summary>A chord running from a shared apex out to the far end.</summary>
    private static Curve TaperedChord(Point3d apex, double z)
        => new LineCurve(apex, new Point3d(Span, 0, z));

    [Fact]
    public void No_end_post_where_the_chords_meet()
    {
        Point3d apex = new(0, 0, 1);

        Truss2D truss = Truss2DGenerator.Generate(
            TaperedChord(apex, Depth), TaperedChord(apex, 0),
            new Truss2DOptions { Divisions = 4, GenerateEndPosts = true });

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

        Truss2D truss = Truss2DGenerator.Generate(
            top, bottom, new Truss2DOptions { Divisions = 6, GenerateEndPosts = true });

        Assert.True(truss.ChordsMeetAtStart);
        Assert.True(truss.ChordsMeetAtEnd);
        Assert.Empty(truss.EndPosts);
    }

    [Fact]
    public void Separated_chords_still_report_as_not_meeting()
    {
        Truss2D truss = Truss2DGenerator.Generate(
            StraightChord(Depth), StraightChord(0), new Truss2DOptions { Divisions = 4 });

        Assert.False(truss.ChordsMeetAtStart);
        Assert.False(truss.ChordsMeetAtEnd);
        Assert.Equal(2, truss.EndPosts.Count());
    }

    [Fact]
    public void A_null_chord_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => Truss2DGenerator.Generate(null!, StraightChord(0)));
    }
}
