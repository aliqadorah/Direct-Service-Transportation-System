using DirectService.Transportation.Application.Invoicing;

namespace DirectService.Transportation.Application.Tests;

/// <summary>B6 invoice and credit-note arithmetic (PRD 8.3, BR-022 … BR-027) with the PRD's numeric examples.</summary>
public sealed class InvoiceUnitTests
{
    [Fact]
    public void PRD_invoice_example_four_trips_720_discount_20_fees_5()
    {
        // 8.3: 4 trips totalling 720.000; discount 20.000; fees 5.000 → base 705.000; tax 112.800; total 817.800 JOD.
        var t = InvoiceCalculator.Invoice(720.000m, 20.000m, 5.000m, 16m, taxInclusive: false);
        Assert.Equal(720.000m, t.Subtotal);
        Assert.Equal(705.000m, t.BaseAmount);
        Assert.Equal(112.800m, t.Tax);
        Assert.Equal(817.800m, t.Total);
    }

    [Fact]
    public void PRD_credit_note_example_one_trip_of_180_returned_BR025()
    {
        // 8.3: one trip of 180.000 returned → share 25 %; discount share 5.000; fees share 1.250; base 176.250; tax 28.200; total 204.450.
        var invoice = InvoiceCalculator.Invoice(720m, 20m, 5m, 16m, false);
        var c = InvoiceCalculator.Credit(180m, 720m, invoice, 16m, false, returnsEverything: false, ReturnedSoFar.None);
        Assert.Equal(180.000m, c.Subtotal);
        Assert.Equal(5.000m, c.DiscountShare);
        Assert.Equal(1.250m, c.FeesShare);
        Assert.Equal(176.250m, c.BaseAmount);
        Assert.Equal(28.200m, c.Tax);
        Assert.Equal(204.450m, c.Total);
    }

    [Fact]
    public void Returning_everything_still_open_absorbs_rounding_BR027()
    {
        // Three equal lines of 100.003 with a discount of 0.010: shares round, the last credit note takes exactly what is left.
        var invoice = InvoiceCalculator.Invoice(300.009m, 0.010m, 0.005m, 16m, false);
        var first = InvoiceCalculator.Credit(100.003m, 300.009m, invoice, 16m, false, false, ReturnedSoFar.None);
        var second = InvoiceCalculator.Credit(100.003m, 300.009m, invoice, 16m, false, false,
            new ReturnedSoFar(first.Subtotal, first.DiscountShare, first.FeesShare, first.Tax, first.Total));
        var before = new ReturnedSoFar(first.Subtotal + second.Subtotal, first.DiscountShare + second.DiscountShare, first.FeesShare + second.FeesShare,
            first.Tax + second.Tax, first.Total + second.Total);
        var last = InvoiceCalculator.Credit(100.003m, 300.009m, invoice, 16m, false, returnsEverything: true, before);
        Assert.Equal(invoice.Total, first.Total + second.Total + last.Total);   // remaining exactly 0.000
        Assert.Equal(invoice.Tax, first.Tax + second.Tax + last.Tax);
        Assert.Equal(invoice.Discount, first.DiscountShare + second.DiscountShare + last.DiscountShare);
        Assert.Equal(last.BaseAmount + last.Tax, last.Total);
    }

    [Fact]
    public void Tax_inclusive_mode_keeps_the_design_checks_DT79()
    {
        // Gross 1,160.000 including 16 % → tax 160.000, base 1,000.000; base = subtotal − discount + fees and total = base + tax.
        var t = InvoiceCalculator.Invoice(1160.000m, 0m, 0m, 16m, taxInclusive: true);
        Assert.Equal(160.000m, t.Tax);
        Assert.Equal(1000.000m, t.BaseAmount);
        Assert.Equal(1160.000m, t.Total);
        Assert.Equal(t.Subtotal - t.Discount + t.ExtraFees, t.BaseAmount);
        var withDiscount = InvoiceCalculator.Invoice(1180.000m, 20m, 0m, 16m, true);
        Assert.Equal(withDiscount.Subtotal - withDiscount.Discount + withDiscount.ExtraFees, withDiscount.BaseAmount);
        Assert.Equal(withDiscount.BaseAmount + withDiscount.Tax, withDiscount.Total);
        Assert.Equal(1160.000m, withDiscount.Total);
    }

    [Theory]
    [InlineData(100.000, 100.000, 0.000)]     // discount = lines: subtotal and discount both net, base 0
    [InlineData(0.010, 0.000, 25.000)]        // tiny lines with large fees: subtotal never negative
    [InlineData(333.333, 33.333, 7.777)]
    [InlineData(1000.001, 0.001, 0.001)]
    public void Tax_inclusive_parts_convert_separately_review_H1(double lines, double discount, double fees)
    {
        var (l, d, f) = ((decimal)lines, (decimal)discount, (decimal)fees);
        var t = InvoiceCalculator.Invoice(l, d, f, 16m, taxInclusive: true);
        Assert.Equal(l - d + f, t.Total);                                  // what the customer was quoted
        Assert.Equal(t.Subtotal - t.Discount + t.ExtraFees, t.BaseAmount); // CK base
        Assert.Equal(t.BaseAmount + t.Tax, t.Total);                       // CK total
        Assert.InRange(t.Discount, 0m, t.Subtotal);                        // CK discount ≤ subtotal
        Assert.True(t.Subtotal >= 0 && t.ExtraFees >= 0 && t.Tax >= 0);
        Assert.InRange(t.Tax - Math.Round(t.BaseAmount * 0.16m, 3), -0.002m, 0.002m);   // tax within rounding of rate × base
    }

    [Fact]
    public void Credit_on_an_inclusive_invoice_uses_net_amounts_review_M1()
    {
        // Two trips of 580.000 including 16 % → invoice 1,160.000 (net 1,000.000). Returning one: net 500.000, tax 80.000, total 580.000.
        var invoice = InvoiceCalculator.Invoice(1160m, 0m, 0m, 16m, true);
        var c = InvoiceCalculator.Credit(580m, 1160m, invoice, 16m, taxInclusive: true, returnsEverything: false, ReturnedSoFar.None);
        Assert.Equal(500.000m, c.Subtotal);
        Assert.Equal(80.000m, c.Tax);
        Assert.Equal(580.000m, c.Total);
    }

    [Fact]
    public void Credit_over_the_remaining_balance_by_rounding_takes_the_balance_review_M2()
    {
        var c = new CreditTotals(100m, 0m, 0m, 100m, 16.001m, 116.001m);
        var capped = InvoiceCalculator.CapToRemaining(c, 116.000m, 0.010m);
        Assert.Equal(116.000m, capped.Total);
        Assert.Equal(16.000m, capped.Tax);
        Assert.Equal(capped.BaseAmount + capped.Tax, capped.Total);
        // Beyond the tolerance the credit note is left alone (BR-026 refuses it).
        Assert.Equal(c, InvoiceCalculator.CapToRemaining(c, 110m, 0.010m));
    }

    [Fact]
    public void Line_amounts_round_half_up_and_tax_once_on_the_base_BR021_BR023()
    {
        Assert.Equal(174.038m, InvoiceCalculator.LineAmount(33.150m, 5.250m));   // 174.0375 → 174.038
        // Tax once on the base, not per line: 3 × 0.005 at 50 % → base 0.015, tax round(0.0075) = 0.008.
        Assert.Equal(0.008m, InvoiceCalculator.Invoice(0.015m, 0, 0, 50m, false).Tax);
    }
}
