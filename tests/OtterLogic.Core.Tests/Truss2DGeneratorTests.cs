using OtterLogic.Core.StructuralForm;
using Rhino.Geometry;
using Xunit;

namespace OtterLogic.Core.Tests;

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
    public void A_vertex_on_either_chord_creates_a_node_on_both()
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
    [InlineData(TrussType.Vertical, 5)]               // interior verticals only
    [InlineData(TrussType.CrossBraced, 12)]           // both diagonals per panel
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

    [Fact]
    public void A_null_chord_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => Truss2DGenerator.Generate(null!, StraightChord(0)));
    }
}
