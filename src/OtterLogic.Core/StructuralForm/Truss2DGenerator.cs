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
/// Nodes sit at shared <em>stations</em> — normalised arc-length positions
/// running 0 to 1 along each chord. Because both chords are evaluated at the
/// same station list, top node <c>i</c> always pairs with bottom node <c>i</c>
/// and every web pattern reduces to index arithmetic.
/// </para>
/// <para>
/// Two ways of arriving at that list, and the difference matters:
/// </para>
/// <list type="bullet">
/// <item><description>
/// With <see cref="Truss2DOptions.Divisions"/> (or
/// <see cref="Truss2DOptions.SnapSpacing"/>) set, the panel count is fixed up
/// front and the stations are laid out evenly, then <em>snapped</em> onto nearby
/// snap points. The member count is whatever you asked for; the snap points only
/// move members, never add them.
/// </description></item>
/// <item><description>
/// With neither set, the snap points <em>are</em> the stations: polyline
/// vertices, curve kinks and picked points each become a node.
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

        double[] stations = ResolveStations(top, bottom, topLength, bottomLength, options);

        var topNodes = new Point3d[stations.Length];
        var bottomNodes = new Point3d[stations.Length];
        for (int i = 0; i < stations.Length; i++)
        {
            topNodes[i] = PointAtStation(top, stations[i]);
            bottomNodes[i] = PointAtStation(bottom, stations[i]);
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

    private static double[] ResolveStations(
        Curve top, Curve bottom, double topLength, double bottomLength, Truss2DOptions options)
    {
        double merge = Math.Clamp(options.SnapTolerance / Math.Max(topLength, bottomLength), 1e-9, 0.25);

        // Everything the geometry and the user say is a real point.
        var targets = new List<double>();
        targets.AddRange(GeometryStations(top, topLength));
        targets.AddRange(GeometryStations(bottom, bottomLength));

        foreach (Point3d point in options.AdditionalSnapPoints)
            targets.Add(StationOfPoint(top, bottom, topLength, bottomLength, point));

        int panels = ResolvePanelCount(options, topLength, bottomLength);

        // No division driver: the snap points are the nodes.
        if (panels <= 0)
            return Sort(targets, merge, includeEnds: true);

        var stations = new double[panels + 1];
        for (int i = 0; i <= panels; i++)
            stations[i] = i / (double)panels;

        // Half a panel each way: far enough to reach a nearby point, never far
        // enough for two stations to swap places or collapse together.
        SnapToTargets(stations, Sort(targets, merge, includeEnds: false), 0.5 / panels);

        return Sort(stations.ToList(), merge, includeEnds: true);
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

    private static double StationOfPoint(
        Curve top, Curve bottom, double topLength, double bottomLength, Point3d point)
    {
        top.ClosestPoint(point, out double topParam);
        bottom.ClosestPoint(point, out double bottomParam);

        double toTop = top.PointAt(topParam).DistanceTo(point);
        double toBottom = bottom.PointAt(bottomParam).DistanceTo(point);

        return toTop <= toBottom
            ? LengthTo(top, topParam) / topLength
            : LengthTo(bottom, bottomParam) / bottomLength;
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

        bool verticals = options.Type is TrussType.Vertical
            or TrussType.WarrenWithVerticals
            or TrussType.Pratt
            or TrussType.Howe;

        if (verticals)
            for (int i = 1; i < panels; i++)
                AddVertical(i);

        double midpoint = panels / 2.0;

        for (int i = 0; i < panels; i++)
        {
            switch (options.Type)
            {
                case TrussType.Vertical:
                    break;

                // A continuous zigzag: alternating panels flip the diagonal.
                case TrussType.Warren:
                case TrussType.WarrenWithVerticals:
                    if (i % 2 == 0) AddDown(i); else AddUp(i);
                    break;

                // Diagonals fall toward mid-span, mirrored about it.
                case TrussType.Pratt:
                    if (i < midpoint) AddDown(i); else AddUp(i);
                    break;

                // Pratt mirrored: diagonals rise toward mid-span.
                case TrussType.Howe:
                    if (i < midpoint) AddUp(i); else AddDown(i);
                    break;

                case TrussType.CrossBraced:
                    AddDown(i);
                    AddUp(i);
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
