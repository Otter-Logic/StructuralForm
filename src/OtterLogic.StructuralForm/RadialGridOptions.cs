using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Inputs to <see cref="RadialGridGenerator"/>.
/// <para>
/// A behaviour-free record, like the other options here: it crosses the
/// boundary into Grasshopper and Rhino, so it stays immutable and trivially
/// serialisable. The logic lives in the generator.
/// </para>
/// </summary>
public sealed record RadialGridOptions
{
    /// <summary>
    /// The centre of the grid and the plane it lies in. Angles are measured
    /// from the plane's X axis, anticlockwise about its Z.
    /// </summary>
    public Plane Plane { get; init; } = Plane.WorldXY;

    /// <summary>
    /// The bays measured outward along a ray, first to last, starting at the
    /// inner ring. A ring at the end of each; at least one; every spacing
    /// greater than <see cref="Tolerance"/>.
    /// </summary>
    public IReadOnlyList<double> RingSpacings { get; init; } = Array.Empty<double>();

    /// <summary>
    /// Half the width of the hole in the middle, measured from the centre
    /// along the plane's X axis: where the inner ring crosses that axis, and
    /// where the rays start. Zero, with <see cref="InnerV"/> zero, runs the
    /// rays into the centre, which is then one node shared by every ray and
    /// gets no ring. Equal to <see cref="InnerV"/> is a round hole; different
    /// is an oval one, the stadium and arena case, with every ring an oval.
    /// One zero and the other not is refused: that is a slit, not a hole.
    /// </summary>
    public double InnerU { get; init; }

    /// <summary>
    /// Half the depth of the hole, measured from the centre along the plane's
    /// Y axis. See <see cref="InnerU"/>.
    /// </summary>
    public double InnerV { get; init; }

    /// <summary>
    /// The angle the grid covers, in degrees, from <see cref="StartAngle"/>
    /// anticlockwise. 360 is the whole way round: the rings close and the last
    /// ray is the first, so it is not drawn twice. 90 is a quarter. Greater
    /// than zero, no more than 360. On an oval this is the ring's parameter
    /// rather than a true angle, which is the same thing on the two axes and
    /// on a circle.
    /// </summary>
    public double Sweep { get; init; } = 360.0;

    /// <summary>
    /// Where the first ray sits, in degrees from the plane's X axis. The
    /// plane's own rotation does the same job; this is for turning a partial
    /// grid within a plane that is already the drawing's.
    /// </summary>
    public double StartAngle { get; init; }

    /// <summary>
    /// How many bays the sweep is cut into. A count rather than an angle
    /// because a sweep that is not a whole number of angle steps leaves a
    /// part bay at one end, and which end is a guess. One more ray than bays,
    /// except on a full sweep. At least one.
    /// </summary>
    public int Bays { get; init; } = 12;

    /// <summary>
    /// How far every ray runs on past the outer ring. Zero ends them at the
    /// ring; the usual drawing runs them on so a grid bubble can sit outside
    /// the building. Rings are never extended: a ring past the end ray of a
    /// partial grid would be a bay that is not there. Never negative.
    /// </summary>
    public double Overhang { get; init; }

    /// <summary>
    /// The smallest spacing that counts as a bay, the inner half-axis below
    /// which the hole is the centre, and the distance below which two nodes
    /// on a ring are the same node. Both front-ends set this from the
    /// document tolerance.
    /// </summary>
    public double Tolerance { get; init; } = 0.01;
}
