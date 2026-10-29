using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Web.Workers;
using Microsoft.Extensions.DependencyInjection;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DirectService.Transportation.Api.Tests;

/// <summary>B12 onboarding and data exchange: setup checklist (P-08), demo company (P-09, G-11), full company export (P-16), erasure on request (SEC-19).</summary>
[Collection(ApiCollection.Name)]
public sealed class OnboardingTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Setup_checklist_follows_the_data_and_manual_marks_P08()
    {
        var o = await f.SignUpOwnerAsync();
        var list = await ReadAsync<SetupChecklistResponse>(await o.Client.GetAsync(U(o, "/setup-checklist")));
        Assert.Equal(8, list.Total);
        Assert.Equal("Done", list.Steps.Single(s => s.Key == "account").Status);
        Assert.Equal("NotStarted", list.Steps.Single(s => s.Key == "chart").Status);
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        list = await ReadAsync<SetupChecklistResponse>(await o.Client.GetAsync(U(o, "/setup-checklist")));
        Assert.Equal("Done", list.Steps.Single(s => s.Key == "chart").Status);
        Assert.Equal("Done", list.Steps.Single(s => s.Key == "mapping").Status);   // the template maps every key
        var marked = await ReadAsync<SetupChecklistResponse>(await o.Client.PostAsJsonAsync(U(o, "/setup-checklist/market"), new SetupMarkRequest(true)));
        Assert.True(marked.Steps.Single(s => s.Key == "market").Marked);
        Assert.Equal(HttpStatusCode.NotFound, (await o.Client.PostAsJsonAsync(U(o, "/setup-checklist/unknown"), new SetupMarkRequest(true))).StatusCode);
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(U(o, "/setup-checklist/market"), new SetupMarkRequest(false))).StatusCode);
        Assert.Equal("Done", (await ReadAsync<SetupChecklistResponse>(await viewer.Client.GetAsync(U(o, "/setup-checklist")))).Steps.Single(s => s.Key == "users").Status);
    }

    [Theory]
    [InlineData("en", "posting accounts still need to be linked to your chart of accounts", "Transport revenue")]
    [InlineData("ar", "من حسابات الترحيل لم تُربط بعد بدليل الحسابات", "إيرادات النقل")]
    public async Task Mapping_step_and_posting_banner_name_the_accounts_never_the_codes_DT224(string language, string sentence, string name)
    {
        var o = await f.SignUpOwnerAsync();
        await (await o.Client.PutAsJsonAsync("/api/v1/me/preferences", new PreferencesRequest(language, null, o.Me.DateFormat))).EnsureOkAsync();
        var step = (await ReadAsync<SetupChecklistResponse>(await o.Client.GetAsync(U(o, "/setup-checklist")))).Steps.Single(s => s.Key == "mapping");
        var status = await ReadAsync<AccountingStatusResponse>(await o.Client.GetAsync(U(o, "/accounting/status")));
        Assert.Equal(status.MissingPostingKeys.Count.ToString(CultureInfo.InvariantCulture) + " " + sentence, step.Detail);
        Assert.Contains(name, step.Items!);
        Assert.Equal(status.MissingPostingKeys.Count, step.Items!.Count);
        Assert.Equal(step.Items, status.MissingPostingKeyNames);   // the shell banner shows the same names
        foreach (var text in step.Items.Append(step.Detail!))
        {
            Assert.DoesNotContain("TRN.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ACC.", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Demo_company_is_filled_with_sample_data_and_not_counted_P09_G11()
    {
        var o = await f.SignUpOwnerAsync();
        var demo = await ReadAsync<DemoCompanyResponse>(await o.Client.PostAsync("/api/v1/tenant/demo-company", null));
        Assert.True(demo.Created);
        Assert.False((await ReadAsync<DemoCompanyResponse>(await o.Client.PostAsync("/api/v1/tenant/demo-company", null))).Created);   // one per tenant
        var trips = await ReadAsync<TripPage>(await o.Client.GetAsync($"/api/v1/companies/{demo.Id}/trips"));
        Assert.Equal(6, trips.Page.TotalCount);
        var invoices = await o.Client.GetAsync($"/api/v1/companies/{demo.Id}/invoices");
        Assert.Contains("INV-00001", await invoices.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var sub = await ReadAsync<SubscriptionResponse>(await o.Client.GetAsync("/api/v1/tenant/subscription"));
        Assert.Equal(1, sub.Usage.Companies);                     // the demo company does not count (G-11)
        // The real company is untouched.
        Assert.Equal(0, (await ReadAsync<TripPage>(await o.Client.GetAsync(U(o, "/trips")))).Page.TotalCount);
    }

    [Fact]
    public async Task Company_export_package_has_every_table_with_public_ids_only_P16()
    {
        var o = await f.SignUpOwnerAsync();
        var customer = await ReadAsync<CustomerResponse>(await o.Client.PostAsJsonAsync(U(o, "/customers"),
            new CustomerRequest("Export Package Co", null, null, "0781234567", null, null, null, null, 0, null, null, null, null, null)));
        var queued = await o.Client.PostAsJsonAsync(U(o, "/exports"), new ExportRequest("company", null, null, null, null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var job = (await queued.Content.ReadFromJsonAsync<ExportJobResponse>())!;
        await new ExportWorker(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Services.GetRequiredService<IExportQueue>(), NullLogger<ExportWorker>.Instance)
            .DrainAsync(CancellationToken.None);
        var file = await o.Client.GetAsync(U(o, "/exports/" + job.Id));
        await file.EnsureOkAsync();
        Assert.Equal("application/zip", file.Content.Headers.ContentType?.MediaType);
        await using var stream = await file.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Contains(zip.Entries, e => e.FullName == "company.json");
        var customers = zip.Entries.Single(e => e.FullName.EndsWith("TRN_Customers.json", StringComparison.Ordinal));
        using var doc = await JsonDocument.ParseAsync(customers.Open());
        var row = doc.RootElement.EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Export Package Co");
        Assert.True(Guid.TryParse(row.GetProperty("id").GetString(), out _));        // public id, never an internal id
        Assert.False(row.TryGetProperty("tenantId", out _));
        Assert.Equal(JsonValueKind.String, row.GetProperty("createdBy").ValueKind);    // reference as a public id
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith("TRN_Customers.xlsx", StringComparison.Ordinal));
        // No key column of any table carries an internal number (SEC-22): references are public ids or empty.
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("json/", StringComparison.Ordinal)))
        {
            using var table = await JsonDocument.ParseAsync(entry.Open());
            foreach (var r in table.RootElement.EnumerateArray())
                foreach (var p in r.EnumerateObject().Where(p => p.Name.EndsWith("Id", StringComparison.Ordinal) || p.Name.EndsWith("By", StringComparison.Ordinal)))
                    Assert.False(p.Value.ValueKind == JsonValueKind.Number, $"{entry.FullName}: {p.Name}");
        }
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(U(o, "/exports"),
            new ExportRequest("company", null, null, null, null, null, null, null, null, null))).StatusCode);
        _ = customer;
    }

    [Fact]
    public async Task Archived_driver_personal_data_is_erased_in_place_SEC19()
    {
        var o = await f.SignUpOwnerAsync();
        var types = (await o.Client.GetFromJsonAsync<List<OwnershipTypeResponse>>(U(o, "/ownership-types")))!;
        var driver = await ReadAsync<DriverResponse>(await o.Client.PostAsJsonAsync(U(o, "/drivers"), new DriverRequest("Private Person", "9871234599", "0791234567", null, "Amman",
            "L-9", "C", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), "Cash", null, null, null, "10-99999", "Volvo", null, 32m, types.Single(t => t.IsBuiltIn).Id, null,
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), null, null, null, null)));
        Assert.Equal("validation.failed", await (await o.Client.PostAsJsonAsync(U(o, $"/drivers/{driver.Id}/anonymise"), new ReasonRequest("Request"))).ProblemCodeAsync());
        await (await o.Client.PostAsJsonAsync(U(o, $"/drivers/{driver.Id}/archive"), new StatusChangeRequest(driver.RowVersion))).EnsureOkAsync();
        await (await o.Client.PostAsJsonAsync(U(o, $"/drivers/{driver.Id}/anonymise"), new ReasonRequest("Data subject request"))).EnsureOkAsync();
        var profile = await o.Client.GetAsync(U(o, $"/drivers/{driver.Id}"));
        var text = await profile.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Private Person", text, StringComparison.Ordinal);
        Assert.DoesNotContain("9871234599", text, StringComparison.Ordinal);
        // The append-only audit log keeps the old values, but no view of it shows them or finds them by text.
        var log = await (await o.Client.GetAsync(U(o, "/audit-log?entityType=TRN.Driver&pageSize=100"))).Content.ReadAsStringAsync();
        Assert.Contains("Anonymise", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Person", log, StringComparison.Ordinal);
        Assert.DoesNotContain("9871234599", log, StringComparison.Ordinal);
        var search = await (await o.Client.GetAsync(U(o, "/audit-log?q=9871234599"))).Content.ReadAsStringAsync();
        Assert.Contains("\"totalCount\":0", search, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Archived_user_erasure_follows_the_user_management_rules_SEC19()
    {
        var o = await f.SignUpOwnerAsync();
        var admin = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Administrator);
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        var owner2 = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Owner);
        await (await o.Client.PostAsJsonAsync(U(o, $"/users/{viewer.Me.Id}/archive"), new ReasonRequest("Left"))).EnsureOkAsync();
        await (await o.Client.PostAsJsonAsync(U(o, $"/users/{owner2.Me.Id}/archive"), new ReasonRequest("Left"))).EnsureOkAsync();
        // An Administrator cannot erase an Owner-level user (SR-01); another tenant cannot reach the user at all.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.PostAsJsonAsync(U(o, $"/users/{owner2.Me.Id}/anonymise"), new ReasonRequest("Request"))).StatusCode);
        var stranger = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PostAsJsonAsync(U(stranger, $"/users/{viewer.Me.Id}/anonymise"), new ReasonRequest("Request"))).StatusCode);
        await (await admin.Client.PostAsJsonAsync(U(o, $"/users/{viewer.Me.Id}/anonymise"), new ReasonRequest("Data subject request"))).EnsureOkAsync();
        // The e-mail is gone from the user, the invitation and every view of the audit log.
        var email = viewer.Email;
        Assert.False(await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Email == email)));
        Assert.False(await f.DbAsync(db => db.Set<Invitation>().IgnoreQueryFilters().AnyAsync(i => i.Email == email)));
        var log = await (await o.Client.GetAsync(U(o, "/audit-log?pageSize=200"))).Content.ReadAsStringAsync();
        Assert.DoesNotContain(email, log, StringComparison.OrdinalIgnoreCase);
    }
}
