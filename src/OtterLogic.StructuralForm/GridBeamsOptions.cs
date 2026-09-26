using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Inputs to <see cref="GridBeamsGenerator"/> other than the columns and the
/// gridlines.
/// <para>
/// A behaviour-free record, for the same reason <see cref="GridColumnsOptions"/>
/// is one: it crosses the boundary into Grasshopper and Rhino, so it stays
/// immutable and trivially serialisable. The logic lives in the generator.
/// </para>
/// </summary>
public sealed record GridBeamsOptions
{
    /// <summary>
    /// Height of the beams, as world Z. Every beam is drawn flat at this
    /// height, and a column is read where it crosses it. Ignored when a
    /// <see cref="Surface"/> is given.
    /// </summary>
    public double Level { get; init; }

    /// <summary>
    /// A surface to put the beams on instead of a level: a sloping roof, a
    /// ramp. Each beam is projected onto it straight down, or up, from where
    /// it lies in plan, so a beam between two columns follows the surface
    /// between them. Null, the default, uses <see cref="Level"/>.
    /// </summary>
    public Brep? Surface { get; init; }

    /// <summary>
    /// How far a column may sit off a gridline, in plan, and still count as
    /// standing on it. Zero, the default, uses <see cref="Tolerance"/>, which
    /// is right for columns the grid tools placed; a model drawn by hand may
    /// have its columns a few millimetres off the line, and this is the knob
    /// for that, chosen rather than read from the model because a column
    /// found by reaching further is a beam drawn to somewhere the column is
    /// not.
    /// </summary>
    public double Reach { get; init; }

    /// <summary>
    /// The distance below which two columns are the same column in plan, and
    /// two nodes the same node. Both front-ends set this from the document
    /// tolerance.
    /// </summary>
    public double Tolerance { get; init; } = 0.01;
}
