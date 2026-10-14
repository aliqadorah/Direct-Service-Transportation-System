using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Configuration;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B3 master data and field configuration through the API: drivers with truck (FR-152…156), customers (FR-157), partners
/// (FR-158), materials and sites (FR-159, FR-160), ownership types (FR-161), reference protection, archive/restore, rename and
/// history (FR-162…166), import/export (FR-164), custom and standard fields (FR-179…182), column views (FR-183), permissions.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MasterDataTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<List<(string Field, string Code, string Message)>> FieldErrorsAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var doc = await r.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("errors").EnumerateArray()
            .Select(e => (e.GetProperty("field").GetString()!, e.GetProperty("code").GetString()!, e.GetProperty("message").GetString()!)).ToList();
    }

    private static async Task<OwnershipTypeResponse> BuiltInAsync(SignedInUser o) =>
        (await o.Client.GetFromJsonAsync<List<OwnershipTypeResponse>>(U(o, "/ownership-types")))!.Single(t => t.IsBuiltIn);

    private static async Task<OwnershipTypeResponse> ExternalTypeAsync(SignedInUser o, string name = "Rented") =>
        (await ReadAsync<OwnershipTypeResponse>(await o.Client.PostAsJsonAsync(U(o, "/ownership-types"), new OwnershipTypeRequest(name, "External", null, null))));

    private static DriverRequest Driver(string ownershipTypeId, string nationalId = "9871234560", string plate = "12-34567", string name = "Ahmad Saleh",
        string? partnerId = null, string method = "Cash", string? iban = null, string? cliq = null, DateOnly? licenceExpiry = null,
        Dictionary<string, JsonElement>? custom = null, string? rowVersion = null, string? clientId = null) =>
        new(name, nationalId, "0791234567", null, "Amman", "L-1001", "C", licenceExpiry ?? Today.AddYears(1), method, iban is null ? null : "Arab Bank", iban, cliq,
            plate, "Volvo FH", null, 32.5m, ownershipTypeId, partnerId, Today.AddYears(1), Today.AddYears(1), Today.AddYears(1), null, custom, clientId, rowVersion);

    private static async Task<DriverResponse> CreateDriverAsync(SignedInUser o, DriverRequest r) =>
        await ReadAsync<DriverResponse>(await o.Client.PostAsJsonAsync(U(o, "/drivers"), r));

    private static CustomerRequest Customer(string name, string? tax = null, Dictionary<string, JsonElement>? custom = null, string? rowVersion = null) =>
        new(name, tax, null, "0781234567", "billing@example.test", null, "Aqaba", null, 30, null, null, custom, null, rowVersion);

    // ------------------------------------------------------------------ drivers (FR-152 … FR-156)

    /// <summary>
    /// Owner feedback 13 Oct 2026: the built-in ownership type (stored "Company", DT-15) is shown in the reader's language everywhere — the type
    /// list, driver records, exports, the report builder — and its Arabic name is accepted back (edit, import); no user type may take either name.
    /// </summary>
    [Fact]
    public async Task Built_in_ownership_type_is_shown_and_accepted_in_the_readers_language()
    {
        var o = await f.SignUpOwnerAsync();
        await (await o.Client.PutAsJsonAsync("/api/v1/me/preferences", new Contracts.Identity.PreferencesRequest("ar", null, o.Me.DateFormat))).EnsureOkAsync();
        var builtIn = await BuiltInAsync(o);
        Assert.Equal("الشركة", builtIn.Name);
        var driver = await CreateDriverAsync(o, Driver(builtIn.Id));
        Assert.Equal("الشركة", driver.OwnershipType.Name);
        // Saving the built-in type with its shown name keeps it (it cannot be renamed); no other type may be called by either name.
        await (await o.Client.PutAsJsonAsync(U(o, $"/ownership-types/{builtIn.Id}"), new OwnershipTypeRequest("الشركة", "Own", null, builtIn.RowVersion))).EnsureOkAsync();
        Assert.Equal("Company", await f.DbAsync(db => db.Set<OwnershipType>().IgnoreQueryFilters().Where(t => t.PublicId != Guid.Empty && t.IsBuiltIn)
            .OrderByDescending(t => t.Id).Select(t => t.Name).FirstAsync()));
        Assert.Equal(HttpStatusCode.Conflict, (await o.Client.PostAsJsonAsync(U(o, "/ownership-types"), new OwnershipTypeRequest("الشركة", "External", null, null))).StatusCode);
        // Exports and the report builder.
        var csv = await (await o.Client.GetAsync(U(o, "/drivers/export?format=csv"))).Content.ReadAsStringAsync();
        Assert.Contains("الشركة", csv, StringComparison.Ordinal);
        Assert.DoesNotContain(",Company,", csv, StringComparison.Ordinal);
        var report = await ReadAsync<Contracts.Reports.ReportResult>(await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"),
            new Contracts.Reports.ReportRunRequest("drivers", new Contracts.Reports.ReportDefinition(null, null, null, null, null, null, true, ["name", "ownershipType"]), null, null)));
        Assert.Contains(report.Rows, r => r.Values.Any(v => v is JsonElement { ValueKind: JsonValueKind.String } s && s.GetString() == "الشركة"));
        // An import naming the type in Arabic links the built-in type.
        var file = "الاسم الكامل,الرقم الوطني,الهاتف 1,رقم الرخصة,فئة الرخصة,تاريخ انتهاء الرخصة,طريقة الدفع,رقم اللوحة,الحمولة (طن),نوع الملكية,تاريخ انتهاء الترخيص,تاريخ انتهاء التأمين,تاريخ انتهاء الفحص\n" +
                   $"سامي العبادي,9871234569,0791234568,L-2,C,{Today.AddYears(1):yyyy-MM-dd},نقد,22-22222,30,الشركة,{Today.AddYears(1):yyyy-MM-dd},{Today.AddYears(1):yyyy-MM-dd},{Today.AddYears(1):yyyy-MM-dd}\n";
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(file)), "file", "drivers.csv" } };
        await (await o.Client.PostAsync(U(o, "/drivers/import"), form)).EnsureOkAsync();
    }

    [Fact]
    public async Task Driver_keys_are_unique_with_arabic_digits_folded_and_plate_spacing_ignored()
    {
        var owner = await f.SignUpOwnerAsync();
        var company = await BuiltInAsync(owner);
        // National ID and plate typed with Arabic-Indic digits are stored with ASCII digits.
        var d = await CreateDriverAsync(owner, Driver(company.Id, nationalId: "٩٨٧١٢٣٤٥٦٠", plate: "١٢-٣٤٥٦٧"));
        Assert.Equal("9871234560", d.NationalId);
        Assert.Equal("12-34567", d.PlateNumber);
        Assert.Equal("Own", d.FleetClass);

        var sameId = await owner.Client.PostAsJsonAsync(U(owner, "/drivers"), Driver(company.Id, nationalId: "9871234560", plate: "99-11111"));
        Assert.Equal(HttpStatusCode.Conflict, sameId.StatusCode);
        Assert.Equal("duplicate.key", await sameId.ProblemCodeAsync());
        var samePlate = await owner.Client.PostAsJsonAsync(U(owner, "/drivers"), Driver(company.Id, nationalId: "1111111111", plate: "12 - 34567"));
        Assert.Equal(HttpStatusCode.Conflict, samePlate.StatusCode);

        // An archived driver still holds its national ID and plate (unique within the company, design index WHERE IsDeleted = 0).
        await (await owner.Client.PostAsJsonAsync(U(owner, $"/drivers/{d.Id}/archive"), new StatusChangeRequest(d.RowVersion))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(U(owner, "/drivers"), Driver(company.Id, plate: "77-00000"))).StatusCode);

        // Another company of another tenant may use the same keys.
        var other = await f.SignUpOwnerAsync();
        await CreateDriverAsync(other, Driver((await BuiltInAsync(other)).Id, nationalId: "9871234560", plate: "12-34567"));
    }

    [Fact]
    public async Task Driver_validation_follows_the_payment_method_and_market_formats()
    {
        var owner = await f.SignUpOwnerAsync();
        var company = await BuiltInAsync(owner);
        var errors = await FieldErrorsAsync(await owner.Client.PostAsJsonAsync(U(owner, "/drivers"),
            Driver(company.Id, nationalId: "12345", method: "Bank") with { Phone1 = "0551234567", CapacityTons = 0 }));
        Assert.Contains(errors, e => e.Field == "nationalId" && e.Code == "validation.format");
        Assert.Contains(errors, e => e.Field == "phone1" && e.Code == "validation.format");
        Assert.Contains(errors, e => e.Field == "iban" && e.Code == "validation.required" && e.Message.Contains("IBAN", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Field == "bankName" && e.Code == "validation.required");
        Assert.Contains(errors, e => e.Field == "capacityTons" && e.Code == "validation.range");

        var cliq = await FieldErrorsAsync(await owner.Client.PostAsJsonAsync(U(owner, "/drivers"), Driver(company.Id, method: "CliQ")));
        Assert.Contains(cliq, e => e.Field == "cliqAlias" && e.Code == "validation.required");
        Assert.Contains(await FieldErrorsAsync(await owner.Client.PostAsJsonAsync(U(owner, "/drivers"), Driver(company.Id, method: "Bank", iban: "JO00BAD"))),
            e => e.Field == "iban" && e.Code == "validation.format");

        var bank = await CreateDriverAsync(owner, Driver(company.Id, method: "Bank", iban: "jo94 cbjo 0010 0000 0000 0131 0003 02"));
        Assert.Equal("JO94CBJO0010000000000131000302", bank.Iban);
    }

    [Fact]
    public async Task Partner_drivers_are_external_fleet_and_truck_changes_appear_in_history_FR153()
    {
        var owner = await f.SignUpOwnerAsync();
        var company = await BuiltInAsync(owner);
        var rented = await ExternalTypeAsync(owner);
        var partner = await ReadAsync<PartnerResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/partners"),
            new PartnerRequest("Al-Noor Transport", "123456", "0771234567", null, null, null, null, null, null, null, null)));

        var refused = await owner.Client.PostAsJsonAsync(U(owner, "/drivers"), Driver(company.Id, partnerId: partner.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("driver.partnerOwnFleet", await refused.ProblemCodeAsync());

        var d = await CreateDriverAsync(owner, Driver(company.Id));
        var moved = await ReadAsync<DriverResponse>(await owner.Client.PutAsJsonAsync(U(owner, $"/drivers/{d.Id}"),
            Driver(rented.Id, plate: "55-12345", partnerId: partner.Id, rowVersion: d.RowVersion)));
        Assert.Equal("External", moved.FleetClass);
        Assert.Equal(partner.Name, moved.Partner!.Name);

        var history = await owner.Client.GetFromJsonAsync<List<HistoryEntry>>(U(owner, $"/drivers/{d.Id}/history"));
        var edit = history!.First(h => h.Action == "Update");
        // Readable (DT-188): the form's labels, names for links, "—" for nothing.
        Assert.Contains(edit.Changes, c => c.Field == "Plate number" && c.OldValue == "12-34567" && c.NewValue == "55-12345");
        Assert.Contains(edit.Changes, c => c.Field == "Ownership type" && c.OldValue == "Company" && c.NewValue == "Rented");
        Assert.Contains(edit.Changes, c => c.Field == "Partner company" && c.OldValue == "—" && c.NewValue == partner.Name);
        Assert.DoesNotContain(edit.Changes, c => c.Field.EndsWith("Id", StringComparison.Ordinal) || c.Field.Contains("Fleet", StringComparison.Ordinal) && c.NewValue == "External");
        Assert.Equal("Test Owner", edit.User);
        Assert.StartsWith("Test Owner edited driver ", edit.Text, StringComparison.Ordinal);
        Assert.Equal("Edited", edit.ActionLabel);
        Assert.Equal("Create", history!.Last().Action);

        // A stale row version is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PutAsJsonAsync(U(owner, $"/drivers/{d.Id}"), Driver(rented.Id, rowVersion: d.RowVersion))).StatusCode);

        var profile = await owner.Client.GetFromJsonAsync<PartnerProfile>(U(owner, $"/partners/{partner.Id}"));
        Assert.Equal(d.Id, Assert.Single(profile!.Drivers).Id);
        var partnerDelete = await owner.Client.PostAsJsonAsync(U(owner, $"/partners/{partner.Id}/delete"), new DeleteRequest("cleanup"));
        Assert.Equal("record.inUse", await partnerDelete.ProblemCodeAsync());
    }

    [Fact]
    public async Task Expired_and_expiring_documents_raise_alerts_FR154()
    {
        var owner = await f.SignUpOwnerAsync();
        var company = await BuiltInAsync(owner);
        var expired = await CreateDriverAsync(owner, Driver(company.Id, nationalId: "1000000001", plate: "10-1", licenceExpiry: Today.AddDays(-1)));
        var soon = await CreateDriverAsync(owner, Driver(company.Id, nationalId: "1000000002", plate: "10-2", licenceExpiry: Today.AddDays(20)));
        await CreateDriverAsync(owner, Driver(company.Id, nationalId: "1000000003", plate: "10-3"));

        var alert = Assert.Single(expired.Alerts);
        Assert.True(alert.Expired);
        Assert.Equal("licence", alert.Document);
        Assert.False(Assert.Single(soon.Alerts).Expired);

        var alerts = await owner.Client.GetFromJsonAsync<List<DriverResponse>>(U(owner, "/drivers/expiry-alerts"));
        Assert.Equal([expired.Id, soon.Id], alerts!.Select(a => a.Id));
        var expiredOnly = await owner.Client.GetFromJsonAsync<PagedResult<DriverResponse>>(U(owner, "/drivers?expiry=expired"));
        Assert.Equal(expired.Id, Assert.Single(expiredOnly!.Items).Id);
        var options = await owner.Client.GetFromJsonAsync<List<DriverOption>>(U(owner, "/drivers/options"));
        Assert.Equal(3, options!.Count);
        Assert.True(options.Single(o => o.Id == expired.Id).Alerts[0].Expired); // the trip form warns, save allowed (FR-155)
    }

    [Fact]
    public async Task Driver_list_searches_filters_sorts_and_pages_FR165()
    {
        var owner = await f.SignUpOwnerAsync();
        var company = await BuiltInAsync(owner);
        var rented = await ExternalTypeAsync(owner);
        await CreateDriverAsync(owner, Driver(company.Id, nationalId: "2000000001", plate: "20-1", name: "Basel"));
        await CreateDriverAsync(owner, Driver(rented.Id, nationalId: "2000000002", plate: "20-2", name: "Ali"));
        var c = await CreateDriverAsync(owner, Driver(rented.Id, nationalId: "2000000003", plate: "20-3", name: "Chadi"));
        await (await owner.Client.PostAsJsonAsync(U(owner, $"/drivers/{c.Id}/archive"), new StatusChangeRequest(c.RowVersion))).EnsureOkAsync();

        var active = await owner.Client.GetFromJsonAsync<PagedResult<DriverResponse>>(U(owner, "/drivers?sort=-fullName"));
        Assert.Equal(["Basel", "Ali"], active!.Items.Select(d => d.FullName));
        var all = await owner.Client.GetFromJsonAsync<PagedResult<DriverResponse>>(U(owner, "/drivers?status=all&pageSize=2&page=2&sort=fullName"));
        Assert.Equal(3, all!.TotalCount);
        Assert.Equal("Chadi", Assert.Single(all.Items).FullName);
        var external = await owner.Client.GetFromJsonAsync<PagedResult<DriverResponse>>(U(owner, "/drivers?fleet=External&status=all"));
        Assert.Equal(2, external!.TotalCount);
        var byPlate = await owner.Client.GetFromJsonAsync<PagedResult<DriverResponse>>(U(owner, "/drivers?q=٢٠-٢"));
        Assert.Equal("Ali", Assert.Single(byPlate!.Items).FullName);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.GetAsync(U(owner, "/drivers?sort=passwordHash"))).StatusCode);
        // archived drivers are hidden from pick lists (FR-162)
        Assert.DoesNotContain((await owner.Client.GetFromJsonAsync<List<DriverOption>>(U(owner, "/drivers/options")))!, o => o.Id == c.Id);
    }

    // ------------------------------------------------------------------ references, archive, delete (FR-161, FR-162, BR-040)

    [Fact]
    public async Task Referenced_records_cannot_be_deleted_and_the_refusal_lists_counts()
    {
        var owner = await f.SignUpOwnerAsync();
        var rented = await ExternalTypeAsync(owner);
        var d = await CreateDriverAsync(owner, Driver(rented.Id));

        var refs = await owner.Client.GetFromJsonAsync<ReferencesResponse>(U(owner, $"/ownership-types/{rented.Id}/references"));
        Assert.False(refs!.CanDelete);
        Assert.Equal(1, Assert.Single(refs.References).Count);
        var refused = await owner.Client.PostAsJsonAsync(U(owner, $"/ownership-types/{rented.Id}/delete"), new DeleteRequest("not needed"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("record.inUse", problem.GetProperty("code").GetString());
        Assert.Contains("1 drivers", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        // Unreferenced records are soft-deleted with a reason (BR-041) and disappear from lists.
        Assert.Equal("validation.failed", await (await owner.Client.PostAsJsonAsync(U(owner, $"/drivers/{d.Id}/delete"), new DeleteRequest(" "))).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsJsonAsync(U(owner, $"/drivers/{d.Id}/delete"), new DeleteRequest("entered twice"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync(U(owner, $"/drivers/{d.Id}"))).StatusCode);
        var publicId = f.Services.GetRequiredService<IUrlTokenService>().Decode<Domain.Trn.Driver>(d.Id);
        var row = await f.DbAsync(db => db.Set<Domain.Trn.Driver>().IgnoreQueryFilters().SingleAsync(x => x.PublicId == publicId));
        Assert.True(row.IsDeleted);
        Assert.Equal("entered twice", row.DeleteReason);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsJsonAsync(U(owner, $"/ownership-types/{rented.Id}/delete"), new DeleteRequest("not needed"))).StatusCode);
    }

    [Fact]
    public async Task The_built_in_company_ownership_type_cannot_be_renamed_reclassified_or_deleted_FR161()
    {
        var owner = await f.SignUpOwnerAsync();
        var company = await BuiltInAsync(owner);
        Assert.Equal("Own", company.FleetClass);
        Assert.Equal("masters.builtInFixed", await (await owner.Client.PutAsJsonAsync(U(owner, $"/ownership-types/{company.Id}"),
            new OwnershipTypeRequest("Our trucks", "Own", null, company.RowVersion))).ProblemCodeAsync());
        Assert.Equal("masters.builtInFixed", await (await owner.Client.PutAsJsonAsync(U(owner, $"/ownership-types/{company.Id}"),
            new OwnershipTypeRequest("Company", "External", null, company.RowVersion))).ProblemCodeAsync());
        Assert.Equal("masters.builtInFixed", await (await owner.Client.PostAsJsonAsync(U(owner, $"/ownership-types/{company.Id}/delete"),
            new DeleteRequest("x"))).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(U(owner, "/ownership-types"), new OwnershipTypeRequest("company", "External", null, null))).StatusCode);
    }

    [Fact]
    public async Task Customer_names_are_unique_among_active_customers_and_restore_checks_it()
    {
        var owner = await f.SignUpOwnerAsync();
        var first = await ReadAsync<CustomerResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/customers"), Customer("Arab Potash")));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(U(owner, "/customers"), Customer("arab  potash"))).StatusCode);

        var archived = await ReadAsync<CustomerResponse>(await owner.Client.PostAsJsonAsync(U(owner, $"/customers/{first.Id}/archive"), new StatusChangeRequest(first.RowVersion)));
        Assert.Equal("Archived", archived.Status);
        var second = await ReadAsync<CustomerResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/customers"), Customer("Arab Potash")));
        var restore = await owner.Client.PostAsJsonAsync(U(owner, $"/customers/{first.Id}/restore"), new StatusChangeRequest(archived.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);

        // rename the active one, then the archived one can come back (DT-40)
        await (await owner.Client.PutAsJsonAsync(U(owner, $"/customers/{second.Id}"), Customer("Arab Potash Co.", rowVersion: second.RowVersion))).EnsureOkAsync();
        await (await owner.Client.PostAsJsonAsync(U(owner, $"/customers/{first.Id}/restore"), new StatusChangeRequest(archived.RowVersion))).EnsureOkAsync();
        var history = await owner.Client.GetFromJsonAsync<List<HistoryEntry>>(U(owner, $"/customers/{first.Id}/history"));
        Assert.Equal(["Restore", "Archive", "Create"], history!.Select(h => h.Action));

        var options = await owner.Client.GetFromJsonAsync<List<MasterOption>>(U(owner, "/customers/options"));
        Assert.Equal(2, options!.Count);
    }

    [Fact]
    public async Task Materials_and_sites_are_unique_in_their_own_list_and_rename_by_identifier()
    {
        var owner = await f.SignUpOwnerAsync();
        var m = await ReadAsync<MaterialResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/materials"), new MaterialRequest("Phosphate", null, null, null)));
        Assert.Equal("ton", m.Unit);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(U(owner, "/materials"), new MaterialRequest("phosphate", "ton", null, null))).StatusCode);
        var renamed = await ReadAsync<MaterialResponse>(await owner.Client.PutAsJsonAsync(U(owner, $"/materials/{m.Id}"), new MaterialRequest("Phosphate (wet)", "ton", null, m.RowVersion)));
        Assert.Equal(m.Id, renamed.Id);

        var load = await ReadAsync<SiteResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/loading-sites"), new SiteRequest("Shidiya Mine", "Ma'an", null, null)));
        // the same name is allowed in the other list (two separate lists, FR-160)
        await ReadAsync<SiteResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/unloading-sites"), new SiteRequest("Shidiya Mine", null, null, null)));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(U(owner, "/loading-sites"), new SiteRequest("Shidiya Mine", null, null, null))).StatusCode);
        // an archived site keeps its name (unique within the company)
        var archived = await ReadAsync<SiteResponse>(await owner.Client.PostAsJsonAsync(U(owner, $"/loading-sites/{load.Id}/archive"), new StatusChangeRequest(load.RowVersion)));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(U(owner, "/loading-sites"), new SiteRequest("Shidiya Mine", null, null, null))).StatusCode);
        Assert.Empty((await owner.Client.GetFromJsonAsync<List<MasterOption>>(U(owner, "/loading-sites/options")))!);
        var details = await owner.Client.GetFromJsonAsync<RecordDetails<SiteResponse>>(U(owner, $"/loading-sites/{archived.Id}"));
        Assert.Equal("Archived", details!.Record.Status);
        Assert.Empty(details.References);
    }

    [Fact]
    public async Task A_repeated_create_with_the_same_client_id_returns_the_first_record_API05()
    {
        var owner = await f.SignUpOwnerAsync();
        var clientId = Guid.NewGuid().ToString();
        var first = await owner.Client.PostAsJsonAsync(U(owner, "/materials"), new MaterialRequest("Sand", null, clientId, null));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var again = await owner.Client.PostAsJsonAsync(U(owner, "/materials"), new MaterialRequest("Sand", null, clientId, null));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<MaterialResponse>())!.Id, (await again.Content.ReadFromJsonAsync<MaterialResponse>())!.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync(U(owner, "/materials"), new MaterialRequest("Gravel", null, "not-a-guid", null))).StatusCode);
    }

    // ------------------------------------------------------------------ field configuration (FR-179 … FR-183)

    [Fact]
    public async Task Custom_fields_validate_values_and_with_data_can_only_be_hidden_FR179_FR181()
    {
        var owner = await f.SignUpOwnerAsync();
        var field = await ReadAsync<CustomFieldResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/custom-fields/TRN.Customer"),
            new CustomFieldRequest("region", "المنطقة", "Region", "List", ["North", "South"], null, null, true, true, 10, false, null, null)));
        Assert.False(field.HasData);
        var limit = await ReadAsync<CustomFieldResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/custom-fields/TRN.Customer"),
            new CustomFieldRequest("creditLimit", "حد الائتمان", "Credit limit", "Number", null, "1000", null, false, false, 20, false, null, null)));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(U(owner, "/custom-fields/TRN.Customer"),
            new CustomFieldRequest("name", "الاسم", null, "Text", null, null, null, false, false, null, false, null, null))).StatusCode); // a standard field key

        var missing = await FieldErrorsAsync(await owner.Client.PostAsJsonAsync(U(owner, "/customers"), Customer("Manaseer")));
        Assert.Contains(missing, e => e.Field == "customValues.region" && e.Code == "validation.required" && e.Message.Contains("Region", StringComparison.Ordinal));
        var badOption = await FieldErrorsAsync(await owner.Client.PostAsJsonAsync(U(owner, "/customers"),
            Customer("Manaseer", custom: new() { ["region"] = JsonSerializer.SerializeToElement("East"), ["unknown"] = JsonSerializer.SerializeToElement(1) })));
        Assert.Contains(badOption, e => e.Field == "customValues.region" && e.Code == "validation.format");
        Assert.Contains(badOption, e => e.Field == "customValues.unknown" && e.Code == "field.unknown");

        var c = await ReadAsync<CustomerResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/customers"),
            Customer("Manaseer", custom: new() { ["region"] = JsonSerializer.SerializeToElement("South") })));
        Assert.Equal("South", c.CustomValues["region"].GetString());
        Assert.Equal(1000m, c.CustomValues["creditLimit"].GetDecimal()); // default applied on create

        var list = await owner.Client.GetFromJsonAsync<List<CustomFieldResponse>>(U(owner, "/custom-fields/TRN.Customer"));
        field = list!.Single(x => x.FieldKey == "region");
        Assert.True(field.HasData);
        Assert.Equal("customField.typeFixed", await (await owner.Client.PutAsJsonAsync(U(owner, $"/custom-fields/field/{field.Id}"),
            new CustomFieldRequest(null, "المنطقة", "Region", "Text", null, null, null, true, true, 10, false, null, field.RowVersion))).ProblemCodeAsync());
        Assert.Equal("customField.optionInUse", await (await owner.Client.PutAsJsonAsync(U(owner, $"/custom-fields/field/{field.Id}"),
            new CustomFieldRequest(null, "المنطقة", "Region", "List", ["North"], null, null, true, true, 10, false, null, field.RowVersion))).ProblemCodeAsync());
        Assert.Equal("customField.hasData", await (await owner.Client.PostAsJsonAsync(U(owner, $"/custom-fields/field/{field.Id}/delete"), new DeleteRequest("x"))).ProblemCodeAsync());

        // Hidden: the stored value stays even when the form no longer sends it.
        await (await owner.Client.PutAsJsonAsync(U(owner, $"/custom-fields/field/{field.Id}"),
            new CustomFieldRequest(null, "المنطقة", "Region", "List", ["North", "South", "Central"], null, null, true, true, 10, true, null, field.RowVersion))).EnsureOkAsync();
        var edited = await ReadAsync<CustomerResponse>(await owner.Client.PutAsJsonAsync(U(owner, $"/customers/{c.Id}"), Customer("Manaseer Group", rowVersion: c.RowVersion)));
        Assert.Equal("South", edited.CustomValues["region"].GetString());
        Assert.False(edited.CustomValues.ContainsKey("creditLimit")); // a visible field cleared by the form is removed

        // A field without data is deleted (soft).
        var unused = (await owner.Client.GetFromJsonAsync<List<CustomFieldResponse>>(U(owner, "/custom-fields/TRN.Customer")))!.Single(x => x.FieldKey == "creditLimit");
        Assert.False(unused.HasData);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsJsonAsync(U(owner, $"/custom-fields/field/{limit.Id}/delete"), new DeleteRequest("not used"))).StatusCode);

        var history = await owner.Client.GetFromJsonAsync<List<HistoryEntry>>(U(owner, $"/customers/{c.Id}/history"));
        // A custom field by its label — kept after the field was deleted (DT-188).
        Assert.Contains(history![0].Changes, ch => ch.Field == "Credit limit" && ch.OldValue == "1,000" && ch.NewValue == "—");
    }

    [Fact]
    public async Task Standard_fields_rename_hide_and_require_with_core_fields_protected_FR182()
    {
        var owner = await f.SignUpOwnerAsync();
        var core = await owner.Client.PutAsJsonAsync(U(owner, "/standard-fields/TRN.Trip"),
            new StandardFieldsRequest([new StandardFieldChange("receiptNo", "Ticket", true, true, null)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, core.StatusCode);
        Assert.Equal("field.core", await core.ProblemCodeAsync());
        Assert.Equal("field.alwaysRequired", await (await owner.Client.PutAsJsonAsync(U(owner, "/standard-fields/TRN.Driver"),
            new StandardFieldsRequest([new StandardFieldChange("nationalId", null, true, false, null)]))).ProblemCodeAsync());
        Assert.Equal("validation.failed", await (await owner.Client.PutAsJsonAsync(U(owner, "/standard-fields/TRN.Customer"),
            new StandardFieldsRequest([new StandardFieldChange("passwordHash", null, false, false, null)]))).ProblemCodeAsync());

        // core trip fields can be renamed (never hidden)
        var trip = await ReadAsync<List<StandardFieldResponse>>(await owner.Client.PutAsJsonAsync(U(owner, "/standard-fields/TRN.Trip"),
            new StandardFieldsRequest([new StandardFieldChange("receiptNo", "Ticket number", false, true, 5)])));
        Assert.Equal("Ticket number", trip[0].Label);

        await (await owner.Client.PutAsJsonAsync(U(owner, "/standard-fields/TRN.Customer"), new StandardFieldsRequest(
        [
            new StandardFieldChange("taxNumber", "VAT number", false, true, null),
            new StandardFieldChange("email", null, true, false, null),
        ]))).EnsureOkAsync();
        var required = await FieldErrorsAsync(await owner.Client.PostAsJsonAsync(U(owner, "/customers"), Customer("Jordan Phosphate")));
        var e = Assert.Single(required);
        Assert.Equal("taxNumber", e.Field);
        Assert.Equal("Field «VAT number» is required.", e.Message);
        await (await owner.Client.PostAsJsonAsync(U(owner, "/customers"), Customer("Jordan Phosphate", tax: "٤٤٥٥٦٦"))).EnsureOkAsync();

        var form = await owner.Client.GetFromJsonAsync<FormConfigResponse>(U(owner, "/forms/TRN.Customer"));
        Assert.DoesNotContain(form!.Fields, x => x.Key == "email");
        Assert.True(form.Fields.Single(x => x.Key == "taxNumber").IsRequired);
        Assert.Equal("VAT number", form.Fields.Single(x => x.Key == "taxNumber").Label);
        Assert.Contains((await owner.Client.GetFromJsonAsync<FormConfigResponse>(U(owner, "/forms/TRN.Customer?includeHidden=true")))!.Fields, x => x.Key == "email");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync(U(owner, "/forms/CORE.User"))).StatusCode);
    }

    [Fact]
    public async Task Column_views_are_personal_with_a_company_default_FR183()
    {
        var owner = await f.SignUpOwnerAsync();
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Viewer);
        var builtIn = await owner.Client.GetFromJsonAsync<ColumnViewResponse>(U(owner, "/column-views/TRN.Trips"));
        Assert.Equal("BuiltIn", builtIn!.Source);

        await (await owner.Client.PutAsJsonAsync(U(owner, "/column-views/TRN.Trips/default"),
            new ColumnViewRequest([new ColumnSetting("receiptNo", 120), new ColumnSetting("tripDate", null), new ColumnSetting("custom.region", 90)]))).EnsureOkAsync();
        var seen = await viewer.Client.GetFromJsonAsync<ColumnViewResponse>(U(owner, "/column-views/TRN.Trips"));
        Assert.Equal("CompanyDefault", seen!.Source);
        Assert.Equal(3, seen.Columns.Count);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PutAsJsonAsync(U(owner, "/column-views/TRN.Trips/default"),
            new ColumnViewRequest([new ColumnSetting("receiptNo", null)]))).StatusCode);
        var personal = await ReadAsync<ColumnViewResponse>(await viewer.Client.PutAsJsonAsync(U(owner, "/column-views/TRN.Trips"),
            new ColumnViewRequest([new ColumnSetting("tripDate", null), new ColumnSetting("receiptNo", null)])));
        Assert.Equal("Personal", personal.Source);
        Assert.Equal("tripDate", personal.Columns[0].Key);
        Assert.Equal("CompanyDefault", (await owner.Client.GetFromJsonAsync<ColumnViewResponse>(U(owner, "/column-views/TRN.Trips")))!.Source);
        Assert.Equal("CompanyDefault", (await ReadAsync<ColumnViewResponse>(await viewer.Client.PostAsync(U(owner, "/column-views/TRN.Trips/reset"), null))).Source);
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.Client.PutAsJsonAsync(U(owner, "/column-views/TRN.Trips"),
            new ColumnViewRequest([new ColumnSetting("a b", null)]))).StatusCode);
    }

    // ------------------------------------------------------------------ import / export (FR-164)

    [Fact]
    public async Task Customers_export_and_import_with_duplicates_detected_and_strict_commit()
    {
        var owner = await f.SignUpOwnerAsync();
        await ReadAsync<CustomerResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/customers"), Customer("Existing Co")));
        var sheets = f.Services.GetRequiredService<ISpreadsheetService>();

        var export = await owner.Client.GetAsync(U(owner, "/customers/export"));
        await export.EnsureOkAsync();
        var exported = sheets.Read(new MemoryStream(await export.Content.ReadAsByteArrayAsync()), "customers.xlsx", 100_000);
        Assert.Equal("Name", exported.Headers[0]);
        Assert.Equal("Existing Co", exported.Rows.Single()[0]);

        var bad = "اسم العميل,الهاتف,مدة السداد,Extra\nNew One,0791112223,15,x\nnew one,,0,\nExisting Co,,0,\nBad Phone,0551112223,0,\n,0791112223,x,\n";
        var preview = await UploadAsync<MasterImportPreview>(owner, "/customers/import/preview", Encoding.UTF8.GetBytes(bad), "c.csv");
        Assert.Equal(5, preview.TotalRows);
        Assert.Equal(1, preview.ValidRows);
        Assert.Contains("Extra", preview.Columns.Unused);
        Assert.Equal("مدة السداد", preview.Columns.Matched["paymentTermsDays"]);
        Assert.Contains(preview.Rows.Single(r => r.Row == 3).Problems, p => p.Contains("duplicated", StringComparison.Ordinal));
        Assert.Contains(preview.Rows.Single(r => r.Row == 4).Problems, p => p.Contains("duplicated", StringComparison.Ordinal));
        Assert.NotEmpty(preview.Rows.Single(r => r.Row == 5).Problems);
        Assert.NotEmpty(preview.Rows.Single(r => r.Row == 6).Problems);
        Assert.Equal("import.hasErrors", await (await UploadAsync(owner, "/customers/import", Encoding.UTF8.GetBytes(bad), "c.csv")).ProblemCodeAsync());

        var good = "Name,Phone,Payment terms\nNew One,0791112223,15\nSecond,,٣٠\n";
        var result = await UploadAsync<ImportResult>(owner, "/customers/import", Encoding.UTF8.GetBytes(good), "c.csv");
        Assert.Equal(2, result.ImportedRows);
        var all = await owner.Client.GetFromJsonAsync<PagedResult<CustomerResponse>>(U(owner, "/customers?sort=name"));
        Assert.Equal(3, all!.TotalCount);
        Assert.Equal(30, all.Items.Single(c => c.Name == "Second").PaymentTermsDays);
        var batch = await f.DbAsync(db => db.Set<ImportBatch>().IgnoreQueryFilters().Where(b => b.EntityType == "TRN.Customer").OrderByDescending(b => b.Id).FirstAsync());
        Assert.Equal(2, batch.ImportedRows);
    }

    [Fact]
    public async Task Drivers_import_links_ownership_types_and_partners_by_name_with_explicit_column_mapping()
    {
        var owner = await f.SignUpOwnerAsync();
        await ExternalTypeAsync(owner, "Owner-driver");
        await owner.Client.PostAsJsonAsync(U(owner, "/partners"), new PartnerRequest("Al-Noor", null, null, null, null, null, null, null, null, null, null));
        var exp = Today.AddYears(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var csv = "Driver,National ID,Phone 1,Licence number,Licence type,Licence expiry,Plate,Capacity,Ownership type,Partner,Registration expiry,Insurance expiry,Inspection expiry\n"
            + $"Sami,٣٣٣٣٣٣٣٣٣٣,0791234567,L1,C,{exp},30-1,30,owner-driver,al-noor,{exp},{exp},{exp}\n"
            + $"Rami,4444444444,0791234567,L2,C,{exp},30-1,30,Company,,{exp},{exp},{exp}\n"
            + $"Fadi,5555555555,0791234567,L3,C,soon,30-3,30,Unknown type,,{exp},{exp},{exp}\n";
        var mapping = JsonSerializer.Serialize(new Dictionary<string, string> { ["fullName"] = "Driver" });
        var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "d.csv" },
            { new StringContent(mapping), "mapping" },
        };
        var preview = await ReadAsync<MasterImportPreview>(await owner.Client.PostAsync(U(owner, "/drivers/import/preview"), content));
        Assert.Equal("Driver", preview.Columns.Matched["fullName"]);
        Assert.Empty(preview.Rows.Single(r => r.Row == 2).Problems);
        Assert.Contains(preview.Rows.Single(r => r.Row == 3).Problems, p => p.Contains("30-1", StringComparison.Ordinal)); // plate duplicated in the file
        Assert.Equal(2, preview.Rows.Single(r => r.Row == 4).Problems.Count); // bad date + unknown ownership type

        var good = string.Join('\n', csv.Split('\n').Take(2)) + "\n";
        await UploadAsync<ImportResult>(owner, "/drivers/import", Encoding.UTF8.GetBytes(good.Replace("Driver,", "Full name,", StringComparison.Ordinal)), "d.csv");
        var sami = (await owner.Client.GetFromJsonAsync<PagedResult<DriverResponse>>(U(owner, "/drivers")))!.Items.Single();
        Assert.Equal("3333333333", sami.NationalId);
        Assert.Equal("External", sami.FleetClass);
        Assert.Equal("Al-Noor", sami.Partner!.Name);

        var export = await owner.Client.GetAsync(U(owner, "/drivers/export?format=csv"));
        await export.EnsureOkAsync();
        var text = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync());
        Assert.Contains("Sami,3333333333,0791234567", text, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> UploadAsync(SignedInUser o, string path, byte[] content, string fileName) =>
        await o.Client.PostAsync(U(o, path), new MultipartFormDataContent { { new ByteArrayContent(content), "file", fileName } });

    private static async Task<T> UploadAsync<T>(SignedInUser o, string path, byte[] content, string fileName) =>
        await ReadAsync<T>(await UploadAsync(o, path, content, fileName));

    // ------------------------------------------------------------------ permissions (PRD 3.4, DT-36)

    [Fact]
    public async Task Master_data_permissions_follow_the_role_presets()
    {
        var owner = await f.SignUpOwnerAsync();
        var company = await BuiltInAsync(owner);
        var d = await CreateDriverAsync(owner, Driver(company.Id));
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Viewer);
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Dispatcher);

        // Viewer: drv.view reads drivers and partners; cannot write master data or configure fields.
        Assert.Equal(HttpStatusCode.OK, (await viewer.Client.GetAsync(U(owner, $"/drivers/{d.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(U(owner, "/drivers"), Driver(company.Id, nationalId: "1212121212", plate: "1-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(U(owner, "/materials"), new MaterialRequest("Sand", null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.GetAsync(U(owner, "/custom-fields/TRN.Driver"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.Client.GetAsync(U(owner, "/forms/TRN.Driver"))).StatusCode);
        // Dispatcher: masters.manage.
        await (await dispatcher.Client.PostAsJsonAsync(U(owner, "/materials"), new MaterialRequest("Sand", null, null, null))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.Client.PutAsJsonAsync(U(owner, "/standard-fields/TRN.Customer"), new StandardFieldsRequest([]))).StatusCode);

        // A custom role without drv.view / masters.manage sees pick lists but not driver records.
        var roles = await owner.Client.GetFromJsonAsync<List<Contracts.Identity.RoleResponse>>(U(owner, "/roles"));
        Assert.NotNull(roles);
        var clerk = await ReadAsync<Contracts.Identity.RoleResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/roles"),
            new Contracts.Identity.RoleRequest("Trip clerk", [PermissionKeys.TripsView, PermissionKeys.TripsEdit], null)));
        var email = $"clerk-{Guid.NewGuid():N}@example.test";
        await (await owner.Client.PostAsJsonAsync(U(owner, "/invitations"), new Contracts.Identity.InviteRequest(email, clerk.Id))).EnsureOkAsync();
        var token = f.Emails.LinkParameters(email, "Invitation")["t"];
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        await (await client.PostAsJsonAsync("/api/v1/auth/invitations/accept", new Contracts.Identity.InvitationAcceptRequest(token, "Clerk", ApiHelpers.Password))).EnsureOkAsync();
        var clerkUser = await f.SignInFirstTimeAsync(client, email, ApiHelpers.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerkUser.Client.GetAsync(U(owner, "/drivers"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerkUser.Client.GetAsync(U(owner, "/partners"))).StatusCode);
        var options = await clerkUser.Client.GetFromJsonAsync<List<DriverOption>>(U(owner, "/drivers/options"));
        Assert.Equal(d.Id, Assert.Single(options!).Id);
        Assert.Equal(HttpStatusCode.OK, (await clerkUser.Client.GetAsync(U(owner, "/customers/options"))).StatusCode);
    }

    [Fact]
    public async Task Review_fixes_hidden_fields_keep_values_and_configuration_cannot_contradict_the_rules()
    {
        var owner = await f.SignUpOwnerAsync();
        // M1: a hidden standard field keeps its stored value when the form saves without it.
        var c = await ReadAsync<CustomerResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/customers"), Customer("Hidden Terms Co")));
        Assert.Equal(30, c.PaymentTermsDays);
        await (await owner.Client.PutAsJsonAsync(U(owner, "/standard-fields/TRN.Customer"),
            new StandardFieldsRequest([new StandardFieldChange("paymentTermsDays", null, true, false, null)]))).EnsureOkAsync();
        var edited = await ReadAsync<CustomerResponse>(await owner.Client.PutAsJsonAsync(U(owner, $"/customers/{c.Id}"),
            Customer("Hidden Terms Co", rowVersion: c.RowVersion) with { PaymentTermsDays = null }));
        Assert.Equal(30, edited.PaymentTermsDays);

        // Fields a rule needs in some cases cannot be hidden; a partner can never be made always required (L6).
        Assert.Equal("field.conditional", await (await owner.Client.PutAsJsonAsync(U(owner, "/standard-fields/TRN.Driver"),
            new StandardFieldsRequest([new StandardFieldChange("iban", null, true, false, null)]))).ProblemCodeAsync());
        Assert.Equal("field.cannotRequire", await (await owner.Client.PutAsJsonAsync(U(owner, "/standard-fields/TRN.Driver"),
            new StandardFieldsRequest([new StandardFieldChange("partnerId", null, false, true, null)]))).ProblemCodeAsync());

        // M2: an external type whose drivers have partners cannot become own fleet.
        var rented = await ExternalTypeAsync(owner);
        var partner = await ReadAsync<PartnerResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/partners"),
            new PartnerRequest("Partner M2", null, null, null, null, null, null, null, null, null, null)));
        await CreateDriverAsync(owner, Driver(rented.Id, partnerId: partner.Id));
        var reclassify = await owner.Client.PutAsJsonAsync(U(owner, $"/ownership-types/{rented.Id}"), new OwnershipTypeRequest("Rented", "Own", null, rented.RowVersion));
        Assert.Equal("ownership.fleetInUse", await reclassify.ProblemCodeAsync());

        // M3: a customer links only to a receivable (Customer) party of shared Accounting.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync(U(owner, "/customers"),
            Customer("Bad Link") with { ApArPartyId = partner.Id })).StatusCode);
    }
}
