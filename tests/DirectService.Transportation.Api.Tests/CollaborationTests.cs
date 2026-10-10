using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Infrastructure.EInvoicing;
using DirectService.Transportation.Web.Workers;
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
        await (await c.PostAsJsonAsync(U(o, "/invoices"), new InvoiceRequest(customer.Id, "Manual", Today, null, null,
            [new ManualLineRequest("Equipment move", 1, 100m)], 0, 0, null, null, null, true, null, null))).EnsureOkAsync();
        await new EInvoiceWorker(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Services.GetRequiredService<EInvoiceSignal>(), NullLogger<EInvoiceWorker>.Instance)
            .RunOnceAsync(CancellationToken.None);

        var page = await ReadAsync<NotificationPage>(await c.GetAsync(U(o, "/notifications")));
        var n = Assert.Single(page.Items, x => x.Type == "EINV.Rejected");
        Assert.StartsWith("/einvoice-submissions/", n.Link, StringComparison.Ordinal);
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
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.GetAsync(U(o, "/audit-log"))).StatusCode);
        Assert.Empty((await ReadAsync<NotificationPage>(await viewer.Client.GetAsync(U(o, "/notifications")))).Items);   // notifications are personal
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
        Assert.Equal("/exports/" + job.Id, note.Link);
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
