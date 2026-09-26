using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Reading curves in plan, which is how a grid is read.
/// <para>
/// Lifted out of <see cref="GridColumnsGenerator"/> when Grid Beams needed
/// the same reading of the same gridlines: two copies of "flatten and set
/// the plumb ones aside" would be two places for the rule about what counts
/// as plumb to drift apart.
/// </para>
/// </summary>
internal static class PlanView
{
    /// <summary>
    /// Every curve projected to world XY, in the order given, with the ones
    /// that flatten to nothing (a column caught in a window selection) left
    /// out and counted. A curve too short to be a gridline at all is dropped
    /// silently: it cannot cross anything and cannot carry a beam.
    /// </summary>
    internal static List<Curve> Flatten(IEnumerable<Curve> curves, double tolerance, out int plumb)
    {
        var plan = new List<Curve>();
        plumb = 0;

        foreach (Curve? curve in curves)
        {
            if (curve is null || !curve.IsValid)
                throw new ArgumentException("Every gridline must be a valid curve.", nameof(curves));

            if (curve.GetLength() <= tolerance) continue;

            Curve? shadow = Curve.ProjectToPlane(curve, Plane.WorldXY);

            if (shadow is null || shadow.GetLength() <= tolerance)
                plumb++;
            else
                plan.Add(shadow);
        }

        return plan;
    }

    /// <summary>
    /// Keep one point where several arrive within tolerance of each other.
    /// The counts here are small enough that a linear scan is fine.
    /// </summary>
    internal static void Merge(List<Point3d> kept, Point3d point, double tolerance)
    {
        foreach (Point3d k in kept)
            if (k.DistanceTo(point) <= tolerance)
                return;

        kept.Add(point);
    }
}
