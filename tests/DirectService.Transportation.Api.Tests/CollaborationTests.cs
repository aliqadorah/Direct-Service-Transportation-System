using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Infrastructure.EInvoicing;
using DirectService.Transportation.Web.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DirectService.Transportation.Application.Collaboration;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B11 collaboration: live record changes after commit (FR-185), global search with Arabic variants and permissions (FR-187), notifications
/// (FR-190), the audit log viewer and its export (FR-188, FR-189), background exports (FR-151).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CollaborationTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    private static CustomerRequest Customer(string name) => new(name, "200014587", null, "0781234567", null, null, "Aqaba", null, 30, null, null, null, null, null);

    [Fact]
    public async Task Changes_are_announced_after_commit_FR185()
    {
        var o = await f.SignUpOwnerAsync();
        var customer = await ReadAsync<CustomerResponse>(await o.Client.PostAsJsonAsync(U(o, "/customers"), Customer("Live Customer Co")));
        Assert.Contains(f.Live.Records, r => r.EntityType == "TRN.Customer" && r.Token == customer.Id && r.Action == "Created");
        // A refused save announces nothing (duplicate name → no new record).
        var before = f.Live.Records.Count(r => r.EntityType == "TRN.Customer" && r.Action == "Created");
        Assert.False((await o.Client.PostAsJsonAsync(U(o, "/customers"), Customer("Live Customer Co"))).IsSuccessStatusCode);
        Assert.Equal(before, f.Live.Records.Count(r => r.EntityType == "TRN.Customer" && r.Action == "Created"));
    }

    [Fact]
    public async Task Global_search_folds_Arabic_variants_and_respects_permissions_FR187()
    {
        var o = await f.SignUpOwnerAsync();
        var c = o.Client;
        await (await c.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        var customer = await ReadAsync<CustomerResponse>(await c.PostAsJsonAsync(U(o, "/customers"), Customer("مؤسسة الأمانة للنقل")));
        var inv = await ReadAsync<InvoiceDetails>(await c.PostAsJsonAsync(U(o, "/invoices"), new InvoiceRequest(customer.Id, "Manual", Today, null, null,
            [new ManualLineRequest("Equipment move", 1, 100m)], 0, 0, null, null, null, true, null, null)));

        var variant = await ReadAsync<SearchResponse>(await c.GetAsync(U(o, "/search?q=" + Uri.EscapeDataString("مؤسسه الامانه"))));
        Assert.Contains(variant.Hits, h => h.Type == "customer" && h.Id == customer.Id);
        var byNumber = await ReadAsync<SearchResponse>(await c.GetAsync(U(o, "/search?q=" + Uri.EscapeDataString(inv.Invoice.Number!))));
        Assert.Contains(byNumber.Hits, h => h.Type == "invoice" && h.Title == inv.Invoice.Number);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(U(o, "/search?q=x"))).StatusCode);

        // A Dispatcher (no inv.view) finds the customer but not the invoice.
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        var limited = await ReadAsync<SearchResponse>(await dispatcher.Client.GetAsync(U(o, "/search?q=" + Uri.EscapeDataString(inv.Invoice.Number!))));
        Assert.DoesNotContain(limited.Hits, h => h.Type == "invoice");
        var other = await f.SignUpOwnerAsync();
        Assert.Empty((await ReadAsync<SearchResponse>(await other.Client.GetAsync(U(other, "/search?q=" + Uri.EscapeDataString("الأمانة"))))).Hits);
    }

    [Fact]
    public async Task Rejected_e_invoice_notifies_and_audit_log_is_read_only_FR188_FR190()
    {
        var o = await f.SignUpOwnerAsync();
        var c = o.Client;
        await (await c.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        var settings = await ReadAsync<EInvoiceSettingsResponse>(await c.GetAsync(U(o, "/settings/e-invoicing")));
        await (await c.PutAsJsonAsync(U(o, "/settings/e-invoicing"),
            new EInvoiceSettingsRequest(true, "Simulator", "12345678", "Sales", "4569874", "client-1", "secret-1", "Test", settings.RowVersion))).EnsureOkAsync();
        var customer = await ReadAsync<CustomerResponse>(await c.PostAsJsonAsync(U(o, "/customers"), Customer("Refusing Co [reject]")));
        var invoice = await ReadAsync<InvoiceDetails>(await c.PostAsJsonAsync(U(o, "/invoices"), new InvoiceRequest(customer.Id, "Manual", Today, null, null,
            [new ManualLineRequest("Equipment move", 1, 100m)], 0, 0, null, null, null, true, null, null)));
        await RunEInvoiceWorkerAsync();

        var page = await ReadAsync<NotificationPage>(await c.GetAsync(U(o, "/notifications")));
        var n = Assert.Single(page.Items, x => x.Type == "EINV.Rejected");
        // DT-189: the link opens the invoice in the company named by its URL token (never its name); codes stay in the details.
        Assert.Equal("invoices/" + invoice.Invoice.Id, n.Link);
        Assert.Equal(o.CompanyId, n.Company);
        Assert.Null(n.LinkProblem);
        Assert.Equal("E-invoice rejected", n.Title);
        Assert.Contains(invoice.Invoice.Number!, n.Message, StringComparison.Ordinal);
        Assert.Contains("E-INV-BUYER", n.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("E-INV-BUYER", n.Message, StringComparison.Ordinal);
        Assert.True(page.Unread >= 1);
        await (await c.PostAsync(U(o, $"/notifications/{n.Id}/read"), null)).EnsureOkAsync();
        Assert.NotNull((await ReadAsync<NotificationPage>(await c.GetAsync(U(o, "/notifications")))).Items.Single(x => x.Id == n.Id).ReadAt);
        await (await c.PostAsync(U(o, "/notifications/read-all"), null)).EnsureOkAsync();
        Assert.Equal(0, (await ReadAsync<NotificationPage>(await c.GetAsync(U(o, "/notifications?unread=true")))).Unread);

        // Audit log: the customer's creation with its name (never an internal id), filters, export recorded as an action.
        var log = await ReadAsync<PagedResult<AuditLogEntry>>(await c.GetAsync(U(o, "/audit-log?entityType=TRN.Customer")));
        Assert.Contains(log.Items, e => e.Action == "Create" && e.Record == "Refusing Co [reject]");
        await (await c.GetAsync(U(o, "/audit-log/export?format=csv"))).EnsureOkAsync();
        var exports = await ReadAsync<PagedResult<AuditLogEntry>>(await c.GetAsync(U(o, "/audit-log?action=Export")));
        Assert.Contains(exports.Items, e => e.Changes.Any(ch => ch.Field == "route" && ch.New!.Contains("audit-log/export", StringComparison.Ordinal)));
        var facets = await c.GetAsync(U(o, "/audit-log/facets"));
        await facets.EnsureOkAsync();
        // FR-189 user filter: the people of the log are offered with their tokens, and the token filters the log.
        using var facetJson = JsonDocument.Parse(await facets.Content.ReadAsStringAsync());
        var me = facetJson.RootElement.GetProperty("users").EnumerateArray().Single(u => u.GetProperty("name").GetString() == o.Me.FullName);
        var mine = await ReadAsync<PagedResult<AuditLogEntry>>(await c.GetAsync(U(o, "/audit-log?user=" + Uri.EscapeDataString(me.GetProperty("id").GetString()!))));
        Assert.NotEmpty(mine.Items);
        Assert.All(mine.Items, e => Assert.Equal(o.Me.FullName, e.User));
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.GetAsync(U(o, "/audit-log"))).StatusCode);
        Assert.Empty((await ReadAsync<NotificationPage>(await viewer.Client.GetAsync(U(o, "/notifications")))).Items);   // notifications are personal
    }

    private Task<int> RunEInvoiceWorkerAsync() =>
        new EInvoiceWorker(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Services.GetRequiredService<EInvoiceSignal>(), NullLogger<EInvoiceWorker>.Instance)
            .RunOnceAsync(CancellationToken.None);

    private static async Task EnableEInvoicingAsync(SignedInUser o, string clientId)
    {
        var settings = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/e-invoicing")));
        await (await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"),
            new EInvoiceSettingsRequest(true, "Simulator", "12345678", "Sales", "4569874", clientId, "secret-1", "Test", settings.RowVersion))).EnsureOkAsync();
    }

    private static async Task<InvoiceDetails> IssueInvoiceAsync(SignedInUser o, string customerId) =>
        await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), new InvoiceRequest(customerId, "Manual", Today, null, null,
            [new ManualLineRequest("Equipment move", 1, 100m)], 0, 0, null, null, null, true, null, null)));

    private static async Task<NotificationPage> NotesAsync(SignedInUser o, string language = "en")
    {
        // The reader's language is their saved preference.
        await (await o.Client.PutAsJsonAsync("/api/v1/me/preferences", new PreferencesRequest(language, null, o.Me.DateFormat))).EnsureOkAsync();
        return await ReadAsync<NotificationPage>(await o.Client.GetAsync(U(o, "/notifications")));
    }

    [Fact]
    public async Task Refused_credentials_give_one_setup_notification_counted_and_cleared_on_success_DT189()
    {
        var o = await f.SignUpOwnerAsync();
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        await EnableEInvoicingAsync(o, "client-[refuse]");
        var customer = await ReadAsync<CustomerResponse>(await o.Client.PostAsJsonAsync(U(o, "/customers"), Customer("Normal Buyer Co")));
        var first = await IssueInvoiceAsync(o, customer.Id);
        await IssueInvoiceAsync(o, customer.Id);
        await RunEInvoiceWorkerAsync();

        // One notification for the company (not one per invoice), to e-invoicing settings, with the number of affected invoices.
        var page = await NotesAsync(o);
        Assert.DoesNotContain(page.Items, n => n.Type == NotificationService.EInvoiceRejected);
        var setup = Assert.Single(page.Items, n => n.Type == NotificationService.EInvoiceSetupError);
        Assert.Equal("E-invoicing is not set up correctly", setup.Title);
        Assert.Equal("JoFotara refused the credentials. Open e-invoicing settings. Affected invoices: 2", setup.Message);
        Assert.Equal("settings/e-invoicing", setup.Link);
        Assert.Equal(o.CompanyId, setup.Company);
        Assert.Contains("CORE-401-001", setup.Details, StringComparison.Ordinal);
        Assert.Null(setup.ReadAt);
        var ar = Assert.Single((await NotesAsync(o, "ar")).Items, n => n.Type == NotificationService.EInvoiceSetupError);
        Assert.Equal("الفوترة الإلكترونية غير مضبوطة بشكل صحيح", ar.Title);
        Assert.EndsWith("الفواتير المتأثرة: 2", ar.Message, StringComparison.Ordinal);

        // A further refusal updates the same notification: still one, count 3.
        await IssueInvoiceAsync(o, customer.Id);
        await RunEInvoiceWorkerAsync();
        var again = Assert.Single((await NotesAsync(o)).Items, n => n.Type == NotificationService.EInvoiceSetupError);
        Assert.Equal(setup.Id, again.Id);
        Assert.EndsWith("Affected invoices: 3", again.Message, StringComparison.Ordinal);

        // Credentials fixed and one invoice resubmitted successfully: the notification disappears.
        await EnableEInvoicingAsync(o, "client-1");
        var submission = (await ReadAsync<EInvoiceSubmissionDetails>(await o.Client.GetAsync(U(o, $"/invoices/{first.Invoice.Id}/e-invoice")))).Submission;
        await (await o.Client.PostAsync(U(o, $"/einvoice-submissions/{submission.Id}/submit"), null)).EnsureOkAsync();
        Assert.DoesNotContain((await NotesAsync(o)).Items, n => n.Type == NotificationService.EInvoiceSetupError);
    }

    /// <summary>Adds a notification as an earlier version stored it (text fixed at creation, API-style link).</summary>
    private Task<int> InsertOldAsync(SignedInUser o, string type, string message, string? link, bool company = true) => f.DbAsync(async db =>
    {
        var u = await db.Set<Domain.Core.User>().IgnoreQueryFilters().Where(x => x.Email == o.Email).Select(x => new { x.Id, x.TenantId }).SingleAsync();
        long? companyId = company
            ? await db.Set<Domain.Core.UserCompanyRole>().IgnoreQueryFilters().Where(r => r.UserId == u.Id).Select(r => r.CompanyId).FirstAsync()
            : null;
        return await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO core.Notifications (PublicId, TenantId, UserId, CompanyId, NotificationType, Message, Link, CreatedAt, IsDeleted) VALUES (NEWID(), {u.TenantId}, {u.Id}, {companyId}, {type}, {message}, {link}, SYSUTCDATETIME(), 0)");
    });

    [Fact]
    public async Task Old_stored_notifications_resolve_their_links_and_read_in_the_readers_language_DT189()
    {
        var o = await f.SignUpOwnerAsync();
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        await EnableEInvoicingAsync(o, "client-1");
        var customer = await ReadAsync<CustomerResponse>(await o.Client.PostAsJsonAsync(U(o, "/customers"), Customer("Old Links Co [reject]")));
        var invoice = await IssueInvoiceAsync(o, customer.Id);
        var submission = (await ReadAsync<EInvoiceSubmissionDetails>(await o.Client.GetAsync(U(o, $"/invoices/{invoice.Invoice.Id}/e-invoice")))).Submission;
        await (await o.Client.PostAsync(U(o, "/notifications/read-all"), null)).EnsureOkAsync();

        var number = invoice.Invoice.Number!;
        await InsertOldAsync(o, "EINV.Rejected", $"{{\"Ar\":\"رُفضت فاتورة إلكترونية: {number} — E-INV-BUYER: refused\",\"En\":\"An e-invoice was rejected: {number} — E-INV-BUYER: refused\"}}",
            "/einvoice-submissions/" + submission.Id);
        await InsertOldAsync(o, "EINV.Rejected", "{\"Ar\":\"x\",\"En\":\"An e-invoice was rejected: INV-09999 — gone\"}", "/einvoice-submissions/notatoken");
        await InsertOldAsync(o, "EINV.Rejected", "{\"Ar\":\"x\",\"En\":\"An e-invoice was rejected: ARD-00001 — gone\"}", "/apar/e-invoice-submissions/notatoken");
        await InsertOldAsync(o, "ACC.PostingFailed", "{\"Ar\":\"x\",\"En\":\"An entry could not be posted and waits in pending postings: Trip 1\"}", "/ledger/pending-postings");
        await InsertOldAsync(o, "CORE.ExportReady", "{\"Ar\":\"x\",\"En\":\"Export ready: gone.xlsx\"}", "/exports/" + Guid.NewGuid().ToString("N"));
        await InsertOldAsync(o, "CORE.InvitationAccepted", "Sara Ali قبل الدعوة / accepted the invitation", null);
        await InsertOldAsync(o, "CORE.PaymentFailed", "{\"Ar\":\"x\",\"En\":\"The renewal payment failed; you have 7 days to update the payment method.\"}", "/tenant/subscription", company: false);
        for (var i = 0; i < 3; i++)
            await InsertOldAsync(o, "EINV.Rejected", $"{{\"Ar\":\"x\",\"En\":\"An e-invoice was rejected: INV-7700{i} — JoFotara refused the credentials (HTTP 401). CORE-401-001\"}}",
                "/einvoice-submissions/" + submission.Id);

        var items = (await NotesAsync(o, "ar")).Items;
        var rejected = Assert.Single(items, n => n.Type == "EINV.Rejected" && n.Message.Contains(number, StringComparison.Ordinal) && n.ReadAt is null);
        Assert.Null(rejected.LinkProblem);
        Assert.Null(rejected.LinkProblem);
        Assert.Equal("invoices/" + invoice.Invoice.Id, rejected.Link);
        Assert.Equal(o.CompanyId, rejected.Company);                       // the URL token, not the company's name
        Assert.NotEqual(rejected.CompanyName, rejected.Company);
        Assert.Equal("رُفضت فاتورة إلكترونية", rejected.Title);
        Assert.Equal("E-INV-BUYER: refused", rejected.Details);
        Assert.Equal(NotificationLinks.Missing, Assert.Single(items, n => n.Message.Contains("INV-09999", StringComparison.Ordinal)).LinkProblem);
        Assert.Equal(NotificationLinks.Missing, Assert.Single(items, n => n.Message.Contains("ARD-00001", StringComparison.Ordinal)).LinkProblem);
        Assert.Equal("accounting/automatic?tab=pending", Assert.Single(items, n => n.Type == "ACC.PostingFailed").Link);
        var export = Assert.Single(items, n => n.Type == "CORE.ExportReady");
        Assert.Equal(NotificationLinks.Missing, export.LinkProblem);
        Assert.Equal("الملف gone.xlsx جاهز للتنزيل.", export.Message);
        var invitation = Assert.Single(items, n => n.Type == "CORE.InvitationAccepted");
        Assert.Equal("settings/users", invitation.Link);
        Assert.Equal("قبل Sara Ali الدعوة وانضم إلى الشركة.", invitation.Message);
        var payment = Assert.Single(items, n => n.Type == "CORE.PaymentFailed");
        Assert.Equal("/tenant/subscription", payment.Link);
        Assert.Null(payment.Company);
        // The three old credential refusals are now one set-up notification, unread.
        Assert.DoesNotContain(items, n => n.Message.Contains("INV-7700", StringComparison.Ordinal));
        var setup = Assert.Single(items, n => n.Type == NotificationService.EInvoiceSetupError);
        Assert.Null(setup.ReadAt);
        Assert.Equal("settings/e-invoicing", setup.Link);
        Assert.Equal(0, await f.DbAsync(db => db.Set<Domain.Core.Notification>().IgnoreQueryFilters()
            .CountAsync(n => n.NotificationType == "EINV.Rejected" && n.Message.Contains("refused the credentials"))));

        // A Dispatcher may open neither tenant screens nor e-invoicing settings: the reason is given instead of a link.
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        await InsertOldAsync(dispatcher, "CORE.PaymentFailed", "{\"Ar\":\"x\",\"En\":\"The renewal payment failed; you have 7 days to update the payment method.\"}", "/tenant/subscription", company: false);
        await InsertOldAsync(dispatcher, NotificationService.EInvoiceSetupError, NotificationService.Message(new Dictionary<string, string?>()), "/settings/e-invoicing");
        var theirs = (await NotesAsync(dispatcher)).Items;
        Assert.All(theirs, n => Assert.Equal(NotificationLinks.Forbidden, n.LinkProblem));
        Assert.All(theirs, n => Assert.Null(n.Link));
        Assert.Equal(2, theirs.Count);
    }

    [Fact]
    public async Task Background_export_is_stored_notified_and_downloadable_by_its_requester_FR151()
    {
        var o = await f.SignUpOwnerAsync();
        var c = o.Client;
        await (await c.PostAsJsonAsync(U(o, "/customers"), Customer("Export Co"))).EnsureOkAsync();
        var queued = await c.PostAsJsonAsync(U(o, "/exports"), new ExportRequest("builder", "customers",
            new ReportDefinition(null, null, null, null, null, null, true, ["name", "city"]), null, null, null, null, null, null, "csv"));
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var job = (await queued.Content.ReadFromJsonAsync<ExportJobResponse>())!;
        await new ExportWorker(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Services.GetRequiredService<IExportQueue>(), NullLogger<ExportWorker>.Instance)
            .DrainAsync(CancellationToken.None);
        var note = Assert.Single((await ReadAsync<NotificationPage>(await c.GetAsync(U(o, "/notifications")))).Items, x => x.Type == "CORE.ExportReady");
        Assert.Equal("exports/" + job.Id, note.Link);
        Assert.Equal(o.CompanyId, note.Company);
        var file = await c.GetAsync(U(o, "/exports/" + job.Id));
        await file.EnsureOkAsync();
        Assert.Contains("Export Co", await file.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.Client.GetAsync(U(o, "/exports/" + job.Id))).StatusCode);
        // A Dispatcher cannot queue an audit-log export.
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.Client.PostAsJsonAsync(U(o, "/exports"),
            new ExportRequest("audit-log", null, null, null, null, null, null, null, null, "csv"))).StatusCode);
    }
}
