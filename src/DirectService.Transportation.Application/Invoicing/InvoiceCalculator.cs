using DirectService.Transportation.Application.Accounting;

namespace DirectService.Transportation.Application.Invoicing;

/// <summary>The stored totals of an invoice (PRD 8.3).</summary>
public sealed record InvoiceTotals(decimal Subtotal, decimal Discount, decimal ExtraFees, decimal BaseAmount, decimal Tax, decimal Total);

/// <summary>The stored amounts of a credit note (BR-025 … BR-027).</summary>
public sealed record CreditTotals(decimal Subtotal, decimal DiscountShare, decimal FeesShare, decimal BaseAmount, decimal Tax, decimal Total);

/// <summary>What earlier active credit notes of the invoice already returned.</summary>
public sealed record ReturnedSoFar(decimal Subtotal, decimal DiscountShare, decimal FeesShare, decimal Tax, decimal Total)
{
    public static readonly ReturnedSoFar None = new(0, 0, 0, 0, 0);
}

/// <summary>
/// Invoice and credit-note arithmetic (PRD 8.3, BR-020 … BR-027): amounts in the currency's decimals, half-up at each amount, tax
/// once on the base. Exclusive tax (default, BR-022): base = subtotal − discount + fees, tax = round(base × rate), total = base + tax.
/// Inclusive tax (company setting, DT-79): lines, discount and fees are entered with the tax included; each part is converted to
/// its net amount separately (subtotal = round(lines ÷ (1 + r)), discount and fees likewise), base = subtotal − discount + fees and
/// tax = gross − base, so the stored amounts satisfy the design's checks (discount ≤ subtotal, base = subtotal − discount + fees,
/// total = base + tax), revenue and other revenue are posted net, and the total equals what the customer was quoted (review H1).
/// </summary>
public static class InvoiceCalculator
{
    public static InvoiceTotals Invoice(decimal linesTotal, decimal discount, decimal extraFees, decimal taxRate, bool taxInclusive, int decimals = 3)
    {
        if (!taxInclusive)
        {
            var baseAmount = linesTotal - discount + extraFees;
            var tax = Money.Round(baseAmount * taxRate / 100m, decimals);
            return new InvoiceTotals(linesTotal, discount, extraFees, baseAmount, tax, baseAmount + tax);
        }
        var gross = linesTotal - discount + extraFees;
        var factor = 1m + taxRate / 100m;
        var netLines = Money.Round(linesTotal / factor, decimals);
        var netDiscount = Money.Round(discount / factor, decimals);
        var netFees = Money.Round(extraFees / factor, decimals);
        var net = netLines - netDiscount + netFees;
        return new InvoiceTotals(netLines, netDiscount, netFees, net, gross - net, gross);
    }

    /// <summary>
    /// A credit note for returned lines worth <paramref name="returnedLines"/> (in line amounts) of an invoice whose lines total
    /// <paramref name="invoiceLines"/>: the returned share carries the same share of discount and fees, and tax at the invoice's rate
    /// (BR-025). When the credit note returns everything still open (<paramref name="returnsEverything"/>) it takes exactly what is
    /// left of each component, so the invoice's returned amounts equal its totals and no rounding difference remains (BR-027).
    /// </summary>
    public static CreditTotals Credit(decimal returnedLines, decimal invoiceLines, InvoiceTotals invoice, decimal taxRate, bool taxInclusive, bool returnsEverything,
        ReturnedSoFar before, int decimals = 3)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(before);
        if (returnsEverything)
        {
            var sub = invoice.Subtotal - before.Subtotal;
            var dis = invoice.Discount - before.DiscountShare;
            var fee = invoice.ExtraFees - before.FeesShare;
            var tx = invoice.Tax - before.Tax;
            return new CreditTotals(sub, dis, fee, sub - dis + fee, tx, invoice.Total - before.Total);
        }
        // Shares of the invoice's stored (net) discount and fees; returned lines converted to net for an inclusive invoice.
        var share = invoiceLines == 0 ? 0m : returnedLines / invoiceLines;
        var discountShare = Money.Round(invoice.Discount * share, decimals);
        var feesShare = Money.Round(invoice.ExtraFees * share, decimals);
        var subtotal = taxInclusive ? Money.Round(returnedLines / (1m + taxRate / 100m), decimals) : returnedLines;
        var baseAmount = subtotal - discountShare + feesShare;
        var tax = Money.Round(baseAmount * taxRate / 100m, decimals);
        return new CreditTotals(subtotal, discountShare, feesShare, baseAmount, tax, baseAmount + tax);
    }

    /// <summary>
    /// BR-027 with payments: a credit note whose proportional total exceeds the remaining balance only by rounding (up to
    /// <paramref name="tolerance"/>) takes exactly the remaining balance, the difference absorbed in its tax (review M2).
    /// </summary>
    public static CreditTotals CapToRemaining(CreditTotals c, decimal remaining, decimal tolerance)
    {
        ArgumentNullException.ThrowIfNull(c);
        var over = c.Total - remaining;
        return over > 0 && over <= tolerance && c.Tax >= over ? c with { Tax = c.Tax - over, Total = remaining } : c;
    }

    /// <summary>Line amount = quantity × unit price, half-up (BR-021).</summary>
    public static decimal LineAmount(decimal quantity, decimal unitPrice, int decimals = 3) => Money.Round(quantity * unitPrice, decimals);
}
