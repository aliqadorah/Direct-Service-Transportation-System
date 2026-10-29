using System.Globalization;
using System.Text;

namespace DirectService.SharedKernel;

/// <summary>
/// Amount in words for printed vouchers (BR-024): Arabic with the currency's main and fractional units and the usual
/// "فقط … لا غير" frame, and an English equivalent. Counted-noun forms follow Arabic number agreement:
/// 1 → singular + واحد, 2 → dual, 3–10 → plural, 11–99 → singular accusative, round hundreds → singular.
/// </summary>
public static class AmountInWords
{
    private sealed record Unit(string Singular, string Dual, string Plural, string Accusative, string En, string EnPlural);

    private sealed record CurrencyWords(Unit Main, Unit Fraction, int FractionDigits);

    private static readonly Dictionary<string, CurrencyWords> Currencies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JOD"] = new(new Unit("دينار", "ديناران", "دنانير", "ديناراً", "Jordanian dinar", "Jordanian dinars"),
                      new Unit("فلس", "فلسان", "فلوس", "فلساً", "fils", "fils"), 3),
    };

    private static readonly string[] Ones = ["", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة"];
    private static readonly string[] Teens = ["عشرة", "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر", "ستة عشر", "سبعة عشر", "ثمانية عشر", "تسعة عشر"];
    private static readonly string[] Tens = ["", "", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون"];
    private static readonly string[] Hundreds = ["", "مائة", "مائتان", "ثلاثمائة", "أربعمائة", "خمسمائة", "ستمائة", "سبعمائة", "ثمانمائة", "تسعمائة"];

    private static readonly Unit[] Scales =
    [
        new("ألف", "ألفان", "آلاف", "ألفاً", "thousand", "thousand"),
        new("مليون", "مليونان", "ملايين", "مليوناً", "million", "million"),
        new("مليار", "ملياران", "مليارات", "ملياراً", "billion", "billion"),
    ];

    /// <summary>Arabic words, e.g. 817.800 JOD → "فقط ثمانمائة وسبعة عشر ديناراً وثمانمائة فلس لا غير".</summary>
    public static string Arabic(decimal amount, string currencyCode = "JOD", int decimals = 3)
    {
        var (major, minor, words) = Split(amount, currencyCode, decimals);
        var parts = new List<string>();
        if (major > 0 || minor == 0) parts.Add(Counted(major, words?.Main ?? new Unit(currencyCode, currencyCode, currencyCode, currencyCode, currencyCode, currencyCode)));
        if (minor > 0) parts.Add(Counted(minor, words?.Fraction ?? new Unit("", "", "", "", "", "")).Trim());
        return "فقط " + string.Join(" و", parts) + " لا غير";
    }

    /// <summary>English words, e.g. "Eight hundred seventeen Jordanian dinars and 800 fils only".</summary>
    public static string English(decimal amount, string currencyCode = "JOD", int decimals = 3)
    {
        var (major, minor, words) = Split(amount, currencyCode, decimals);
        var sb = new StringBuilder();
        sb.Append(EnglishNumber(major));
        sb.Append(' ').Append(major == 1 ? words?.Main.En ?? currencyCode : words?.Main.EnPlural ?? currencyCode);
        if (minor > 0)
            sb.Append(" and ").Append(minor.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(words?.Fraction.EnPlural ?? string.Empty);
        sb.Append(" only");
        var text = sb.ToString().Replace("  ", " ", StringComparison.Ordinal);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static (long Major, long Minor, CurrencyWords? Words) Split(decimal amount, string currencyCode, int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var words = Currencies.GetValueOrDefault(currencyCode);
        var digits = words?.FractionDigits ?? decimals;
        var rounded = Math.Round(amount, digits, MidpointRounding.AwayFromZero);
        var major = (long)decimal.Truncate(rounded);
        var minor = (long)((rounded - major) * (decimal)Math.Pow(10, digits));
        return (major, minor, words);
    }

    /// <summary>A number with its counted noun in the right form.</summary>
    private static string Counted(long n, Unit unit)
    {
        if (n == 0) return "صفر " + unit.Singular;
        if (n == 1) return unit.Singular + " واحد";
        if (n == 2) return unit.Dual;
        return ArabicNumber(n) + " " + Form(n, unit);
    }

    private static string Form(long n, Unit unit)
    {
        var lastTwo = n % 100;
        if (lastTwo == 0) return unit.Singular;
        if (lastTwo is >= 3 and <= 10) return unit.Plural;
        return unit.Accusative;
    }

    /// <summary>Arabic words of a positive integer (masculine counting).</summary>
    internal static string ArabicNumber(long n, bool nounFollows = true)
    {
        if (n == 0) return "صفر";
        var groups = new List<string>();
        var scaleIndex = -1;
        while (n > 0)
        {
            var group = (int)(n % 1000);
            n /= 1000;
            if (group > 0)
            {
                if (scaleIndex < 0)
                {
                    groups.Insert(0, BelowThousand(group));
                }
                else
                {
                    var scale = Scales[Math.Min(scaleIndex, Scales.Length - 1)];
                    // Directly before the counted noun (lower groups all zero) the scale word takes its construct form:
                    // ألفا دينار، خمسة عشر ألف دينار.
                    var construct = nounFollows && groups.Count == 0;
                    groups.Insert(0, group switch
                    {
                        1 => scale.Singular,
                        2 => construct ? scale.Dual[..^1] : scale.Dual,
                        _ => BelowThousand(group) + " " + (construct && Form(group, scale) == scale.Accusative ? scale.Singular : Form(group, scale)),
                    });
                }
            }
            scaleIndex++;
        }
        return string.Join(" و", groups);
    }

    private static string BelowThousand(int n)
    {
        var parts = new List<string>();
        if (n >= 100) parts.Add(Hundreds[n / 100]);
        var rest = n % 100;
        if (rest >= 20)
        {
            parts.Add(rest % 10 == 0 ? Tens[rest / 10] : Ones[rest % 10] + " و" + Tens[rest / 10]);
        }
        else if (rest >= 10)
        {
            parts.Add(Teens[rest - 10]);
        }
        else if (rest > 0)
        {
            parts.Add(Ones[rest]);
        }
        return string.Join(" و", parts);
    }

    private static readonly string[] EnOnes =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen",
        "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    ];

    private static readonly string[] EnTens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    private static readonly string[] EnScales = ["", " thousand", " million", " billion", " trillion"];

    internal static string EnglishNumber(long n)
    {
        if (n == 0) return "zero";
        var groups = new List<string>();
        var scale = 0;
        while (n > 0)
        {
            var group = (int)(n % 1000);
            if (group > 0) groups.Insert(0, EnglishBelowThousand(group) + EnScales[Math.Min(scale, EnScales.Length - 1)]);
            n /= 1000;
            scale++;
        }
        return string.Join(" ", groups);
    }

    private static string EnglishBelowThousand(int n)
    {
        var parts = new List<string>();
        if (n >= 100) parts.Add(EnOnes[n / 100] + " hundred");
        var rest = n % 100;
        if (rest >= 20) parts.Add(rest % 10 == 0 ? EnTens[rest / 10] : EnTens[rest / 10] + "-" + EnOnes[rest % 10]);
        else if (rest > 0) parts.Add(EnOnes[rest]);
        return string.Join(" ", parts);
    }
}
