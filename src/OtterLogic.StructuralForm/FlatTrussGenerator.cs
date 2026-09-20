using Rhino;
using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Builds a 2D truss between a top and a bottom chord.
/// <para>
/// The whole engine. Both the Grasshopper component and the Rhino command call
/// <see cref="Generate"/> and nothing else, so the two front-ends cannot drift.
/// </para>
/// <para>
/// Nodes sit at <em>stations</em> — positions running 0 to 1 along a chord,
/// measured as a fraction of its length. One list, shared by both chords, so
/// top node <c>i</c> and bottom node <c>i</c> sit at the same station and every
/// web pattern reduces to index arithmetic.
/// </para>
/// <para>
/// Which length is <see cref="FlatTrussOptions.MeasureOnPlan"/>'s to say. Along
/// the chord itself by default, which is the only reading that means anything
/// for a truss standing on end or running through space. On plan for a roof
/// truss, because a pitched top chord is longer than the level bottom chord
/// under it: divide each by its own length and node <c>i</c> lands a different
/// distance along each of them, leaving every vertical leaning. See
/// <see cref="StationLayout.ChordRuler"/> for how either is measured.
/// </para>
/// <para>
/// Two ways of arriving at the list, and the difference matters:
/// </para>
/// <list type="bullet">
/// <item><description>
/// With <see cref="FlatTrussOptions.Divisions"/> (or
/// <see cref="FlatTrussOptions.Spacing"/>) set, the panel count is fixed up
/// front and laid out evenly, then <em>snapped</em> onto nearby points
/// in two passes. The chords' own points come first and win: polyline vertices
/// and curve kinks on either chord, which a truss has to honour or its chord
/// members cut the corners. What they leave free is then offered to the picked
/// points in <see cref="FlatTrussOptions.AdditionalSnapPoints"/>, each of which
/// has to lie on one of the chords to count at all, and takes the one station
/// nearest to it.
/// Whatever did not snap is then spread evenly between the stations that did,
/// so the panels around a snapped node do not come out short and long against
/// the rest. The member count is whatever you asked for; snap points move
/// members, never add them.
/// </description></item>
/// <item><description>
/// With neither set, the snap points <em>are</em> the stations: polyline
/// vertices, curve kinks and picked points each become a node, and there is
/// nothing left over to spread.
/// </description></item>
/// </list>
/// </summary>
public static class FlatTrussGenerator
{
    public static FlatTruss Generate(Curve topChord, Curve bottomChord, FlatTrussOptions? options = null)
    {
        if (topChord is null) throw new ArgumentNullException(nameof(topChord));
        if (bottomChord is null) throw new ArgumentNullException(nameof(bottomChord));
        if (!topChord.IsValid) throw new ArgumentException("Top chord is not a valid curve.", nameof(topChord));
        if (!bottomChord.IsValid) throw new ArgumentException("Bottom chord is not a valid curve.", nameof(bottomChord));

        options ??= new FlatTrussOptions();

        Validate(options);

        Curve top = AsNurbs(topChord);
        Curve bottom = WorkingCurve.AlignToStart(AsNurbs(bottomChord), top);

        if (top.GetLength() <= RhinoMath.ZeroTolerance || bottom.GetLength() <= RhinoMath.ZeroTolerance)
            throw new ArgumentException("Both chords must have length.");

        var topRuler = new StationLayout.ChordRuler(top, options.SnapTolerance, options.MeasureOnPlan);
        var bottomRuler = new StationLayout.ChordRuler(bottom, options.SnapTolerance, options.MeasureOnPlan);

        double[] stations = StationLayout.Resolve(
            new[] { topRuler, bottomRuler },
            new StationRequest(
                options.Divisions, options.Spacing, options.Strictness,
                options.AdditionalSnapPoints, options.SnapTolerance),
            out int unusedSnapPoints, out int offChordSnapPoints);

        var topNodes = new Point3d[stations.Length];
        var bottomNodes = new Point3d[stations.Length];
        for (int i = 0; i < stations.Length; i++)
        {
            topNodes[i] = topRuler.PointAtStation(stations[i]);
            bottomNodes[i] = bottomRuler.PointAtStation(stations[i]);
        }

        // Chords that converge to a shared point need no post there.
        bool meetAtStart = topNodes[0].DistanceTo(bottomNodes[0]) <= options.SnapTolerance;
        bool meetAtEnd = topNodes[^1].DistanceTo(bottomNodes[^1]) <= options.SnapTolerance;

        // Top chord nodes first, then bottom: the order FlatTruss.Nodes lists
        // them in, which is what the members' indices refer to.
        var web = new WebBuilder(options.SnapTolerance);
        web.AddChord(topNodes, 0, TrussMemberRole.TopChord);
        web.AddChord(bottomNodes, stations.Length, TrussMemberRole.BottomChord);
        web.AddFace(
            topNodes, 0, bottomNodes, stations.Length,
            options.Type, options.Flip, options.GenerateEndPosts, meetAtStart, meetAtEnd,
            new FaceRoles(TrussMemberRole.Vertical, TrussMemberRole.Diagonal, TrussMemberRole.EndPost));

        List<TrussMember> members = web.Members;
        bool planar = IsPlanar(topNodes, bottomNodes, options.SnapTolerance);

        return new FlatTruss(
            topNodes, bottomNodes, members, options, planar, meetAtStart, meetAtEnd,
            unusedSnapPoints, offChordSnapPoints);
    }

    /// <summary>
    /// The checks on the options themselves, apart from the chords. Internal
    /// because a box truss nests these same options for its side faces and has
    /// to refuse the same things in the same words.
    /// </summary>
    internal static void Validate(FlatTrussOptions options)
    {
        if (options.Divisions < 0)
            throw new ArgumentException("Divisions cannot be negative.", nameof(options));
        if (options.Spacing < 0.0)
            throw new ArgumentException("Spacing cannot be negative.", nameof(options));
        if (!Enum.IsDefined(options.Strictness))
            throw new ArgumentException(
                $"Snap strictness {(int)options.Strictness} does not exist. Valid values are "
                + $"0-{Enum.GetValues<SnapStrictness>().Length - 1}.",
                nameof(options));
        if (!Enum.IsDefined(options.Type))
            throw new ArgumentException(
                $"Truss type {(int)options.Type} does not exist. Valid values are "
                + $"0-{Enum.GetValues<TrussType>().Length - 1}.",
                nameof(options));
    }

    private static Curve AsNurbs(Curve chord) => WorkingCurve.AsNurbs(chord);

    private static bool IsPlanar(Point3d[] topNodes, Point3d[] bottomNodes, double tolerance)
    {
        var all = new List<Point3d>(topNodes.Length + bottomNodes.Length);
        all.AddRange(topNodes);
        all.AddRange(bottomNodes);

        if (all.Count < 4) return true;

        if (Plane.FitPlaneToPoints(all, out Plane plane) != PlaneFitResult.Success)
            return false;

        double allowance = Math.Max(tolerance, RhinoMath.SqrtEpsilon);
        return all.All(p => Math.Abs(plane.DistanceTo(p)) <= allowance);
    }
}
