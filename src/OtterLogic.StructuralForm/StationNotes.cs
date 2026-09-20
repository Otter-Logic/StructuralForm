namespace OtterLogic.StructuralForm;

/// <summary>
/// What a truss has to say about its snap points. The same three observations
/// whichever truss makes them, because they are about the station layout the
/// trusses share — so the wording is shared with it.
/// </summary>
internal static class StationNotes
{
    internal static void AddTo(
        List<FormNote> notes, int chordCount, int offChord, int unused,
        SnapStrictness strictness, int divisions, int panelCount)
    {
        // "Either" is only English for two.
        string chords = chordCount == 2 ? "either chord" : "any of the chords";

        // Off the chords entirely: a different mistake from the one below,
        // and a different fix, so it gets its own words rather than being
        // folded into a single count of things that did not work.
        if (offChord > 0)
            notes.Add(new FormNote(
                FormNoteLevel.Warning,
                offChord == 1
                    ? $"One snap point was discounted for not lying on {chords}. "
                      + "Snap points have to sit on the curves you picked."
                    : $"{offChord} snap points were discounted for not lying on "
                      + $"{chords}. Snap points have to sit on the curves you picked."));

        // A pick that changed nothing is the one failure here with no
        // visible symptom, so it is the one most worth saying out loud.
        if (unused > 0)
            notes.Add(new FormNote(
                FormNoteLevel.Remark,
                unused == 1
                    ? "One snap point was too far from a panel point to be used. "
                      + "Set strictness to Strict to place a node on it."
                    : $"{unused} snap points were too far from a panel point to be "
                      + "used. Set strictness to Strict to place a node on each of them."));

        // Strict grew the truss rather than drop a point. Worth saying,
        // because the panel count that comes back is not the one asked for.
        if (strictness == SnapStrictness.Strict
            && divisions > 0
            && panelCount > divisions)
            notes.Add(new FormNote(
                FormNoteLevel.Remark,
                $"Strict snapping needed {panelCount} panels to give every snap point a node; "
                + $"{divisions} were asked for."));
    }
}
