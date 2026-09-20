using Rhino;
using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Fills the panels a set of beams encloses with evenly spaced members.
/// <para>
/// The whole engine. Both the Grasshopper component and the Rhino command call
/// <see cref="Generate"/> and nothing else, so the two front-ends cannot drift.
/// </para>
/// <para>
/// The input is a floor's worth of primary beams, selected in one sweep and in
/// no particular order. Nothing is said about which beams bound which panel:
/// wherever the beams close a loop, that loop is a panel. Three steps, and the
/// split between them is the design:
/// </para>
/// <list type="number">
/// <item><description>
/// <em>Finding the panels</em> is done flat, on the plane that best fits the
/// beams, because enclosure is a flat question — a loop either closes when seen
/// square-on or it does not. A pitched or gently curved roof reads the same as
/// a level floor.
/// </description></item>
/// <item><description>
/// <em>Building each panel</em> goes back to the beams themselves. Every stretch
/// of a panel's outline is cut out of the curve it came from, at the parameters
/// the flat search reported, so nothing downstream is measured on a projection
/// and a member lands on the beam rather than on its shadow.
/// </description></item>
/// <item><description>
/// <em>Filling</em> is the truss rule turned on its side: the two supporting
/// sides are divided into the same number of equal parts along their own
/// length, and member <c>i</c> joins point <c>i</c> of one to point <c>i</c> of
/// the other. In a rectangle that is parallel members at even centres. In a
/// splayed bay they fan, dividing both beams evenly, which is how such a bay is
/// normally framed and the one layout that never runs a member into a side.
/// </description></item>
/// </list>
/// <para>
/// Only four-sided panels are filled. See <see cref="BeamInfill.IrregularPanels"/>
/// for why that is a decision rather than an omission.
/// </para>
/// </summary>
public static class BeamInfillGenerator
{
    public static BeamInfill Generate(IEnumerable<Curve> beams, BeamInfillOptions? options = null)
    {
        if (beams is null) throw new ArgumentNullException(nameof(beams));

        options ??= new BeamInfillOptions();

        if (options.Divisions < 0)
            throw new ArgumentException("Divisions cannot be negative.", nameof(options));
        if (options.Spacing < 0.0)
            throw new ArgumentException("Spacing cannot be negative.", nameof(options));
        if (options.Tolerance <= 0.0)
            throw new ArgumentException("Tolerance has to be greater than zero.", nameof(options));
        if (options.CornerAngle <= 0.0 || options.CornerAngle >= 180.0)
            throw new ArgumentException("Corner angle has to be between 0 and 180 degrees.", nameof(options));

        var working = new List<Curve>();

        foreach (Curve? beam in beams)
        {
            if (beam is null || !beam.IsValid)
                throw new ArgumentException("Every beam must be a valid curve.", nameof(beams));

            // A curve too short to be a beam cannot bound anything either.
            if (beam.GetLength() > options.Tolerance)
                working.Add(WorkingCurve.AsNurbs(beam));
        }

        var panels = new List<BeamPanel>();
        var skipped = new List<Curve>();
        int irregular = 0, withOpenings = 0, loose = 0;

        if (!TryFitFloor(working, out Plane floor))
            return new BeamInfill(panels, skipped, options, 0, 0, 0, 0);

        // Flattened copies, index-matched to the beams they came from. A curve
        // standing square to the floor flattens to a point and drops out here.
        var flat = new List<Curve>(working.Count);
        var source = new List<Curve>(working.Count);

        foreach (Curve beam in working)
        {
            Curve? shadow = Curve.ProjectToPlane(beam, floor);
            if (shadow is null || shadow.GetLength() <= options.Tolerance) continue;

            flat.Add(shadow);
            source.Add(beam);
        }

        int edgeOn = working.Count - source.Count;

        using CurveBooleanRegions? regions = flat.Count == 0
            ? null
            : Curve.CreateBooleanRegions(flat, floor, false, options.Tolerance);

        for (int region = 0; region < (regions?.RegionCount ?? 0); region++)
        {
            if (regions!.BoundaryCount(region) > 1)
            {
                withOpenings++;
                skipped.AddRange(regions.RegionCurves(region).Take(1));
                continue;
            }

            List<Curve> pieces = OutlinePieces(regions, region, source, options.Tolerance);
            List<int> corners = CornerJoints(pieces, options.CornerAngle);

            if (corners.Count != 4)
            {
                irregular++;
                skipped.Add(JoinOutline(pieces, options.Tolerance) ?? regions.RegionCurves(region)[0]);
                continue;
            }

            if (HasGaps(pieces, options.Tolerance)) loose++;

            panels.Add(Fill(pieces, corners, options, regions.RegionCurves(region)[0]));
        }

        return new BeamInfill(panels, skipped, options, irregular, withOpenings, loose, edgeOn);
    }

    /// <summary>
    /// The plane the panels are found on: the best fit through the beams' ends
    /// and middles.
    /// <para>
    /// Fitted rather than assumed to be world XY, so a pitched roof is searched
    /// square-on instead of foreshortened, and a wall of rails between posts
    /// works at all. For a level floor the two are the same plane. How well the
    /// beams sit on it does not matter — only that no two of them swap sides
    /// when flattened, which holds for anything that is recognisably one floor.
    /// </para>
    /// </summary>
    private static bool TryFitFloor(List<Curve> beams, out Plane floor)
    {
        floor = Plane.WorldXY;

        var samples = new List<Point3d>(beams.Count * 3);
        foreach (Curve beam in beams)
        {
            samples.Add(beam.PointAtStart);
            samples.Add(beam.PointAtNormalizedLength(0.5));
            samples.Add(beam.PointAtEnd);
        }

        return samples.Count >= 3
            && Plane.FitPlaneToPoints(samples, out floor) == PlaneFitResult.Success
            && floor.IsValid;
    }

    /// <summary>
    /// One panel's outline as an ordered ring of smooth pieces of the real
    /// beams.
    /// <para>
    /// The flat search says which beam each stretch of the outline came from,
    /// over what part of its domain, and whether the outline runs along it
    /// backwards. Because the flattened copies keep their beams'
    /// parameterisation, the same interval cut from the beam itself is the same
    /// stretch in space — no pulling points back onto curves, and no tolerance
    /// spent doing it.
    /// </para>
    /// <para>
    /// Pieces are then cut again at their own kinks, so that every place the
    /// outline can turn is a joint between two pieces and corners only have to
    /// be looked for at joints.
    /// </para>
    /// </summary>
    private static List<Curve> OutlinePieces(
        CurveBooleanRegions regions, int region, List<Curve> source, double tolerance)
    {
        var pieces = new List<Curve>();

        for (int segment = 0; segment < regions.SegmentCount(region, 0); segment++)
        {
            int beam = regions.SegmentDetails(region, 0, segment, out Interval domain, out bool reversed);
            if (beam < 0 || beam >= source.Count) continue;

            domain.MakeIncreasing();

            Curve stretch = source[beam].Trim(domain) ?? source[beam].DuplicateCurve();
            if (reversed) stretch.Reverse();

            foreach (Curve piece in SplitAtKinks(stretch))
                if (piece.GetLength() > tolerance)
                    pieces.Add(piece);
        }

        return pieces;
    }

    private static IEnumerable<Curve> SplitAtKinks(Curve curve)
    {
        var kinks = new List<double>();
        Interval domain = curve.Domain;
        double cursor = domain.T0;

        while (curve.GetNextDiscontinuity(Continuity.C1_locus_continuous, cursor, domain.T1, out double next))
        {
            kinks.Add(next);
            cursor = next;
        }

        if (kinks.Count == 0) return new[] { curve };

        Curve[]? split = curve.Split(kinks);
        return split is { Length: > 0 } ? split : new[] { curve };
    }

    /// <summary>
    /// The joints at which the outline turns a corner: joint <c>j</c> is the one
    /// between piece <c>j</c> and the piece after it, the ring closing from the
    /// last piece back to the first.
    /// <para>
    /// A joint is not a corner by being a joint. Two collinear beams meeting at
    /// a column midway along a panel's side are one side, and so is a faceted
    /// edge beam; only the turn says otherwise.
    /// </para>
    /// </summary>
    private static List<int> CornerJoints(List<Curve> pieces, double cornerAngle)
    {
        var corners = new List<int>();
        double threshold = RhinoMath.ToRadians(cornerAngle);

        for (int j = 0; j < pieces.Count; j++)
        {
            Vector3d arriving = pieces[j].TangentAtEnd;
            Vector3d leaving = pieces[(j + 1) % pieces.Count].TangentAtStart;

            if (Vector3d.VectorAngle(arriving, leaving) > threshold)
                corners.Add(j);
        }

        return corners;
    }

    private static bool HasGaps(List<Curve> pieces, double tolerance)
    {
        for (int j = 0; j < pieces.Count; j++)
            if (pieces[j].PointAtEnd.DistanceTo(pieces[(j + 1) % pieces.Count].PointAtStart) > tolerance)
                return true;

        return false;
    }

    private static Curve? JoinOutline(List<Curve> pieces, double tolerance)
    {
        if (pieces.Count == 0) return null;

        Curve[] joined = Curve.JoinCurves(pieces, tolerance);
        return joined.Length == 1 ? joined[0] : null;
    }

    /// <summary>
    /// Place the members in one four-sided panel.
    /// <para>
    /// Sides are numbered round the outline from the first corner, so sides 0
    /// and 2 face each other and so do 1 and 3. Members running along one pair
    /// are carried by the other pair, and "long" is the mean of a pair — a
    /// trapezium's two long sides are not the same length, and neither alone
    /// should decide.
    /// </para>
    /// </summary>
    private static BeamPanel Fill(
        List<Curve> pieces, List<int> corners, BeamInfillOptions options, Curve flatOutline)
    {
        // Side k runs from the piece after corner k up to and including the
        // piece that ends at corner k + 1.
        var sides = new PolyCurve[4];

        for (int k = 0; k < 4; k++)
        {
            sides[k] = new PolyCurve();

            int piece = (corners[k] + 1) % pieces.Count;
            int last = corners[(k + 1) % 4];

            while (true)
            {
                sides[k].AppendSegment(pieces[piece]);
                if (piece == last) break;
                piece = (piece + 1) % pieces.Count;
            }
        }

        var corner = new Point3d[4];
        for (int k = 0; k < 4; k++)
            corner[k] = sides[k].PointAtStart;

        double along02 = (sides[0].GetLength() + sides[2].GetLength()) / 2.0;
        double along13 = (sides[1].GetLength() + sides[3].GetLength()) / 2.0;

        bool longIs02 = Math.Abs(along02 - along13) > options.Tolerance
            ? along02 > along13
            : SquareRunsAlong02(corner);

        bool runAlong02 = longIs02 != options.Flip;

        // The two sides that carry the members, turned to run the same way so
        // that a fraction along one pairs with the same fraction along the
        // other. Going round a ring, opposite sides always run against each
        // other, so exactly one of them needs reversing.
        Curve near = sides[runAlong02 ? 3 : 0].DuplicateCurve();
        Curve far = sides[runAlong02 ? 1 : 2];
        near.Reverse();

        int bays = PanelCount.Resolve(
            options.Divisions, options.Spacing, Math.Max(near.GetLength(), far.GetLength()));

        var members = new List<Line>(Math.Max(0, bays - 1));

        for (int i = 1; i < bays; i++)
        {
            double station = i / (double)bays;
            var member = new Line(near.PointAtNormalizedLength(station), far.PointAtNormalizedLength(station));

            if (member.Length > options.Tolerance)
                members.Add(member);
        }

        Curve outline = JoinOutline(pieces, options.Tolerance) ?? flatOutline;

        return new BeamPanel(outline, corner, members.ToArray(), bays, !options.Flip);
    }

    /// <summary>
    /// Which way is "long" in a panel whose sides are equal.
    /// <para>
    /// Neither, so it goes to whichever pair lies closer to world X. Arbitrary,
    /// but arbitrary the same way in every panel: left to the order the corners
    /// happened to be found in, a floor of square bays would come out with its
    /// members running both ways at random.
    /// </para>
    /// </summary>
    private static bool SquareRunsAlong02(Point3d[] corner)
    {
        Vector3d along02 = (corner[1] - corner[0]) + (corner[2] - corner[3]);
        Vector3d along13 = (corner[2] - corner[1]) + (corner[3] - corner[0]);

        along02.Unitize();
        along13.Unitize();

        return Math.Abs(along02.X) >= Math.Abs(along13.X);
    }
}
