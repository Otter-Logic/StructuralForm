using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Runs primary beams along gridlines, between the columns that stand on them.
/// <para>
/// The whole engine. Both the Grasshopper component and the Rhino command call
/// <see cref="Generate"/> and nothing else, so the two front-ends cannot drift.
/// </para>
/// <para>
/// The partner of <see cref="GridColumnsGenerator"/>: that stands columns
/// where gridlines cross, this runs beams between the columns that stand on
/// a gridline. Reading the columns rather than the crossings is the point. A
/// crossing in an atrium has no column and should get no beam through it,
/// and a column that was deleted after the grid was made should take its
/// beams with it. Three steps:
/// </para>
/// <list type="number">
/// <item><description>
/// <em>Every column becomes a point in plan</em>: where it crosses the level,
/// or the nearer end when it stops short, which is counted. Columns stacked
/// storey on storey are one point. A curve that runs further in plan than it
/// rises is not a column and is set aside.
/// </description></item>
/// <item><description>
/// <em>Every gridline is flattened</em> and the columns on it found, within
/// the reach, and sorted along it.
/// </description></item>
/// <item><description>
/// <em>The piece of gridline between each consecutive pair</em> is a beam,
/// lifted to the level or projected onto the surface. A gridline's end past
/// its last column gets nothing: a cantilever is a decision, not a default.
/// </description></item>
/// </list>
/// </summary>
public static class GridBeamsGenerator
{
    public static GridBeams Generate(IEnumerable<Curve> columns, IEnumerable<Curve> gridlines, GridBeamsOptions? options = null)
    {
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (gridlines is null) throw new ArgumentNullException(nameof(gridlines));

        options ??= new GridBeamsOptions();

        if (options.Tolerance <= 0.0)
            throw new ArgumentException("Tolerance has to be greater than zero.", nameof(options));
        if (!double.IsFinite(options.Level))
            throw new ArgumentException("Level has to be a finite height.", nameof(options));
        if (!double.IsFinite(options.Reach) || options.Reach < 0.0)
            throw new ArgumentException("Reach has to be zero or more.", nameof(options));
        if (options.Surface is not null && !options.Surface.IsValid)
            throw new ArgumentException("The surface has to be valid.", nameof(options));

        double tolerance = options.Tolerance;
        double reach = options.Reach > 0.0 ? options.Reach : tolerance;

        List<Curve> plan = PlanView.Flatten(gridlines, tolerance, out int plumbGridlines);

        // Step 1: the columns as points in plan.
        var positions = new List<Point3d>();
        var reaches = new List<bool>();
        int notColumns = 0;

        foreach (Curve? column in columns)
        {
            if (column is null || !column.IsValid)
                throw new ArgumentException("Every column must be a valid curve.", nameof(columns));

            BoundingBox box = column.GetBoundingBox(true);
            double rise = box.Max.Z - box.Min.Z;
            double run = new Point3d(box.Max.X, box.Max.Y, 0).DistanceTo(new Point3d(box.Min.X, box.Min.Y, 0));

            if (rise <= tolerance || run > rise)
            {
                notColumns++;
                continue;
            }

            (Point3d at, bool reached) = WhereItMeets(column, options, tolerance);
            at.Z = 0.0;

            int existing = positions.FindIndex(p => p.DistanceTo(at) <= tolerance);
            if (existing < 0)
            {
                positions.Add(at);
                reaches.Add(reached);
            }
            else
            {
                // Stacked columns: one position, reached if any of them reaches.
                reaches[existing] |= reached;
            }
        }

        // Step 2 and 3: along each gridline, the columns on it, and the pieces
        // between them.
        var byGridline = new List<IReadOnlyList<Curve>>(plan.Count);
        var nodesInPlan = new List<Point3d>();
        var used = new bool[positions.Count];
        int oneColumn = 0;
        int offSurface = 0;

        foreach (Curve line in plan)
        {
            var stations = new List<(double T, int Column)>();

            for (int c = 0; c < positions.Count; c++)
            {
                if (!line.ClosestPoint(positions[c], out double t)) continue;
                if (line.PointAt(t).DistanceTo(positions[c]) > reach) continue;

                used[c] = true;
                stations.Add((t, c));
            }

            stations.Sort((a, b) => a.T.CompareTo(b.T));

            // Two columns within tolerance along the line would be a beam of
            // no length between them; the merge in step 1 makes that rare,
            // and a curve's parameter is not a length, so it is checked here
            // by distance.
            for (int s = stations.Count - 1; s > 0; s--)
                if (line.PointAt(stations[s].T).DistanceTo(line.PointAt(stations[s - 1].T)) <= tolerance)
                    stations.RemoveAt(s);

            var beams = new List<Curve>();

            if (stations.Count < 2)
            {
                oneColumn++;
                byGridline.Add(beams.AsReadOnly());
                continue;
            }

            foreach ((double t, _) in stations)
                PlanView.Merge(nodesInPlan, line.PointAt(t), tolerance);

            for (int s = 1; s < stations.Count; s++)
            {
                Curve? piece = line.Trim(stations[s - 1].T, stations[s].T);
                if (piece is null) continue;

                if (options.Surface is null)
                {
                    piece.Translate(0.0, 0.0, options.Level);
                    beams.Add(piece);
                    continue;
                }

                Curve[] projected = ProjectOnto(piece, options.Surface, tolerance);
                if (projected.Length == 0)
                    offSurface++;
                else
                    beams.AddRange(projected);
            }

            byGridline.Add(beams.AsReadOnly());
        }

        int offGrid = used.Count(u => !u);
        int shortOfLevel = 0;
        for (int c = 0; c < positions.Count; c++)
            if (used[c] && !reaches[c]) shortOfLevel++;

        // Nodes are the column positions on the beams, lifted the same way
        // the beams were, so a node is on its beam whether that is flat or
        // on a slope.
        var nodes = new List<Point3d>(nodesInPlan.Count);
        foreach (Point3d p in nodesInPlan)
        {
            if (options.Surface is null)
            {
                nodes.Add(new Point3d(p.X, p.Y, options.Level));
                continue;
            }

            Point3d[] hits = Intersection.ProjectPointsToBreps(new[] { options.Surface }, new[] { p }, Vector3d.ZAxis, tolerance);
            if (hits is { Length: > 0 })
                nodes.Add(hits.OrderBy(h => Math.Abs(h.Z - p.Z)).First());
        }

        // By position, not by pick order, so that the same model gives the
        // same result whichever column was clicked first.
        nodes.Sort((p, q) => p.X != q.X ? p.X.CompareTo(q.X) : p.Y.CompareTo(q.Y));

        return new GridBeams(byGridline, nodes, plan, options, offGrid, oneColumn, shortOfLevel, notColumns, plumbGridlines, offSurface);
    }

    /// <summary>
    /// Where a column meets the level or the surface, and whether it reaches
    /// it. A leaning column meets the level somewhere other than its foot,
    /// and that is where the beam should come to; a column that stops short
    /// is read at whichever end is nearer, and counted.
    /// </summary>
    private static (Point3d At, bool Reached) WhereItMeets(Curve column, GridBeamsOptions options, double tolerance)
    {
        if (options.Surface is null)
        {
            var level = new Plane(new Point3d(0, 0, options.Level), Vector3d.ZAxis);
            CurveIntersections? hits = Intersection.CurvePlane(column, level, tolerance);

            if (hits is { Count: > 0 })
                return (hits[0].PointA, true);
        }
        else
        {
            if (Intersection.CurveBrep(column, options.Surface, tolerance, out _, out Point3d[] points) && points.Length > 0)
                return (points[0], true);
        }

        // Short of the level: the end nearer to it, which for a level is the
        // end whose height is closest and for a surface the end nearer the
        // surface, judged the same way through the surface's closest point.
        Point3d start = column.PointAtStart, end = column.PointAtEnd;

        double gap(Point3d p) => options.Surface is null
            ? Math.Abs(p.Z - options.Level)
            : options.Surface.ClosestPoint(p).DistanceTo(p);

        Point3d nearer = gap(start) <= gap(end) ? start : end;
        return (nearer, gap(nearer) <= tolerance);
    }

    /// <summary>
    /// A beam's plan piece projected onto the surface along world Z, either
    /// way: a roof is above its plan and a basement slab below it, and which
    /// is not worth asking. Several curves come back where the surface has a
    /// hole under the beam; each is a beam.
    /// </summary>
    private static Curve[] ProjectOnto(Curve piece, Brep surface, double tolerance)
    {
        Curve[] up = Curve.ProjectToBrep(piece, surface, Vector3d.ZAxis, tolerance) ?? Array.Empty<Curve>();
        if (up.Length > 0) return up;

        return Curve.ProjectToBrep(piece, surface, -Vector3d.ZAxis, tolerance) ?? Array.Empty<Curve>();
    }
}
