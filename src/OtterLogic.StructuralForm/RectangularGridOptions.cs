using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Inputs to <see cref="RectangularGridGenerator"/>.
/// <para>
/// A behaviour-free record, like the other options here: it crosses the
/// boundary into Grasshopper and Rhino, so it stays immutable and trivially
/// serialisable. The logic lives in the generator.
/// </para>
/// </summary>
public sealed record RectangularGridOptions
{
    /// <summary>
    /// Where the grid sits and which way it faces. The first gridline each way
    /// passes through the origin; X spacings run along the plane's X axis and
    /// Y spacings along its Y. A skewed or rotated grid is a rotated plane, so
    /// the generator never needs an angle of its own.
    /// </summary>
    public Plane Plane { get; init; } = Plane.WorldXY;

    /// <summary>
    /// The bays measured along the plane's X axis, first to last. One more
    /// gridline than there are spacings, each running the full depth of the
    /// grid in Y. At least one; every spacing greater than
    /// <see cref="Tolerance"/>.
    /// </summary>
    public IReadOnlyList<double> XSpacings { get; init; } = Array.Empty<double>();

    /// <summary>
    /// The bays measured along the plane's Y axis, first to last. One more
    /// gridline than there are spacings, each running the full width of the
    /// grid in X. At least one; every spacing greater than
    /// <see cref="Tolerance"/>.
    /// </summary>
    public IReadOnlyList<double> YSpacings { get; init; } = Array.Empty<double>();

    /// <summary>
    /// How far every gridline runs past the outermost gridline it crosses, at
    /// both ends. Zero gives a grid whose lines end exactly at the corners;
    /// the usual drawing has them run on so a grid bubble can sit off the
    /// building. Never negative: a gridline that stops short of the outer
    /// gridline is not a gridline.
    /// </summary>
    public double Overhang { get; init; }

    /// <summary>
    /// The smallest spacing that counts as a bay. Both front-ends set this
    /// from the document tolerance.
    /// </summary>
    public double Tolerance { get; init; } = 0.01;
}
