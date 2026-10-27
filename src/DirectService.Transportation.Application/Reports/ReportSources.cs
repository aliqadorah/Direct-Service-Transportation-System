using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Application.Reports;

/// <summary>Field types of the report builder.</summary>
public static class FieldTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Money = "money";
    public const string Quantity = "quantity";
    public const string Date = "date";
    public const string Bool = "bool";
    public const string Ref = "ref";

    public static bool IsNumeric(string type) => type is Number or Money or Quantity;
}

/// <summary>
/// A field of a data source: key, labels, type, the value expression over the source row (for refs: the name) and, for refs, the public id
/// and the entity type of its token.
/// </summary>
public sealed record FieldDef(string Key, string Ar, string En, string Type, LambdaExpression Value, LambdaExpression? Pid = null, Type? Entity = null,
    bool Groupable = true, bool Custom = false, bool Nullable = false)
{
    public bool Measurable => FieldTypes.IsNumeric(Type);
}

/// <summary>
/// A data source of the report builder (FR-143): key, labels, the permissions that open it (any of them, FR-147), the queryable rows, the
/// fields, the main date (period override), the custom-field entity and JSON column, the default status filter and the row link.
/// </summary>
public sealed record SourceDef(string Key, string Ar, string En, string[] Permissions, Type Row, Func<IAppDbContext, IQueryable> Query, IReadOnlyList<FieldDef> Fields,
    string? DateField, string? CustomEntity, LambdaExpression? CustomJson, LambdaExpression? DefaultFilter, LambdaExpression RowPid, string RowLink);

/// <summary>Typed helper to declare the fields of a source.</summary>
internal sealed class Fields<T>
{
    public List<FieldDef> All { get; } = [];

    public Fields<T> Text(string key, string ar, string en, Expression<Func<T, string?>> e, bool groupable = true)
    {
        All.Add(new FieldDef(key, ar, en, FieldTypes.Text, e, Groupable: groupable));
        return this;
    }

    public Fields<T> Date(string key, string ar, string en, Expression<Func<T, DateOnly>> e)
    {
        All.Add(new FieldDef(key, ar, en, FieldTypes.Date, e));
        return this;
    }

    public Fields<T> NullableDate(string key, string ar, string en, Expression<Func<T, DateOnly?>> e)
    {
        All.Add(new FieldDef(key, ar, en, FieldTypes.Date, e, Groupable: false, Nullable: true));
        return this;
    }

    public Fields<T> Amount(string key, string ar, string en, string type, Expression<Func<T, decimal>> e)
    {
        All.Add(new FieldDef(key, ar, en, type, e, Groupable: false));
        return this;
    }

    public Fields<T> NullableAmount(string key, string ar, string en, string type, Expression<Func<T, decimal?>> e)
    {
        All.Add(new FieldDef(key, ar, en, type, e, Groupable: false, Nullable: true));
        return this;
    }

    public Fields<T> Count(string key, string ar, string en, Expression<Func<T, int>> e)
    {
        All.Add(new FieldDef(key, ar, en, FieldTypes.Number, e, Groupable: false));
        return this;
    }

    public Fields<T> Bool(string key, string ar, string en, Expression<Func<T, bool>> e)
    {
        All.Add(new FieldDef(key, ar, en, FieldTypes.Bool, e));
        return this;
    }

    public Fields<T> Ref<TEntity>(string key, string ar, string en, Expression<Func<T, string?>> name, Expression<Func<T, Guid?>> pid)
    {
        All.Add(new FieldDef(key, ar, en, FieldTypes.Ref, name, pid, typeof(TEntity)));
        return this;
    }
}

/// <summary>The ten data sources of FR-143.</summary>
public static class ReportSources
{
    private const string Active = "Active";

    private static SourceDef Source<T>(string key, string ar, string en, string[] permissions, Func<IAppDbContext, IQueryable<T>> query, Fields<T> fields, string? dateField,
        string? customEntity, Expression<Func<T, string?>>? customJson, Expression<Func<T, bool>>? defaultFilter, Expression<Func<T, Guid>> pid, string link) where T : class =>
        new(key, ar, en, permissions, typeof(T), db => query(db), fields.All, dateField, customEntity, customJson, defaultFilter, pid, link);

    public static readonly IReadOnlyList<SourceDef> All =
    [
        Source<Trip>("trips", "الرحلات", "Trips", [PermissionKeys.TripsView], db => db.Set<Trip>(), new Fields<Trip>()
                .Text("receiptNo", "رقم السند", "Receipt no.", t => t.ReceiptNo, groupable: false)
                .Date("tripDate", "تاريخ الرحلة", "Trip date", t => t.TripDate)
                .Date("loadingDate", "تاريخ التحميل", "Loading date", t => t.LoadingDate)
                .Ref<Driver>("driver", "السائق", "Driver", t => t.Driver.FullName, t => t.Driver.PublicId)
                .Ref<Customer>("customer", "العميل", "Customer", t => t.Customer.Name, t => t.Customer.PublicId)
                .Ref<Partner>("partner", "الشريك", "Partner", t => t.Partner != null ? t.Partner.Name : null, t => t.Partner != null ? t.Partner.PublicId : null)
                .Ref<LoadingSite>("loadingSite", "موقع التحميل", "Loading site", t => t.LoadingSite.Name, t => t.LoadingSite.PublicId)
                .Ref<UnloadingSite>("unloadingSite", "موقع التنزيل", "Unloading site", t => t.UnloadingSite.Name, t => t.UnloadingSite.PublicId)
                .Ref<Material>("material", "المادة", "Material", t => t.Material.Name, t => t.Material.PublicId)
                .Text("fleetClass", "فئة الأسطول", "Fleet class", t => t.FleetClass)
                .Text("plate", "رقم اللوحة", "Plate", t => t.TruckPlateAtSave)
                .Amount("loadedWeight", "الوزن المحمّل", "Loaded weight", FieldTypes.Quantity, t => t.LoadedWeight)
                .NullableAmount("unloadedWeight", "الوزن المنزّل", "Unloaded weight", FieldTypes.Quantity, t => t.UnloadedWeight)
                .Amount("billingQty", "كمية الفوترة", "Billing quantity", FieldTypes.Quantity, t => t.BillingQty)
                .Amount("customerPrice", "سعر العميل", "Customer price", FieldTypes.Money, t => t.CustomerPrice)
                .Amount("driverRate", "سعر السائق", "Driver rate", FieldTypes.Money, t => t.DriverRate)
                .Amount("revenue", "الإيراد", "Revenue", FieldTypes.Money, t => t.Revenue)
                .Amount("driverWage", "أجر السائق", "Driver wage", FieldTypes.Money, t => t.DriverWage)
                .Amount("partnerCost", "تكلفة الشريك", "Partner cost", FieldTypes.Money, t => t.PartnerCost)
                .Amount("margin", "الهامش", "Margin", FieldTypes.Money, t => t.Revenue - t.DriverWage - t.PartnerCost)
                .NullableAmount("shortageWeight", "النقص (طن)", "Shortage (t)", FieldTypes.Quantity, t => t.ShortageWeight)
                .NullableAmount("shortagePct", "النقص %", "Shortage %", FieldTypes.Number, t => t.ShortagePct)
                .Bool("shortageFlag", "نقص مؤشّر", "Shortage flagged", t => t.IsShortageFlagged)
                .Bool("tariffBreach", "مخالفة تعرفة", "Tariff breach", t => t.IsTariffBreach)
                .Bool("needsReview", "تحتاج مراجعة", "Needs review", t => t.NeedsReview)
                .Bool("invoiced", "مفوترة", "Invoiced", t => t.InvoiceId != null)
                // DT-271: settled = paid (driver trips in a settlement; partner trips covered by the partner's payments, oldest first).
                .Bool("settled", "مسوّاة / مدفوعة", "Settled / paid", Trips.TripService.IsPaid)
                .Amount("partnerOpen", "المتبقي للشريك", "Open to partner", FieldTypes.Money, t => t.PartnerCoverage != null ? t.PartnerCoverage.OpenAmount : 0m)
                .Text("status", "الحالة", "Status", t => t.Status),
            "tripDate", FieldCatalog.Trip, t => t.CustomValues, t => t.Status == Active, t => t.PublicId, "trip"),

        Source<Invoice>("invoices", "الفواتير", "Invoices", [PermissionKeys.InvView], db => db.Set<Invoice>(), new Fields<Invoice>()
                .Text("number", "الرقم", "Number", i => i.Number, groupable: false)
                .Date("invoiceDate", "التاريخ", "Date", i => i.InvoiceDate)
                .Date("dueDate", "الاستحقاق", "Due date", i => i.DueDate)
                .Ref<Customer>("customer", "العميل", "Customer", i => i.Customer.Name, i => i.Customer.PublicId)
                .Text("type", "النوع", "Type", i => i.InvoiceType)
                .Amount("subtotal", "المجموع", "Subtotal", FieldTypes.Money, i => i.Subtotal)
                .Amount("discount", "الخصم", "Discount", FieldTypes.Money, i => i.Discount)
                .Amount("extraFees", "الرسوم", "Extra fees", FieldTypes.Money, i => i.ExtraFees)
                .Amount("tax", "الضريبة", "Tax", FieldTypes.Money, i => i.Tax)
                .Amount("total", "الإجمالي", "Total", FieldTypes.Money, i => i.Total)
                .Amount("paid", "المدفوع", "Paid", FieldTypes.Money, i => i.PaidAmount)
                .Amount("returned", "المرتجع", "Returned", FieldTypes.Money, i => i.ReturnedAmount)
                .Amount("remaining", "المتبقي", "Remaining", FieldTypes.Money, i => i.RemainingAmount)
                .Text("status", "الحالة", "Status", i => i.Status),
            "invoiceDate", null, null, i => i.Status == "Approved" || i.Status == "Closed", i => i.PublicId, "invoice"),

        Source<CreditNote>("credit-notes", "الإشعارات الدائنة", "Credit notes", [PermissionKeys.InvView], db => db.Set<CreditNote>(), new Fields<CreditNote>()
                .Text("number", "الرقم", "Number", c => c.Number, groupable: false)
                .Date("creditDate", "التاريخ", "Date", c => c.CreditDate)
                .Ref<Customer>("customer", "العميل", "Customer", c => c.Customer.Name, c => c.Customer.PublicId)
                .Text("invoice", "الفاتورة", "Invoice", c => c.Invoice.Number, groupable: false)
                .Amount("subtotal", "المجموع", "Subtotal", FieldTypes.Money, c => c.Subtotal)
                .Amount("tax", "الضريبة", "Tax", FieldTypes.Money, c => c.Tax)
                .Amount("total", "الإجمالي", "Total", FieldTypes.Money, c => c.Total)
                .Text("reason", "السبب", "Reason", c => c.Reason, groupable: false)
                .Text("status", "الحالة", "Status", c => c.Status),
            "creditDate", null, null, c => c.Status == "Posted", c => c.PublicId, "credit-note"),

        Source<Receipt>("receipts", "سندات القبض", "Receipts", [PermissionKeys.RcptManage, PermissionKeys.InvView], db => db.Set<Receipt>(), new Fields<Receipt>()
                .Text("number", "الرقم", "Number", r => r.Number, groupable: false)
                .Date("receiptDate", "التاريخ", "Date", r => r.ReceiptDate)
                .Ref<Customer>("customer", "العميل", "Customer", r => r.Customer.Name, r => r.Customer.PublicId)
                // Revision 6: one invoice, or the first of several (by invoice date); "On account" = nothing allocated.
                .Text("invoice", "الفاتورة", "Invoice", r => r.Allocations.OrderBy(a => a.Invoice.InvoiceDate).Select(a => a.Invoice.Number).FirstOrDefault(), groupable: false)
                .Bool("onAccount", "على الحساب", "On account", r => !r.Allocations.Any())
                .Amount("amount", "المبلغ", "Amount", FieldTypes.Money, r => r.Amount)
                .Amount("unallocated", "غير مخصص", "Unallocated", FieldTypes.Money, r => r.Amount - (r.Allocations.Sum(a => (decimal?)a.Amount) ?? 0))
                .Text("method", "طريقة الدفع", "Method", r => r.PaymentMethod)
                .Text("status", "الحالة", "Status", r => r.Status),
            "receiptDate", null, null, r => r.Status == Active, r => r.PublicId, "receipt"),

        Source<Advance>("advances", "السلف", "Advances", [PermissionKeys.DrvView, PermissionKeys.DrvAdvance], db => db.Set<Advance>(), new Fields<Advance>()
                .Text("number", "الرقم", "Number", a => a.Number, groupable: false)
                .Date("advanceDate", "التاريخ", "Date", a => a.AdvanceDate)
                .Ref<Driver>("driver", "السائق", "Driver", a => a.Driver.FullName, a => a.Driver.PublicId)
                .Amount("amount", "المبلغ", "Amount", FieldTypes.Money, a => a.Amount)
                .Text("method", "طريقة الدفع", "Method", a => a.PaymentMethod)
                .Bool("settled", "مسوّاة", "Settled", a => a.SettlementId != null)
                .Text("status", "الحالة", "Status", a => a.Status),
            "advanceDate", null, null, a => a.Status == Active, a => a.PublicId, "advance"),

        Source<Settlement>("settlements", "التسويات", "Settlements", [PermissionKeys.DrvView, PermissionKeys.DrvSettle], db => db.Set<Settlement>(), new Fields<Settlement>()
                .Text("number", "الرقم", "Number", s => s.Number, groupable: false)
                .Date("settlementDate", "التاريخ", "Date", s => s.SettlementDate)
                .Ref<Driver>("driver", "السائق", "Driver", s => s.Driver.FullName, s => s.Driver.PublicId)
                .Amount("gross", "الأجور", "Gross wages", FieldTypes.Money, s => s.GrossWages)
                .Amount("advances", "السلف", "Advances", FieldTypes.Money, s => s.AdvancesTotal)
                .Amount("deductions", "الخصومات", "Deductions", FieldTypes.Money, s => s.DeductionsTotal)
                .Amount("net", "الصافي", "Net", FieldTypes.Money, s => s.NetPayable)
                .Text("method", "طريقة الدفع", "Method", s => s.PaymentMethod)
                .Text("status", "الحالة", "Status", s => s.Status),
            "settlementDate", null, null, s => s.Status == Active, s => s.PublicId, "settlement"),

        Source<PartnerPayment>("partner-payments", "دفعات الشركاء", "Partner payments", [PermissionKeys.DrvView, PermissionKeys.DrvSettle], db => db.Set<PartnerPayment>(),
            new Fields<PartnerPayment>()
                .Text("number", "الرقم", "Number", p => p.Number, groupable: false)
                .Date("paymentDate", "التاريخ", "Date", p => p.PaymentDate)
                .Ref<Partner>("partner", "الشريك", "Partner", p => p.Partner.Name, p => p.Partner.PublicId)
                .Amount("amount", "المبلغ", "Amount", FieldTypes.Money, p => p.Amount)
                .Text("method", "طريقة الدفع", "Method", p => p.PaymentMethod)
                .Text("status", "الحالة", "Status", p => p.Status),
            "paymentDate", null, null, p => p.Status == Active, p => p.PublicId, "partner-payment"),

        Source<EntryLine>("journal-lines", "سطور القيود", "Journal lines", [PermissionKeys.AcctView], db => db.Set<EntryLine>(), new Fields<EntryLine>()
                .Date("entryDate", "التاريخ", "Date", l => l.JournalEntry.EntryDate)
                .Text("entry", "القيد", "Entry", l => l.JournalEntry.Number, groupable: false)
                .Ref<Account>("account", "الحساب", "Account", l => l.Account.Code + " " + l.Account.NameAr, l => l.Account.PublicId)
                .Text("accountType", "نوع الحساب", "Account type", l => l.Account.AccountType)
                .Ref<CostCentre>("costCentre", "مركز التكلفة", "Cost centre", l => l.CostCentre != null ? l.CostCentre.Code + " " + l.CostCentre.Name : null,
                    l => l.CostCentre != null ? l.CostCentre.PublicId : null)
                .Text("source", "المصدر", "Source", l => l.JournalEntry.SourceType)
                .Bool("manual", "يدوي", "Manual", l => l.JournalEntry.IsManual)
                .Text("description", "البيان", "Description", l => l.JournalEntry.Description, groupable: false)
                .Amount("debit", "مدين", "Debit", FieldTypes.Money, l => l.Debit)
                .Amount("credit", "دائن", "Credit", FieldTypes.Money, l => l.Credit)
                .Amount("net", "الصافي (مدين − دائن)", "Net (debit − credit)", FieldTypes.Money, l => l.Debit - l.Credit)
                .Text("status", "حالة القيد", "Entry status", l => l.JournalEntry.Status),
            "entryDate", null, null, null, l => l.JournalEntry.PublicId, "journal-entry"),

        Source<Driver>("drivers", "السائقون", "Drivers", [PermissionKeys.DrvView, PermissionKeys.MastersManage], db => db.Set<Driver>(), new Fields<Driver>()
                .Text("name", "الاسم", "Name", d => d.FullName)
                .Text("plate", "رقم اللوحة", "Plate", d => d.PlateNumber)
                .Text("truckType", "نوع الشاحنة", "Truck type", d => d.TruckType)
                .Ref<OwnershipType>("ownershipType", "نوع الملكية", "Ownership type", d => d.OwnershipType.Name, d => d.OwnershipType.PublicId)
                .Ref<Partner>("partner", "الشريك", "Partner", d => d.Partner != null ? d.Partner.Name : null, d => d.Partner != null ? d.Partner.PublicId : null)
                .Amount("capacity", "الحمولة (طن)", "Capacity (t)", FieldTypes.Quantity, d => d.CapacityTons)
                .Date("licenceExpiry", "انتهاء الرخصة", "Licence expiry", d => d.LicenceExpiry)
                .Date("registrationExpiry", "انتهاء الترخيص", "Registration expiry", d => d.RegistrationExpiry)
                .Date("insuranceExpiry", "انتهاء التأمين", "Insurance expiry", d => d.InsuranceExpiry)
                .Date("inspectionExpiry", "انتهاء الفحص", "Inspection expiry", d => d.InspectionExpiry)
                .Text("paymentMethod", "طريقة الدفع", "Payment method", d => d.PaymentMethod)
                .Text("status", "الحالة", "Status", d => d.Status),
            "licenceExpiry", FieldCatalog.Driver, d => d.CustomValues, d => d.Status == Active, d => d.PublicId, "driver"),

        Source<Customer>("customers", "العملاء", "Customers", [], db => db.Set<Customer>(), new Fields<Customer>()
                .Text("name", "الاسم", "Name", c => c.Name)
                .Text("taxNumber", "الرقم الضريبي", "Tax number", c => c.TaxNumber, groupable: false)
                .Text("city", "المدينة", "City", c => c.City)
                .Count("paymentTerms", "مدة السداد (يوم)", "Payment terms (days)", c => c.PaymentTermsDays)
                .Text("status", "الحالة", "Status", c => c.Status),
            null, FieldCatalog.Customer, c => c.CustomValues, c => c.Status == Active, c => c.PublicId, "customer"),
    ];

    public static SourceDef? Find(string? key) => All.FirstOrDefault(s => s.Key == key);
}
