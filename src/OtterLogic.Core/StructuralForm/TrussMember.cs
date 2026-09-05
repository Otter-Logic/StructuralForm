using Rhino.Geometry;

namespace OtterLogic.Core.StructuralForm;

/// <summary>What part of the truss a member belongs to.</summary>
public enum TrussMemberRole
{
    TopChord,
    BottomChord,
    Web,
    EndPost,
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
