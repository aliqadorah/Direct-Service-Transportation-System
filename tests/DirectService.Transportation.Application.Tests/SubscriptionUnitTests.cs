using DirectService.Transportation.Application.Platform;

namespace DirectService.Transportation.Application.Tests;

/// <summary>Owner decision 13 Oct 2026: plan fees are tax-inclusive; net and tax are derived backwards and always add up to the fee.</summary>
public sealed class SubscriptionUnitTests
{
    [Theory]
    [InlineData("50", "43.103", "6.897")]
    [InlineData("100", "86.207", "13.793")]
    [InlineData("29.990", "25.853", "4.137")]
    [InlineData("0.010", "0.009", "0.001")]
    [InlineData("0", "0", "0")]
    public void Tax_inclusive_fee_splits_into_net_and_tax_that_add_up_exactly(string fee, string net, string tax)
    {
        var gross = decimal.Parse(fee, System.Globalization.CultureInfo.InvariantCulture);
        var (n, t) = BillingService.Split(gross, 16m, 3);
        Assert.Equal(decimal.Parse(net, System.Globalization.CultureInfo.InvariantCulture), n);
        Assert.Equal(decimal.Parse(tax, System.Globalization.CultureInfo.InvariantCulture), t);
        Assert.Equal(gross, n + t);
    }

    [Fact]
    public void Every_fee_in_fils_adds_up_and_a_negative_amount_is_zero()
    {
        for (var fils = 0; fils <= 200_000; fils += 7)
        {
            var gross = fils / 1000m;
            var (n, t) = BillingService.Split(gross, 16m, 3);
            Assert.Equal(gross, n + t);
            Assert.True(t >= 0 && n >= 0);
        }
        Assert.Equal((0m, 0m), BillingService.Split(-5m, 16m, 3));
    }
}
