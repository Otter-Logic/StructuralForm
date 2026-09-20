namespace OtterLogic.StructuralForm;

/// <summary>
/// Inputs to <see cref="BoxTrussGenerator"/> other than the chords.
/// <para>
/// A box truss is flat trusses sharing chords, and its options say so: what it
/// has in common with a flat truss is <em>nested</em> as <see cref="Sides"/>
/// rather than copied out field by field, and only what a flat truss has no
/// word for — the faces between twin chords — is added here. A rule changed on
/// <see cref="FlatTrussOptions"/> is then changed for both, and the boundary
/// between the two is visible wherever these are built.
/// </para>
/// </summary>
public sealed record BoxTrussOptions
{
    /// <summary>
    /// Everything shared with a flat truss, meaning exactly what it means
    /// there: how the truss is divided and measured, what the division owes the
    /// snap points, whether the ends are closed, and the web of the
    /// <em>side</em> faces — the ones running from a top chord to a bottom one,
    /// each of which is a flat truss in its own right.
    /// <para>
    /// The division applies to the truss as a whole. There is one station list
    /// however many chords share it, which is what keeps a panel point a single
    /// cross-section through the truss rather than three or four near misses.
    /// </para>
    /// </summary>
    public FlatTrussOptions Sides { get; init; } = new();

    /// <summary>
    /// Web pattern of the <em>lacing</em> faces: between the two top chords,
    /// and between the two bottom ones. The same patterns a side face has, read
    /// the same way, with struts where a side has verticals.
    /// <para>
    /// Warren with verticals by default — a strut at every panel point and one
    /// diagonal per panel — because a lacing face is there to hold two chords
    /// in line and at their spacing, and it takes both members to do both.
    /// <see cref="TrussType.Vierendeel"/> gives struts alone, for when the
    /// diagonal bracing is something else's job: a deck, a roof sheet.
    /// </para>
    /// </summary>
    public TrussType LacingType { get; init; } = TrussType.WarrenWithVerticals;

    /// <summary>
    /// Mirror every lacing diagonal within its own panel. Separate from the
    /// sides' own flip because the two are read in different views, and getting
    /// one the right way round should not turn the other.
    /// </summary>
    public bool FlipLacing { get; init; }
}
