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
    /// One node on every chord, and where each chord fixes it.
    /// <para>
    /// The unit the whole layout works in. A ring is a panel point of the
    /// truss: the top node, the bottom node, and on a box truss the two beside
    /// them. <see cref="Fixed"/> holds the station a chord's own snap point or
    /// vertex pins it to, and null where that chord has nothing to say and
    /// follows the ring.
    /// </para>
    /// </summary>
    private sealed class Ring
    {
        internal Ring(int chords) => Fixed = new double?[chords];

        /// <summary>Per chord: the station it pins this ring to, or null.</summary>
        internal double?[] Fixed { get; }

        /// <summary>Where the ring sits as a whole - the mean of what pins it.</summary>
        internal double Station { get; set; }

        /// <summary>
        /// True when a chord's own geometry pins this ring, rather than only a
        /// picked point. Those win the first snapping pass, because a node
        /// anywhere but a kink leaves a chord member cutting the corner.
        /// </summary>
        internal bool FromGeometry { get; set; }
    }

    /// <summary>The finished layout: the shared spine, and each chord's own stations.</summary>
    private readonly record struct Layout(double[] Spine, double[][] PerChord);

    /// <summary>
    /// The stations every chord is divided at, one list shared by all of them.
    /// <para>
    /// What a surface grid wants: a station is a position across the whole
    /// form, and a grid line has to mean the same thing on both edges it spans.
    /// A truss wants <see cref="ResolvePaired"/> instead.
    /// </para>
    /// </summary>
    internal static double[] Resolve(
        IReadOnlyList<ChordRuler> chords,
        StationRequest options,
        out int unusedSnapPoints,
        out int offChordSnapPoints)
        => Build(chords, options, pairAcrossChords: false, out unusedSnapPoints, out offChordSnapPoints)
            .Spine;

    /// <summary>
    /// One station list per chord, the same length, paired by index: entry
    /// <c>i</c> of every list is the same panel point of the truss.
    /// <para>
    /// The lists are separate because a snap point belongs to the chord it was
    /// picked on. Set out seven points along the top chord and seven along the
    /// bottom, not quite above each other, and what you have asked for is seven
    /// panel points with a slightly leaning vertical at each - not fourteen.
    /// Collapsing them onto one shared list gave fourteen: every point became a
    /// station on <em>both</em> chords, so each one arrived twice, a hand's
    /// width apart, with a vertical between the two halves of the pair.
    /// </para>
    /// <para>
    /// So the chords' points are grouped into <see cref="Ring"/>s first, and a
    /// ring is what gets a node. Where only one chord pins a ring, the others
    /// follow it to the same station and the vertical stands up straight, which
    /// is what a lone chord vertex should do. Where two chords pin it, each
    /// keeps its own point and the vertical leans between them, which is what
    /// two deliberately placed points should do.
    /// </para>
    /// <para>
    /// <see cref="StationRequest.Divisions"/> still fixes how many rings there
    /// are, and the rings that nothing pins are still spread evenly between the
    /// ones that are - along the chord or on plan, as asked. That is the only
    /// thing measuring on plan changes here; a pinned ring sits where its point
    /// is either way.
    /// </para>
    /// </summary>
    internal static double[][] ResolvePaired(
        IReadOnlyList<ChordRuler> chords,
        StationRequest options,
        out int unusedSnapPoints,
        out int offChordSnapPoints)
        => Build(chords, options, pairAcrossChords: true, out unusedSnapPoints, out offChordSnapPoints)
            .PerChord;

    private static Layout Build(
        IReadOnlyList<ChordRuler> chords,
        StationRequest options,
        bool pairAcrossChords,
        out int unusedSnapPoints,
        out int offChordSnapPoints)
    {
        double merge = Math.Clamp(
            options.Tolerance / chords.Max(c => c.Length), 1e-9, 0.25);

        // Everything that wants a node, kept against the chord it came from:
        // that chord's vertices and kinks, plus the points picked on it.
        List<(double Station, bool Natural)>[] pinned = Pins(chords, options, out offChordSnapPoints);

        // Grouped across chords, so two points meant as a pair become one panel
        // point rather than two.
        List<Ring> rings = GroupIntoRings(pinned, chords.Count, merge, pairAcrossChords);

        int panels = PanelCount.Resolve(options.Divisions, options.Spacing, chords.Max(c => c.Length));

        // The spine decides how many rings there are and where the unpinned
        // ones sit. It is one station per ring, so the rules below are the same
        // ones they always were - they simply count a pair once.
        var geometry = rings.Where(r => r.FromGeometry).Select(r => r.Station).ToList();
        var picked = rings.Where(r => !r.FromGeometry).Select(r => r.Station).ToArray();

        double[] spine;
        int[] ringOf;

        if (panels <= 0)
        {
            // No division driver: every ring is a station in its own right,
            // ends included. Nothing is competing for a fixed number of nodes,
            // so there is nothing to prioritise and no strictness to apply.
            var all = new List<double>(geometry);
            all.AddRange(picked);

            spine = Sort(all, merge, includeEnds: true);
        }
        else if (options.Strictness == SnapStrictness.Strict)
        {
            // Strict hands the decision the other way round: the rings are the
            // fixed setting-out, and the panels are shared between them.
            var fixedPoints = new List<double>(geometry);
            fixedPoints.AddRange(picked);

            spine = StrictStations(fixedPoints, panels, merge);
        }
        else
        {
            // Just under half a panel each way: far enough to reach a nearby
            // ring, never far enough for two stations to swap places or
            // collapse together. Strictly under, because a whole half panel is
            // the one distance at which two neighbours can land on the same
            // value - one reaching forward, one reaching back - and the two
            // rules run separately, so nothing else would notice them meeting.
            double reach = 0.5 / panels;

            spine = EvenStations(panels);

            // The ends never move, so they anchor the spread like any snapped
            // node.
            var anchored = new bool[spine.Length];
            anchored[0] = true;
            anchored[^1] = true;

            // Rule 1, and it wins, because it goes first and claims
            // exclusively: the rings the chords' own geometry pins.
            SnapToStations(spine, Sort(geometry, merge, includeEnds: false), reach, anchored);

            // Rule 2: the picked rings, offered whatever rule 1 left free.
            SnapToStations(
                spine, Sort(new List<double>(picked), merge, includeEnds: false), reach, anchored);

            SpreadBetweenAnchors(spine, anchored);
        }

        // Which ring, if any, each station of the spine ended up being. Matched
        // after the fact rather than threaded through the rules above, so the
        // three routes to a spine stay interchangeable.
        ringOf = MatchRings(spine, rings, merge);

        double[][] perChord = PerChord(spine, rings, ringOf, chords.Count, pairAcrossChords);

        unusedSnapPoints = CountMissing(pinned, perChord, merge);

        return new Layout(spine, perChord);
    }

    /// <summary>
    /// What each chord wants a node at: its own vertices and kinks, and the
    /// picked points lying on it.
    /// <para>
    /// Kept per chord rather than pooled, because which chord a point is on is
    /// the whole question. A point on the top chord fixes the top node of its
    /// ring and says nothing about the bottom one.
    /// </para>
    /// </summary>
    private static List<(double Station, bool Natural)>[] Pins(
        IReadOnlyList<ChordRuler> chords, StationRequest options, out int offChord)
    {
        offChord = 0;

        var pinned = new List<(double, bool)>[chords.Count];

        for (int c = 0; c < chords.Count; c++)
            pinned[c] = chords[c].GeometryStations().Select(station => (station, true)).ToList();

        foreach (Point3d point in options.SnapPoints)
        {
            int chord = NearestChord(chords, point, out double station, out double distance);

            // A snap point has to be on a chord. Off them there is no honest
            // answer to where it means: projected square onto a sloped chord it
            // lands at the foot of the perpendicular rather than the plan
            // position it was picked at, and every extra metre to the side
            // drags that further away.
            if (distance <= options.Tolerance)
                pinned[chord].Add((station, false));
            else
                offChord++;
        }

        foreach (List<(double Station, bool Natural)> chord in pinned)
            chord.Sort((a, b) => a.Station.CompareTo(b.Station));

        return pinned;
    }

    /// <summary>
    /// Group what the chords pin into rings, walking all of them in step.
    /// <para>
    /// Two points on different chords belong to the same panel point when each
    /// is nearer to the other than to the next point along its own chord. That
    /// is a local test with no scale in it, which is the point: whether a pair
    /// of picked points was meant as a pair is a fact about how far apart they
    /// are relative to their neighbours, not about how many divisions were
    /// asked for. A quarter of the span caps it, so two lone points at opposite
    /// ends of a truss are never read as a pair.
    /// </para>
    /// <para>
    /// With <paramref name="pair"/> off every pin becomes its own ring, which
    /// is the older shared-station behaviour exactly, and what a surface grid
    /// still wants.
    /// </para>
    /// </summary>
    private static List<Ring> GroupIntoRings(
        List<(double Station, bool Natural)>[] pinned, int chords, double merge, bool pair)
    {
        const double Cap = 0.25;

        // A pin at a chord end is not a constraint: every chord has a node at
        // each end whatever else happens, so the spine supplies them. Left in,
        // they would also pair - an end read as a locus discontinuity on one
        // chord absorbing a real point near the end of another, and dragging
        // the ring that point should have had off to the mean of the two.
        var live = new List<(double Station, bool Natural)>[chords];
        for (int c = 0; c < chords; c++)
            live[c] = pinned[c]
                .Where(pin => pin.Station > merge && pin.Station < 1.0 - merge)
                .ToList();

        pinned = live;

        var cursors = new int[chords];
        var rings = new List<Ring>();

        while (true)
        {
            // The earliest pin not yet spoken for, across every chord.
            int lead = -1;
            for (int c = 0; c < chords; c++)
            {
                if (cursors[c] >= pinned[c].Count) continue;
                if (lead >= 0 && pinned[c][cursors[c]].Station >= pinned[lead][cursors[lead]].Station) continue;
                lead = c;
            }

            if (lead < 0) break;

            var ring = new Ring(chords);
            double station = pinned[lead][cursors[lead]].Station;

            ring.Fixed[lead] = station;
            ring.FromGeometry = pinned[lead][cursors[lead]].Natural;
            cursors[lead]++;

            if (pair)
            {
                // The next pin still to come on the leading chord. What stops a
                // point being dragged into the wrong ring is that it may be
                // nearer to this one than to that.
                double leadNext = Next(pinned[lead], cursors[lead]);

                for (int c = 0; c < chords; c++)
                {
                    if (c == lead || cursors[c] >= pinned[c].Count) continue;

                    double candidate = pinned[c][cursors[c]].Station;
                    double apart = Math.Abs(candidate - station);

                    // Mutually nearest, both ways round: the candidate has to
                    // prefer this ring to the leading chord's next pin, and the
                    // ring has to prefer the candidate to that chord's next pin.
                    // One direction is not enough - a lone point halfway along
                    // is nearest to plenty of things that are not nearest to it.
                    if (apart >= Math.Abs(candidate - leadNext)) continue;
                    if (apart >= Math.Abs(station - Next(pinned[c], cursors[c] + 1))) continue;

                    // And a backstop for the pins with no neighbour to be
                    // judged against: two lone points at opposite ends of a
                    // truss are two panel points, not one flat-lying vertical.
                    if (apart >= Cap) continue;

                    ring.Fixed[c] = candidate;
                    ring.FromGeometry |= pinned[c][cursors[c]].Natural;
                    cursors[c]++;
                }
            }

            double sum = 0.0;
            int count = 0;
            foreach (double? value in ring.Fixed)
            {
                if (value is not double v) continue;
                sum += v;
                count++;
            }

            ring.Station = sum / count;
            rings.Add(ring);
        }

        return rings;

        // The next pin on a chord, or infinitely far off when there is none -
        // which leaves the cap to do the deciding.
        static double Next(List<(double Station, bool Natural)> chord, int at)
            => at < chord.Count ? chord[at].Station : double.PositiveInfinity;
    }

    /// <summary>
    /// Which ring each station of the spine represents, or -1 where the spine
    /// put a station nothing pinned.
    /// <para>
    /// Nearest first and claimed exclusively, the same assignment used
    /// everywhere else here, so a ring that lost its station to a nearer one
    /// does not then steal a second.
    /// </para>
    /// </summary>
    private static int[] MatchRings(double[] spine, List<Ring> rings, double merge)
    {
        var ringOf = new int[spine.Length];
        Array.Fill(ringOf, -1);

        if (rings.Count == 0) return ringOf;

        var candidates = new List<(int Station, int Ring, double Distance)>();

        for (int i = 0; i < spine.Length; i++)
            for (int r = 0; r < rings.Count; r++)
                candidates.Add((i, r, Math.Abs(rings[r].Station - spine[i])));

        candidates.Sort(Nearest);

        var ringTaken = new bool[rings.Count];

        foreach (var (station, ring, distance) in candidates)
        {
            if (ringOf[station] >= 0 || ringTaken[ring]) continue;

            // A ring only owns a station the spine actually put there. Anything
            // further off is a ring the division could not honour, and its
            // chords' points go unused rather than dragging a station to them.
            if (distance > merge) continue;

            ringOf[station] = ring;
            ringTaken[ring] = true;
        }

        return ringOf;
    }

    /// <summary>
    /// Each chord's own stations: the spine, with a chord's own pin substituted
    /// wherever its ring has one.
    /// <para>
    /// Substituting rather than spreading is deliberate. A ring pinned by one
    /// chord alone - a lone vertex on a pitched top chord - leaves every other
    /// chord on the spine, at that same station, so the vertical under the apex
    /// stands up straight. Only a chord that pinned the ring itself moves.
    /// </para>
    /// </summary>
    private static double[][] PerChord(
        double[] spine, List<Ring> rings, int[] ringOf, int chords, bool pair)
    {
        var perChord = new double[chords][];

        for (int c = 0; c < chords; c++)
        {
            var stations = (double[])spine.Clone();

            if (pair)
            {
                for (int i = 0; i < stations.Length; i++)
                {
                    if (ringOf[i] < 0) continue;
                    if (rings[ringOf[i]].Fixed[c] is not double own) continue;

                    stations[i] = own;
                }

                KeepInOrder(stations, spine);
            }

            perChord[c] = stations;
        }

        return perChord;
    }

    /// <summary>
    /// Undo any substitution that put a chord's stations out of order.
    /// <para>
    /// Pins arrive sorted and rings are built in order, so this should never
    /// fire. It exists because the alternative to checking is a chord that
    /// doubles back on itself, which turns a chord member inside out and is far
    /// harder to diagnose than a node that quietly stayed on the spine.
    /// </para>
    /// </summary>
    private static void KeepInOrder(double[] stations, double[] spine)
    {
        for (int i = 1; i < stations.Length; i++)
        {
            if (stations[i] > stations[i - 1]) continue;

            // Whichever of the two is the substitution goes back to the spine.
            if (stations[i] != spine[i]) stations[i] = spine[i];
            else stations[i - 1] = spine[i - 1];
        }
    }

    /// <summary>
    /// How many picked points did not end up as a node on their own chord.
    /// <para>
    /// Counted against the finished lists rather than tracked through the
    /// snapping, so it stays true whichever route built them - and cannot drift
    /// from what the geometry actually shows, which is the whole reason it is
    /// reported.
    /// </para>
    /// </summary>
    private static int CountMissing(
        List<(double Station, bool Natural)>[] pinned, double[][] perChord, double merge)
    {
        int missing = 0;

        for (int c = 0; c < pinned.Length; c++)
            foreach ((double station, bool natural) in pinned[c])
            {
                if (natural) continue;
                if (perChord[c].Any(s => Math.Abs(s - station) <= merge)) continue;

                missing++;
            }

        return missing;
    }

    /// <summary>
    /// The station of the chord nearest <paramref name="point"/>, which chord
    /// that is, and how far off it sits.
    /// <para>
    /// Which chord is the whole question now: a point on the top chord fixes
    /// the top node of its ring and leaves the others to follow.
    /// </para>
    /// <para>
    /// Ties go to the chord listed first, which is a top chord: strictly nearer
    /// is what it takes to displace it, so the same pick always lands the same
    /// way.
    /// </para>
    /// </summary>
    private static int NearestChord(
        IReadOnlyList<ChordRuler> chords, Point3d point, out double station, out double distance)
    {
        int nearest = 0;
        station = chords[0].StationNearest(point, out distance);

        for (int i = 1; i < chords.Count; i++)
        {
            double candidate = chords[i].StationNearest(point, out double away);
            if (away >= distance) continue;

            nearest = i;
            station = candidate;
            distance = away;
        }

        return nearest;
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
    private static double[] StrictStations(List<double> fixedPoints, int panels, double merge)
    {
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
