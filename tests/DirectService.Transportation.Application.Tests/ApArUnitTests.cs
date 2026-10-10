using DirectService.Transportation.Application.ApAr;
using DirectService.Transportation.Application.EInvoicing;
using DirectService.Transportation.Contracts.ApAr;

namespace DirectService.Transportation.Application.Tests;

/// <summary>B9 rules without a database: AP/AR tax and payment state, ageing buckets, national lines of mixed-rate receivables (D-15).</summary>
public sealed class ApArUnitTests
{
    [Fact]
    public void Tax_is_rounded_once_over_the_lines_BR021()
    {
        // 3,120 × 16% + 410 × 16% = 564.8; 0.333 × 16% + 0.333 × 16% = 0.10656 → 0.107 (per-line rounding would give 0.106).
        Assert.Equal(564.800m, ApArRules.Tax([(3120m, 16m), (410m, 16m)], 3));
        Assert.Equal(0.107m, ApArRules.Tax([(0.333m, 16m), (0.333m, 16m)], 3));
        Assert.Equal(0m, ApArRules.Tax([(100m, 0m)], 3));
    }

    [Theory]
    [InlineData("Posted", 100, 100, ApArCodes.Open)]
    [InlineData("Posted", 100, 40, ApArCodes.Partial)]
    [InlineData("Posted", 100, 0, ApArCodes.Closed)]
    [InlineData("Void", 100, 100, ApArCodes.Void)]
    public void Payment_status_follows_the_remaining_balance(string status, int total, int remaining, string expected) =>
        Assert.Equal(expected, ApArRules.PaymentStatus(status, total, remaining));

    [Theory]
    [InlineData("bills", ApArCodes.SupplierBill)]
    [InlineData("Receivables", ApArCodes.Receivable)]
    public void Kinds_from_routes(string value, string kind) => Assert.Equal(kind, ApArRules.Kind(value));

    [Fact]
    public void Ageing_buckets_by_days_past_due_FR128()
    {
        var b = AgeingService.Buckets([(-5, 10m), (0, 1m), (1, 2m), (30, 3m), (31, 4m), (60, 5m), (61, 6m), (90, 7m), (91, 8m), (400, 9m)], 12.5m);
        Assert.Equal(11m, b.NotDue);
        Assert.Equal(5m, b.Days1To30);
        Assert.Equal(9m, b.Days31To60);
        Assert.Equal(13m, b.Days61To90);
        Assert.Equal(17m, b.Over90);
        Assert.Equal(55m, b.Total);
        Assert.Equal(12.5m, b.Unapplied);
    }

    [Fact]
    public void Mixed_rate_lines_add_up_exactly_to_the_stored_tax_D15()
    {
        // 0.333 @16% + 0.333 @16% + 5.000 @0%: stored tax 0.107 (rounded once); lines 0.053 + 0.053 + 0 → one unit spread to a taxable line.
        var lines = EInvoiceDocumentBuilder.AllocateByRate([("a", 0.333m, 16m), ("b", 0.333m, 16m), ("exempt", 5m, 0m)], 0.107m, 3);
        Assert.Equal(3, lines.Count);
        Assert.Equal(0.107m, lines.Sum(l => l.Tax));
        Assert.Equal(0m, lines.Single(l => l.Description == "exempt").Tax);
        Assert.All(lines, l => Assert.Equal(l.NetAmount, l.Quantity * l.UnitPrice - l.Discount));
    }
}
