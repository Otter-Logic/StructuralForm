using Rhino;
using Rhino.Geometry;

namespace OtterLogic.Core.StructuralForm;

/// <summary>
/// Builds a 2D truss between a top and a bottom chord.
/// <para>
/// The whole engine. Both the Grasshopper component and the Rhino command call
/// <see cref="Generate"/> and nothing else, so the two front-ends cannot drift.
/// </para>
/// <para>
/// Nodes sit at <em>stations</em> — normalised arc-length positions running 0 to
/// 1 along a chord. Each chord carries its own station list of the same length,
/// so top node <c>i</c> always pairs with bottom node <c>i</c> and every web
/// pattern reduces to index arithmetic, while the two chords stay free to place
/// that node at different points along their own length.
/// </para>
/// <para>
/// Two ways of arriving at those lists, and the difference matters:
/// </para>
/// <list type="bullet">
/// <item><description>
/// With <see cref="Truss2DOptions.Divisions"/> (or
/// <see cref="Truss2DOptions.SnapSpacing"/>) set, the panel count is fixed up
/// front and both chords are laid out evenly, then each is <em>snapped</em>
/// independently onto its own nearby snap points. A point beside the bottom
/// chord moves the bottom node and leaves the top one where it was. The member
/// count is whatever you asked for; snap points move members, never add them.
/// </description></item>
/// <item><description>
/// With neither set, the snap points <em>are</em> the stations: polyline
/// vertices, curve kinks and picked points each become a node. Here the two
/// chords must share one list, because the points are what decide how many
/// panels there are and the chords have to agree on that.
/// </description></item>
/// </list>
/// </summary>
public static class Truss2DGenerator
{
    public static Truss2D Generate(Curve topChord, Curve bottomChord, Truss2DOptions? options = null)
    {
        if (topChord is null) throw new ArgumentNullException(nameof(topChord));
        if (bottomChord is null) throw new ArgumentNullException(nameof(bottomChord));
        if (!topChord.IsValid) throw new ArgumentException("Top chord is not a valid curve.", nameof(topChord));
        if (!bottomChord.IsValid) throw new ArgumentException("Bottom chord is not a valid curve.", nameof(bottomChord));

        options ??= new Truss2DOptions();

        if (options.Divisions < 0)
            throw new ArgumentException("Divisions cannot be negative.", nameof(options));
        if (options.SnapSpacing < 0.0)
            throw new ArgumentException("Snap spacing cannot be negative.", nameof(options));

        Curve top = topChord.DuplicateCurve();
        Curve bottom = AlignToStart(bottomChord.DuplicateCurve(), top);

        double topLength = top.GetLength();
        double bottomLength = bottom.GetLength();

        if (topLength <= RhinoMath.ZeroTolerance || bottomLength <= RhinoMath.ZeroTolerance)
            throw new ArgumentException("Both chords must have length.");

        var (topStations, bottomStations) = ResolveStations(top, bottom, topLength, bottomLength, options);

        var topNodes = new Point3d[topStations.Length];
        var bottomNodes = new Point3d[bottomStations.Length];
        for (int i = 0; i < topStations.Length; i++)
        {
            topNodes[i] = PointAtStation(top, topStations[i]);
            bottomNodes[i] = PointAtStation(bottom, bottomStations[i]);
        }

        // Chords that converge to a shared point need no post there.
        bool meetAtStart = topNodes[0].DistanceTo(bottomNodes[0]) <= options.SnapTolerance;
        bool meetAtEnd = topNodes[^1].DistanceTo(bottomNodes[^1]) <= options.SnapTolerance;

        var members = BuildMembers(topNodes, bottomNodes, options, meetAtStart, meetAtEnd);
        bool planar = IsPlanar(topNodes, bottomNodes, options.SnapTolerance);

        return new Truss2D(topNodes, bottomNodes, members, options.Type, planar, meetAtStart, meetAtEnd);
    }

    /// <summary>
    /// Flip the bottom chord if it runs against the top one, so station 0 sits at
    /// the same end of both. Without this, picking the chords in a natural order
    /// silently produces a crossed truss.
    /// </summary>
    private static Curve AlignToStart(Curve bottom, Curve top)
    {
        double aligned = bottom.PointAtStart.DistanceTo(top.PointAtStart);
        double flipped = bottom.PointAtStart.DistanceTo(top.PointAtEnd);

        if (flipped < aligned)
            bottom.Reverse();

        return bottom;
    }

    /// <summary>
    /// The station list for each chord. Always the same length, so nodes pair by
    /// index, but not necessarily the same values.
    /// </summary>
    private static (double[] Top, double[] Bottom) ResolveStations(
        Curve top, Curve bottom, double topLength, double bottomLength, Truss2DOptions options)
    {
        double merge = Math.Clamp(options.SnapTolerance / Math.Max(topLength, bottomLength), 1e-9, 0.25);

        // Each chord owns its snap points: its own vertices and kinks, plus the
        // picked points lying nearer to it than to the other chord.
        var topTargets = new List<double>(GeometryStations(top, topLength));
        var bottomTargets = new List<double>(GeometryStations(bottom, bottomLength));

        foreach (Point3d point in options.AdditionalSnapPoints)
            AssignToNearerChord(top, bottom, topLength, bottomLength, point, topTargets, bottomTargets);

        int panels = ResolvePanelCount(options, topLength, bottomLength);

        // No division driver: the snap points decide the panel count, so the two
        // chords have to agree on one shared list.
        if (panels <= 0)
        {
            double[] shared = Sort(topTargets.Concat(bottomTargets).ToList(), merge, includeEnds: true);
            return (shared, shared);
        }

        double[] topStations = EvenStations(panels);
        double[] bottomStations = EvenStations(panels);

        // Half a panel each way: far enough to reach a nearby point, never far
        // enough for two stations to swap places or collapse together. Snapping
        // runs per chord, so a point beside one chord leaves the other alone.
        double radius = 0.5 / panels;
        SnapToTargets(topStations, Sort(topTargets, merge, includeEnds: false), radius);
        SnapToTargets(bottomStations, Sort(bottomTargets, merge, includeEnds: false), radius);

        // Deliberately not de-duplicated afterwards: merging a pair on one chord
        // but not the other would leave the lists different lengths and break the
        // pairing. The snap radius already keeps stations in order and apart.
        return (topStations, bottomStations);
    }

    private static double[] EvenStations(int panels)
    {
        var stations = new double[panels + 1];
        for (int i = 0; i <= panels; i++)
            stations[i] = i / (double)panels;

        return stations;
    }

    /// <summary>
    /// How many panels to lay out before snapping. Divisions wins over spacing;
    /// zero from both hands control to the geometry.
    /// </summary>
    private static int ResolvePanelCount(Truss2DOptions options, double topLength, double bottomLength)
    {
        if (options.Divisions > 0)
            return options.Divisions;

        if (options.SnapSpacing > RhinoMath.ZeroTolerance)
        {
            double longest = Math.Max(topLength, bottomLength);
            return Math.Max(1, (int)Math.Round(longest / options.SnapSpacing, MidpointRounding.AwayFromZero));
        }

        return 0;
    }

    /// <summary>
    /// Pull each interior station onto the nearest snap point within
    /// <paramref name="radius"/>.
    /// <para>
    /// Assignment is greedy, nearest pair first, and both sides are claimed
    /// exclusively — otherwise two stations converge on one popular point and
    /// the panel either side degenerates. The end stations stay pinned to the
    /// chord ends.
    /// </para>
    /// </summary>
    private static void SnapToTargets(double[] stations, double[] targets, double radius)
    {
        if (targets.Length == 0 || stations.Length < 3) return;

        var candidates = new List<(int Station, int Target, double Distance)>();

        for (int s = 1; s < stations.Length - 1; s++)
        {
            for (int t = 0; t < targets.Length; t++)
            {
                double distance = Math.Abs(targets[t] - stations[s]);
                if (distance <= radius)
                    candidates.Add((s, t, distance));
            }
        }

        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var stationClaimed = new bool[stations.Length];
        var targetClaimed = new bool[targets.Length];

        foreach (var (station, target, _) in candidates)
        {
            if (stationClaimed[station] || targetClaimed[target]) continue;

            stations[station] = targets[target];
            stationClaimed[station] = true;
            targetClaimed[target] = true;
        }
    }

    /// <summary>
    /// Stations implied by the curve itself: polyline vertices, or tangent
    /// discontinuities on anything else. A plain line contributes none, which is
    /// why <see cref="Truss2DOptions.Divisions"/> exists.
    /// </summary>
    private static IEnumerable<double> GeometryStations(Curve curve, double totalLength)
    {
        if (curve.TryGetPolyline(out Polyline polyline))
        {
            double travelled = 0.0;
            for (int i = 1; i < polyline.Count - 1; i++)
            {
                travelled += polyline[i].DistanceTo(polyline[i - 1]);
                yield return travelled / totalLength;
            }

            yield break;
        }

        Interval domain = curve.Domain;
        double cursor = domain.T0;

        while (curve.GetNextDiscontinuity(Continuity.C1_locus_continuous, cursor, domain.T1, out double next))
        {
            yield return LengthTo(curve, next) / totalLength;
            cursor = next;
        }
    }

    /// <summary>
    /// File a picked point with whichever chord it sits closer to. It becomes a
    /// snap target for that chord alone. Snapping a top node onto a point that
    /// plainly belongs to the bottom chord is what the shared-station model used
    /// to do, and it is not what anyone means by snapping.
    /// </summary>
    private static void AssignToNearerChord(
        Curve top,
        Curve bottom,
        double topLength,
        double bottomLength,
        Point3d point,
        List<double> topTargets,
        List<double> bottomTargets)
    {
        top.ClosestPoint(point, out double topParam);
        bottom.ClosestPoint(point, out double bottomParam);

        double toTop = top.PointAt(topParam).DistanceTo(point);
        double toBottom = bottom.PointAt(bottomParam).DistanceTo(point);

        if (toTop <= toBottom)
            topTargets.Add(LengthTo(top, topParam) / topLength);
        else
            bottomTargets.Add(LengthTo(bottom, bottomParam) / bottomLength);
    }

    private static double LengthTo(Curve curve, double parameter)
        => curve.GetLength(new Interval(curve.Domain.T0, parameter));

    /// <summary>Sort, drop anything out of range, and merge near-coincident values.</summary>
    private static double[] Sort(List<double> values, double tolerance, bool includeEnds)
    {
        if (includeEnds)
        {
            values.Add(0.0);
            values.Add(1.0);
        }

        values.Sort();

        var kept = new List<double>(values.Count);
        foreach (double value in values)
        {
            if (value < 0.0 || value > 1.0) continue;
            if (kept.Count > 0 && value - kept[^1] < tolerance) continue;
            kept.Add(value);
        }

        if (!includeEnds) return kept.ToArray();

        // Merging can swallow the ends, and the ends are never optional.
        if (kept.Count == 0 || kept[0] > tolerance) kept.Insert(0, 0.0);
        if (1.0 - kept[^1] < tolerance) kept[^1] = 1.0; else kept.Add(1.0);

        return kept.ToArray();
    }

    private static Point3d PointAtStation(Curve curve, double station)
    {
        // Pin the ends exactly: arc-length inversion drifts by a hair otherwise,
        // and a truss whose end post misses the chord by 1e-9 is a nuisance.
        if (station <= 0.0) return curve.PointAtStart;
        if (station >= 1.0) return curve.PointAtEnd;

        return curve.NormalizedLengthParameter(station, out double parameter)
            ? curve.PointAt(parameter)
            : curve.PointAtNormalizedLength(station);
    }

    private static IReadOnlyList<TrussMember> BuildMembers(
        Point3d[] topNodes,
        Point3d[] bottomNodes,
        Truss2DOptions options,
        bool meetAtStart,
        bool meetAtEnd)
    {
        int stations = topNodes.Length;
        int panels = stations - 1;
        int bottomOffset = stations;

        var members = new List<TrussMember>();
        var seen = new HashSet<(int, int)>();

        void Add(int startNode, int endNode, Point3d start, Point3d end, TrussMemberRole role)
        {
            var key = startNode < endNode ? (startNode, endNode) : (endNode, startNode);
            if (!seen.Add(key)) return;
            if (start.DistanceTo(end) <= options.SnapTolerance) return;   // drop degenerate members

            members.Add(new TrussMember(new Line(start, end), role, startNode, endNode));
        }

        void AddVertical(int i) => Add(i, bottomOffset + i, topNodes[i], bottomNodes[i], TrussMemberRole.Web);
        void AddDown(int i) => Add(i, bottomOffset + i + 1, topNodes[i], bottomNodes[i + 1], TrussMemberRole.Web);
        void AddUp(int i) => Add(bottomOffset + i, i + 1, bottomNodes[i], topNodes[i + 1], TrussMemberRole.Web);

        for (int i = 0; i < panels; i++)
        {
            Add(i, i + 1, topNodes[i], topNodes[i + 1], TrussMemberRole.TopChord);
            Add(bottomOffset + i, bottomOffset + i + 1, bottomNodes[i], bottomNodes[i + 1], TrussMemberRole.BottomChord);
        }

        bool verticals = options.Type is TrussType.Vierendeel
            or TrussType.WarrenWithVerticals
            or TrussType.Pratt
            or TrussType.Howe
            or TrussType.CrossBraced;

        if (verticals)
            for (int i = 1; i < panels; i++)
                AddVertical(i);

        // Flip mirrors each diagonal within its own panel, which is the same as
        // swapping which way the two helpers run. Cross-braced draws both, so it
        // comes out identical either way.
        Action<int> down = options.Flip ? AddUp : AddDown;
        Action<int> up = options.Flip ? AddDown : AddUp;

        double midpoint = panels / 2.0;

        for (int i = 0; i < panels; i++)
        {
            switch (options.Type)
            {
                case TrussType.Vierendeel:
                    break;

                // A continuous zigzag: alternating panels flip the diagonal.
                case TrussType.Warren:
                case TrussType.WarrenWithVerticals:
                    if (i % 2 == 0) down(i); else up(i);
                    break;

                // Diagonals fall toward mid-span, mirrored about it.
                case TrussType.Pratt:
                    if (i < midpoint) down(i); else up(i);
                    break;

                // Pratt mirrored: diagonals rise toward mid-span.
                case TrussType.Howe:
                    if (i < midpoint) up(i); else down(i);
                    break;

                case TrussType.CrossBraced:
                    down(i);
                    up(i);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(options), options.Type, "Unhandled truss type.");
            }
        }

        if (options.GenerateEndPosts)
        {
            // Where the chords meet, the post would collapse onto the shared
            // point and clash with the chords running into it.
            if (!meetAtStart)
                Add(0, bottomOffset, topNodes[0], bottomNodes[0], TrussMemberRole.EndPost);

            if (!meetAtEnd)
                Add(panels, bottomOffset + panels, topNodes[panels], bottomNodes[panels], TrussMemberRole.EndPost);
        }

        return members;
    }

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
