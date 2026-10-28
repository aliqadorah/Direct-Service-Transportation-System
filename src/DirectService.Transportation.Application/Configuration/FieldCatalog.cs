namespace DirectService.Transportation.Application.Configuration;

/// <summary>
/// The entities whose fields are configurable (PRD 7.9.3, flow 9.14: trips, drivers, customers) and their built-in fields in
/// default display order.
/// </summary>
public static class FieldCatalog
{
    public const string Trip = "TRN.Trip";
    public const string Driver = "TRN.Driver";
    public const string Customer = "TRN.Customer";

    public static readonly IReadOnlyList<string> Entities = [Trip, Driver, Customer];

    private static StandardField Core(string key, string ar, string en) => new(key, ar, en, IsCore: true, AlwaysRequired: true);

    private static StandardField Required(string key, string ar, string en) => new(key, ar, en, IsCore: false, AlwaysRequired: true);

    private static StandardField Optional(string key, string ar, string en) => new(key, ar, en, IsCore: false, AlwaysRequired: false);

    private static readonly Dictionary<string, IReadOnlyList<StandardField>> Fields = new Dictionary<string, IReadOnlyList<StandardField>>(StringComparer.Ordinal)
    {
        // PRD 7.1.1; core fields per US-27 (receipt number, date, driver, customer, sites, material, loaded weight, price, wage).
        [Trip] =
        [
            Core("receiptNo", "رقم الإيصال", "Receipt number"),
            Core("tripDate", "تاريخ الرحلة", "Trip date"),
            Required("loadingDate", "تاريخ التحميل", "Loading date"),
            Optional("unloadingDate", "تاريخ التنزيل", "Unloading date"),
            Core("driverId", "السائق", "Driver"),
            Core("customerId", "العميل", "Customer"),
            Core("loadingSiteId", "موقع التحميل", "Loading site"),
            Core("unloadingSiteId", "موقع التنزيل", "Unloading site"),
            Core("materialId", "المادة", "Material"),
            Core("loadedWeight", "الوزن المحمّل (طن)", "Loaded weight (t)"),
            Optional("unloadedWeight", "الوزن المنزّل (طن)", "Unloaded weight (t)"),
            Core("customerPrice", "سعر العميل (للطن)", "Customer price (per ton)"),
            Optional("partnerId", "الشريك", "Partner"),
            Core("driverRate", "أجرة السائق (للطن)", "Driver rate (per ton)"),
            Required("partnerRate", "سعر الشريك (للطن)", "Partner rate (per ton)"),
            Optional("notes", "ملاحظات", "Notes"),
        ],
        // PRD 7.8.1
        [Driver] =
        [
            Required("fullName", "الاسم الكامل", "Full name"),
            Required("nationalId", "الرقم الوطني", "National ID"),
            Required("phone1", "الهاتف 1", "Phone 1"),
            Optional("phone2", "الهاتف 2", "Phone 2"),
            Optional("address", "العنوان", "Address"),
            Required("licenceNo", "رقم الرخصة", "Licence number"),
            Required("licenceType", "فئة الرخصة", "Licence type"),
            Required("licenceExpiry", "تاريخ انتهاء الرخصة", "Licence expiry"),
            Required("paymentMethod", "طريقة الدفع", "Payment method"),
            Optional("bankName", "اسم البنك", "Bank name"),
            Optional("iban", "رقم الآيبان", "IBAN"),
            Optional("cliqAlias", "اسم CliQ المستعار", "CliQ alias"),
            Required("plateNumber", "رقم اللوحة", "Plate number"),
            Optional("truckType", "نوع الشاحنة", "Truck type"),
            Optional("trailerNo", "رقم المقطورة", "Trailer number"),
            Required("capacityTons", "الحمولة (طن)", "Capacity (t)"),
            Required("ownershipTypeId", "نوع الملكية", "Ownership type"),
            Optional("partnerId", "الشركة الشريكة", "Partner company"),
            Required("registrationExpiry", "تاريخ انتهاء الترخيص", "Registration expiry"),
            Required("insuranceExpiry", "تاريخ انتهاء التأمين", "Insurance expiry"),
            Required("inspectionExpiry", "تاريخ انتهاء الفحص", "Inspection expiry"),
            Optional("notes", "ملاحظات", "Notes"),
        ],
        // PRD 7.8.2
        [Customer] =
        [
            Required("name", "اسم العميل", "Name"),
            Optional("taxNumber", "الرقم الضريبي", "Tax number"),
            Optional("registrationNo", "السجل التجاري / الرقم الوطني", "Commercial registration / national ID"),
            Optional("phone", "الهاتف", "Phone"),
            Optional("email", "البريد الإلكتروني", "Email"),
            Optional("address", "العنوان", "Address"),
            Optional("city", "المدينة", "City"),
            Optional("contactPerson", "الشخص المسؤول", "Contact person"),
            Optional("paymentTermsDays", "مدة السداد (أيام)", "Payment terms (days)"),
            Optional("notes", "ملاحظات", "Notes"),
        ],
    };

    /// <summary>
    /// Optional fields that a business rule requires in some cases (IBAN and bank name when paid by bank, CliQ alias when paid by
    /// CliQ): they cannot be hidden, or a record needing them could not be saved from the form (review M1).
    /// </summary>
    public static bool IsConditional(string entityType, string key) =>
        entityType == Driver && key is "bankName" or "iban" or "cliqAlias";

    /// <summary>Optional fields a business rule forbids in some cases (a partner on an own-fleet driver): never made always required (L6).</summary>
    public static bool CannotRequire(string entityType, string key) =>
        entityType is Driver or Trip && key == "partnerId";

    public static bool IsEntity(string? entityType) => entityType is not null && Fields.ContainsKey(entityType);

    public static IReadOnlyList<StandardField> For(string entityType) => Fields[entityType];

    public static StandardField? Find(string entityType, string? key) =>
        key is null ? null : For(entityType).FirstOrDefault(f => f.Key == key);
}

/// <summary>The configurable entities of this application for the shared field engine (<see cref="IFieldCatalogProvider"/>, DT-318).</summary>
public sealed class TransportationFieldCatalog(IAppDbContext db) : IFieldCatalogProvider
{
    public IReadOnlyList<string> Entities => FieldCatalog.Entities;

    public IReadOnlyList<StandardField> For(string entityType) => FieldCatalog.For(entityType);

    public bool IsConditional(string entityType, string key) => FieldCatalog.IsConditional(entityType, key);

    public bool CannotRequire(string entityType, string key) => FieldCatalog.CannotRequire(entityType, key);

    public IQueryable<string?> CustomValues(string entityType) => entityType switch
    {
        FieldCatalog.Trip => db.Set<Domain.Trn.Trip>().Select(t => t.CustomValues),
        FieldCatalog.Driver => db.Set<Domain.Trn.Driver>().Select(t => t.CustomValues),
        FieldCatalog.Customer => db.Set<Domain.Trn.Customer>().Select(t => t.CustomValues),
        _ => throw Fail.NotFound(),
    };
}
