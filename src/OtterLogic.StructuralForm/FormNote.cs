namespace OtterLogic.StructuralForm;

/// <summary>How loudly a front-end should say a <see cref="FormNote"/>.</summary>
public enum FormNoteLevel
{
    /// <summary>Worth knowing. The result is fine.</summary>
    Remark,

    /// <summary>Probably a modelling mistake, but not an error.</summary>
    Warning,
}

/// <summary>
/// Something worth telling the user about a generated result. The wording is
/// the domain's, so both front-ends say the same thing; the level is a hint each
/// one maps onto whatever it has — a Grasshopper bubble, a command-line line.
/// <para>
/// One type for every generator here rather than one each. It began as the
/// truss's own, and the second tool needed exactly the same two fields: a
/// second copy would have been a second thing for the front-ends to map.
/// </para>
/// </summary>
public readonly record struct FormNote(FormNoteLevel Level, string Message);
