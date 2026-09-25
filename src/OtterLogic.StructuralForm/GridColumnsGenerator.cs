using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Stands a column at every crossing of a set of gridlines.
/// <para>
/// The whole engine. Both the Grasshopper component and the Rhino command call
/// <see cref="Generate"/> and nothing else, so the two front-ends cannot drift.
/// </para>
/// <para>
/// The input is the gridlines as the user drew them — lines, arcs, polylines,
/// at whatever height — picked in any order. Two steps:
/// </para>
/// <list type="number">
/// <item><description>
/// <em>The grid is flattened</em> to world XY. Crossing is a plan question: a
/// gridline drawn at ground and one traced off a floor plan at the third
/// storey are the same grid, and it would be wrong to read them as not
/// meeting because they sit at different heights. Anything that flattens to
/// nothing — a column already in the model — is set aside and counted.
/// </description></item>
/// <item><description>
/// <em>Every pair is crossed</em>, the crossings are merged where several
/// gridlines pass through one point, and a vertical line is stood at each
/// from <see cref="GridColumnsOptions.Base"/> to <see cref="GridColumnsOptions.Top"/>.
/// </description></item>
/// </list>
/// <para>
/// Every crossing gets a column. Which crossings should not — the one in the
/// middle of an atrium, the one on a transfer beam — is a decision the user
/// takes on the picked curves or on the result, not one this guesses at.
/// </para>
/// </summary>
public static class GridColumnsGenerator
{
    public static GridColumns Generate(IEnumerable<Curve> gridlines, GridColumnsOptions? options = null)
    {
        if (gridlines is null) throw new ArgumentNullException(nameof(gridlines));

        options ??= new GridColumnsOptions();

        if (options.Tolerance <= 0.0)
            throw new ArgumentException("Tolerance has to be greater than zero.", nameof(options));
        if (!double.IsFinite(options.Base) || !double.IsFinite(options.Top))
            throw new ArgumentException("Base and Top have to be finite heights.", nameof(options));

        var plan = new List<Curve>();
        int plumb = 0;

        foreach (Curve? gridline in gridlines)
        {
            if (gridline is null || !gridline.IsValid)
                throw new ArgumentException("Every gridline must be a valid curve.", nameof(gridlines));

            // A curve too short to be a gridline cannot cross anything either.
            if (gridline.GetLength() <= options.Tolerance) continue;

            Curve? shadow = Curve.ProjectToPlane(gridline, Plane.WorldXY);

            if (shadow is null || shadow.GetLength() <= options.Tolerance)
                plumb++;
            else
                plan.Add(shadow);
        }

        var crossings = new List<Point3d>();
        int overlapping = 0;

        for (int a = 0; a < plan.Count; a++)
        {
            for (int b = a + 1; b < plan.Count; b++)
            {
                CurveIntersections? events = Intersection.CurveCurve(plan[a], plan[b], options.Tolerance, options.Tolerance);
                if (events is null) continue;

                bool overlapped = false;

                foreach (IntersectionEvent hit in events)
                {
                    if (hit.IsOverlap)
                    {
                        overlapped = true;
                        continue;
                    }

                    // The two curves are already in one plane, so the two
                    // reported points differ by no more than the tolerance;
                    // either will do, and A is taken for determinism.
                    Point3d point = hit.PointA;
                    point.Z = 0.0;
                    Merge(crossings, point, options.Tolerance);
                }

                if (overlapped) overlapping++;
            }
        }

        // By position, not by pick order, so that the same grid gives the
        // same result whichever curve was clicked first.
        crossings.Sort((p, q) => p.X != q.X ? p.X.CompareTo(q.X) : p.Y.CompareTo(q.Y));

        var columns = new List<Line>(crossings.Count);

        if (Math.Abs(options.Top - options.Base) > options.Tolerance)
        {
            foreach (Point3d crossing in crossings)
                columns.Add(new Line(
                    new Point3d(crossing.X, crossing.Y, options.Base),
                    new Point3d(crossing.X, crossing.Y, options.Top)));
        }

        return new GridColumns(columns, crossings, plan, options, plumb, overlapping);
    }

    /// <summary>
    /// Keep one crossing where several gridlines pass through the same point.
    /// <para>
    /// Three gridlines through a point are three pairs, so the point arrives
    /// three times; a T-junction where a gridline ends on another arrives once
    /// from that pair and again from the pair either side of it. The count of
    /// crossings is small enough that a linear scan is fine.
    /// </para>
    /// </summary>
    private static void Merge(List<Point3d> crossings, Point3d point, double tolerance)
    {
        foreach (Point3d kept in crossings)
            if (kept.DistanceTo(point) <= tolerance)
                return;

        crossings.Add(point);
    }
}
