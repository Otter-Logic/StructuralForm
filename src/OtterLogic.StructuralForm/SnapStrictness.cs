namespace OtterLogic.StructuralForm;

/// <summary>
/// How hard a division has to try to land on the snap points.
/// <para>
/// Only meaningful alongside <see cref="FlatTrussOptions.Divisions"/> or
/// <see cref="FlatTrussOptions.Spacing"/>. With neither set the geometry drives
/// and every detected point is already a node, so there is nothing to trade off.
/// </para>
/// <para>
/// Values are explicit and must stay stable: they are what a Grasshopper
/// definition stores on the wire and what the Rhino command persists between
/// sessions. Append new members, never renumber existing ones.
/// </para>
/// </summary>
public enum SnapStrictness
{
    /// <summary>
    /// The division wins. Panel count is exactly what was asked for, and a
    /// station only moves onto a snap point it can reach without disturbing the
    /// layout — half a panel each way. A point further off than that is left
    /// unused, and the truss keeps its regular spacing.
    /// <para>
    /// The default, because a regular truss is what most sets of chords want
    /// and an oddly placed point is more often a stray pick than an intention.
    /// <see cref="FlatTruss.UnusedSnapPoints"/> reports what went unused, so
    /// this stays a choice rather than a silent loss.
    /// </para>
    /// </summary>
    Relaxed = 0,

    /// <summary>
    /// The snap points win. Every one of them becomes a node however oddly it
    /// sits, and the panels asked for are shared out between them in proportion
    /// to the gaps they leave — so the spacing is even <em>between</em> fixed
    /// points rather than across the whole truss.
    /// <para>
    /// Panel count is still honoured where it can be. When there are more snap
    /// points than the requested panels can accommodate, the count grows to fit
    /// them, because under this rule a point not becoming a node would defeat
    /// the choice. The truss says so in <see cref="FlatTruss.Notes"/>.
    /// </para>
    /// </summary>
    Strict = 1,
}
