using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// A radial structural grid: rays outward from a centre or from the ring
/// round a hole, rings about it, and a node wherever a ray meets a ring.
/// </summary>
public sealed class RadialGrid
{
    internal RadialGrid(
        List<Line> rays,
        List<Curve> rings,
        List<IReadOnlyList<Point3d>> rows,
        Point3d? centre,
        List<double> ringOffsets,
        List<double> angles,
        RadialGridOptions options,
        List<FormNote> notes)
    {
        Rays = rays.AsReadOnly();
        Rings = rings.AsReadOnly();
        Rows = rows.AsReadOnly();
        Centre = centre;
        RingOffsets = ringOffsets.AsReadOnly();
        Angles = angles.AsReadOnly();
        Options = options;
        Notes = notes.AsReadOnly();

        // The centre is item 0 of every row when the rays meet there, so it
        // arrives once per ray; the flat list wants it once.
        var nodes = new List<Point3d>(rows.Count * ringOffsets.Count + 1);
        if (centre.HasValue) nodes.Add(centre.Value);
        foreach (IReadOnlyList<Point3d> row in rows)
            for (int i = centre.HasValue ? 1 : 0; i < row.Count; i++)
                nodes.Add(row[i]);
        Nodes = nodes.AsReadOnly();
    }

    /// <summary>
    /// One line per ray, in angle order from <see cref="RadialGridOptions.StartAngle"/>,
    /// each from the inner ring (the centre, when there is no hole) to the
    /// outer ring plus the overhang. One more than the bays, except on a full
    /// sweep, where the last ray would be the first. On an oval the rays are
    /// straight but do not pass through the centre: each meets every ring at
    /// the same parameter, which is what keeps the bays even ring to ring.
    /// </summary>
    public IReadOnlyList<Line> Rays { get; }

    /// <summary>
    /// One curve per entry of <see cref="RingOffsets"/>, innermost first.
    /// Circles and arcs when the hole is round or there is none; ovals and
    /// oval arcs otherwise, with the spacing added to both half-axes. Partial
    /// rings are drawn from the first ray to the last.
    /// </summary>
    public IReadOnlyList<Curve> Rings { get; }

    /// <summary>
    /// The nodes, one list per ray: <c>Rows[i][k]</c> is where the
    /// <c>i</c>-th ray meets the <c>k</c>-th ring. When the rays meet at the
    /// centre, <see cref="Centre"/> is item 0 of every row, the same point
    /// repeated, so that item <c>k + 1</c> is always the <c>k</c>-th ring
    /// whether or not there is a hole in the middle.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Point3d>> Rows { get; }

    /// <summary>
    /// Every node once: the centre first when there is one, then
    /// <see cref="Rows"/> ray by ray. What a front-end bakes.
    /// </summary>
    public IReadOnlyList<Point3d> Nodes { get; }

    /// <summary>
    /// The point the rays meet at, when there is no hole. Null when there is
    /// one: no ray reaches the centre, so it is nowhere a column could stand.
    /// </summary>
    public Point3d? Centre { get; }

    /// <summary>
    /// How far outward each ring sits from the inner ring, innermost first:
    /// zero for the inner ring itself when there is a hole, then the running
    /// total of the spacings. Ring <c>k</c> has half-axes
    /// <see cref="RadialGridOptions.InnerU"/> and <see cref="RadialGridOptions.InnerV"/>
    /// plus this.
    /// </summary>
    public IReadOnlyList<double> RingOffsets { get; }

    /// <summary>
    /// The angle of every ray in degrees from the plane's X axis, in
    /// <see cref="Rays"/> order. On an oval, the ring parameter rather than a
    /// true angle: the same thing on the axes and on a circle.
    /// </summary>
    public IReadOnlyList<double> Angles { get; }

    /// <summary>True when the sweep is the whole 360 and the rings close.</summary>
    public bool IsFullSweep => Options.Sweep >= 360.0 - RadialGridGenerator.AngleTolerance;

    /// <summary>True when the hole is oval rather than round, and so every ring is.</summary>
    public bool IsOval => Centre is null && Math.Abs(Options.InnerU - Options.InnerV) > Options.Tolerance;

    public RadialGridOptions Options { get; }

    /// <summary>
    /// Things the user should know. Today, one: bays so narrow somewhere on
    /// the innermost ring that neighbouring nodes fall within tolerance of
    /// each other, which is a grid that will read as one column where several
    /// were meant.
    /// </summary>
    public IReadOnlyList<FormNote> Notes { get; }
}
