using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>The form a picked curve is put into before a generator reads it.</summary>
internal static class WorkingCurve
{
    /// <summary>
    /// A working copy of a curve, in NURBS form.
    /// <para>
    /// The same curve, but the form whose parameterisation survives projection.
    /// Project an arc and the result runs at a different speed along itself, so
    /// a parameter stops meaning the same place on the two of them — measured
    /// on a 12 m chord that is a 24 mm error in every node. The generators read
    /// a curve and its projection at the same parameter, so that correspondence
    /// has to be exact rather than close.
    /// </para>
    /// </summary>
    internal static Curve AsNurbs(Curve curve) => curve.ToNurbsCurve() ?? curve.DuplicateCurve();

    /// <summary>
    /// Flip a chord if it runs against <paramref name="reference"/>, so station
    /// 0 sits at the same end of both. Without this, picking the chords in a
    /// natural order silently produces a crossed truss.
    /// </summary>
    internal static Curve AlignToStart(Curve chord, Curve reference)
    {
        double aligned = chord.PointAtStart.DistanceTo(reference.PointAtStart);
        double flipped = chord.PointAtStart.DistanceTo(reference.PointAtEnd);

        if (flipped < aligned)
            chord.Reverse();

        return chord;
    }
}
