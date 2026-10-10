using DirectService.Transportation.Application.EInvoicing;

namespace DirectService.Transportation.Application.Tests;

/// <summary>B7: splitting the stored invoice amounts into national e-invoice lines (DT-82) — exact sums, tax within one unit per line.</summary>
public sealed class EInvoiceUnitTests
{
    [Fact]
    public void PRD_invoice_lines_carry_discount_fees_and_tax_exactly()
    {
        // 8.3: four trips of 180.000, discount 20.000, fees 5.000 → tax 112.800, total 817.800.
        var lines = EInvoiceDocumentBuilder.Allocate([("T-1", 180m), ("T-2", 180m), ("T-3", 180m), ("T-4", 180m)], 720m, 20m, 5m, "Night loading", 112.800m, 16m, 3);
        Assert.Equal(5, lines.Count);                                      // fees as their own line
        Assert.Equal(725.000m, lines.Sum(l => l.Quantity * l.UnitPrice));  // TaxExclusive = subtotal + fees
        Assert.Equal(20.000m, lines.Sum(l => l.Discount));
        Assert.Equal(112.800m, lines.Sum(l => l.Tax));
        Assert.All(lines, l => Assert.Equal(l.Quantity * l.UnitPrice - l.Discount, l.NetAmount));
        Assert.All(lines, l => Assert.InRange(l.Tax - Math.Round(l.NetAmount * 0.16m, 3, MidpointRounding.AwayFromZero), -0.001m, 0.001m));
        Assert.Equal("Night loading", lines[^1].Description);
    }

    [Fact]
    public void Tax_inclusive_invoice_and_its_credit_note_split_exactly_review_M4()
    {
        // Three lines quoted tax-included, discount and fees tax-included (DT-79).
        decimal[] gross = [333.333m, 250.250m, 99.999m];
        var t = Invoicing.InvoiceCalculator.Invoice(gross.Sum(), 12.345m, 7.777m, 16m, taxInclusive: true);
        var lines = EInvoiceDocumentBuilder.Allocate(gross.Select((g, i) => ($"L{i}", g)).ToList(), t.Subtotal, t.Discount, t.ExtraFees, "Fees", t.Tax, 16m, 3);
        Assert.Equal(t.Subtotal + t.ExtraFees, lines.Sum(l => l.UnitPrice));
        Assert.Equal(t.Discount, lines.Sum(l => l.Discount));
        Assert.Equal(t.Tax, lines.Sum(l => l.Tax));
        Assert.Equal(t.Total, lines.Sum(l => l.NetAmount + l.Tax));
        // Each line's tax stays within a few fils of its own rounding (tolerance to confirm on the JoFotara test environment, R-01).
        Assert.All(lines, l => Assert.InRange(l.Tax - Math.Round(l.NetAmount * 0.16m, 3, MidpointRounding.AwayFromZero), -0.003m, 0.003m));

        var invoice = t;
        var c = Invoicing.InvoiceCalculator.Credit(250.250m, gross.Sum(), invoice, 16m, true, false, Invoicing.ReturnedSoFar.None);
        var cl = EInvoiceDocumentBuilder.Allocate([("L1", 250.250m)], c.Subtotal, c.DiscountShare, c.FeesShare, "Fees", c.Tax, 16m, 3);
        Assert.Equal(c.Total, cl.Sum(l => l.NetAmount + l.Tax));
    }

    [Theory]
    [InlineData(100.001, 3)]
    [InlineData(0.010, 3)]
    [InlineData(999.999, 7)]
    public void Split_adds_up_exactly_in_smallest_units(double amount, int parts)
    {
        var weights = Enumerable.Range(1, parts).Select(i => (decimal)i).ToList();
        var split = EInvoiceDocumentBuilder.Split((decimal)amount, weights, 3);
        Assert.Equal((decimal)amount, split.Sum());
        Assert.All(split, p => Assert.Equal(p, Math.Round(p, 3)));
    }

    [Fact]
    public void Rounding_difference_to_the_stored_tax_is_spread_one_unit_per_line()
    {
        // Three lines of 0.003 at 50 %: per-line tax 0.002 each (0.006) but the document's tax is round(0.009 × 50 %) = 0.005.
        var lines = EInvoiceDocumentBuilder.Allocate([("a", 0.003m), ("b", 0.003m), ("c", 0.003m)], 0.009m, 0m, 0m, null, 0.005m, 50m, 3);
        Assert.Equal(0.005m, lines.Sum(l => l.Tax));
        Assert.All(lines, l => Assert.InRange(l.Tax, 0.001m, 0.002m));
    }
}
