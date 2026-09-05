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
/// Nodes are placed at shared <em>stations</em> — normalised arc-length
/// positions running 0 to 1 along each chord. Stations come from three sources,
/// merged and de-duplicated: the geometry itself (polyline vertices and curve
/// kinks), an optional target spacing, and any additional points the user
/// picked. Because both chords are evaluated at the same station list, top node
/// <c>i</c> always pairs with bottom node <c>i</c> and every web pattern reduces
/// to index arithmetic.
/// </para>
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

        var members = BuildMembers(topNodes, bottomNodes, options);
        bool planar = IsPlanar(topNodes, bottomNodes, options.SnapTolerance);

        return new Truss2D(topNodes, bottomNodes, members, options.Type, planar);
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
        var stations = new List<double> { 0.0, 1.0 };

        // Default nodes: wherever the geometry already says something happens.
        stations.AddRange(GeometryStations(top, topLength));
        stations.AddRange(GeometryStations(bottom, bottomLength));

        // Optional even subdivision, driven off the longer chord so both agree.
        if (options.SnapSpacing > RhinoMath.ZeroTolerance)
        {
            double longest = Math.Max(topLength, bottomLength);
            int panels = Math.Max(1, (int)Math.Round(longest / options.SnapSpacing, MidpointRounding.AwayFromZero));
            for (int i = 1; i < panels; i++)
                stations.Add(i / (double)panels);
        }

        // User-picked points, pulled onto whichever chord is nearer.
        foreach (Point3d point in options.AdditionalSnapPoints)
            stations.Add(StationOfPoint(top, bottom, topLength, bottomLength, point));

        double merge = options.SnapTolerance / Math.Max(topLength, bottomLength);
        return Deduplicate(stations, merge);
    }

    /// <summary>
    /// Stations implied by the curve itself: polyline vertices, or tangent
    /// discontinuities on anything else. A plain line contributes none, which is
    /// why <see cref="Truss2DOptions.SnapSpacing"/> exists.
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

    private static double[] Deduplicate(List<double> stations, double tolerance)
    {
        tolerance = Math.Clamp(tolerance, 1e-9, 0.25);

        stations.Sort();

        var kept = new List<double>(stations.Count);
        foreach (double station in stations)
        {
            if (station < 0.0 || station > 1.0) continue;
            if (kept.Count > 0 && station - kept[^1] < tolerance) continue;
            kept.Add(station);
        }

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
        Point3d[] topNodes, Point3d[] bottomNodes, Truss2DOptions options)
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
            Add(0, bottomOffset, topNodes[0], bottomNodes[0], TrussMemberRole.EndPost);
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
