using OtterLogic.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

[Collection(RhinoCollection.Name)]
public class BoxTrussGeneratorTests
{
    private const double Span = 12.0;
    private const double Depth = 2.0;
    private const double Width = 1.5;

    private static Curve Chord(double y, double z)
        => new LineCurve(new Point3d(0, y, z), new Point3d(Span, y, z));

    private static Curve[] TwoTop => new[] { Chord(0, Depth), Chord(Width, Depth) };
    private static Curve[] TwoBottom => new[] { Chord(0, 0), Chord(Width, 0) };
    private static Curve[] OneTop => new[] { Chord(Width / 2, Depth) };
    private static Curve[] OneBottom => new[] { Chord(Width / 2, 0) };

    private static BoxTrussOptions Options(
        int divisions = 6,
        TrussType type = TrussType.Warren,
        TrussType lacing = TrussType.WarrenWithVerticals,
        bool endPosts = true)
        => new()
        {
            Sides = new FlatTrussOptions { Divisions = divisions, Type = type, GenerateEndPosts = endPosts },
            LacingType = lacing,
        };

    // ---- shapes -------------------------------------------------------------

    [Fact]
    public void Two_and_two_make_a_box()
    {
        BoxTruss truss = BoxTrussGenerator.Generate(TwoTop, TwoBottom, Options());

        Assert.Equal(4, truss.ChordCount);
        Assert.Equal(6, truss.PanelCount);

        Assert.Equal(12, truss.TopChord.Count());          // two chords, six panels each
        Assert.Equal(12, truss.BottomChord.Count());
        Assert.Equal(12, truss.Diagonals.Count());         // Warren: one per panel, two side faces
        Assert.Empty(truss.Verticals);
        Assert.Equal(4, truss.EndPosts.Count());           // two side faces, both ends

        // Lacing top and bottom: a diagonal per panel, a strut at every station.
        Assert.Equal(12, truss.Lacing.Count());
        Assert.Equal(14, truss.Struts.Count());
    }

    [Fact]
    public void Two_top_and_one_bottom_make_a_triangle_laced_across_the_top()
    {
        BoxTruss truss = BoxTrussGenerator.Generate(TwoTop, OneBottom, Options());

        Assert.Equal(3, truss.ChordCount);
        Assert.Equal(12, truss.TopChord.Count());
        Assert.Equal(6, truss.BottomChord.Count());
        Assert.Equal(12, truss.Diagonals.Count());         // both side faces share the bottom chord
        Assert.Equal(6, truss.Lacing.Count());             // one lacing face
        Assert.Equal(7, truss.Struts.Count());

        Assert.All(truss.Lacing, line => Assert.Equal(Depth, line.From.Z, 6));
        Assert.All(truss.Struts, line => Assert.Equal(Depth, line.From.Z, 6));
    }

    [Fact]
    public void One_top_and_two_bottom_make_a_triangle_laced_across_the_bottom()
    {
        BoxTruss truss = BoxTrussGenerator.Generate(OneTop, TwoBottom, Options());

        Assert.Equal(3, truss.ChordCount);
        Assert.Equal(6, truss.TopChord.Count());
        Assert.Equal(12, truss.BottomChord.Count());
        Assert.Equal(12, truss.Diagonals.Count());

        Assert.All(truss.Lacing, line => Assert.Equal(0.0, line.From.Z, 6));
    }

    [Fact]
    public void One_of_each_is_a_flat_truss_and_is_refused()
    {
        var error = Assert.Throws<ArgumentException>(
            () => BoxTrussGenerator.Generate(OneTop, OneBottom, Options()));

        Assert.Contains("flat truss", error.Message);
    }

    [Fact]
    public void Three_of_anything_is_refused()
    {
        var three = new[] { Chord(0, Depth), Chord(1, Depth), Chord(2, Depth) };

        Assert.Throws<ArgumentException>(() => BoxTrussGenerator.Generate(three, OneBottom, Options()));
        Assert.Throws<ArgumentException>(() => BoxTrussGenerator.Generate(OneTop, three, Options()));
    }

    // ---- what it shares with a flat truss ---------------------------------

    /// <summary>
    /// The point of one station list: a panel point is a single cross-section
    /// through the truss, on every chord at once.
    /// </summary>
    [Fact]
    public void Every_chord_steps_at_the_same_stations()
    {
        BoxTruss truss = BoxTrussGenerator.Generate(TwoTop, TwoBottom, Options());

        for (int i = 0; i <= truss.PanelCount; i++)
        {
            double x = i * Span / 6.0;

            Assert.All(truss.TopNodes.Concat(truss.BottomNodes), chord => Assert.Equal(x, chord[i].X, 6));
        }
    }

    [Fact]
    public void A_side_face_is_the_flat_truss_between_the_same_two_chords()
    {
        var sides = new FlatTrussOptions { Divisions = 6, Type = TrussType.Pratt };

        BoxTruss box = BoxTrussGenerator.Generate(TwoTop, TwoBottom, new BoxTrussOptions { Sides = sides });
        FlatTruss flat = FlatTrussGenerator.Generate(Chord(0, Depth), Chord(0, 0), sides);

        // Everything the box has in the y = 0 plane, chords apart.
        var face = box.Members
            .Where(m => m.Role is TrussMemberRole.Vertical or TrussMemberRole.Diagonal or TrussMemberRole.EndPost)
            .Where(m => Math.Abs(m.Line.From.Y) < 1e-9 && Math.Abs(m.Line.To.Y) < 1e-9)
            .Select(m => m.Line)
            .ToList();

        var expected = flat.Web.Concat(flat.EndPosts).ToList();

        Assert.Equal(expected.Count, face.Count);
        Assert.All(expected, line => Assert.Contains(face, f => f.From == line.From && f.To == line.To));
    }

    [Fact]
    public void A_kink_on_any_chord_is_a_panel_point_on_all_of_them()
    {
        var kinked = new PolylineCurve(new[]
        {
            new Point3d(0, Width, 0), new Point3d(5, Width, -0.5), new Point3d(Span, Width, 0),
        });

        // On plan, so that the station of the kink is the same distance along
        // the straight chords as along the longer, dipping one.
        var options = new BoxTrussOptions { Sides = new FlatTrussOptions { MeasureOnPlan = true } };

        BoxTruss truss = BoxTrussGenerator.Generate(TwoTop, new Curve[] { Chord(0, 0), kinked }, options);

        Assert.Equal(2, truss.PanelCount);
        Assert.All(truss.TopNodes.Concat(truss.BottomNodes), chord => Assert.Equal(5.0, chord[1].X, 6));
    }

    [Fact]
    public void Snap_points_are_taken_from_any_chord()
    {
        var options = new BoxTrussOptions
        {
            Sides = new FlatTrussOptions
            {
                Divisions = 4,
                Strictness = SnapStrictness.Strict,
                AdditionalSnapPoints = new[] { new Point3d(1.0, Width, 0), new Point3d(1.0, 5, 5) },
            },
        };

        BoxTruss truss = BoxTrussGenerator.Generate(TwoTop, TwoBottom, options);

        Assert.Contains(truss.TopNodes[0], n => Math.Abs(n.X - 1.0) < 1e-6);
        Assert.Equal(1, truss.OffChordSnapPoints);
        Assert.Contains(truss.Notes, n => n.Message.Contains("any of the chords"));
    }

    [Fact]
    public void Members_carry_node_indices_that_resolve_against_Nodes()
    {
        BoxTruss truss = BoxTrussGenerator.Generate(TwoTop, TwoBottom, Options(type: TrussType.CrossBraced));
        var nodes = truss.Nodes;

        Assert.All(truss.Members, member =>
        {
            Assert.Equal(nodes[member.StartNode], member.Line.From);
            Assert.Equal(nodes[member.EndNode], member.Line.To);
        });
    }

    [Fact]
    public void No_member_is_drawn_twice()
    {
        BoxTruss truss = BoxTrussGenerator.Generate(
            TwoTop, OneBottom, Options(type: TrussType.CrossBraced, lacing: TrussType.CrossBraced));

        var keys = truss.Members
            .Select(m => (Math.Min(m.StartNode, m.EndNode), Math.Max(m.StartNode, m.EndNode)))
            .ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // ---- lacing -------------------------------------------------------------

    [Fact]
    public void Vierendeel_lacing_is_struts_alone()
    {
        BoxTruss truss = BoxTrussGenerator.Generate(TwoTop, TwoBottom, Options(lacing: TrussType.Vierendeel));

        Assert.Empty(truss.Lacing);
        Assert.Equal(14, truss.Struts.Count());
    }

    [Fact]
    public void End_struts_go_with_the_end_posts()
    {
        BoxTruss truss = BoxTrussGenerator.Generate(TwoTop, TwoBottom, Options(endPosts: false));

        Assert.Empty(truss.EndPosts);
        Assert.Equal(10, truss.Struts.Count());            // interior stations only, top and bottom
    }

    [Fact]
    public void Flipping_the_lacing_leaves_the_sides_alone()
    {
        BoxTruss plain = BoxTrussGenerator.Generate(TwoTop, TwoBottom, Options());
        BoxTruss flipped = BoxTrussGenerator.Generate(
            TwoTop, TwoBottom, Options() with { FlipLacing = true });

        Assert.Equal(plain.Diagonals, flipped.Diagonals);
        Assert.NotEqual(plain.Lacing, flipped.Lacing);
    }

    // ---- the order the chords arrive in ------------------------------------

    /// <summary>
    /// Paired the wrong way, both side faces run corner to corner through the
    /// middle of the box.
    /// </summary>
    [Fact]
    public void Bottom_chords_picked_in_the_other_order_still_sit_under_their_own_top_chord()
    {
        BoxTruss truss = BoxTrussGenerator.Generate(
            TwoTop, new[] { Chord(Width, 0), Chord(0, 0) }, Options(type: TrussType.Vierendeel));

        Assert.All(truss.Verticals, post => Assert.Equal(0.0, post.Direction.Y, 6));
        Assert.All(truss.EndPosts, post => Assert.Equal(0.0, post.Direction.Y, 6));

        Assert.Equal(truss.TopNodes[0][0].Y, truss.BottomNodes[0][0].Y, 6);
    }

    [Fact]
    public void A_chord_drawn_backwards_does_not_cross_the_truss()
    {
        Curve backwards = Chord(Width, 0);
        backwards.Reverse();

        BoxTruss truss = BoxTrussGenerator.Generate(
            TwoTop, new[] { Chord(0, 0), backwards }, Options(type: TrussType.Vierendeel));

        Assert.All(truss.Verticals, post => Assert.Equal(0.0, post.Direction.X, 6));
        Assert.All(truss.Struts, strut => Assert.Equal(0.0, strut.Direction.X, 6));
    }

    // ---- chords that meet ---------------------------------------------------

    /// <summary>Two top chords drawn to a point at each end, over one bottom chord.</summary>
    [Fact]
    public void Top_chords_that_meet_at_the_ends_get_no_strut_there()
    {
        Curve Bowed(double y) => new PolylineCurve(new[]
        {
            new Point3d(0, 0, Depth), new Point3d(Span / 2, y, Depth), new Point3d(Span, 0, Depth),
        });

        BoxTruss truss = BoxTrussGenerator.Generate(
            new[] { Bowed(-1), Bowed(1) }, new[] { Chord(0, 0) }, Options(divisions: 4));

        Assert.True(truss.EndsSuppressed);
        Assert.Equal(3, truss.Struts.Count());             // interior stations only
        Assert.Equal(2, truss.Lacing.Count());             // and no diagonal in either end panel
        Assert.Contains(truss.Notes, n => n.Message.Contains("meet at an end"));

        Assert.True(truss.DistinctNodes.Count < truss.Nodes.Count);
    }

    [Fact]
    public void Nonsense_options_are_refused_in_the_flat_truss_s_words()
    {
        Assert.Throws<ArgumentException>(() => BoxTrussGenerator.Generate(
            TwoTop, TwoBottom, new BoxTrussOptions { Sides = new FlatTrussOptions { Divisions = -1 } }));

        var error = Assert.Throws<ArgumentException>(() => BoxTrussGenerator.Generate(
            TwoTop, TwoBottom, new BoxTrussOptions { LacingType = (TrussType)99 }));

        Assert.Contains("99", error.Message);
    }
}
