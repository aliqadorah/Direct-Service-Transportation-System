using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Infrastructure.EInvoicing;
using DirectService.Transportation.Web.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B7 national e-invoicing through the API with the simulator connector: settings with write-only encrypted credentials (FR-059),
/// queueing at approval with the counter (FR-043), automatic and manual submission (FR-061), identifiers and QR (FR-062), rejection and
/// resubmission, failures to Rejected (FR-063, D-04), credit notes only after the invoice is accepted (FR-064), voids, report (FR-065).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class EInvoiceTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1);

    private static EInvoiceSettingsRequest Simulator(bool enabled = true, string? rowVersion = null, string? clientId = "client-77", string? secret = "s3cret-77") =>
        new(enabled, "Simulator", "12345678", "Sales", "4569874", clientId, secret, "Test", rowVersion);

    /// <summary>A company with the chart of accounts, e-invoicing on (simulator) and a customer.</summary>
    private async Task<(SignedInUser Owner, CustomerResponse Customer)> WorldAsync(string customerName = "Arab Potash", bool enable = true)
    {
        var o = await f.SignUpOwnerAsync();
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        if (enable)
        {
            // Provisioned with the company (disabled): saving needs its row version.
            var current = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/e-invoicing")));
            await (await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"), Simulator(rowVersion: current.RowVersion))).EnsureOkAsync();
        }
        var customer = await ReadAsync<CustomerResponse>(await o.Client.PostAsJsonAsync(U(o, "/customers"),
            new CustomerRequest(customerName, "200014587", null, "0781234567", null, null, "Aqaba", null, 30, null, null, null, null, null)));
        return (o, customer);
    }

    private static async Task<InvoiceDetails> InvoiceAsync(SignedInUser o, CustomerResponse c, decimal price = 350m) =>
        await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), new InvoiceRequest(c.Id, "Manual", Today, null, null,
            [new ManualLineRequest("Equipment move", 2, price), new ManualLineRequest("Night loading", 1, 170m)], 10m, 5m, "Escort", "TaxInvoice", null, true, null, null)));

    private static async Task<EInvoiceSubmissionDetails> OfInvoiceAsync(SignedInUser o, string invoiceId) =>
        await ReadAsync<EInvoiceSubmissionDetails>(await o.Client.GetAsync(U(o, $"/invoices/{invoiceId}/e-invoice")));

    private static async Task<EInvoiceSubmissionDetails> SubmitAsync(SignedInUser o, string submissionId) =>
        await ReadAsync<EInvoiceSubmissionDetails>(await o.Client.PostAsync(U(o, $"/einvoice-submissions/{submissionId}/submit"), null));

    private Task<int> RunWorkerAsync() =>
        new EInvoiceWorker(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Services.GetRequiredService<EInvoiceSignal>(), NullLogger<EInvoiceWorker>.Instance)
            .RunOnceAsync(CancellationToken.None);

    // ------------------------------------------------------------------ settings (FR-059, SEC-09)

    [Fact]
    public async Task Special_sales_tax_cannot_be_saved_and_the_two_offered_types_can_DT223()
    {
        var (o, _) = await WorldAsync(enable: false);
        var current = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/e-invoicing")));
        var special = await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"), Simulator(rowVersion: current.RowVersion) with { RegistrationType = EInvoiceCodes.Special });
        Assert.Equal(HttpStatusCode.BadRequest, special.StatusCode);
        var text = await special.Content.ReadAsStringAsync();
        Assert.Contains(ErrorCodes.EInvoiceSpecialNotSupported, text, StringComparison.Ordinal);
        Assert.Contains("registrationType", text, StringComparison.Ordinal);
        Assert.Null(await f.DbAsync(db => db.Set<EInvoiceSettings>().IgnoreQueryFilters().Where(s => s.RegistrationType == EInvoiceCodes.Special).Select(s => (long?)s.Id).FirstOrDefaultAsync()));
        foreach (var type in new[] { EInvoiceCodes.Income, EInvoiceCodes.Sales })
        {
            var saved = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"),
                Simulator(rowVersion: current.RowVersion) with { RegistrationType = type }));
            Assert.Equal(type, saved.RegistrationType);
            current = saved;
        }
    }

    [Fact]
    public async Task Income_tax_can_set_the_company_rate_to_zero_in_the_same_save_and_declining_keeps_it_DT226()
    {
        var (o, c) = await WorldAsync(enable: false);
        var draft = await InvoiceAsync(o, c);   // made at 16%: existing documents keep their rate
        var current = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/e-invoicing")));
        Assert.Equal(16m, current.CompanyTaxRate);

        // Only with income tax.
        var wrong = await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"), Simulator(enabled: false, rowVersion: current.RowVersion) with { SetTaxRateToZero = true });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("setTaxRateToZero", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Declined: income tax saved, the rate stays.
        current = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"),
            Simulator(enabled: false, rowVersion: current.RowVersion) with { RegistrationType = EInvoiceCodes.Income }));
        Assert.Equal(EInvoiceCodes.Income, current.RegistrationType);
        Assert.Equal(16m, current.CompanyTaxRate);
        Assert.Equal(16m, (await ReadAsync<MarketSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/market")))).TaxRate);

        // Accepted: one save, rate 0%, audited as a company setting change with the reason; the draft keeps 16%.
        current = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"),
            Simulator(enabled: false, rowVersion: current.RowVersion) with { RegistrationType = EInvoiceCodes.Income, SetTaxRateToZero = true }));
        Assert.Equal(0m, current.CompanyTaxRate);
        Assert.Equal(0m, (await ReadAsync<MarketSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/market")))).TaxRate);
        Assert.Equal(16m, (await ReadAsync<InvoiceDetails>(await o.Client.GetAsync(U(o, $"/invoices/{draft.Invoice.Id}")))).Invoice.TaxRate);
        var audit = await f.DbAsync(async db =>
        {
            var pid = f.Services.GetRequiredService<IUrlTokenService>().Decode<DirectService.Platform.Domain.Company>(o.CompanyId)!.Value;
            var id = await db.Set<DirectService.Platform.Domain.Company>().IgnoreQueryFilters().Where(x => x.PublicId == pid).Select(x => x.Id).SingleAsync();
            return await db.Set<AuditLog>().IgnoreQueryFilters()
                .Where(a => a.EntityType == "CORE.Company" && a.EntityId == id && a.Action == "SettingChange").Select(a => new { a.OldValues, a.NewValues }).ToListAsync();
        });
        var change = Assert.Single(audit, a => a.NewValues != null && a.NewValues.Contains("TaxRate", StringComparison.Ordinal));
        Assert.Contains("16", change.OldValues, StringComparison.Ordinal);
        Assert.Contains("E-invoicing: income tax", change.NewValues + change.OldValues, StringComparison.Ordinal);

        // Already 0%: asking again is accepted and changes nothing.
        await (await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"),
            Simulator(enabled: false, rowVersion: current.RowVersion) with { RegistrationType = EInvoiceCodes.Income, SetTaxRateToZero = true })).EnsureOkAsync();
    }

    [Fact]
    public async Task Settings_store_credentials_encrypted_write_only_and_require_values_when_enabled()
    {
        var (o, _) = await WorldAsync(enable: false);
        var empty = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/e-invoicing")));
        Assert.False(empty.IsEnabled);
        Assert.Contains("Simulator", empty.Connectors);   // allowed in this environment only

        var refused = await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"), new EInvoiceSettingsRequest(true, "Simulator", null, null, null, null, null, null, empty.RowVersion));
        var text = await refused.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        foreach (var field in new[] { "sellerTaxNumber", "incomeSourceSequence", "clientId", "secretKey" }) Assert.Contains(field, text, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"), Simulator(rowVersion: empty.RowVersion) with { Environment = "Production" })).StatusCode);

        var saved = await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"), Simulator(rowVersion: empty.RowVersion));
        var body = await saved.Content.ReadAsStringAsync();
        await saved.EnsureOkAsync();
        Assert.DoesNotContain("s3cret-77", body, StringComparison.Ordinal);
        Assert.DoesNotContain("client-77", body, StringComparison.Ordinal);
        var settings = (await saved.Content.ReadFromJsonAsync<EInvoiceSettingsResponse>())!;
        Assert.True(settings.HasClientId && settings.HasSecretKey && settings.IsEnabled);
        var stored = await f.DbAsync(db => db.Set<EInvoiceSettings>().IgnoreQueryFilters().OrderByDescending(s => s.Id).FirstAsync());
        Assert.DoesNotContain("s3cret-77", Encoding.UTF8.GetString(stored.SecretKeyEncrypted!), StringComparison.Ordinal);

        // Empty credentials keep the stored ones; the row version is required and must be current.
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"), Simulator(clientId: null, secret: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"), Simulator(rowVersion: empty.RowVersion))).StatusCode);
        var again = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"), Simulator(rowVersion: settings.RowVersion, clientId: null, secret: null)));
        Assert.True(again.HasSecretKey);
        // The audit log never holds the secrets.
        var audit = await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().Where(a => a.EntityType != null && a.EntityType.Contains("EInvoiceSettings")).Select(a => a.NewValues).ToListAsync());
        Assert.All(audit, a => Assert.DoesNotContain("s3cret", a ?? string.Empty, StringComparison.Ordinal));

        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.Client.GetAsync(U(o, "/settings/e-invoicing"))).StatusCode);
    }

    // ------------------------------------------------------------------ submission (FR-060 … FR-063)

    [Fact]
    public async Task Approval_queues_with_the_counter_and_the_worker_submits_with_QR_printed_FR061_FR062()
    {
        var (o, c) = await WorldAsync();
        var inv = await InvoiceAsync(o, c);
        Assert.Equal("Queued", inv.Invoice.EInvoiceStatus);
        var queued = await OfInvoiceAsync(o, inv.Invoice.Id);
        Assert.Equal(1, queued.Submission.Icv);

        await RunWorkerAsync();
        var done = await OfInvoiceAsync(o, inv.Invoice.Id);
        Assert.Equal("Submitted", done.Submission.Status);
        Assert.Equal("SIM-1", done.Submission.SystemNumber);
        Assert.True(done.Submission.HasQr);
        Assert.NotNull(done.Submission.SubmittedAt);
        Assert.Contains("INV-00001", done.RequestPayload!, StringComparison.Ordinal);
        Assert.Contains("SUBMITTED", done.ResponsePayload!, StringComparison.Ordinal);
        Assert.Equal("Submitted", (await ReadAsync<InvoiceDetails>(await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}")))).Invoice.EInvoiceStatus);
        // QR on the printout (FR-062).
        var html = await (await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("<svg", html, StringComparison.Ordinal);
        // Accepted: void refused, cancel by credit note only (FR-046); a second submission is refused.
        Assert.Equal("invoice.eInvoiced", await (await o.Client.PostAsJsonAsync(U(o, $"/invoices/{inv.Invoice.Id}/void"), new InvoiceVoidRequest("x", inv.Invoice.RowVersion))).ProblemCodeAsync());
        Assert.Equal("einvoice.notSubmittable", await (await o.Client.PostAsync(U(o, $"/einvoice-submissions/{done.Submission.Id}/submit"), null)).ProblemCodeAsync());

        // The next document takes the next counter.
        var second = await InvoiceAsync(o, c, 400m);
        Assert.Equal(2, (await OfInvoiceAsync(o, second.Invoice.Id)).Submission.Icv);
    }

    [Fact]
    public async Task Credit_note_waits_for_the_invoice_then_references_it_FR064()
    {
        var (o, c) = await WorldAsync();
        var inv = await InvoiceAsync(o, c);
        var invSub = await OfInvoiceAsync(o, inv.Invoice.Id);
        var cn = await ReadAsync<CreditNoteDetails>(await o.Client.PostAsJsonAsync(U(o, "/credit-notes"),
            new CreditNoteRequest(inv.Invoice.Id, Today, "Returned equipment move", [new CreditLineRequest(inv.Lines[0].Id, 1)], null)));
        var cnSub = await ReadAsync<EInvoiceSubmissionDetails>(await o.Client.GetAsync(U(o, $"/credit-notes/{cn.CreditNote.Id}/e-invoice")));
        Assert.Equal(2, cnSub.Submission.Icv);

        // Before the invoice is accepted the credit note stays queued, waiting.
        var waiting = await SubmitAsync(o, cnSub.Submission.Id);
        Assert.Equal("Queued", waiting.Submission.Status);
        Assert.True(waiting.Submission.WaitingForInvoice);
        Assert.Contains(waiting.Submission.Problems, p => p.Code == "einvoice.waitingForInvoice" && p.Message.Length > 10);

        await SubmitAsync(o, invSub.Submission.Id);
        var sent = await SubmitAsync(o, cnSub.Submission.Id);
        Assert.Equal("Submitted", sent.Submission.Status);
        Assert.Equal(inv.Invoice.Number, sent.OriginalInvoice!.Number);
        Assert.Contains("<cbc:InvoiceTypeCode name=\"022\">381</cbc:InvoiceTypeCode>", sent.RequestPayload!, StringComparison.Ordinal);
        Assert.Contains("BillingReference", sent.RequestPayload!, StringComparison.Ordinal);
        Assert.Contains("Returned equipment move", sent.RequestPayload!, StringComparison.Ordinal);
        // An accepted credit note cannot be voided.
        Assert.Equal("credit.eInvoiced", await (await o.Client.PostAsJsonAsync(U(o, $"/credit-notes/{cn.CreditNote.Id}/void"),
            new CreditNoteVoidRequest("x", cn.CreditNote.RowVersion))).ProblemCodeAsync());
    }

    [Fact]
    public async Task Rejected_documents_are_corrected_and_resubmitted_and_failures_end_Rejected_D04()
    {
        var (o, c) = await WorldAsync("Arab Potash [reject]");
        var inv = await InvoiceAsync(o, c);
        var sub = (await OfInvoiceAsync(o, inv.Invoice.Id)).Submission;
        var rejected = await SubmitAsync(o, sub.Id);
        Assert.Equal("Rejected", rejected.Submission.Status);
        Assert.Contains("Simulated rejection", rejected.Submission.LastError, StringComparison.Ordinal);
        // The worker does not retry a rejection; the attention list shows it (FR-061).
        await RunWorkerAsync();
        Assert.Equal("Rejected", (await OfInvoiceAsync(o, inv.Invoice.Id)).Submission.Status);
        Assert.Contains((await ReadAsync<EInvoicePage>(await o.Client.GetAsync(U(o, "/einvoice-submissions?status=attention")))).Page.Items, i => i.Id == sub.Id);

        // Correct the customer, resubmit: the document is rebuilt with the same UUID and counter.
        var customer = await ReadAsync<CustomerResponse>(await o.Client.GetAsync(U(o, $"/customers/{c.Id}")));
        await (await o.Client.PutAsJsonAsync(U(o, $"/customers/{c.Id}"), new CustomerRequest("Arab Potash", "200014587", null, "0781234567", null, null, "Aqaba", null, 30,
            null, null, null, null, customer.RowVersion))).EnsureOkAsync();
        var accepted = await SubmitAsync(o, sub.Id);
        Assert.Equal("Submitted", accepted.Submission.Status);
        Assert.Equal(sub.Icv, accepted.Submission.Icv);

        // Technical failures: retried, then Rejected with the last error after the maximum attempts (2 in tests).
        var (o2, c2) = await WorldAsync("Jordan Phosphate [fail]");
        var inv2 = await InvoiceAsync(o2, c2);
        var s2 = (await OfInvoiceAsync(o2, inv2.Invoice.Id)).Submission;
        var first = await SubmitAsync(o2, s2.Id);
        Assert.Equal("Queued", first.Submission.Status);
        Assert.Equal(1, first.Submission.Attempts);
        Assert.Contains("503", first.Submission.LastError, StringComparison.Ordinal);
        // The request may have arrived: no void until a resubmission tells (review H1); no connector switch while pending (M2).
        Assert.Equal("einvoice.outcomeUnknown", await (await o2.Client.PostAsJsonAsync(U(o2, $"/invoices/{inv2.Invoice.Id}/void"),
            new InvoiceVoidRequest("x", inv2.Invoice.RowVersion))).ProblemCodeAsync());
        var cfg = await ReadAsync<EInvoiceSettingsResponse>(await o2.Client.GetAsync(U(o2, "/settings/e-invoicing")));
        Assert.Equal("einvoice.pendingDocuments", await (await o2.Client.PutAsJsonAsync(U(o2, "/settings/e-invoicing"),
            Simulator(rowVersion: cfg.RowVersion, clientId: null, secret: null) with { Connector = "JoFotara" })).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await o2.Client.PutAsJsonAsync(U(o2, "/settings/e-invoicing"),
            Simulator(rowVersion: cfg.RowVersion, secret: "bad secret"))).StatusCode);   // header-safe credentials (M5)
        var second = await SubmitAsync(o2, s2.Id);
        Assert.Equal("Rejected", second.Submission.Status);
        Assert.Equal(2, second.Submission.Attempts);
    }

    [Fact]
    public async Task Problems_keep_documents_queued_voids_cancel_and_disabled_companies_do_not_queue()
    {
        var (o, c) = await WorldAsync();
        var inv = await InvoiceAsync(o, c);
        var sub = (await OfInvoiceAsync(o, inv.Invoice.Id)).Submission;
        // Switched off after queueing: the document stays queued with the problem listed (FR-060, 9.7).
        var settings = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/e-invoicing")));
        await (await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"), Simulator(enabled: false, rowVersion: settings.RowVersion, clientId: null, secret: null))).EnsureOkAsync();
        var problem = await SubmitAsync(o, sub.Id);
        Assert.Equal("Queued", problem.Submission.Status);
        Assert.Equal("einvoice.notEnabled", Assert.Single(problem.Submission.Problems).Code);
        Assert.Null(problem.Submission.LastError);

        // A new invoice of a disabled company is not queued.
        var plain = await InvoiceAsync(o, c, 400m);
        Assert.Equal("NotRequired", plain.Invoice.EInvoiceStatus);
        Assert.Equal(HttpStatusCode.NotFound, (await o.Client.GetAsync(U(o, $"/invoices/{plain.Invoice.Id}/e-invoice"))).StatusCode);

        // A credit note on an invoice that was never e-invoiced is not queued either (review H2).
        var cnPlain = await ReadAsync<CreditNoteDetails>(await o.Client.PostAsJsonAsync(U(o, "/credit-notes"),
            new CreditNoteRequest(plain.Invoice.Id, Today, "Returned", [new CreditLineRequest(plain.Lines[0].Id, 1)], null)));
        settings = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/e-invoicing")));
        await (await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"), Simulator(rowVersion: settings.RowVersion, clientId: null, secret: null))).EnsureOkAsync();
        var cnPlain2 = await ReadAsync<CreditNoteDetails>(await o.Client.PostAsJsonAsync(U(o, "/credit-notes"),
            new CreditNoteRequest(plain.Invoice.Id, Today, "Returned again", [new CreditLineRequest(plain.Lines[1].Id, null)], null)));
        Assert.Equal("NotRequired", cnPlain.CreditNote.EInvoiceStatus);
        Assert.Equal("NotRequired", cnPlain2.CreditNote.EInvoiceStatus);

        // Void of a queued invoice cancels its submission.
        var current = await ReadAsync<InvoiceDetails>(await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}")));
        await (await o.Client.PostAsJsonAsync(U(o, $"/invoices/{inv.Invoice.Id}/void"), new InvoiceVoidRequest("Wrong customer", current.Invoice.RowVersion))).EnsureOkAsync();
        var cancelled = await OfInvoiceAsync(o, inv.Invoice.Id);
        Assert.Equal("NotRequired", cancelled.Submission.Status);
        Assert.Equal("Wrong customer", cancelled.Submission.LastError);
    }

    // ------------------------------------------------------------------ report, permissions, isolation (FR-065)

    [Fact]
    public async Task Report_totals_by_status_export_permissions_and_isolation_FR065()
    {
        var (o, c) = await WorldAsync();
        var a = await InvoiceAsync(o, c);
        var b = await InvoiceAsync(o, c, 400m);
        await SubmitAsync(o, (await OfInvoiceAsync(o, a.Invoice.Id)).Submission.Id);
        var period = $"from={Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        var report = await ReadAsync<EInvoicePage>(await o.Client.GetAsync(U(o, $"/einvoice-submissions?{period}")));
        Assert.Equal(2, report.Page.TotalCount);
        Assert.Equal(a.Invoice.Total, report.Totals.Single(t => t.Status == "Submitted").Total);
        Assert.Equal(b.Invoice.Total, report.Totals.Single(t => t.Status == "Queued").Total);
        Assert.Single((await ReadAsync<EInvoicePage>(await o.Client.GetAsync(U(o, $"/einvoice-submissions?status=queued&{period}")))).Page.Items);
        var export = await o.Client.GetAsync(U(o, $"/einvoice-submissions/export?{period}"));
        await export.EnsureOkAsync();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", export.Content.Headers.ContentType!.MediaType);

        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        await (await viewer.Client.GetAsync(U(o, "/einvoice-submissions"))).EnsureOkAsync();
        var bSub = (await OfInvoiceAsync(o, b.Invoice.Id)).Submission.Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsync(U(o, $"/einvoice-submissions/{bSub}/submit"), null)).StatusCode);
        // reports.view opens every report (PRD 3.4), the e-invoicing report included; the document itself needs inv.view.
        await (await dispatcher.Client.GetAsync(U(o, "/einvoice-submissions"))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.Client.GetAsync(U(o, $"/einvoice-submissions/{bSub}"))).StatusCode);
        var other = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/einvoice-submissions/{bSub}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync(U(other, $"/einvoice-submissions/{bSub}/submit"), null)).StatusCode);
    }
}
