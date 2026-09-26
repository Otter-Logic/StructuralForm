using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// The result of running beams along gridlines between the columns that
/// stand on them, and what was found on the way.
/// </summary>
public sealed class GridBeams
{
    internal GridBeams(
        List<IReadOnlyList<Curve>> byGridline,
        List<Point3d> nodes,
        List<Curve> plan,
        GridBeamsOptions options,
        int columnsOffGrid,
        int gridlinesWithOneColumn,
        int columnsShortOfLevel,
        int notColumns,
        int plumbGridlines,
        int beamsOffSurface)
    {
        ByGridline = byGridline.AsReadOnly();
        Nodes = nodes.AsReadOnly();
        Plan = plan.AsReadOnly();
        Options = options;
        ColumnsOffGrid = columnsOffGrid;
        GridlinesWithOneColumn = gridlinesWithOneColumn;
        ColumnsShortOfLevel = columnsShortOfLevel;
        NotColumns = notColumns;
        PlumbGridlines = plumbGridlines;
        BeamsOffSurface = beamsOffSurface;

        var beams = new List<Curve>();
        foreach (IReadOnlyList<Curve> line in byGridline)
            beams.AddRange(line);
        Beams = beams.AsReadOnly();
    }

    /// <summary>
    /// Every beam: the pieces of each gridline between consecutive columns on
    /// it, at the level or on the surface. Straight where the gridline is
    /// straight, arcs where it is an arc. <see cref="ByGridline"/>
    /// flattened, so the order is gridline by gridline and then along it.
    /// </summary>
    public IReadOnlyList<Curve> Beams { get; }

    /// <summary>
    /// The beams grouped by the gridline they lie along, one list per entry of
    /// <see cref="Plan"/>, empty for a gridline that got none. Along the line
    /// in its own direction, so item <c>k</c> runs from the <c>k</c>-th
    /// column on the line to the next.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Curve>> ByGridline { get; }

    /// <summary>
    /// Where beams meet columns, at the level or on the surface, with
    /// coincident ones merged: a column at the crossing of two gridlines is
    /// one node with beams both ways. Ordered by X and then Y, so the same
    /// model gives the same order however it was picked.
    /// </summary>
    public IReadOnlyList<Point3d> Nodes { get; }

    /// <summary>
    /// The gridlines flattened to Z = 0, which is what the columns were found
    /// on and the beams were cut from. Same order as the gridlines given,
    /// minus any that stood vertical.
    /// </summary>
    public IReadOnlyList<Curve> Plan { get; }

    public GridBeamsOptions Options { get; }

    /// <summary>
    /// Columns that stand on no gridline, within the reach. A column on its
    /// own gets no beam and is worth knowing about: it is usually a gridline
    /// missed in the pick, or a column drawn a little off its line.
    /// </summary>
    public int ColumnsOffGrid { get; }

    /// <summary>
    /// Gridlines with fewer than two columns on them, and so no beam. A
    /// gridline picked by mistake, or a run of columns on a line that was not
    /// picked.
    /// </summary>
    public int GridlinesWithOneColumn { get; }

    /// <summary>
    /// Column positions, merged in plan, where no column reaches the level or
    /// the surface: the beams there are drawn where the column would meet it
    /// if it carried on, and are said to be.
    /// </summary>
    public int ColumnsShortOfLevel { get; }

    /// <summary>
    /// Curves picked as columns that run further in plan than they rise: a
    /// beam or a brace swept up in the selection. Left out, since a beam
    /// drawn to the middle of one would be a beam to nowhere.
    /// </summary>
    public int NotColumns { get; }

    /// <summary>Curves picked as gridlines that stand vertical: a column in the wrong pick. Ignored.</summary>
    public int PlumbGridlines { get; }

    /// <summary>
    /// Beams that could not be projected onto the surface because no part of
    /// it lies over or under them, and were left out.
    /// </summary>
    public int BeamsOffSurface { get; }

    public double TotalLength => Beams.Sum(b => b.GetLength());

    /// <summary>
    /// What is worth telling the user about this result, if anything. The
    /// wording lives here so both front-ends say the same thing.
    /// </summary>
    public IReadOnlyList<FormNote> Notes
    {
        get
        {
            var notes = new List<FormNote>(6);

            if (Beams.Count == 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    "No beams were drawn. A beam runs along a gridline between two columns that stand on "
                    + "it, so check that the columns sit on the gridlines picked, or raise the reach."));

            if (ColumnsOffGrid > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    (ColumnsOffGrid == 1 ? "One column stands" : $"{ColumnsOffGrid} columns stand")
                    + " on none of the gridlines and got no beam. A gridline missed in the pick, or a "
                    + "column a little off its line: raise the reach for the second."));

            if (GridlinesWithOneColumn > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    (GridlinesWithOneColumn == 1 ? "One gridline has" : $"{GridlinesWithOneColumn} gridlines have")
                    + " fewer than two columns on it and got no beam."));

            if (ColumnsShortOfLevel > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    (ColumnsShortOfLevel == 1 ? "One column does" : $"{ColumnsShortOfLevel} columns do")
                    + " not reach the level of the beams, which were drawn where the column would meet "
                    + "it if it carried on."));

            if (NotColumns > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    (NotColumns == 1 ? "One curve picked as a column runs" : $"{NotColumns} curves picked as columns run")
                    + " further in plan than up, and " + (NotColumns == 1 ? "was" : "were") + " left out as not a column."));

            if (PlumbGridlines > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    PlumbGridlines == 1
                        ? "One curve picked as a gridline stands vertical and was ignored."
                        : $"{PlumbGridlines} curves picked as gridlines stand vertical and were ignored."));

            if (BeamsOffSurface > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    (BeamsOffSurface == 1 ? "One beam lies" : $"{BeamsOffSurface} beams lie")
                    + " outside the surface in plan, so there was nothing to project onto, and "
                    + (BeamsOffSurface == 1 ? "it was" : "they were") + " left out."));

            return notes.AsReadOnly();
        }
    }
}
