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
/// measured as a fraction of its <em>plan</em> length. One list, shared by both
/// chords, so top node <c>i</c> and bottom node <c>i</c> sit at the same plan
/// position and every web pattern reduces to index arithmetic.
/// </para>
/// <para>
/// Plan rather than along-the-chord because a pitched top chord is longer than
/// the level bottom chord under it: divide each by its own length and node
/// <c>i</c> lands a different distance along each of them, leaving every
/// vertical leaning. See <see cref="ChordRuler"/> for how that is measured.
/// </para>
/// <para>
/// Two ways of arriving at the list, and the difference matters:
/// </para>
/// <list type="bullet">
/// <item><description>
/// With <see cref="Truss2DOptions.Divisions"/> (or
/// <see cref="Truss2DOptions.SnapSpacing"/>) set, the panel count is fixed up
/// front and laid out evenly on plan, then <em>snapped</em> onto nearby points —
/// the vertices and kinks of either chord, plus the picked points. Whatever did
/// not snap is then spread evenly between the stations that did, so the panels
/// around a snapped node do not come out short and long against the rest. The
/// member count is whatever you asked for; snap points move members, never add
/// them.
/// </description></item>
/// <item><description>
/// With neither set, the snap points <em>are</em> the stations: polyline
/// vertices, curve kinks and picked points each become a node, and there is
/// nothing left over to spread.
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
        if (!Enum.IsDefined(options.Type))
            throw new ArgumentException(
                $"Truss type {(int)options.Type} does not exist. Valid values are "
                + $"0-{Enum.GetValues<TrussType>().Length - 1}.",
                nameof(options));

        Curve top = AsNurbs(topChord);
        Curve bottom = AlignToStart(AsNurbs(bottomChord), top);

        if (top.GetLength() <= RhinoMath.ZeroTolerance || bottom.GetLength() <= RhinoMath.ZeroTolerance)
            throw new ArgumentException("Both chords must have length.");

        var topRuler = new ChordRuler(top, options.SnapTolerance);
        var bottomRuler = new ChordRuler(bottom, options.SnapTolerance);

        double[] stations = ResolveStations(topRuler, bottomRuler, options);

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

        var members = BuildMembers(topNodes, bottomNodes, options, meetAtStart, meetAtEnd);
        bool planar = IsPlanar(topNodes, bottomNodes, options.SnapTolerance);

        return new Truss2D(topNodes, bottomNodes, members, options, planar, meetAtStart, meetAtEnd);
    }

    /// <summary>
    /// A working copy of a chord, in NURBS form.
    /// <para>
    /// The same curve, but the form whose parameterisation survives projection.
    /// Project an arc and the result runs at a different speed along itself, so
    /// a parameter stops meaning the same place on the two of them — measured
    /// on a 12 m chord that is a 24 mm error in every node. Everything below
    /// reads a chord and its plan projection at the same parameter, so that
    /// correspondence has to be exact rather than close.
    /// </para>
    /// </summary>
    private static Curve AsNurbs(Curve chord) => chord.ToNurbsCurve() ?? chord.DuplicateCurve();

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
    /// The stations both chords are divided at. One list, shared.
    /// <para>
    /// A station is a fraction of plan distance, which is a position the two
    /// chords can genuinely agree on, and a panel point is where the whole truss
    /// steps — top node and bottom node together. Dividing each chord against
    /// its own points would put node <c>i</c> at a different plan position on
    /// each and leave the member between them leaning, which is the very thing
    /// measuring on plan exists to prevent. It matters more once the unsnapped
    /// stations are spread, because then one anchor on one chord shifts every
    /// node after it and the whole run of verticals goes over.
    /// </para>
    /// <para>
    /// Which chord a picked point belongs to still decides <em>where</em> it
    /// lands, since it is measured against that chord. It no longer decides who
    /// has to move once it is there.
    /// </para>
    /// </summary>
    private static double[] ResolveStations(ChordRuler top, ChordRuler bottom, Truss2DOptions options)
    {
        double merge = Math.Clamp(
            options.SnapTolerance / Math.Max(top.Length, bottom.Length), 1e-9, 0.25);

        // Everything that wants a node: the vertices and kinks of both chords,
        // plus every picked point, measured against whichever it sits nearer to.
        var targets = new List<double>(top.GeometryStations());
        targets.AddRange(bottom.GeometryStations());

        foreach (Point3d point in options.AdditionalSnapPoints)
            targets.Add(StationOfNearerChord(top, bottom, point));

        int panels = ResolvePanelCount(options, top.Length, bottom.Length);

        // No division driver: the targets are the stations, ends included.
        if (panels <= 0)
            return Sort(targets, merge, includeEnds: true);

        // Half a panel each way: far enough to reach a nearby point, never far
        // enough for two stations to swap places or collapse together.
        double[] stations = EvenStations(panels);
        bool[] anchored = SnapToTargets(stations, Sort(targets, merge, includeEnds: false), 0.5 / panels);

        SpreadBetweenAnchors(stations, anchored);

        return stations;
    }

    /// <summary>
    /// Spread the stations that did not snap evenly between the ones that did.
    /// <para>
    /// Snapping on its own leaves the two panels either side of a snapped node
    /// short and long while the whole rest of the chord keeps the spacing it was
    /// laid out with. On a drawing that reads as a mistake, because it is not
    /// how anyone sets a truss out: the fixed points are fixed, and what lies
    /// between two of them is divided evenly.
    /// </para>
    /// <para>
    /// The chord ends count as fixed points, so a chord with nothing snapped is
    /// spread evenly end to end, which is where it started. Panel count is never
    /// touched — stations move between anchors, they are not added or removed.
    /// </para>
    /// </summary>
    private static void SpreadBetweenAnchors(double[] stations, bool[] anchored)
    {
        int previous = 0;

        for (int next = 1; next < stations.Length; next++)
        {
            if (!anchored[next]) continue;

            double span = stations[next] - stations[previous];
            int panels = next - previous;

            for (int i = previous + 1; i < next; i++)
                stations[i] = stations[previous] + span * (i - previous) / panels;

            previous = next;
        }
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
    /// <para>
    /// Returns which stations ended up fixed, ends included, for
    /// <see cref="SpreadBetweenAnchors"/> to divide between.
    /// </para>
    /// </summary>
    private static bool[] SnapToTargets(double[] stations, double[] targets, double radius)
    {
        var claimed = new bool[stations.Length];

        // The ends never move, so they anchor the spread like any snapped node.
        claimed[0] = true;
        claimed[^1] = true;

        if (targets.Length == 0 || stations.Length < 3) return claimed;

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

        var targetClaimed = new bool[targets.Length];

        foreach (var (station, target, _) in candidates)
        {
            if (claimed[station] || targetClaimed[target]) continue;

            stations[station] = targets[target];
            claimed[station] = true;
            targetClaimed[target] = true;
        }

        return claimed;
    }

    /// <summary>
    /// The plan station of a picked point, measured against whichever chord it
    /// sits nearer to.
    /// <para>
    /// Which chord still matters: a point beside the sagging middle of a bottom
    /// chord is at a different plan position from the point on the top chord
    /// directly above it, and the one the user picked beside is the one they
    /// meant. What comes out is a plan position, and both chords step there.
    /// </para>
    /// </summary>
    private static double StationOfNearerChord(ChordRuler top, ChordRuler bottom, Point3d point)
    {
        double topStation = top.StationNearest(point, out double toTop);
        double bottomStation = bottom.StationNearest(point, out double toBottom);

        return toTop <= toBottom ? topStation : bottomStation;
    }

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

    private static List<TrussMember> BuildMembers(
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

        void AddVertical(int i) => Add(i, bottomOffset + i, topNodes[i], bottomNodes[i], TrussMemberRole.Vertical);
        void AddDown(int i) => Add(i, bottomOffset + i + 1, topNodes[i], bottomNodes[i + 1], TrussMemberRole.Diagonal);
        void AddUp(int i) => Add(bottomOffset + i, i + 1, bottomNodes[i], topNodes[i + 1], TrussMemberRole.Diagonal);

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

    /// <summary>
    /// The ruler a chord is measured and divided by: the chord projected onto
    /// the world XY plane.
    /// <para>
    /// Panels are set out by <em>plan</em> distance, not by distance along the
    /// chord. Two chords at different slopes cover the same span in different
    /// lengths, so dividing each by its own length puts node <c>i</c> at a
    /// different place along each of them and leaves every vertical leaning —
    /// which is what a truss with a pitched top chord and a level bottom one
    /// looks like. Dividing by plan distance puts the pair at the same place on
    /// plan, and the member between them stands up straight.
    /// </para>
    /// <para>
    /// A chord seen edge-on in plan has no plan length to divide, so it measures
    /// along itself instead and behaves as it always did. Nothing else in the
    /// generator knows the difference.
    /// </para>
    /// </summary>
    private sealed class ChordRuler
    {
        private readonly Curve _chord;
        private readonly Curve _plan;

        internal ChordRuler(Curve chord, double tolerance)
        {
            _chord = chord;

            Curve? plan = Curve.ProjectToPlane(chord, Plane.WorldXY);
            _plan = plan is not null && plan.GetLength() > tolerance ? plan : chord;

            Length = _plan.GetLength();
        }

        /// <summary>Plan length of the chord, and the distance stations are fractions of.</summary>
        internal double Length { get; }

        /// <summary>Where a parameter on the chord falls, as a fraction of plan length.</summary>
        internal double StationAt(double parameter)
            => _plan.GetLength(new Interval(_plan.Domain.T0, parameter)) / Length;

        internal Point3d PointAtStation(double station)
        {
            // Pin the ends exactly: arc-length inversion drifts by a hair
            // otherwise, and a truss whose end post misses the chord by 1e-9 is
            // a nuisance.
            if (station <= 0.0) return _chord.PointAtStart;
            if (station >= 1.0) return _chord.PointAtEnd;

            return _plan.NormalizedLengthParameter(station, out double parameter)
                ? _chord.PointAt(parameter)
                : _chord.PointAtNormalizedLength(station);
        }

        /// <summary>
        /// Stations implied by the chord itself — polyline vertices and other
        /// tangent breaks. A plain line contributes none, which is why
        /// <see cref="Truss2DOptions.Divisions"/> exists.
        /// </summary>
        internal IEnumerable<double> GeometryStations()
        {
            Interval domain = _chord.Domain;
            double cursor = domain.T0;

            while (_chord.GetNextDiscontinuity(Continuity.C1_locus_continuous, cursor, domain.T1, out double next))
            {
                yield return StationAt(next);
                cursor = next;
            }
        }

        /// <summary>
        /// The station of the point on this chord nearest <paramref name="point"/>,
        /// and how far away that leaves it. Nearness is measured in full 3D: it
        /// decides which chord a picked point belongs to, and that is a question
        /// about where the point actually is.
        /// </summary>
        internal double StationNearest(Point3d point, out double distance)
        {
            _chord.ClosestPoint(point, out double parameter);
            distance = _chord.PointAt(parameter).DistanceTo(point);

            return StationAt(parameter);
        }
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
