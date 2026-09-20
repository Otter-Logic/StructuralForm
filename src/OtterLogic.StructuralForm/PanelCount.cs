using Rhino;

namespace OtterLogic.StructuralForm;

/// <summary>
/// How many panels a length is divided into, from the two ways of asking.
/// <para>
/// Shared because every generator here asks the same pair of questions — a
/// count, or a spacing to derive one from — and the rule between them has to be
/// the same everywhere: a user who has learnt that Divisions overrides Spacing
/// on one tool should not have to learn otherwise on the next.
/// </para>
/// </summary>
internal static class PanelCount
{
    /// <summary>
    /// Divisions wins over spacing; zero from both returns zero, which each
    /// generator reads in its own way.
    /// <para>
    /// Spacing is a target, not a limit: the length is divided by it and
    /// rounded to the nearest whole panel, so the panels that come out may be
    /// a little over it. Somebody who needs a hard maximum sets Divisions.
    /// </para>
    /// </summary>
    internal static int Resolve(int divisions, double spacing, double length)
    {
        if (divisions > 0)
            return divisions;

        if (spacing > RhinoMath.ZeroTolerance)
            return Math.Max(1, (int)Math.Round(length / spacing, MidpointRounding.AwayFromZero));

        return 0;
    }
}
