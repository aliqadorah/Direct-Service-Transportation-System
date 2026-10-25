using System.Globalization;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Contracts.Errors;

namespace DirectService.Transportation.Application.Tests;

/// <summary>
/// Owner request 25 Oct 2026 (DT-255…DT-257) without a database: tax on a journal line (amount before tax × rate, half-up to the currency's
/// decimals), the automatic tax line's note, the seeded tax codes, the default tax accounts and the field labels and messages the new rules use.
/// </summary>
public sealed class JournalTaxUnitTests
{
    [Theory]
    [InlineData("100", "16", 3, "16.000")]
    [InlineData("33.333", "7", 3, "2.333")]      // 2.33331
    [InlineData("0.125", "10", 3, "0.013")]      // 0.0125: half-up, not to even
    [InlineData("0.025", "10", 3, "0.003")]      // 0.0025
    [InlineData("10.005", "5", 3, "0.500")]      // 0.50025
    [InlineData("1234.567", "16", 3, "197.531")] // 197.53072
    [InlineData("100", "0", 3, "0")]
    [InlineData("0.001", "1", 3, "0")]           // too small for a fils: no tax line
    [InlineData("12.345", "16", 2, "1.98")]      // a two-decimal currency: 1.9752
    public void Tax_is_the_amount_times_the_rate_rounded_half_up_to_the_currency(string amount, string rate, int decimals, string expected) =>
        Assert.Equal(P(expected), JournalTax.Amount(P(amount), P(rate), decimals));

    [Fact]
    public void Gross_is_amount_plus_tax()
    {
        var tax = JournalTax.Amount(100m, 16m, 3);
        Assert.Equal(116m, 100m + tax);
    }

    [Fact]
    public void The_automatic_tax_line_names_the_code_and_the_line_in_both_languages()
    {
        var note = JournalTax.Note("ضريبة المبيعات 16%", "VAT 16%", "VAT_JO_16", 2);
        Assert.Equal("ضريبة المبيعات 16% على السطر 2 / VAT 16% on line 2", note);
        Assert.EndsWith("VAT_JO_5 on line 1", JournalTax.Note("ضريبة", null, "VAT_JO_5", 1), StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_tax_codes_are_the_jordanian_vat_rates_in_order()
    {
        Assert.Equal([0m, 1m, 2m, 4m, 5m, 7m, 8m, 10m, 16m], JournalTax.Defaults.Select(d => d.Rate));
        Assert.Equal("VAT_JO_16", JournalTax.Defaults[^1].Code);
        Assert.Equal(("ضريبة المبيعات 16%", "VAT 16%"), (JournalTax.Defaults[^1].NameAr, JournalTax.Defaults[^1].NameEn));
        Assert.Equal(JournalTax.Defaults.Select(d => d.SortOrder).Order(), JournalTax.Defaults.Select(d => d.SortOrder));
        Assert.All(JournalTax.Defaults, d => Assert.True(ChartOfAccountsService.IsCode(d.Code), d.Code));
    }

    [Fact]
    public void The_default_tax_accounts_are_liability_asset_and_expense_under_template_groups()
    {
        Assert.Equal(["Liability", "Asset", "Expense"], ChartTemplate.TaxAccounts.Select(t => t.Type));
        foreach (var t in ChartTemplate.TaxAccounts)
        {
            var group = ChartTemplate.Accounts.Single(a => a.Code == t.GroupCode);
            var account = ChartTemplate.Accounts.Single(a => a.Code == t.TemplateCode);
            Assert.Equal((t.Type, t.Type, t.GroupCode, true), (group.Type, account.Type, account.ParentCode, account.IsTax));
            Assert.False(string.IsNullOrWhiteSpace(t.NameAr));
            Assert.False(string.IsNullOrWhiteSpace(t.NameEn));
        }
    }

    [Theory]
    [InlineData("lines[1].taxCodeId", "Line 2 · Tax code")]
    [InlineData("lines[0].taxAccountId", "Line 1 · Tax account")]
    [InlineData("lines[3].reference", "Line 4 · Reference")]
    [InlineData("lineReference", "Line reference")]
    [InlineData("taxCode", "Tax code")]
    [InlineData("taxAccount", "Tax account")]
    [InlineData("rate", "Rate")]
    [InlineData("sortOrder", "Order")]
    [InlineData("isTaxAccount", "Tax account")]
    public void New_fields_have_labels(string field, string english)
    {
        Assert.True(FieldLabels.TryLabel(field, "en", out var label));
        Assert.Equal(english, label);
        Assert.True(FieldLabels.TryLabel(field, "ar", out var arabic));
        Assert.False(string.IsNullOrWhiteSpace(arabic));
    }

    [Fact]
    public void The_balance_message_explains_that_tax_is_on_the_same_side()
    {
        var en = ErrorCatalog.Format(ErrorCodes.JournalUnbalancedWithTax, "en", "116.000", "16.000", "100.000", "0.000");
        Assert.Contains("including tax: debit 116.000 (tax 16.000), credit 100.000 (tax 0.000)", en, StringComparison.Ordinal);
        Assert.Contains("same side", en, StringComparison.Ordinal);
        Assert.Contains("116.000", ErrorCatalog.Format(ErrorCodes.JournalUnbalancedWithTax, "ar", "116.000", "16.000", "100.000", "0.000"), StringComparison.Ordinal);
    }

    private static decimal P(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);
}
