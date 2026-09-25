using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// The result of standing columns on a set of gridlines: a column at every
/// crossing, and what was found on the way.
/// </summary>
public sealed class GridColumns
{
    internal GridColumns(
        List<Line> columns,
        List<Point3d> crossings,
        List<Curve> plan,
        GridColumnsOptions options,
        int plumbCurves,
        int overlappingPairs)
    {
        Columns = columns.AsReadOnly();
        Crossings = crossings.AsReadOnly();
        Plan = plan.AsReadOnly();
        Options = options;
        PlumbCurves = plumbCurves;
        OverlappingPairs = overlappingPairs;
    }

    /// <summary>
    /// One line per crossing, from <see cref="GridColumnsOptions.Base"/> to
    /// <see cref="GridColumnsOptions.Top"/>, in the same order as
    /// <see cref="Crossings"/>. Empty when the two heights are equal.
    /// </summary>
    public IReadOnlyList<Line> Columns { get; }

    /// <summary>
    /// Where the gridlines cross in plan, at Z = 0, with coincident crossings
    /// merged: three gridlines through one point are one crossing, not three.
    /// <para>
    /// Ordered by X and then Y rather than by the order the gridlines were
    /// picked in, so the same grid gives the same order however it was
    /// selected.
    /// </para>
    /// </summary>
    public IReadOnlyList<Point3d> Crossings { get; }

    /// <summary>
    /// The gridlines flattened to Z = 0, which is what the crossings were
    /// found on. Returned so a front-end can show the grid the columns were
    /// read from rather than the curves as drawn, which may sit at any height.
    /// </summary>
    public IReadOnlyList<Curve> Plan { get; }

    public GridColumnsOptions Options { get; }

    /// <summary>
    /// Curves that stand vertical, and so have no length in plan to cross
    /// anything with. Columns already in the model, swept up in a window
    /// selection — ignored rather than refused, because picking round them is
    /// the tedious part of selecting a grid.
    /// </summary>
    public int PlumbCurves { get; }

    /// <summary>
    /// Pairs of gridlines that run along each other for a stretch rather than
    /// crossing — the same gridline picked twice, or two drawn on top of each
    /// other. No column is placed along the overlap, since there is no one
    /// point to stand it at.
    /// </summary>
    public int OverlappingPairs { get; }

    public double TotalLength => Columns.Sum(c => c.Length);

    /// <summary>
    /// What is worth telling the user about this result, if anything. The
    /// wording lives here so both front-ends say the same thing.
    /// </summary>
    public IReadOnlyList<FormNote> Notes
    {
        get
        {
            var notes = new List<FormNote>(4);

            if (Crossings.Count == 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    "No crossings were found. Gridlines have to cross, or meet, in plan for a column to stand there."));

            if (OverlappingPairs > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    (OverlappingPairs == 1
                        ? "One pair of gridlines runs along the same line "
                        : $"{OverlappingPairs} pairs of gridlines run along the same line ")
                    + "rather than crossing, and no column was placed along it. "
                    + "Usually a gridline picked twice."));

            if (PlumbCurves > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    PlumbCurves == 1
                        ? "One curve stands vertical and was ignored."
                        : $"{PlumbCurves} curves stand vertical and were ignored."));

            // Nothing visibly wrong with a grid of crossings and no columns, so say why.
            if (Crossings.Count > 0 && Columns.Count == 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    "The crossings were found but no columns placed, because Top and Base are the same height."));

            return notes.AsReadOnly();
        }
    }
}
