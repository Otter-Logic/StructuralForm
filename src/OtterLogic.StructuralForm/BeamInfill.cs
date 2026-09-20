using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// One four-sided panel enclosed by the beams, and the members placed in it.
/// <para>
/// <see cref="StartNodes"/> item <c>i</c> and <see cref="EndNodes"/> item
/// <c>i</c> are the two ends of <see cref="Members"/> item <c>i</c> — the same
/// pairing by index a truss has between its chords, and for the same reason: a
/// downstream step that wants to split the supporting beams at these points
/// should not have to rediscover which end landed on which beam.
/// </para>
/// </summary>
public sealed class BeamPanel
{
    internal BeamPanel(
        Curve outline, Point3d[] corners, Line[] members, int divisions, bool alongLongerSide)
    {
        Outline = outline;
        Corners = Array.AsReadOnly(corners);
        Members = Array.AsReadOnly(members);
        StartNodes = Array.AsReadOnly(members.Select(m => m.From).ToArray());
        EndNodes = Array.AsReadOnly(members.Select(m => m.To).ToArray());
        Divisions = divisions;
        AlongLongerSide = alongLongerSide;
    }

    /// <summary>The panel's boundary, built from the beams themselves rather than their projection.</summary>
    public Curve Outline { get; }

    /// <summary>The four corners, in order round the outline.</summary>
    public IReadOnlyList<Point3d> Corners { get; }

    public IReadOnlyList<Line> Members { get; }
    public IReadOnlyList<Point3d> StartNodes { get; }
    public IReadOnlyList<Point3d> EndNodes { get; }

    /// <summary>Bays the panel was divided into. Zero when nothing drove the division.</summary>
    public int Divisions { get; }

    /// <summary>
    /// Whether the members run the long way across this panel. False under
    /// <see cref="BeamInfillOptions.Flip"/>; kept per panel because it is the
    /// panel's own sides that decide which way is long.
    /// </summary>
    public bool AlongLongerSide { get; }
}

/// <summary>
/// The result of filling a set of beams: every panel they enclose, the members
/// placed in each, and the panels that were found but left alone.
/// </summary>
public sealed class BeamInfill
{
    private readonly Lazy<IReadOnlyList<Point3d>> _nodes;

    internal BeamInfill(
        List<BeamPanel> panels,
        List<Curve> skippedPanels,
        BeamInfillOptions options,
        int irregularPanels,
        int panelsWithOpenings,
        int loosePanels,
        int edgeOnBeams)
    {
        Panels = panels.AsReadOnly();
        SkippedPanels = skippedPanels.AsReadOnly();
        Options = options;
        IrregularPanels = irregularPanels;
        PanelsWithOpenings = panelsWithOpenings;
        LoosePanels = loosePanels;
        EdgeOnBeams = edgeOnBeams;

        _nodes = new Lazy<IReadOnlyList<Point3d>>(MergeNodes);
    }

    public IReadOnlyList<BeamPanel> Panels { get; }

    /// <summary>
    /// Outlines of the panels that were found and not filled — the ones counted
    /// by <see cref="IrregularPanels"/> and <see cref="PanelsWithOpenings"/>.
    /// <para>
    /// Returned as geometry, not just a count, because on a floor of two
    /// hundred panels "three were skipped" is no help without knowing which.
    /// </para>
    /// </summary>
    public IReadOnlyList<Curve> SkippedPanels { get; }

    public BeamInfillOptions Options { get; }

    /// <summary>
    /// Panels without exactly four sides, left empty.
    /// <para>
    /// Members are placed between opposite sides, and a triangle or an L has no
    /// opposite sides to place them between. Any rule for those would be a
    /// guess at a decision that is the engineer's — trimmers, a fanned layout,
    /// a change of direction — so the panel is handed back instead. One extra
    /// beam drawn across it usually turns it into panels this can fill.
    /// </para>
    /// </summary>
    public int IrregularPanels { get; }

    /// <summary>Panels with a separate loop of beams inside them, left empty for the same reason.</summary>
    public int PanelsWithOpenings { get; }

    /// <summary>
    /// Panels whose beams cross when seen square-on to the floor but do not
    /// touch in space. Filled anyway, but it usually means the selection took
    /// in more than one level.
    /// </summary>
    public int LoosePanels { get; }

    /// <summary>
    /// Curves that stand square to the floor, and so could not bound a panel
    /// on it. Columns swept up in a window selection, nearly always — ignored
    /// rather than refused, because picking round them is the tedious part of
    /// selecting a floor.
    /// </summary>
    public int EdgeOnBeams { get; }

    public IEnumerable<Line> Members => Panels.SelectMany(p => p.Members);

    public double TotalLength => Members.Sum(m => m.Length);

    /// <summary>
    /// Where the members land on the beams, with coincident points merged.
    /// <para>
    /// Two panels sharing a beam divide it identically from either side, so
    /// their members arrive at the same points along it. One node each, not
    /// two on top of each other, is what the next step wants — these are the
    /// points the supporting beams need splitting at before analysis.
    /// </para>
    /// </summary>
    public IReadOnlyList<Point3d> Nodes => _nodes.Value;

    private IReadOnlyList<Point3d> MergeNodes()
    {
        var kept = new List<Point3d>();

        foreach (Point3d node in Panels.SelectMany(p => p.StartNodes.Concat(p.EndNodes)))
            if (!kept.Any(k => k.DistanceTo(node) <= Options.Tolerance))
                kept.Add(node);

        return kept.AsReadOnly();
    }

    /// <summary>
    /// What is worth telling the user about this result, if anything. The
    /// wording lives here so both front-ends say the same thing.
    /// </summary>
    public IReadOnlyList<FormNote> Notes
    {
        get
        {
            var notes = new List<FormNote>(5);

            if (Panels.Count == 0 && SkippedPanels.Count == 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    "No closed panels were found. Beams have to meet, within tolerance, to enclose one."));

            if (LoosePanels > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Warning,
                    (LoosePanels == 1
                        ? "One panel is bounded by beams that cross without touching. "
                        : $"{LoosePanels} panels are bounded by beams that cross without touching. ")
                    + "Check that the selection is a single floor."));

            if (IrregularPanels > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    (IrregularPanels == 1
                        ? "One panel does not have four sides and was left empty. "
                        : $"{IrregularPanels} panels do not have four sides and were left empty. ")
                    + "A beam drawn across one will usually split it into panels that can be filled."));

            if (PanelsWithOpenings > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    PanelsWithOpenings == 1
                        ? "One panel has a separate loop of beams inside it and was left empty."
                        : $"{PanelsWithOpenings} panels have a separate loop of beams inside them "
                          + "and were left empty."));

            if (EdgeOnBeams > 0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    EdgeOnBeams == 1
                        ? "One curve stands square to the floor and was ignored."
                        : $"{EdgeOnBeams} curves stand square to the floor and were ignored."));

            // Nothing visibly wrong with a floor of empty panels, so say why.
            if (Panels.Count > 0 && Options.Divisions <= 0 && Options.Spacing <= 0.0)
                notes.Add(new FormNote(
                    FormNoteLevel.Remark,
                    "The panels were found but left empty. Set Divisions or Spacing to fill them."));

            return notes.AsReadOnly();
        }
    }
}
