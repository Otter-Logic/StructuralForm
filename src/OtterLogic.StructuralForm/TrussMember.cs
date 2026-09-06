using OtterLogic.Core;
using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// What part of the truss a member belongs to.
/// <para>
/// Deliberately finer-grained than the four structural families: verticals and
/// diagonals are split apart because they are sized and specified separately,
/// so a front-end that sorts members by role can hand a downstream tool
/// something it can put a section against.
/// </para>
/// </summary>
public enum TrussMemberRole
{
    TopChord,
    BottomChord,

    /// <summary>Web member running from a top node to the bottom node paired with it.</summary>
    Vertical,

    /// <summary>Web member running across a panel, from one chord to the other.</summary>
    Diagonal,

    EndPost,
}

/// <summary>
/// Readable names for the roles.
/// <para>
/// Both front-ends need the same words and neither should be inventing them:
/// the Rhino command names a layer after each role, the Grasshopper component
/// names an output port. A role renamed here is renamed in both.
/// </para>
/// </summary>
public static class TrussMemberRoles
{
    /// <summary>The roles in the order a truss is usually read: chords, web, ends.</summary>
    public static readonly IReadOnlyList<TrussMemberRole> All = new[]
    {
        TrussMemberRole.TopChord,
        TrussMemberRole.BottomChord,
        TrussMemberRole.Vertical,
        TrussMemberRole.Diagonal,
        TrussMemberRole.EndPost,
    };

    /// <summary>"EndPost" as "End post".</summary>
    public static string DisplayName(this TrussMemberRole role) => Naming.Humanise(role);
}

/// <summary>
/// One straight member, tagged with its structural role and the node indices it
/// spans. The indices let a downstream analysis step rebuild connectivity
/// without re-matching coordinates.
/// </summary>
public sealed record TrussMember(Line Line, TrussMemberRole Role, int StartNode, int EndNode)
{
    public double Length => Line.Length;
}
