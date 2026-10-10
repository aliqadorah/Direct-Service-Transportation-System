namespace DirectService.Transportation.Application.Abstractions;

/// <summary>
/// Arabic spelling variants for search (FR-187, A-12): hamza forms of alef (أ إ آ → ا), taa marbuta (ة → ه) and alef maqsura (ى → ي) are
/// folded on both sides; diacritics and case are already ignored by the database collation (Arabic_100_CI_AI_SC_UTF8).
/// </summary>
public static class SqlText
{
    public const string From = "أإآةى";
    public const string To = "اااهي";

    /// <summary>TRANSLATE(value, 'أإآةى', 'اااهي') in queries.</summary>
    public static string? Fold(string? value) => throw new NotSupportedException("Only for database queries.");

    /// <summary>The same folding in memory (for the search text).</summary>
    public static string FoldText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var k = From.IndexOf(chars[i], StringComparison.Ordinal);
            if (k >= 0) chars[i] = To[k];
        }
        return new string(chars);
    }
}
