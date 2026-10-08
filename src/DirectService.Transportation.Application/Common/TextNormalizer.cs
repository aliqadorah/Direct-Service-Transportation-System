using System.Text;

namespace DirectService.Transportation.Application.Common;

/// <summary>Text normalisation shared by validation, uniqueness keys and search.</summary>
public static class TextNormalizer
{
    /// <summary>Converts Arabic-Indic (٠-٩) and Eastern Arabic-Indic (۰-۹) digits to ASCII 0-9.</summary>
    public static string ToAsciiDigits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            sb.Append(ch switch
            {
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                _ => ch,
            });
        }
        return sb.ToString();
    }

    /// <summary>Trimmed text with ASCII digits; null when empty. Used for codes and numbers stored in VARCHAR columns.</summary>
    public static string? CleanCode(string? value)
    {
        var v = ToAsciiDigits(value).Trim();
        return v.Length == 0 ? null : v;
    }

    /// <summary>Trimmed person-typed text; inner whitespace collapsed; null when empty.</summary>
    public static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Uniqueness key that ignores letter case and spaces (e.g. TRN_Trips.ReceiptNoKey, FR "Receipt number").</summary>
    public static string Key(string? value) =>
        ToAsciiDigits(value).Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();

    /// <summary>Digits only (verification codes).</summary>
    public static string DigitsOnly(string? value) =>
        new(ToAsciiDigits(value).Where(char.IsAsciiDigit).ToArray());
}
