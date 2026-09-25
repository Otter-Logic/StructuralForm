namespace OtterLogic.StructuralForm;

/// <summary>
/// Inputs to <see cref="GridColumnsGenerator"/> other than the gridlines.
/// <para>
/// A behaviour-free record, for the same reason <see cref="FlatTrussOptions"/>
/// is one: it crosses the boundary into Grasshopper and Rhino, so it stays
/// immutable and trivially serialisable. The logic lives in the generator.
/// </para>
/// </summary>
public sealed record GridColumnsOptions
{
    /// <summary>
    /// Height of the foot of every column, in model units. World Z, not a
    /// height above anything: the gridlines are flattened to Z = 0 before
    /// their crossings are found, so wherever they were drawn the columns
    /// stand between these two heights.
    /// </summary>
    public double Base { get; init; }

    /// <summary>
    /// Height of the head of every column. A column is the line from
    /// <see cref="Base"/> to here, so a top below the base is a column drawn
    /// downward — a pile from a pile cap — rather than a mistake.
    /// <para>
    /// Equal to <see cref="Base"/> places nothing: the crossings are still
    /// found and reported, which is the way to check the gridlines read as
    /// intended before deciding how tall the columns are.
    /// </para>
    /// </summary>
    public double Top { get; init; }

    /// <summary>
    /// How close two gridlines have to come, in plan, to count as crossing,
    /// and the distance below which two crossings are the same crossing. Both
    /// front-ends set this from the document tolerance.
    /// </summary>
    public double Tolerance { get; init; } = 0.01;
}
