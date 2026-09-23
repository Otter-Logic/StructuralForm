using Rhino;
using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Builds a 3D truss on three or four chords: one or two top, one or two
/// bottom.
/// <para>
/// The whole engine. Both the Grasshopper component and the Rhino command call
/// <see cref="Generate"/> and nothing else, so the two front-ends cannot drift.
/// </para>
/// <para>
/// A box truss is flat trusses sharing chords, and is built as exactly that.
/// Every chord is divided at one shared list of stations by
/// <see cref="StationLayout"/> — the same layout, under the same rules, that a
/// flat truss gets — and the web then goes in face by face:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A <em>side</em> face runs from a top chord to a bottom chord and is a flat
/// truss in every respect: <see cref="FlatTrussOptions.Type"/>, verticals,
/// diagonals, end posts.
/// </description></item>
/// <item><description>
/// A <em>lacing</em> face runs between twin chords — top to top, bottom to
/// bottom — and takes <see cref="BoxTrussOptions.LacingType"/>, with struts for
/// verticals. It exists only where there are twins, which is the whole
/// difference between the shapes: two and two make a box with a lacing face top
/// and bottom, two and one make a triangle with one.
/// </description></item>
/// </list>
/// <para>
/// One of each is not a box truss. It is refused rather than quietly built,
/// because the result would be a <see cref="FlatTruss"/> without the things
/// that tool knows to say about one.
/// </para>
/// </summary>
public static class BoxTrussGenerator
{
    private static readonly FaceRoles SideRoles =
        new(TrussMemberRole.Vertical, TrussMemberRole.Diagonal, TrussMemberRole.EndPost);

    // The member closing a lacing face at its end is a strut like any other in
    // that face: same job, same section.
    private static readonly FaceRoles LacingRoles =
        new(TrussMemberRole.Strut, TrussMemberRole.Lacing, TrussMemberRole.Strut);

    public static BoxTruss Generate(
        IReadOnlyList<Curve> topChords, IReadOnlyList<Curve> bottomChords, BoxTrussOptions? options = null)
    {
        if (topChords is null) throw new ArgumentNullException(nameof(topChords));
        if (bottomChords is null) throw new ArgumentNullException(nameof(bottomChords));

        if (topChords.Count is < 1 or > 2)
            throw new ArgumentException(
                $"A box truss takes one or two top chords; {topChords.Count} were given.", nameof(topChords));
        if (bottomChords.Count is < 1 or > 2)
            throw new ArgumentException(
                $"A box truss takes one or two bottom chords; {bottomChords.Count} were given.",
                nameof(bottomChords));
        if (topChords.Count + bottomChords.Count < 3)
            throw new ArgumentException(
                "One top chord and one bottom chord is a flat truss. A box truss needs a second "
                + "chord on at least one side: two and one for a triangular truss, two and two for a box.");

        if (topChords.Concat(bottomChords).Any(chord => chord is null || !chord.IsValid))
            throw new ArgumentException("Every chord must be a valid curve.");

        options ??= new BoxTrussOptions();
        FlatTrussOptions sides = options.Sides;

        FlatTrussGenerator.Validate(sides);

        if (!Enum.IsDefined(options.LacingType))
            throw new ArgumentException(
                $"Lacing type {(int)options.LacingType} does not exist. Valid values are "
                + $"0-{Enum.GetValues<TrussType>().Length - 1}.",
                nameof(options));

        // Every chord is turned to run the way the first top chord does, so
        // station 0 is one end of the truss and not a mixture of both.
        Curve lead = WorkingCurve.AsNurbs(topChords[0]);

        Curve[] tops = topChords
            .Select((chord, k) => k == 0 ? lead : WorkingCurve.AlignToStart(WorkingCurve.AsNurbs(chord), lead))
            .ToArray();
        Curve[] bottoms = bottomChords
            .Select(chord => WorkingCurve.AlignToStart(WorkingCurve.AsNurbs(chord), lead))
            .ToArray();

        if (tops.Concat(bottoms).Any(chord => chord.GetLength() <= RhinoMath.ZeroTolerance))
            throw new ArgumentException("Every chord must have length.");

        if (tops.Length == 2 && bottoms.Length == 2)
            PairBottomsWithTops(tops, bottoms);

        // Tops first, then bottoms: the order the nodes are indexed in.
        Curve[] chords = tops.Concat(bottoms).ToArray();

        StationLayout.ChordRuler[] rulers = chords
            .Select(chord => new StationLayout.ChordRuler(chord, sides.SnapTolerance, sides.MeasureOnPlan))
            .ToArray();

        // Per chord, paired by index: a point picked on one chord fixes that
        // chord's node of its panel point and leaves the others to follow.
        double[][] stations = StationLayout.ResolvePaired(
            rulers,
            new StationRequest(
                sides.Divisions, sides.Spacing, sides.Strictness,
                sides.AdditionalSnapPoints, sides.SnapTolerance),
            out int unusedSnapPoints, out int offChordSnapPoints);

        int count = stations[0].Length;

        Point3d[][] nodes = rulers
            .Select((ruler, c) => stations[c].Select(ruler.PointAtStation).ToArray())
            .ToArray();

        // Chord c's nodes sit at c * count onwards in the truss's node list.
        int[] Indices(int c) => Enumerable.Range(c * count, count).ToArray();

        var web = new WebBuilder(nodes.SelectMany(chord => chord).ToArray(), sides.SnapTolerance);

        for (int c = 0; c < chords.Length; c++)
            web.AddChord(
                Indices(c),
                c < tops.Length ? TrussMemberRole.TopChord : TrussMemberRole.BottomChord);

        bool suppressed = false;

        void Face(int a, int b, TrussType type, bool flip, FaceRoles roles)
        {
            // Chords that converge to a shared point need no member there.
            bool meetAtStart = nodes[a][0].DistanceTo(nodes[b][0]) <= sides.SnapTolerance;
            bool meetAtEnd = nodes[a][^1].DistanceTo(nodes[b][^1]) <= sides.SnapTolerance;

            suppressed |= meetAtStart || meetAtEnd;

            web.AddFace(
                Indices(a), Indices(b),
                type, flip, sides.GenerateEndPosts, meetAtStart, meetAtEnd, roles);
        }

        // Side faces: each top chord down to the bottom chord under it, and a
        // lone chord to both of its opposite numbers.
        int firstBottom = tops.Length;
        int sideCount = Math.Max(tops.Length, bottoms.Length);

        for (int k = 0; k < sideCount; k++)
            Face(
                Math.Min(k, tops.Length - 1),
                firstBottom + Math.Min(k, bottoms.Length - 1),
                sides.Type, sides.Flip, SideRoles);

        // Lacing faces: wherever a chord has a twin.
        if (tops.Length == 2)
            Face(0, 1, options.LacingType, options.FlipLacing, LacingRoles);

        if (bottoms.Length == 2)
            Face(firstBottom, firstBottom + 1, options.LacingType, options.FlipLacing, LacingRoles);

        return new BoxTruss(
            nodes[..tops.Length], nodes[tops.Length..], web.Members, options,
            suppressed, unusedSnapPoints, offChordSnapPoints);
    }

    /// <summary>
    /// Put the bottom chords in the order that sets each under its own top
    /// chord.
    /// <para>
    /// The chords arrive in whatever order they were picked, and the wrong
    /// pairing is not a subtle failure: both side faces run corner to corner
    /// through the middle of the box. Of the two possible pairings, the one
    /// with the shorter side faces is the box — judged at mid-length, because
    /// chords drawn to a point at the ends say nothing there.
    /// </para>
    /// </summary>
    private static void PairBottomsWithTops(Curve[] tops, Curve[] bottoms)
    {
        Point3d Middle(Curve chord) => chord.PointAtNormalizedLength(0.5);

        double straight = Middle(tops[0]).DistanceTo(Middle(bottoms[0]))
                        + Middle(tops[1]).DistanceTo(Middle(bottoms[1]));
        double crossed = Middle(tops[0]).DistanceTo(Middle(bottoms[1]))
                       + Middle(tops[1]).DistanceTo(Middle(bottoms[0]));

        if (crossed < straight)
            (bottoms[0], bottoms[1]) = (bottoms[1], bottoms[0]);
    }
}
