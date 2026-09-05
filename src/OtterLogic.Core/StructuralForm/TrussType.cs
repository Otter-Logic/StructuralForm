namespace OtterLogic.Core.StructuralForm;

/// <summary>
/// Web bracing patterns for a 2D truss.
/// <para>
/// Values are explicit and must stay stable: they are what a Grasshopper
/// definition stores on the wire and what the Rhino command persists between
/// sessions. Append new members, never renumber existing ones.
/// </para>
/// </summary>
public enum TrussType
{
    /// <summary>Continuous zigzag of diagonals, no verticals.</summary>
    Warren = 0,

    /// <summary>Warren zigzag with a vertical at every interior node.</summary>
    WarrenWithVerticals = 1,

    /// <summary>Verticals throughout, diagonals sloping down toward mid-span.</summary>
    Pratt = 2,

    /// <summary>Verticals throughout, diagonals sloping up toward mid-span — Pratt mirrored.</summary>
    Howe = 3,

    /// <summary>
    /// Posts only, no diagonals. Carries load through rigid joints and bending
    /// rather than triangulation, so the panels stay open.
    /// </summary>
    Vierendeel = 4,

    /// <summary>Both diagonals in every panel, plus a vertical at each interior node.</summary>
    CrossBraced = 5,
}
