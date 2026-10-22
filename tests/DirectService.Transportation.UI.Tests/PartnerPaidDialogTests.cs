using DirectService.Transportation.Web.Client.Pages.Trips;
using DirectService.Transportation.Web.Client.Services;
using DirectService.Transportation.Web.Client.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner decision 21 Oct 2026 on DT-228 (DT-230): the dialog for a partner trip already paid — the warning with the amount the partner would
/// owe, the three choices with their explanations, a reason for (a) and (b), and (c) leading to the payment voucher (with a warning when the
/// payment covered other trips too). Arabic and English.
/// </summary>
public sealed class PartnerPaidDialogTests : PageTestBase
{
    private (string Handling, string Reason)? _chosen;

    private IRenderedComponent<PartnerPaidDialog> Dialog(decimal paymentAmount, string? reason = "Load cancelled") =>
        Render<PartnerPaidDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Company, Company)
            .Add(x => x.TripId, "t9")
            .Add(x => x.Reason, reason)
            .Add(x => x.Problem, new ApiProblem { Code = "trip.partnerPaid", Status = 409, Overpaid = 88m, TripCost = 88m, PaymentId = "pp1", PaymentNumber = "PPY-00004", PaymentAmount = paymentAmount })
            .Add(x => x.OnChoose, EventCallback.Factory.Create<(string, string)>(this, c => _chosen = c)));

    private void Choose(IRenderedComponent<PartnerPaidDialog> cut, string value) => Act(() => cut.Find($"input[type=radio][value={value}]").Change(value));

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<PartnerPaidDialog> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains(text, StringComparison.Ordinal));

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void The_warning_and_the_three_choices_are_shown_with_their_explanations(string language)
    {
        L.SetLanguage(language);
        var cut = Dialog(88m);
        var text = Text(cut);
        Assert.Contains(L.F("trip.paidWarn", Fmt.Money(88m)), text);
        foreach (var (label, help) in new[] { ("trip.paidReturn", "trip.paidReturnHelp"), ("trip.paidDeduct", "trip.paidDeductHelp"), ("trip.paidNotPaid", "trip.paidNotPaidHelp") })
        {
            Assert.Contains(L[label], text);
            Assert.Contains(L.F(help, Fmt.Money(88m)), text);
        }
        Assert.Equal(3, cut.FindAll("input[type=radio]").Count);
        Assert.True(Button(cut, L["trip.paidConfirm"]).HasAttribute("disabled"));   // nothing chosen yet
    }

    [Theory]
    [InlineData("Return")]
    [InlineData("Deduct")]
    public void Return_or_deduct_needs_a_reason_and_is_sent_with_it(string handling)
    {
        L.SetLanguage("en");
        var cut = Dialog(88m, reason: null);
        Choose(cut, handling);
        Assert.True(Button(cut, L["trip.paidConfirm"]).HasAttribute("disabled"));   // no reason yet
        Act(() => cut.Find("#paid-reason").Input("Customer cancelled the load"));
        Act(() => Button(cut, L["trip.paidConfirm"]).Click());
        Assert.Equal((handling, "Customer cancelled the load"), _chosen);
    }

    [Fact]
    public void Not_actually_paid_leads_to_the_payment_voucher_and_warns_when_it_covered_other_trips()
    {
        L.SetLanguage("en");
        var single = Dialog(88m);
        Choose(single, "NotPaid");
        Assert.Contains(L.F("trip.paidPayment", "PPY-00004", Fmt.Money(88m)), Text(single));
        Assert.DoesNotContain(L["trip.paidSeveral"], Text(single));
        Assert.Empty(single.FindAll("#paid-reason"));   // the payment is voided with its own reason
        Act(() => Button(single, L["trip.paidGoToPayment"]).Click());
        Assert.EndsWith($"/c/{Company}/partner-accounts/payments/pp1?void=1&trip=t9", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
        Assert.Null(_chosen);

        var several = Dialog(250m);
        Choose(several, "NotPaid");
        Assert.Contains(L["trip.paidSeveral"], Text(several));
    }
}
