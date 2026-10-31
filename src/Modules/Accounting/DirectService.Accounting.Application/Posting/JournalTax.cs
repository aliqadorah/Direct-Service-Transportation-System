namespace DirectService.Accounting.Application.Posting;

/// <summary>Tax on a manual journal line (owner request 25 Oct 2026, DT-255): the line's debit or credit is the amount before tax.</summary>
public static class JournalTax
{
    /// <summary>Tax = amount × rate ÷ 100, rounded half-up to the currency's decimals (BR-020; 3 for JOD).</summary>
    public static decimal Amount(decimal amount, decimal rate, int decimals) => Money.Round(amount * rate / 100m, decimals);

    /// <summary>The note of an automatic tax line, both languages (stored texts are bilingual, like reversal descriptions).</summary>
    public static string Note(string nameAr, string? nameEn, string code, int lineNo) =>
        $"{nameAr} على السطر {lineNo} / {nameEn ?? code} on line {lineNo}";

    /// <summary>
    /// The editable default tax codes of a new company: the Jordanian VAT rates (the owner can edit or deactivate them). Arabic description
    /// "ضريبة المبيعات العامة n%" (owner decision 26 Oct 2026, DT-263; before: "ضريبة المبيعات n%", renamed by Rev6_BudgetProfitLoss where unchanged).
    /// </summary>
    public static readonly IReadOnlyList<(string Code, decimal Rate, string NameAr, string NameEn, int SortOrder)> Defaults =
        new[] { 0m, 1m, 2m, 4m, 5m, 7m, 8m, 10m, 16m }
            .Select((rate, i) => ($"VAT_JO_{rate:0}", rate, $"ضريبة المبيعات العامة {rate:0}%", $"VAT {rate:0}%", (i + 1) * 10)).ToList();
}
