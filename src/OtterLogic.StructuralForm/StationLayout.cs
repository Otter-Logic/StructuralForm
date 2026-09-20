using Rhino;
using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// What a truss asks of <see cref="StationLayout"/>: the part of its options
/// that decides where the panel points go, and nothing about what is built
/// between them.
/// <para>
/// Its own record rather than either truss's options, so the layout cannot come
/// to depend on a field only one of them has.
/// </para>
/// </summary>
internal sealed record StationRequest(
    int Divisions,
    double Spacing,
    SnapStrictness Strictness,
    IReadOnlyList<Point3d> SnapPoints,
    double Tolerance);

/// <summary>
/// Where the panel points of a truss go: one list of stations, shared by every
/// chord it has.
/// <para>
/// Lifted out of <see cref="FlatTrussGenerator"/> when
/// <see cref="BoxTrussGenerator"/> arrived. A box truss is set out exactly as a
/// flat one is — divisions drive, the chords' own points win, picked points
/// take what is left, the rest is spread — only over three or four chords
/// instead of two. Nothing here knows how many there are, or what gets built
/// between them.
/// </para>
/// </summary>
internal static class StationLayout
{
    /// <summary>
    /// The stations every chord is divided at. One list, shared.
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
    internal static double[] Resolve(
        IReadOnlyList<ChordRuler> chords,
        StationRequest options,
        out int unusedSnapPoints,
        out int offChordSnapPoints)
    {
        double merge = Math.Clamp(
            options.Tolerance / chords.Max(c => c.Length), 1e-9, 0.25);

        // The chords' own points: polyline vertices and curve kinks, on either
        // chord. These are the ones a truss has to honour — a node anywhere but
        // a kink leaves a chord member cutting the corner — so they are checked
        // first and claim whatever they can reach.
        var natural = chords.SelectMany(c => c.GeometryStations()).ToList();

        // The picked points, reduced to stations once, here. Everything below
        // works in station space: a point that survives this is already at a
        // plan position, so nothing downstream has to know it came from a
        // Point3d, and the two rules become the same operation twice.
        double[] picked = OnChordStations(chords, options, out offChordSnapPoints);

        int panels = PanelCount.Resolve(options.Divisions, options.Spacing, chords.Max(c => c.Length));
        double[] stations;

        if (panels <= 0)
        {
            // No division driver: every detected point is a station in its own
            // right, natural and picked alike, ends included. Nothing is
            // competing for a fixed number of nodes, so there is nothing to
            // prioritise and no strictness to apply.
            var all = new List<double>(natural);
            all.AddRange(picked);

            stations = Sort(all, merge, includeEnds: true);
        }
        else if (options.Strictness == SnapStrictness.Strict)
        {
            // Strict hands the decision the other way round: the points are the
            // fixed setting-out, and the panels are shared between them.
            stations = StrictStations(natural, picked, panels, merge);
        }
        else
        {
            // Just under half a panel each way: far enough to reach a nearby
            // point, never far enough for two stations to swap places or
            // collapse together. Strictly under, because a whole half panel is
            // the one distance at which two neighbours can land on the same
            // value — one reaching forward, one reaching back — and the two
            // rules run separately, so nothing else would notice them meeting.
            double reach = 0.5 / panels;

            stations = EvenStations(panels);

            // The ends never move, so they anchor the spread like any snapped
            // node.
            var anchored = new bool[stations.Length];
            anchored[0] = true;
            anchored[^1] = true;

            // Rule 1, and it wins, because it goes first and claims
            // exclusively: the chords' own points.
            SnapToStations(stations, Sort(natural, merge, includeEnds: false), reach, anchored);

            // Rule 2: the picked points, offered whatever rule 1 left free.
            SnapToStations(
                stations, Sort(new List<double>(picked), merge, includeEnds: false), reach, anchored);

            SpreadBetweenAnchors(stations, anchored);
        }

        unusedSnapPoints = CountMissing(picked, stations, merge);

        return stations;
    }

    /// <summary>
    /// The picked points that are actually on a chord, as stations; the rest
    /// counted out through <paramref name="offChord"/>.
    /// <para>
    /// A snap point has to be on the top or bottom chord. Off them there is no
    /// honest answer to where it means: projected square onto a sloped chord it
    /// lands at the foot of the perpendicular rather than the plan position it
    /// was picked at, and every extra metre to the side drags that further
    /// away. Requiring it on the curve removes the question rather than
    /// answering it badly, and is what lets everything downstream work in plain
    /// station space.
    /// </para>
    /// </summary>
    private static double[] OnChordStations(
        IReadOnlyList<ChordRuler> chords, StationRequest options, out int offChord)
    {
        offChord = 0;

        if (options.SnapPoints.Count == 0) return Array.Empty<double>();

        var stations = new List<double>(options.SnapPoints.Count);

        foreach (Point3d point in options.SnapPoints)
        {
            double station = StationOfNearestChord(chords, point, out double distance);

            if (distance <= options.Tolerance)
                stations.Add(station);
            else
                offChord++;
        }

        return stations.ToArray();
    }

    /// <summary>
    /// How many of <paramref name="wanted"/> did not end up as a node.
    /// <para>
    /// Counted against the finished list rather than tracked through the
    /// snapping, so it stays true whichever route built it — and cannot drift
    /// from what the geometry actually shows, which is the whole reason it is
    /// reported.
    /// </para>
    /// </summary>
    private static int CountMissing(double[] wanted, double[] stations, double merge)
    {
        int missing = 0;

        foreach (double target in wanted)
            if (!stations.Any(station => Math.Abs(station - target) <= merge))
                missing++;

        return missing;
    }

    /// <summary>
    /// The stations when the snap points win: every one of them is a node, and
    /// the panels are shared out between them.
    /// <para>
    /// This is how a truss is actually set out against fixed points on a
    /// drawing. The fixed points come first — a purlin line, a hanger, the
    /// vertices of the chords themselves — and each bay between two of them is
    /// then divided evenly on its own. Spacing is regular <em>within</em> a
    /// bay rather than across the whole truss, which is the trade the relaxed
    /// rule refuses to make.
    /// </para>
    /// <para>
    /// The requested panel count is still honoured wherever it can be: it is
    /// shared between the bays in proportion to their plan width. It only grows
    /// when there are more fixed points than the count can accommodate, since
    /// dropping one would defeat the whole point of asking for strict.
    /// </para>
    /// </summary>
    private static double[] StrictStations(
        List<double> natural, double[] picked, int panels, double merge)
    {
        var fixedPoints = new List<double>(natural);
        fixedPoints.AddRange(picked);

        // Merging near-coincident points here is what keeps a point that lands
        // on a chord vertex from splitting one node into two a hair apart.
        double[] anchors = Sort(fixedPoints, merge, includeEnds: true);

        int bays = anchors.Length - 1;
        int[] share = Apportion(anchors, Math.Max(panels, bays));

        var stations = new List<double>(share.Sum() + 1);

        for (int bay = 0; bay < bays; bay++)
        {
            double from = anchors[bay];
            double width = anchors[bay + 1] - from;

            // The closing anchor of each bay is the opening one of the next, so
            // it is added once, by the next bay — or after the loop, for the end.
            for (int step = 0; step < share[bay]; step++)
                stations.Add(from + width * step / share[bay]);
        }

        stations.Add(1.0);

        return stations.ToArray();
    }

    /// <summary>
    /// Share <paramref name="total"/> panels between the bays that
    /// <paramref name="anchors"/> divides the truss into, in proportion to how
    /// wide each bay is on plan.
    /// <para>
    /// Every bay takes one panel before anything is shared, because a bay with
    /// no panel in it is two fixed points with no member between them. The rest
    /// goes out by largest remainder - hand out the whole panels each bay has
    /// earned, then give what is left to the bays that came closest to earning
    /// another. Rounding each bay independently would not add up to the count
    /// that was asked for, which is the one thing this has to guarantee.
    /// </para>
    /// </summary>
    private static int[] Apportion(double[] anchors, int total)
    {
        int bays = anchors.Length - 1;
        var share = new int[bays];

        var widths = new double[bays];
        double span = 0.0;

        for (int i = 0; i < bays; i++)
        {
            widths[i] = anchors[i + 1] - anchors[i];
            span += widths[i];
            share[i] = 1;
        }

        int spare = total - bays;
        if (spare <= 0 || span <= 0.0) return share;

        // Whole panels earned, and how close each bay came to earning one more.
        var remainder = new (int Bay, double Fraction)[bays];
        int handed = 0;

        for (int i = 0; i < bays; i++)
        {
            double earned = widths[i] / span * spare;
            int whole = (int)Math.Floor(earned);

            share[i] += whole;
            handed += whole;
            remainder[i] = (i, earned - whole);
        }

        // Largest fraction first, ties to the lower bay so the same truss comes
        // out the same way every time.
        Array.Sort(remainder, (a, b) => b.Fraction != a.Fraction
            ? b.Fraction.CompareTo(a.Fraction)
            : a.Bay.CompareTo(b.Bay));

        for (int i = 0; i < spare - handed; i++)
            share[remainder[i].Bay]++;

        return share;
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
    /// Pull each still-free interior station onto the nearest target within
    /// <paramref name="radius"/> of it, measured on plan.
    /// <para>
    /// Assignment is greedy, nearest pair first, and both sides are claimed
    /// exclusively — otherwise two stations converge on one popular point and
    /// the panel either side degenerates.
    /// </para>
    /// <para>
    /// <paramref name="claimed"/> is the caller's, carried between calls rather
    /// than returned, and that is what makes the priority work: the chords' own
    /// points go through first and take what they can reach, the picked points
    /// are then offered the same routine and find those stations already spoken
    /// for. One rule, run twice, instead of two rules to keep in step.
    /// </para>
    /// </summary>
    private static void SnapToStations(
        double[] stations, double[] targets, double radius, bool[] claimed)
    {
        if (targets.Length == 0 || stations.Length < 3) return;

        var candidates = new List<(int Station, int Target, double Distance)>();

        for (int s = 1; s < stations.Length - 1; s++)
        {
            for (int t = 0; t < targets.Length; t++)
            {
                double distance = Math.Abs(targets[t] - stations[s]);
                if (distance < radius)
                    candidates.Add((s, t, distance));
            }
        }

        candidates.Sort(Nearest);

        var targetClaimed = new bool[targets.Length];

        foreach (var (station, target, _) in candidates)
        {
            if (claimed[station] || targetClaimed[target]) continue;

            stations[station] = targets[target];
            claimed[station] = true;
            targetClaimed[target] = true;
        }
    }

    /// <summary>
    /// Greedy assignment order: nearest pair first, then lowest station, then
    /// lowest target.
    /// <para>
    /// The last two are only tie-breaks, but they are not decoration.
    /// <see cref="List{T}.Sort(Comparison{T})"/> is unstable, so without them a
    /// point equidistant between two stations — a point in the middle of a
    /// panel, which is nothing unusual — lands on whichever one the sort
    /// happened to leave first, and the same truss comes out differently.
    /// </para>
    /// </summary>
    private static int Nearest((int Station, int Other, double Distance) a, (int Station, int Other, double Distance) b)
    {
        int byDistance = a.Distance.CompareTo(b.Distance);
        if (byDistance != 0) return byDistance;

        int byStation = a.Station.CompareTo(b.Station);
        return byStation != 0 ? byStation : a.Other.CompareTo(b.Other);
    }

    /// <summary>
    /// The station of a picked point, measured against whichever chord it sits
    /// nearest to, and how far from that chord it is.
    /// <para>
    /// Which chord still matters: a point beside the sagging middle of a bottom
    /// chord is at a different station from the point on the top chord directly
    /// above it, and the one the user picked beside is the one they meant. What
    /// comes out is a station, and every chord steps there.
    /// </para>
    /// <para>
    /// Ties go to the chord listed first, which is a top chord: strictly nearer
    /// is what it takes to displace it, so the same pick always lands the same
    /// way.
    /// </para>
    /// </summary>
    private static double StationOfNearestChord(
        IReadOnlyList<ChordRuler> chords, Point3d point, out double distance)
    {
        double station = chords[0].StationNearest(point, out distance);

        for (int i = 1; i < chords.Count; i++)
        {
            double candidate = chords[i].StationNearest(point, out double away);
            if (away >= distance) continue;

            station = candidate;
            distance = away;
        }

        return station;
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

    /// <summary>
    /// The ruler a chord is measured and divided by: the chord itself, or the
    /// chord projected onto the world XY plane when
    /// <see cref="FlatTrussOptions.MeasureOnPlan"/> asks for it.
    /// <para>
    /// Along the chord is the default because it is the only measure every
    /// truss has. One standing on end has no plan length at all, and one
    /// running through space has a plan length that says nothing about it.
    /// </para>
    /// <para>
    /// On plan is for the roof truss. Two chords at different slopes cover the
    /// same span in different lengths, so dividing each by its own length puts
    /// node <c>i</c> at a different place along each of them and leaves every
    /// vertical leaning — which is what a truss with a pitched top chord and a
    /// level bottom one looks like. Dividing by plan distance puts the pair at
    /// the same place on plan, and the member between them stands up straight.
    /// </para>
    /// <para>
    /// A chord seen edge-on in plan has no plan length to divide, so it measures
    /// along itself whatever was asked for. Nothing else in the generator knows
    /// which ruler it was handed, which is why the rest of it still talks about
    /// plan positions: measured along the chord, the ruler and the chord are
    /// simply the same curve.
    /// </para>
    /// </summary>
    internal sealed class ChordRuler
    {
        private readonly Curve _chord;
        private readonly Curve _plan;

        internal ChordRuler(Curve chord, double tolerance, bool onPlan)
        {
            _chord = chord;

            Curve? plan = onPlan ? Curve.ProjectToPlane(chord, Plane.WorldXY) : null;
            _plan = plan is not null && plan.GetLength() > tolerance ? plan : chord;

            Length = _plan.GetLength();
        }

        /// <summary>Length of the ruler, and the distance stations are fractions of.</summary>
        internal double Length { get; }

        /// <summary>Where a parameter on the chord falls, as a fraction of the ruler's length.</summary>
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
        /// The chord's own parameter at a station. For a chord that is an edge
        /// of a surface this is a surface parameter too, which is how a grid
        /// gets from stations along its edges to nodes across its middle.
        /// </summary>
        internal double ParameterAtStation(double station)
        {
            if (station <= 0.0) return _chord.Domain.T0;
            if (station >= 1.0) return _chord.Domain.T1;

            if (_plan.NormalizedLengthParameter(station, out double parameter))
                return parameter;

            return _chord.NormalizedLengthParameter(station, out parameter)
                ? parameter
                : _chord.Domain.ParameterAt(station);
        }

        /// <summary>
        /// Stations implied by the chord itself — polyline vertices and other
        /// tangent breaks. A plain line contributes none, which is why
        /// <see cref="FlatTrussOptions.Divisions"/> exists.
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
}
