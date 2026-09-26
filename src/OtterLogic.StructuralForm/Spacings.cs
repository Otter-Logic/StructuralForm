using System.Globalization;

namespace OtterLogic.StructuralForm;

/// <summary>
/// A run of bay spacings typed as text: <c>6000</c>, <c>3x6000, 8000</c>,
/// <c>6 6 8</c>. What the Rhino commands ask for, and what a storey list will
/// be when Copy Storey arrives — one parser, so the two cannot disagree about
/// what <c>3x4000</c> means.
/// <para>
/// Lives in the domain rather than the Rhino adaptor because the reading is a
/// claim about the notation, not about the host: a Grasshopper text input fed
/// the same string has to give the same bays. Grasshopper's number lists skip
/// it entirely and hand the generators a list.
/// </para>
/// <para>
/// Numbers are read in the invariant culture, so the decimal mark is always a
/// point. A comma is a separator here whatever the machine's locale says,
/// because <c>6000, 8000</c> is how a list is typed and a locale that read it
/// as one number would silently make a single odd bay.
/// </para>
/// </summary>
public static class Spacings
{
    private static readonly char[] Separators = { ',', ';', ' ', '\t' };
    private static readonly char[] RepeatMarks = { 'x', 'X', '*' };

    /// <summary>
    /// Reads a spacing list. <c>3x6000</c> is three bays of 6000; <c>*</c> is
    /// accepted for <c>x</c>. Every spacing has to be a finite number greater
    /// than zero — a zero bay is two gridlines in one place, which nobody
    /// means.
    /// </summary>
    /// <returns>
    /// True with the expanded bays, or false with <paramref name="error"/>
    /// saying which token was wrong, in words a command line can print.
    /// </returns>
    public static bool TryParse(string? text, out IReadOnlyList<double> spacings, out string? error)
    {
        var bays = new List<double>();
        spacings = bays;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "No spacings were given. Type one or more, like 6000 or 3x6000, 8000.";
            return false;
        }

        foreach (string token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            int count = 1;
            string value = token;
            int mark = token.IndexOfAny(RepeatMarks);

            if (mark >= 0)
            {
                if (!int.TryParse(token[..mark], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1)
                {
                    error = $"'{token}' has to be a count, an x and a spacing, like 3x6000.";
                    return false;
                }

                value = token[(mark + 1)..];
            }

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double spacing)
                || !double.IsFinite(spacing)
                || spacing <= 0.0)
            {
                error = $"'{token}' is not a spacing greater than zero.";
                return false;
            }

            for (int i = 0; i < count; i++)
                bays.Add(spacing);
        }

        return true;
    }

    /// <summary>
    /// <see cref="TryParse"/> for callers that would rather throw.
    /// </summary>
    public static IReadOnlyList<double> Parse(string text)
        => TryParse(text, out IReadOnlyList<double> spacings, out string? error)
            ? spacings
            : throw new FormatException(error);

    /// <summary>
    /// The list written back the way it would be typed, with runs collapsed:
    /// 6000, 6000, 6000, 8000 becomes <c>3x6000, 8000</c>. For showing a
    /// remembered value in a prompt, where the expanded list would be long and
    /// the run form is what the user typed in the first place.
    /// </summary>
    public static string Describe(IReadOnlyList<double> spacings, string separator = ", ")
    {
        if (spacings is null) throw new ArgumentNullException(nameof(spacings));

        var runs = new List<string>();

        for (int i = 0; i < spacings.Count;)
        {
            int j = i + 1;
            while (j < spacings.Count && spacings[j] == spacings[i]) j++;

            string value = spacings[i].ToString("0.###", CultureInfo.InvariantCulture);
            runs.Add(j - i == 1 ? value : $"{j - i}x{value}");
            i = j;
        }

        return string.Join(separator, runs);
    }

    /// <summary>Running totals from zero: the offset of each gridline from the first.</summary>
    internal static List<double> Offsets(IReadOnlyList<double> spacings, double start = 0.0)
    {
        var offsets = new List<double>(spacings.Count + 1) { start };
        double at = start;

        foreach (double spacing in spacings)
            offsets.Add(at += spacing);

        return offsets;
    }
}
