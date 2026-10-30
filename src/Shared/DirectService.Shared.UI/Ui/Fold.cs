using System.Text;

namespace DirectService.Shared.UI.Ui;

/// <summary>
/// Search folding for in-page filtering of short lists, matching the server's Arabic-insensitive search (CI_AI collation, B11): case,
/// diacritics and tatweel ignored; أ إ آ ٱ → ا, ة → ه, ى → ي, ؤ → و, ئ → ي; Arabic-Indic digits → Western.
/// </summary>
public static class Fold
{
    public static string Text(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in Fmt.Western(s))
        {
            if (ch is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ') continue; // harakat, superscript alef, tatweel
            sb.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => char.ToLowerInvariant(ch),
            });
        }
        return sb.ToString().Trim();
    }

    public static bool Contains(string? haystack, string? needle) =>
        string.IsNullOrWhiteSpace(needle) || Text(haystack).Contains(Text(needle), StringComparison.Ordinal);
}
