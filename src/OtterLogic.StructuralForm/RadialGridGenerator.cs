using Rhino;
using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Sets out a radial structural grid from its ring spacings, the hole in the
/// middle, a sweep and a bay count.
/// <para>
/// The whole engine. Both the Grasshopper component and the Rhino command call
/// <see cref="Generate"/> and nothing else, so the two front-ends cannot drift.
/// </para>
/// <para>
/// Rays at equal steps across the sweep; a ring at the running total of the
/// spacings; a node where each ray meets each ring. Rhino draws the round
/// case as a <c>Line</c>, an <c>ArrayPolar</c>, and an <c>Arc</c> or
/// <c>Circle</c> per ring, three commands, and the polar array cannot stop
/// at a quarter. Grasshopper's own Radial grid repeats one ring spacing,
/// always closes the circle, and gives cells rather than rays and rings.
/// Neither has the oval case at all.
/// </para>
/// <para>
/// <strong>The oval.</strong> A stadium or an arena has a hole in the middle
/// that is longer than it is wide, and every ring follows it. Ring <c>k</c>
/// is the oval with half-axes <c>InnerU + offset</c> and <c>InnerV + offset</c>:
/// the spacing added to both axes, which is how such grids are set out in
/// practice. (A true offset of an oval is not an oval, and would put every
/// ring on a different curve type.) Along every ray the nodes are then
/// exactly the spacing apart; what is a little less between the axes is the
/// clear width between rings measured square to them, because a ray is not
/// quite square to an oval there.
/// </para>
/// <para>
/// Rays on an oval are set out by the ring's parameter <c>t</c>, not by the
/// angle from the centre: ray <c>t</c> starts at <c>(U cos t, V sin t)</c>
/// and runs in the direction <c>(cos t, sin t)</c>. That direction is chosen
/// because it meets every ring at the same parameter, so a ray is one
/// straight line, the nodes along it are evenly spaced, and the bays are
/// wider along the long sides and tighter round the ends, which is the look
/// of every stadium grid. A ray aimed at the centre would cross the rings at
/// drifting parameters and the bays would not line up ring to ring. On a
/// circle the parameter is the angle and the two are the same construction.
/// </para>
/// <para>
/// The two decisions that are not arithmetic: a full sweep is drawn with
/// <em>bays</em> rays rather than <em>bays + 1</em>, because the last ray of
/// a 360 sweep sits on the first and two lines in one place is the mistake a
/// grid tool exists to avoid; and no hole makes the centre one node rather
/// than one per ray, for the same reason.
/// </para>
/// </summary>
public static class RadialGridGenerator
{
    /// <summary>
    /// How close to 360 a sweep has to come to be read as a full sweep, in
    /// degrees. A sweep typed as 360 is exactly 360; one computed from a
    /// division may be off in the last place, and should still close.
    /// </summary>
    internal const double AngleTolerance = 1e-6;

    public static RadialGrid Generate(RadialGridOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        if (!options.Plane.IsValid)
            throw new ArgumentException("The plane has to be valid.", nameof(options));
        if (options.Tolerance <= 0.0)
            throw new ArgumentException("Tolerance has to be greater than zero.", nameof(options));
        if (!double.IsFinite(options.InnerU) || options.InnerU < 0.0 || !double.IsFinite(options.InnerV) || options.InnerV < 0.0)
            throw new ArgumentException("Inner U and Inner V have to be zero or more.", nameof(options));
        if (!double.IsFinite(options.Sweep) || options.Sweep <= 0.0 || options.Sweep > 360.0 + AngleTolerance)
            throw new ArgumentException("Sweep has to be greater than zero and no more than 360 degrees.", nameof(options));
        if (!double.IsFinite(options.StartAngle))
            throw new ArgumentException("Start angle has to be a finite number of degrees.", nameof(options));
        if (options.Bays < 1)
            throw new ArgumentException("At least one bay is needed.", nameof(options));
        if (!double.IsFinite(options.Overhang) || options.Overhang < 0.0)
            throw new ArgumentException("Overhang has to be zero or more.", nameof(options));

        RectangularGridGenerator.CheckSpacings(options.RingSpacings, "ring", options.Tolerance);

        Plane plane = options.Plane;
        double tolerance = options.Tolerance;

        // A half-axis under the tolerance is zero as far as the document can
        // tell. Both zero is no hole: the rays meet at the centre, and there
        // is no ring there because a ring of no size is a point. One zero and
        // the other not is a slit, which nobody means.
        bool uZero = options.InnerU <= tolerance;
        bool vZero = options.InnerV <= tolerance;
        if (uZero != vZero)
            throw new ArgumentException(
                "Inner U and Inner V have to be both zero, for rays that meet at the centre, or both greater than zero, for a hole.",
                nameof(options));

        bool hasCentre = uZero;
        double innerU = hasCentre ? 0.0 : options.InnerU;
        double innerV = hasCentre ? 0.0 : options.InnerV;

        List<double> offsets = Spacings.Offsets(options.RingSpacings);
        if (hasCentre) offsets.RemoveAt(0);
        double outer = offsets[^1];

        bool full = options.Sweep >= 360.0 - AngleTolerance;
        int rayCount = full ? options.Bays : options.Bays + 1;
        double step = options.Sweep / options.Bays;

        var angles = new List<double>(rayCount);
        for (int i = 0; i < rayCount; i++)
            angles.Add(options.StartAngle + i * step);

        var rays = new List<Line>(rayCount);
        var rows = new List<IReadOnlyList<Point3d>>(rayCount);
        Point3d? centre = hasCentre ? plane.Origin : null;

        foreach (double degrees in angles)
        {
            double t = RhinoMath.ToRadians(degrees);
            double cos = Math.Cos(t);
            double sin = Math.Sin(t);

            double end = outer + options.Overhang;
            rays.Add(new Line(
                plane.PointAt(innerU * cos, innerV * sin),
                plane.PointAt((innerU + end) * cos, (innerV + end) * sin)));

            var row = new List<Point3d>(offsets.Count + 1);
            if (centre.HasValue) row.Add(centre.Value);
            foreach (double offset in offsets)
                row.Add(plane.PointAt((innerU + offset) * cos, (innerV + offset) * sin));
            rows.Add(row.AsReadOnly());
        }

        var notes = new List<FormNote>();
        var rings = new List<Curve>(offsets.Count);

        foreach (double offset in offsets)
            rings.Add(Ring(plane, innerU + offset, innerV + offset, full, angles[0], options.Sweep, tolerance, notes));

        // The bay is narrowest somewhere on the innermost ring. Under the
        // tolerance, the document sees one node where the grid has several,
        // and a column tool will stand one column there.
        if (rayCount > 1)
        {
            int inner = centre.HasValue ? 1 : 0;
            double narrowest = double.MaxValue;
            for (int i = 1; i < rows.Count; i++)
                narrowest = Math.Min(narrowest, rows[i - 1][inner].DistanceTo(rows[i][inner]));
            if (full)
                narrowest = Math.Min(narrowest, rows[^1][inner].DistanceTo(rows[0][inner]));

            if (narrowest <= tolerance)
                notes.Add(new FormNote(FormNoteLevel.Warning,
                    $"The bays are {narrowest:0.###} wide at their narrowest on the innermost ring, within the tolerance of {tolerance:0.###}: "
                    + "its nodes sit on top of each other. Fewer bays, or a larger hole, would separate them."));
        }

        return new RadialGrid(rays, rings, rows, centre, offsets, angles, options, notes);
    }

    /// <summary>
    /// One ring: a circle or arc when it is round, because those are the
    /// objects a Rhino user expects to find on the layer; an oval, or a piece
    /// of one, otherwise.
    /// <para>
    /// A partial oval is the whole oval with its seam moved to the first ray
    /// and the rest trimmed off. The seam is moved rather than the trim
    /// wrapped because a trim across the seam of a closed curve is the one
    /// case Rhino's trim is documented to refuse.
    /// </para>
    /// </summary>
    private static Curve Ring(Plane plane, double u, double v, bool full, double startDegrees, double sweepDegrees, double tolerance, List<FormNote> notes)
    {
        double start = RhinoMath.ToRadians(startDegrees);
        double sweep = RhinoMath.ToRadians(sweepDegrees);

        if (Math.Abs(u - v) <= tolerance)
        {
            if (full) return new ArcCurve(new Circle(plane, u));

            // Arcs start on the plane's X axis, so the plane is turned to the
            // first ray first.
            Plane arcPlane = plane;
            arcPlane.Rotate(start, plane.ZAxis, plane.Origin);
            return new ArcCurve(new Arc(arcPlane, u, sweep));
        }

        NurbsCurve oval = new Ellipse(plane, u, v).ToNurbsCurve();
        if (full) return oval;

        Point3d first = plane.PointAt(u * Math.Cos(start), v * Math.Sin(start));
        Point3d last = plane.PointAt(u * Math.Cos(start + sweep), v * Math.Sin(start + sweep));

        if (oval.ClosestPoint(first, out double seam) && oval.ChangeClosedCurveSeam(seam)
            && oval.ClosestPoint(last, out double end)
            && oval.Trim(oval.Domain.Min, end) is Curve piece)
            return piece;

        notes.Add(new FormNote(FormNoteLevel.Warning,
            $"The ring with half-axes {u:0.###} and {v:0.###} could not be cut to the sweep, so it is drawn whole."));
        return oval;
    }
}
