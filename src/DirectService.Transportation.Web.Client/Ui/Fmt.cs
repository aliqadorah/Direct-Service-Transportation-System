using System.Globalization;

namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>
/// Display formats (DT-65): Western digits in both languages, thousands separators, amounts with the company currency's decimals
/// (3 for JOD), weights in tonnes with 3 decimals, dates in the user's format (default YYYY-MM-DD, FR-178). Formatting only — the API
/// does every calculation and rounding.
/// </summary>
public static partial class Fmt
{
    public const string DefaultDateFormat = "yyyy-MM-dd";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Money(decimal? v, int decimals = 3) =>
        v is null ? string.Empty : v.Value.ToString("#,##0." + new string('0', Math.Clamp(decimals, 0, 6)), Inv).TrimEnd('.');

    public static string Qty(decimal? v, int decimals = 3) => Money(v, decimals);

    public static string Count(long? v) => v?.ToString("#,##0", Inv) ?? string.Empty;

    public static string Percent(decimal? v, int decimals = 2) => v is null ? string.Empty : v.Value.ToString("0." + new string('#', decimals), Inv) + "%";

    public static string Date(DateOnly? d, string? format = null) => d?.ToString(Safe(format), Inv) ?? string.Empty;

    public static string Date(DateTime? d, string? format = null) => d?.ToString(Safe(format), Inv) ?? string.Empty;

    public static string DateTime(DateTime? d, string? format = null) => d?.ToString(Safe(format) + " HH:mm", Inv) ?? string.Empty;

    /// <summary>The user's date format, limited to the patterns the profile offers (no free-form patterns reach ToString).</summary>
    public static string Safe(string? format) => format is "yyyy-MM-dd" or "dd/MM/yyyy" or "MM/dd/yyyy" or "dd-MM-yyyy" ? format : DefaultDateFormat;

    /// <summary>Parses a typed amount: Western or Arabic-Indic digits, thousands separators and the Arabic decimal mark are accepted.</summary>
    public static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = Western(text).Replace('٫', '.').Trim();
        // A comma is a thousands separator only in groups of three ("1,234.5"); "1,5" is refused rather than read as 15.
        if (s.Contains(',', StringComparison.Ordinal) && !GroupedNumber().IsMatch(s)) return false;
        s = s.Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Inv, out value);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^-?\d{1,3}(,\d{3})+(\.\d+)?$")]
    private static partial System.Text.RegularExpressions.Regex GroupedNumber();

    /// <summary>Arabic-Indic (٠…٩) and Eastern Arabic-Indic (۰…۹) digits to Western digits; the Arabic thousands mark to a comma.</summary>
    public static string Western(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Create(text.Length, text, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                span[i] = c switch
                {
                    >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                    >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                    '٬' => ',',
                    _ => c,
                };
            }
        });
    }
}
