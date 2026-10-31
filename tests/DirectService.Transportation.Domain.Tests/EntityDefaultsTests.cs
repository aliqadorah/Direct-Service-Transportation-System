using DirectService.Accounting.Domain.Acc;
using DirectService.Accounting.Domain.Cfg;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Domain.Tests;

/// <summary>
/// The application always sends column values (DT-10), so new entities must start with the design's
/// defaults (Database design rev. 4 + rev. 5).
/// </summary>
public sealed class EntityDefaultsTests
{
    [Fact]
    public void Revision_5_defaults()
    {
        Assert.Equal("Claim", new Invoice().LayoutType);
        Assert.Equal("Active", new Company().Status);
        var t = new Tenant();
        Assert.Equal(8, t.PasswordMinLength);
        Assert.True(t.PasswordRequireLetter);
        Assert.True(t.PasswordRequireDigit);
        Assert.Equal(30, t.SessionIdleTimeoutMinutes);
    }

    [Fact]
    public void Market_defaults_for_the_launch_market()
    {
        var c = new Company();
        Assert.Equal("JO", c.CountryCode);
        Assert.Equal("JOD", c.CurrencyCode);
        Assert.Equal(3, c.CurrencyDecimals);
        Assert.Equal(16.00m, c.TaxRate);
        Assert.False(c.TaxInclusive);
        Assert.Equal("Asia/Amman", c.TimeZone);
    }

    [Fact]
    public void Status_and_workflow_defaults()
    {
        Assert.Equal("Trial", new Tenant().Status);
        Assert.Equal("Monthly", new Tenant().BillingCycle);
        Assert.Equal("Active", new User().Status);
        Assert.Equal("ar", new User().LanguageCode);
        Assert.Equal("dd/MM/yyyy", new User().DateFormat);
        Assert.Equal("Draft", new Invoice().Status);
        Assert.Equal("Active", new Trip().Status);
        Assert.Equal("Posted", new CreditNote().Status);
        Assert.Equal("Posted", new JournalEntry().Status);
        Assert.Equal("Open", new Period().Status);
        Assert.Equal("Queued", new EInvoiceSubmission().Status);
        Assert.Equal("Requested", new SupportAccessGrant().Status);
        Assert.Equal("Strict", new ImportBatch().Mode);
        Assert.Equal("Cash", new Driver().PaymentMethod);
        Assert.Equal("ton", new Material().Unit);
        Assert.Equal("LoadingSite", new InvoiceLayout().Grouping);
        Assert.Equal("JoFotara", new EInvoiceSettings().Connector);
        Assert.Equal("Test", new EInvoiceSettings().Environment);
        Assert.Equal((byte)5, new NumberSequence().Digits);
        Assert.Equal(1, new NumberSequence().NextNumber);
        Assert.True(new Plan().IsActive);
        Assert.True(new Account().IsPostable);
        Assert.True(new InvoiceLayout().ShowLogo && new InvoiceLayout().ShowQr);
    }

    [Fact]
    public void Every_auditable_entity_gets_a_random_public_id()
    {
        var a = new Trip();
        var b = new Trip();
        Assert.NotEqual(Guid.Empty, a.PublicId);
        Assert.NotEqual(a.PublicId, b.PublicId);
        Assert.Equal(4, a.PublicId.Version); // random (v4) — not sequential (SEC-22)
    }
}
