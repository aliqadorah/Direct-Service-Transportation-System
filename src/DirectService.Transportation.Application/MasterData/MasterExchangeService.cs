using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.MasterData;

/// <summary>
/// Import and export of every master-data list (FR-164, FR-165, FR-180; ADR-13). Exports: Excel or CSV of the filtered list with
/// the configured labels and the visible custom fields. Imports: columns matched by header (Arabic, English, field key, the company's
/// label) or by an explicit mapping; every row is validated by the same rules as the form (the record services' ApplyAsync), plus
/// duplicate detection on the unique keys within the file and against the company's records. Strict and add-only (DT-44): any
/// rejected row blocks the import; a committed import is one transaction logged in cfg.ImportBatches.
/// </summary>
public sealed class MasterExchangeService(IAppDbContext db, ISpreadsheetService sheets, IRequestContext ctx, IUrlTokenService tokens,
    MasterDataSupport support, StandardFieldService fields, CustomFieldService customFields, Reports.ReportExporter exporter,
    DriverService drivers, CustomerService customers, PartnerService partners, MaterialService materials,
    LoadingSiteService loadingSites, UnloadingSiteService unloadingSites, OwnershipTypeService ownershipTypes)
{
    public static readonly IReadOnlyList<string> Kinds =
        ["drivers", "customers", "partners", "materials", "loading-sites", "unloading-sites", "ownership-types"];

    /// <summary>A column of a list: field key, default headers and extra header aliases accepted on import.</summary>
    private sealed record Column(string Key, string Ar, string En, params string[] Aliases);

    private sealed record Prepared(int Row, string? Key, Action Add);

    private bool En => ctx.Language == "en";

    // ============================================================ columns

    private static readonly Column[] DriverColumns =
    [
        new("fullName", "الاسم الكامل", "Full name", "اسم السائق", "Name", "الاسم"),
        new("nationalId", "الرقم الوطني", "National ID"),
        new("phone1", "الهاتف 1", "Phone 1", "الهاتف", "Phone", "الجوال"),
        new("phone2", "الهاتف 2", "Phone 2"),
        new("address", "العنوان", "Address"),
        new("licenceNo", "رقم الرخصة", "Licence number"),
        new("licenceType", "فئة الرخصة", "Licence type", "نوع الرخصة"),
        new("licenceExpiry", "تاريخ انتهاء الرخصة", "Licence expiry", "انتهاء الرخصة"),
        new("paymentMethod", "طريقة الدفع", "Payment method"),
        new("bankName", "اسم البنك", "Bank name", "البنك"),
        new("iban", "رقم الآيبان", "IBAN", "الآيبان"),
        new("cliqAlias", "اسم CliQ المستعار", "CliQ alias", "CliQ"),
        new("plateNumber", "رقم اللوحة", "Plate number", "اللوحة", "Plate"),
        new("truckType", "نوع الشاحنة", "Truck type"),
        new("trailerNo", "رقم المقطورة", "Trailer number"),
        new("capacityTons", "الحمولة (طن)", "Capacity (t)", "الحمولة", "Capacity"),
        new("ownershipTypeId", "نوع الملكية", "Ownership type"),
        new("partnerId", "الشركة الشريكة", "Partner company", "الشريك", "Partner"),
        new("registrationExpiry", "تاريخ انتهاء الترخيص", "Registration expiry", "انتهاء الترخيص"),
        new("insuranceExpiry", "تاريخ انتهاء التأمين", "Insurance expiry", "انتهاء التأمين"),
        new("inspectionExpiry", "تاريخ انتهاء الفحص", "Inspection expiry", "انتهاء الفحص"),
        new("notes", "ملاحظات", "Notes"),
        new("status", "الحالة", "Status"),
    ];

    private static readonly Column[] CustomerColumns =
    [
        new("name", "اسم العميل", "Name", "العميل", "Customer", "الاسم"),
        new("taxNumber", "الرقم الضريبي", "Tax number"),
        new("registrationNo", "السجل التجاري / الرقم الوطني", "Commercial registration / national ID", "السجل التجاري", "Registration number"),
        new("phone", "الهاتف", "Phone"),
        new("email", "البريد الإلكتروني", "Email"),
        new("address", "العنوان", "Address"),
        new("city", "المدينة", "City"),
        new("contactPerson", "الشخص المسؤول", "Contact person", "جهة الاتصال"),
        new("paymentTermsDays", "مدة السداد (أيام)", "Payment terms (days)", "مدة السداد", "Payment terms"),
        new("notes", "ملاحظات", "Notes"),
        new("status", "الحالة", "Status"),
    ];

    private static readonly Column[] PartnerColumns =
    [
        new("name", "اسم الشريك", "Name", "الشريك", "Partner", "الاسم"),
        new("taxNumber", "الرقم الضريبي", "Tax number"),
        new("phone", "الهاتف", "Phone"),
        new("address", "العنوان", "Address"),
        new("contactPerson", "الشخص المسؤول", "Contact person", "جهة الاتصال"),
        new("bankName", "اسم البنك", "Bank name", "البنك"),
        new("iban", "رقم الآيبان", "IBAN", "الآيبان"),
        new("cliqAlias", "اسم CliQ المستعار", "CliQ alias", "CliQ"),
        new("notes", "ملاحظات", "Notes"),
        new("status", "الحالة", "Status"),
    ];

    private static readonly Column[] MaterialColumns =
    [
        new("name", "اسم المادة", "Name", "المادة", "Material", "الاسم"),
        new("unit", "الوحدة", "Unit"),
        new("status", "الحالة", "Status"),
    ];

    private static readonly Column[] SiteColumns =
    [
        new("name", "اسم الموقع", "Name", "الموقع", "Site", "الاسم"),
        new("area", "المنطقة / المدينة", "Area", "المنطقة", "المدينة", "City"),
        new("status", "الحالة", "Status"),
    ];

    private static readonly Column[] OwnershipColumns =
    [
        new("name", "نوع الملكية", "Name", "الاسم", "Ownership type"),
        new("fleetClass", "تصنيف الأسطول", "Fleet class", "الأسطول", "Fleet"),
    ];

    /// <summary>The columns an import of the list reads (key, Arabic and English header), in template order; status is export-only.</summary>
    public static IReadOnlyList<(string Key, string Ar, string En)> ImportColumns(string kind) =>
        ColumnsOf(kind).Where(c => c.Key != "status").Select(c => (c.Key, c.Ar, c.En)).ToList();

    private static Column[] ColumnsOf(string kind) => kind switch
    {
        "drivers" => DriverColumns,
        "customers" => CustomerColumns,
        "partners" => PartnerColumns,
        "materials" => MaterialColumns,
        "loading-sites" or "unloading-sites" => SiteColumns,
        "ownership-types" => OwnershipColumns,
        _ => throw Fail.NotFound(),
    };

    private static string? CustomEntity(string kind) => kind switch
    {
        "drivers" => FieldCatalog.Driver,
        "customers" => FieldCatalog.Customer,
        _ => null,
    };

    private static string EntityTypeOf(string kind) => kind switch
    {
        "drivers" => DriverService.EntityType,
        "customers" => CustomerService.EntityType,
        "partners" => PartnerService.EntityType,
        "materials" => MaterialService.EntityType,
        "loading-sites" => "TRN.LoadingSite",
        "unloading-sites" => "TRN.UnloadingSite",
        "ownership-types" => OwnershipTypeService.EntityType,
        _ => throw Fail.NotFound(),
    };

    /// <summary>Header of a column in the user's language; driver and customer fields use the company's label (FR-182).</summary>
    private async Task<string> HeaderAsync(string kind, Column c, CancellationToken ct)
    {
        var entity = CustomEntity(kind);
        if (entity is not null && FieldCatalog.Find(entity, c.Key) is not null)
            return (await fields.RulesAsync(entity, ct).ConfigureAwait(false)).Label(c.Key);
        return En ? c.En : c.Ar;
    }

    // ============================================================ export

    private string ListTitle(string kind) => (ctx.Language == "en", kind) switch
    {
        (true, "drivers") => "Drivers", (false, "drivers") => "السائقون", (true, "customers") => "Customers", (false, "customers") => "العملاء",
        (true, "partners") => "Partners", (false, "partners") => "الشركاء", (true, "materials") => "Materials", (false, "materials") => "المواد",
        (true, "loading-sites") => "Loading sites", (false, "loading-sites") => "مواقع التحميل", (true, "unloading-sites") => "Unloading sites",
        (false, "unloading-sites") => "مواقع التنزيل", _ => kind,
    };

    public async Task<GeneratedFile> ExportAsync(string kind, MasterListQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var csv = (format?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "xlsx" or "pdf" or "html" => false,
            "csv" => true,
            _ => throw Fail.Field("format", ErrorCodes.ValidationFormat, new FieldRef("format")),
        };
        var entity = CustomEntity(kind);
        var rules = entity is null ? null : await fields.RulesAsync(entity, ct).ConfigureAwait(false);
        // Hidden standard fields are left out of exports like hidden custom fields (FR-182).
        var all = ColumnsOf(kind);
        var shown = all.Select((c, i) => (c, i)).Where(x => rules is null || !rules.IsHidden(x.c.Key)).ToList();
        var columns = shown.Select(x => x.c).ToList();
        var custom = entity is null ? [] : (await customFields.DefinitionsAsync(entity, ct).ConfigureAwait(false)).Where(d => !d.IsHidden).ToList();
        var headers = new List<string>();
        foreach (var c in columns) headers.Add(await HeaderAsync(kind, c, ct).ConfigureAwait(false));
        headers.AddRange(custom.Select(customFields.Label));

        var rows = new List<IReadOnlyList<object?>>();
        switch (kind)
        {
            case "drivers":
                var (driverList, _) = await drivers.AllAsync(q, ct).ConfigureAwait(false);
                foreach (var d in driverList)
                {
                    rows.Add([.. new object?[]
                    {
                        d.FullName, d.NationalId, d.Phone1, d.Phone2, d.Address, d.LicenceNo, d.LicenceType, d.LicenceExpiry, d.PaymentMethod,
                        d.BankName, d.Iban, d.CliqAlias, d.PlateNumber, d.TruckType, d.TrailerNo, d.CapacityTons, OwnershipTypeNames.Display(d.OwnershipType.Name, ctx.Language),
                        d.Partner?.Name, d.RegistrationExpiry, d.InsuranceExpiry, d.InspectionExpiry, d.Notes, d.Status,
                    }, .. CustomCells(custom, d.CustomValues)]);
                }
                break;
            case "customers":
                foreach (var c in await customers.AllAsync(q, ct).ConfigureAwait(false))
                {
                    rows.Add([.. new object?[]
                    {
                        c.Name, c.TaxNumber, c.RegistrationNo, c.Phone, c.Email, c.Address, c.City, c.ContactPerson, c.PaymentTermsDays, c.Notes, c.Status,
                    }, .. CustomCells(custom, c.CustomValues)]);
                }
                break;
            case "partners":
                foreach (var p in await partners.AllAsync(q, ct).ConfigureAwait(false))
                    rows.Add([p.Name, p.TaxNumber, p.Phone, p.Address, p.ContactPerson, p.BankName, p.Iban, p.CliqAlias, p.Notes, p.Status]);
                break;
            case "materials":
                foreach (var m in await materials.AllAsync(q, ct).ConfigureAwait(false)) rows.Add([m.Name, m.Unit, m.Status]);
                break;
            case "loading-sites":
                foreach (var s in await loadingSites.AllAsync(q, ct).ConfigureAwait(false)) rows.Add([s.Name, s.Area, s.Status]);
                break;
            case "unloading-sites":
                foreach (var s in await unloadingSites.AllAsync(q, ct).ConfigureAwait(false)) rows.Add([s.Name, s.Area, s.Status]);
                break;
            case "ownership-types":
                foreach (var t in await ownershipTypes.ListAsync(ct).ConfigureAwait(false)) rows.Add([t.Name, t.FleetClass]);
                break;
        }
        if (shown.Count != all.Length)
        {
            var keep = shown.Select(x => x.i).ToList();
            rows = rows.Select(r => (IReadOnlyList<object?>)[.. keep.Select(i => r[i]), .. r.Skip(all.Length)]).ToList();
        }
        if (Reports.ReportExporter.IsPrintout(format))   // FR-150 printout of the master list
            return await exporter.ListPrintoutAsync(ListTitle(kind), kind, headers, rows, format!, ct).ConfigureAwait(false);
        var name = kind + (csv ? ".csv" : ".xlsx");
        return csv
            ? new GeneratedFile(name, "text/csv", sheets.WriteCsv(headers, rows))
            : new GeneratedFile(name, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", sheets.WriteXlsx(kind, headers, rows));
    }

    private static IEnumerable<object?> CustomCells(IReadOnlyList<CustomFieldDefinition> defs, string? json)
    {
        var values = CustomFieldService.Read(json);
        foreach (var d in defs)
        {
            if (!values.TryGetValue(d.FieldKey, out var v)) yield return null;
            else if (d.FieldType == Contracts.Configuration.CustomFieldTypes.Number && v.TryGetDecimal(out var n)) yield return n;
            else if (d.FieldType == Contracts.Configuration.CustomFieldTypes.Checkbox) yield return v.ValueKind == JsonValueKind.True;
            else yield return CustomFieldService.Text(v);
        }
    }

    // ============================================================ import

    public async Task<MasterImportPreview> PreviewAsync(string kind, Stream content, string fileName, long length, string? mapping, CancellationToken ct)
    {
        var (_, preview) = await AnalyseAsync(kind, content, fileName, length, mapping, ct).ConfigureAwait(false);
        return preview;
    }

    public async Task<ImportResult> ImportAsync(string kind, Stream content, string fileName, long length, string? mapping, CancellationToken ct)
    {
        var (prepared, preview) = await AnalyseAsync(kind, content, fileName, length, mapping, ct).ConfigureAwait(false);
        if (preview.RejectedRows > 0) throw Fail.Rule(ErrorCodes.ImportHasErrors, preview.RejectedRows);
        var batch = await db.InTransactionAsync(async c =>
        {
            foreach (var p in prepared) p.Add();
            var b = ImportParse.Batch(EntityTypeOf(kind), fileName, prepared.Count, prepared.Count, ctx.UserId!.Value);
            db.Set<ImportBatch>().Add(b);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return b;
        }, ct).ConfigureAwait(false);
        return new ImportResult(tokens.Encode<ImportBatch>(batch.PublicId), prepared.Count, prepared.Count);
    }

    /// <summary>Column matching: explicit mapping (field key → header) first, then the default headers, aliases and the company's labels.</summary>
    private sealed class Columns(SheetData sheet, IReadOnlyDictionary<string, string> mapping)
    {
        public Dictionary<string, string> Matched { get; } = new(StringComparer.Ordinal);

        public int Find(string key, IEnumerable<string> aliases)
        {
            var index = mapping.TryGetValue(key, out var header) ? sheet.Column(header) : sheet.Column([key, .. aliases]);
            if (index >= 0) Matched[key] = sheet.Headers[index];
            return index;
        }

        public IReadOnlyList<string> Unused =>
            sheet.Headers.Where(h => !string.IsNullOrWhiteSpace(h) && !Matched.Values.Contains(h, StringComparer.Ordinal)).ToList();
    }

    private async Task<(List<Prepared> Rows, MasterImportPreview Preview)> AnalyseAsync(
        string kind, Stream content, string fileName, long length, string? mappingJson, CancellationToken ct)
    {
        var columnsSpec = ColumnsOf(kind);
        var mapping = ParseMapping(mappingJson);
        var sheet = sheets.Read(content, fileName, length);
        var columns = new Columns(sheet, mapping);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in columnsSpec.Where(c => c.Key != "status"))
        {
            var aliases = new List<string>(c.Aliases) { c.Ar, c.En };
            if (CustomEntity(kind) is { } e && FieldCatalog.Find(e, c.Key) is not null) aliases.Add(await HeaderAsync(kind, c, ct).ConfigureAwait(false));
            index[c.Key] = columns.Find(c.Key, aliases);
        }
        var nameKey = columnsSpec[0].Key;
        ImportParse.RequireColumns(sheet, (index[nameKey], En ? columnsSpec[0].En : columnsSpec[0].Ar));

        var customDefs = CustomEntity(kind) is { } entity
            ? (await customFields.DefinitionsAsync(entity, ct).ConfigureAwait(false)).Where(d => !d.IsHidden).ToList()
            : [];
        var customIndex = customDefs.ToDictionary(d => d.FieldKey,
            d => columns.Find("custom." + d.FieldKey, new[] { d.FieldKey, d.LabelAr, d.LabelEn }.Where(a => a is not null).Select(a => a!)), StringComparer.Ordinal);

        var problems = new ImportProblems(ctx.Language);
        var prepared = new List<Prepared>();
        var keys = new List<(int, string?)>();
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var row in sheet.Rows)
        {
            string? Cell(string key) => index.TryGetValue(key, out var i) ? row[i] : null;
            var rowKey = TextNormalizer.CleanText(Cell(nameKey));
            keys.Add((row.RowNumber, rowKey));

            // Custom values (drivers, customers) parsed by type; a value that does not parse is a problem of the row.
            var custom = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var d in customDefs)
            {
                var text = customIndex[d.FieldKey] >= 0 ? row[customIndex[d.FieldKey]] : null;
                if (text is null) continue;
                if (!CustomFieldService.TryParseCell(d, text, out var v)) problems.Add(row.RowNumber, rowKey, ErrorCodes.ImportInvalid, customFields.Label(d), text);
                else if (v is JsonElement value) custom[d.FieldKey] = value;
            }

            try
            {
                var item = await PrepareAsync(kind, row.RowNumber, rowKey, Cell, custom, problems, ct).ConfigureAwait(false);
                if (item is null) continue;
                foreach (var (uniqueKey, value) in item.Unique)
                {
                    if (value is null) continue;
                    var set = seen.TryGetValue(uniqueKey, out var s) ? s : seen[uniqueKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!set.Add(value)) problems.Add(row.RowNumber, rowKey, ErrorCodes.ImportDuplicate, value);
                }
                if (!problems.Has(row.RowNumber)) prepared.Add(item.Prepared);
            }
            catch (AppException ex)
            {
                if (ex.Errors.Count > 0)
                {
                    foreach (var e in ex.Errors) problems.Add(row.RowNumber, rowKey, e.Code, e.Args);
                }
                else
                {
                    problems.Add(row.RowNumber, rowKey, ex.Code == ErrorCodes.DuplicateKey ? ErrorCodes.ImportDuplicate : ex.Code, ex.Args);
                }
            }
        }

        var p = problems.Preview(keys);
        var preview = new MasterImportPreview(p.TotalRows, p.ValidRows, p.RejectedRows, p.Rows, new ImportColumns(columns.Matched, columns.Unused));
        return (prepared, preview);
    }

    private static Dictionary<string, string> ParseMapping(string? json) => ImportMapping.Parse(json);

    private sealed record Item(Prepared Prepared, IReadOnlyList<(string Key, string? Value)> Unique);

    /// <summary>
    /// Builds the record of one row with the form's rules. Returns null when a cell could not be read (problem already added);
    /// rule violations surface as <see cref="AppException"/>.
    /// </summary>
    private async Task<Item?> PrepareAsync(string kind, int row, string? rowKey, Func<string, string?> cell, Dictionary<string, JsonElement> custom,
        ImportProblems problems, CancellationToken ct)
    {
        switch (kind)
        {
            case "drivers":
            {
                var ok = true;
                DateOnly? Date(string key, string label)
                {
                    var text = cell(key);
                    if (text is null) return null;
                    if (ImportParse.TryDate(text, out var d)) return d;
                    problems.Add(row, rowKey, ErrorCodes.ImportInvalid, label, text);
                    ok = false;
                    return null;
                }
                var rules = await fields.RulesAsync(FieldCatalog.Driver, ct).ConfigureAwait(false);
                decimal? capacity = null;
                if (cell("capacityTons") is { } capText)
                {
                    if (ImportParse.TryAmount(capText, 3, out var cap)) capacity = cap;
                    else { problems.Add(row, rowKey, ErrorCodes.ImportInvalid, rules.Label("capacityTons"), capText); ok = false; }
                }
                var method = cell("paymentMethod") is { } m ? PaymentMethod(m) : null;
                if (cell("paymentMethod") is { } mText && method is null) { problems.Add(row, rowKey, ErrorCodes.ImportInvalid, rules.Label("paymentMethod"), mText); ok = false; }
                var ownershipToken = await LinkAsync<OwnershipType>(OwnershipTypeNames.IsBuiltIn(cell("ownershipTypeId")) ? Platform.CompanyProvisioningService.BuiltInOwnershipType : cell("ownershipTypeId"), t => t.Name, row, rowKey, rules.Label("ownershipTypeId"), problems, ct).ConfigureAwait(false);
                var partnerToken = await LinkAsync<Partner>(cell("partnerId"), p => p.Name, row, rowKey, rules.Label("partnerId"), problems, ct).ConfigureAwait(false);
                if (ownershipToken == "" || partnerToken == "") ok = false;
                var request = new DriverRequest(cell("fullName"), cell("nationalId"), cell("phone1"), cell("phone2"), cell("address"), cell("licenceNo"),
                    cell("licenceType"), Date("licenceExpiry", rules.Label("licenceExpiry")), method, cell("bankName"), cell("iban"), cell("cliqAlias"),
                    cell("plateNumber"), cell("truckType"), cell("trailerNo"), capacity, NullIfEmpty(ownershipToken), NullIfEmpty(partnerToken),
                    Date("registrationExpiry", rules.Label("registrationExpiry")), Date("insuranceExpiry", rules.Label("insuranceExpiry")),
                    Date("inspectionExpiry", rules.Label("inspectionExpiry")), cell("notes"), custom, null, null);
                if (!ok) return null;
                var d = new Driver();
                await drivers.ApplyAsync(d, request, isNew: true, ct).ConfigureAwait(false);
                return new Item(new Prepared(row, rowKey, () => db.Set<Driver>().Add(d)), [("nationalId", d.NationalId), ("plateNumber", d.PlateNumber)]);
            }
            case "customers":
            {
                int? terms = null;
                if (cell("paymentTermsDays") is { } t)
                {
                    if (int.TryParse(TextNormalizer.ToAsciiDigits(t).Trim(), out var days)) terms = days;
                    else
                    {
                        problems.Add(row, rowKey, ErrorCodes.ImportInvalid, (await fields.RulesAsync(FieldCatalog.Customer, ct).ConfigureAwait(false)).Label("paymentTermsDays"), t);
                        return null;
                    }
                }
                var request = new CustomerRequest(cell("name"), cell("taxNumber"), cell("registrationNo"), cell("phone"), cell("email"), cell("address"),
                    cell("city"), cell("contactPerson"), terms, null, cell("notes"), custom, null, null);
                var c = new Customer();
                await customers.ApplyAsync(c, request, isNew: true, ct).ConfigureAwait(false);
                return new Item(new Prepared(row, rowKey, () => db.Set<Customer>().Add(c)), [("name", c.Name)]);
            }
            case "partners":
            {
                var request = new PartnerRequest(cell("name"), cell("taxNumber"), cell("phone"), cell("address"), cell("contactPerson"), cell("bankName"),
                    cell("iban"), cell("cliqAlias"), cell("notes"), null, null);
                var p = new Partner();
                await partners.ApplyAsync(p, request, ct).ConfigureAwait(false);
                return new Item(new Prepared(row, rowKey, () => db.Set<Partner>().Add(p)), [("name", p.Name)]);
            }
            case "materials":
            {
                var m = new Material();
                await materials.ApplyAsync(m, new MaterialRequest(cell("name"), cell("unit"), null, null), ct).ConfigureAwait(false);
                return new Item(new Prepared(row, rowKey, () => db.Set<Material>().Add(m)), [("name", m.Name)]);
            }
            case "loading-sites":
            {
                var s = new LoadingSite();
                await loadingSites.ApplyAsync(s, new SiteRequest(cell("name"), cell("area"), null, null), ct).ConfigureAwait(false);
                return new Item(new Prepared(row, rowKey, () => db.Set<LoadingSite>().Add(s)), [("name", s.Name)]);
            }
            case "unloading-sites":
            {
                var s = new UnloadingSite();
                await unloadingSites.ApplyAsync(s, new SiteRequest(cell("name"), cell("area"), null, null), ct).ConfigureAwait(false);
                return new Item(new Prepared(row, rowKey, () => db.Set<UnloadingSite>().Add(s)), [("name", s.Name)]);
            }
            case "ownership-types":
            {
                var fleet = cell("fleetClass") is { } f ? FleetClass(f) : null;
                if (cell("fleetClass") is { } fText && fleet is null)
                {
                    problems.Add(row, rowKey, ErrorCodes.ImportInvalid, En ? "fleet class" : "تصنيف الأسطول", fText);
                    return null;
                }
                var t = new OwnershipType();
                await ownershipTypes.ApplyAsync(t, new OwnershipTypeRequest(cell("name"), fleet, null, null), ct).ConfigureAwait(false);
                return new Item(new Prepared(row, rowKey, () => db.Set<OwnershipType>().Add(t)), [("name", t.Name)]);
            }
            default:
                throw Fail.NotFound();
        }
    }

    /// <summary>
    /// Token of a linked record named in a cell (ownership type, partner), matched by name (case/space-insensitive). Null when the
    /// cell is empty; "" when unknown or archived (problem added).
    /// </summary>
    private async Task<string?> LinkAsync<T>(string? text, Func<T, string> name, int row, string? rowKey, string label, ImportProblems problems, CancellationToken ct)
        where T : AuditableEntity
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var key = TextNormalizer.Key(MasterDataSupport.Name(text));
        var all = await LinkCandidatesAsync<T>(ct).ConfigureAwait(false);
        var match = all.Where(x => TextNormalizer.Key(name(x)) == key).ToList();
        var active = match.Where(x => x is not IArchivable a || a.Status == MasterCodes.Active).ToList();
        if (active.Count == 1) return support.Token<T>(active[0].PublicId);
        problems.Add(row, rowKey, ErrorCodes.ImportUnknown, label, text);
        return string.Empty;
    }

    private readonly Dictionary<Type, object> _candidates = [];

    private async Task<IReadOnlyList<T>> LinkCandidatesAsync<T>(CancellationToken ct) where T : AuditableEntity
    {
        if (_candidates.TryGetValue(typeof(T), out var cached)) return (IReadOnlyList<T>)cached;
        var list = await db.Set<T>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        _candidates[typeof(T)] = list;
        return list;
    }

    private static string? NullIfEmpty(string? token) => string.IsNullOrEmpty(token) ? null : token;

    private static string? PaymentMethod(string text) => TextNormalizer.Key(text) switch
    {
        "CASH" or "نقد" or "نقدي" or "نقداً" or "نقدا" or "كاش" => "Cash",
        "BANK" or "بنك" or "تحويلبنكي" or "حوالةبنكية" => "Bank",
        "CLIQ" or "كليك" => "CliQ",
        _ => null,
    };

    private static string? FleetClass(string text) => TextNormalizer.Key(text) switch
    {
        "OWN" or "خاص" or "أسطولخاص" or "اسطولخاص" or "ملكالشركة" => MasterCodes.Own,
        "EXTERNAL" or "خارجي" or "أسطولخارجي" or "اسطولخارجي" => MasterCodes.External,
        _ => null,
    };
}
