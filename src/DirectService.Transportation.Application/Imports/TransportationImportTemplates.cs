using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Pricing;
using DirectService.Transportation.Application.Trips;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Domain.Cfg;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Col = DirectService.SharedKernel.ImportTemplateColumn;

namespace DirectService.Transportation.Application.Imports;

/// <summary>
/// The import templates of this application (<see cref="IImportTemplateProvider"/>, DT-317): the columns, formats, allowed values and sample
/// rows of trips, the master-data lists, tariffs, chart of accounts (with opening balances) and journal entries, in the reader's language.
/// The shared <c>ImportTemplateService</c> lays out the file. The accounting kinds split off with the Accounting module (Stage 3).
/// </summary>
public sealed class TransportationImportTemplates(IAppDbContext db, IRequestContext ctx, IStandardFields fields, ICustomFields customFields,
    MasterDataSupport support) : IImportTemplateProvider
{
    public IReadOnlyList<string> Kinds { get; } =
        ["trips", "drivers", "customers", "partners", "materials", "loading-sites", "unloading-sites", "ownership-types", "tariffs", "accounts", "journal-entries"];

    private bool En => ctx.Language == "en";

    private string T(string ar, string en) => En ? en : ar;

    public async Task<ImportTemplateContent> ContentAsync(string kind, CancellationToken ct)
    {
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var (title, cols, extra) = kind switch
        {
            "trips" => (T("الرحلات", "Trips"), await TripsAsync(today, ct).ConfigureAwait(false), null),
            "drivers" => (T("السائقون", "Drivers"), await DriversAsync(today, ct).ConfigureAwait(false), null),
            "customers" => (T("العملاء", "Customers"), await CustomersAsync(today, ct).ConfigureAwait(false), null),
            "partners" => (T("الشركاء", "Partners"), Partners(), null),
            "materials" => (T("المواد", "Materials"), Simple("materials", [T("بوتاس", "Potash"), T("طن", "Ton")]), null),
            "loading-sites" => (T("مواقع التحميل", "Loading sites"), Simple("loading-sites", [T("غور الصافي", "Ghor Al-Safi"), T("الكرك", "Karak")]), null),
            "unloading-sites" => (T("مواقع التنزيل", "Unloading sites"), Simple("unloading-sites", [T("ميناء العقبة", "Aqaba Port"), T("العقبة", "Aqaba")]), null),
            "ownership-types" => (T("أنواع الملكية", "Ownership types"), OwnershipTypes(), null),
            "tariffs" => (T("التعرفات", "Tariffs"), Tariffs(today), null),
            "accounts" => (T("دليل الحسابات", "Chart of accounts"), Accounts(), null),
            "journal-entries" => (T("القيود", "Journal entries"), await JournalAsync(today, debitLine: true, ct).ConfigureAwait(false), await JournalAsync(today, debitLine: false, ct).ConfigureAwait(false)),
            _ => throw Fail.NotFound(),
        };
        // The journal sample is an entry of two rows; every other kind has one row per record.
        var replace = kind == "journal-entries"
            ? T("صفّي القيد المثال) بقيودك؛ كل قيد صفان أو أكثر بالرقم المرجعي نفسه.", " (the two lines of the sample entry) with your entries; each entry is two or more rows with the same reference.")
            : null;
        return new ImportTemplateContent(title, cols, extra, replace);
    }

    // ------------------------------------------------------------ formats

    private string DateFormat => T("تاريخ yyyy-mm-dd (أو dd/mm/yyyy)", "Date yyyy-mm-dd (or dd/mm/yyyy)");
    private string Number(int decimals) => T($"رقم، حتى {decimals} خانات عشرية", $"Number, up to {decimals} decimals");
    private string Text(int max) => T($"نص، حتى {max} حرف", $"Text, up to {max} characters");
    private string ActiveName(string ar, string en) => T($"اسم {ar} نشط كما هو في البيانات الأساسية", $"Name of an active {en} exactly as in the master data");

    // ------------------------------------------------------------ trips (FR-013)

    private async Task<List<Col>> TripsAsync(DateOnly today, CancellationToken ct)
    {
        var rules = await fields.RulesAsync(FieldCatalog.Trip, ct).ConfigureAwait(false);
        Col C(string key, string format, string allowed, object? sample) => new(rules.Label(key), TripImportService.RequiredColumns.Contains(key), format, allowed, sample);
        var cols = new List<Col>
        {
            C("receiptNo", Text(30), T("فريد في الشركة وفي الملف", "Unique in the company and in the file"), "10001"),
            C("tripDate", DateFormat, T("ليس بعد اليوم، في فترة مفتوحة", "Not after today, in an open period"), today),
            C("loadingDate", DateFormat, T("فارغ = تاريخ الرحلة؛ يحدد شهر الأجرة", "Empty = trip date; sets the rate month"), today),
            C("unloadingDate", DateFormat, T("ليس قبل تاريخ التحميل", "Not before the loading date"), today),
            C("driverId", Text(200), T("الرقم الوطني أو رقم اللوحة أو الاسم الكامل لسائق نشط", "National ID, plate number or full name of an active driver"), T("سامي العبادي", "Sami Abbadi")),
            C("customerId", Text(200), ActiveName("عميل", "customer"), T("شركة البوتاس العربية", "Arab Potash")),
            C("loadingSiteId", Text(200), ActiveName("موقع تحميل", "loading site"), T("غور الصافي", "Ghor Al-Safi")),
            C("unloadingSiteId", Text(200), ActiveName("موقع تنزيل", "unloading site"), T("ميناء العقبة", "Aqaba Port")),
            C("materialId", Text(200), ActiveName("مادة", "material"), T("بوتاس", "Potash")),
            C("loadedWeight", T("رقم بالطن، حتى 3 خانات عشرية، أكبر من صفر", "Tons, up to 3 decimals, above zero"), "", 31.850m),
            C("unloadedWeight", T("رقم بالطن، حتى 3 خانات عشرية", "Tons, up to 3 decimals"), "", 31.600m),
            C("customerPrice", Number(4), T("فارغ = سعر التعرفة", "Empty = the tariff price"), null),
            C("partnerId", Text(200), T("فارغ = شريك السائق", "Empty = the driver's partner"), null),
            C("driverRate", Number(4), T("فارغ = الأجرة الشهرية لموقع التحميل", "Empty = the monthly rate of the loading site"), null),
            C("partnerRate", Number(4), T("فارغ = تعرفة الشريك", "Empty = the partner's tariff"), null),
            C("notes", Text(1000), "", T("مثال — استبدل هذا الصف", "Example — replace this row")),
        };
        cols.AddRange(await CustomAsync(FieldCatalog.Trip, today, ct).ConfigureAwait(false));
        return cols;
    }

    // ------------------------------------------------------------ master data (FR-164)

    private IEnumerable<(string Key, string Header)> Headers(string kind) => MasterExchangeService.ImportColumns(kind).Select(c => (c.Key, En ? c.En : c.Ar));

    private async Task<List<Col>> DriversAsync(DateOnly today, CancellationToken ct)
    {
        var rules = await fields.RulesAsync(FieldCatalog.Driver, ct).ConfigureAwait(false);
        var types = await db.Set<OwnershipType>().AsNoTracking().OrderBy(t => t.Id).Select(t => new { t.Name, t.IsBuiltIn }).ToListAsync(ct).ConfigureAwait(false);
        // The built-in type in the reader's language (owner feedback 13 Oct 2026); the import accepts it in either language.
        var builtIn = OwnershipTypeNames.Display(types.FirstOrDefault(t => t.IsBuiltIn)?.Name ?? Platform.CompanyProvisioningService.BuiltInOwnershipType, ctx.Language);
        var info = new Dictionary<string, (string Format, string Allowed, object? Sample)>(StringComparer.Ordinal)
        {
            ["fullName"] = (Text(200), T("فريد بين السائقين", "Unique among drivers"), T("سامي العبادي", "Sami Abbadi")),
            ["nationalId"] = (T("10 أرقام", "10 digits"), T("فريد بين السائقين", "Unique among drivers"), "9871234561"),
            ["phone1"] = (T("رقم جوال أردني 07XXXXXXXX", "Jordanian mobile 07XXXXXXXX"), "", "0791234567"),
            ["phone2"] = (T("رقم جوال أردني 07XXXXXXXX", "Jordanian mobile 07XXXXXXXX"), "", null),
            ["address"] = (Text(300), "", T("عمّان", "Amman")),
            ["licenceNo"] = (Text(50), "", "L-12345"),
            ["licenceType"] = (Text(20), T("مثل C أو D", "e.g. C or D"), "C"),
            ["licenceExpiry"] = (DateFormat, "", today.AddYears(1)),
            ["paymentMethod"] = (Text(10), T("نقد، بنك، كليك (Cash, Bank, CliQ)", "Cash, Bank, CliQ (نقد، بنك، كليك)"), T("نقد", "Cash")),
            ["bankName"] = (Text(100), T("مطلوب عند الدفع بالبنك", "Required when paid by bank"), null),
            ["iban"] = (T("آيبان أردني JO + 28 حرفاً ورقماً", "Jordanian IBAN: JO + 28 letters and digits"), T("مطلوب عند الدفع بالبنك", "Required when paid by bank"), null),
            ["cliqAlias"] = (Text(50), T("مطلوب عند الدفع بكليك", "Required when paid by CliQ"), null),
            ["plateNumber"] = (T("مثل 10-12345", "e.g. 10-12345"), T("فريد بين السائقين", "Unique among drivers"), "10-54321"),
            ["truckType"] = (Text(100), "", "Volvo"),
            ["trailerNo"] = (Text(30), "", null),
            ["capacityTons"] = (T("رقم بالطن، حتى 3 خانات عشرية", "Tons, up to 3 decimals"), "", 32.000m),
            ["ownershipTypeId"] = (Text(100), T("أحد أنواع الملكية: ", "One of the ownership types: ") + string.Join(T("، ", ", "), types.Select(t => OwnershipTypeNames.Display(t.Name, ctx.Language))), builtIn),
            ["partnerId"] = (Text(200), T("اسم شركة شريكة نشطة؛ فارغ لسائق الأسطول الخاص", "Name of an active partner company; empty for an own-fleet driver"), null),
            ["registrationExpiry"] = (DateFormat, "", today.AddYears(1)),
            ["insuranceExpiry"] = (DateFormat, "", today.AddYears(1)),
            ["inspectionExpiry"] = (DateFormat, "", today.AddYears(1)),
            ["notes"] = (Text(1000), "", T("مثال — استبدل هذا الصف", "Example — replace this row")),
        };
        var cols = Headers("drivers").Select(h => new Col(h.Header, rules.IsRequired(h.Key), info[h.Key].Format, info[h.Key].Allowed, info[h.Key].Sample)).ToList();
        cols.AddRange(await CustomAsync(FieldCatalog.Driver, today, ct).ConfigureAwait(false));
        return cols;
    }

    private async Task<List<Col>> CustomersAsync(DateOnly today, CancellationToken ct)
    {
        var rules = await fields.RulesAsync(FieldCatalog.Customer, ct).ConfigureAwait(false);
        var info = new Dictionary<string, (string Format, string Allowed, object? Sample)>(StringComparer.Ordinal)
        {
            ["name"] = (Text(200), T("فريد بين العملاء", "Unique among customers"), T("شركة البوتاس العربية", "Arab Potash")),
            ["taxNumber"] = (T("أرقام فقط", "Digits only"), "", "200014587"),
            ["registrationNo"] = (Text(50), "", null),
            ["phone"] = (T("رقم أردني", "Jordanian number"), "", "0781234567"),
            ["email"] = (T("بريد إلكتروني", "E-mail address"), "", null),
            ["address"] = (Text(300), "", T("عمّان", "Amman")),
            ["city"] = (Text(100), "", T("عمّان", "Amman")),
            ["contactPerson"] = (Text(100), "", null),
            ["paymentTermsDays"] = (T("عدد أيام صحيح", "Whole number of days"), T("0 إلى 3650", "0 to 3650"), 30),
            ["notes"] = (Text(1000), "", T("مثال — استبدل هذا الصف", "Example — replace this row")),
        };
        var cols = Headers("customers").Select(h => new Col(h.Header, rules.IsRequired(h.Key), info[h.Key].Format, info[h.Key].Allowed, info[h.Key].Sample)).ToList();
        cols.AddRange(await CustomAsync(FieldCatalog.Customer, today, ct).ConfigureAwait(false));
        return cols;
    }

    private List<Col> Partners()
    {
        var info = new Dictionary<string, (bool Required, string Format, string Allowed, object? Sample)>(StringComparer.Ordinal)
        {
            ["name"] = (true, Text(200), T("فريد بين الشركاء", "Unique among partners"), T("النور للنقل", "Al-Noor Transport")),
            ["taxNumber"] = (false, T("أرقام فقط", "Digits only"), "", null),
            ["phone"] = (false, T("رقم أردني", "Jordanian number"), "", "0791234567"),
            ["address"] = (false, Text(300), "", null),
            ["contactPerson"] = (false, Text(100), "", null),
            ["bankName"] = (false, Text(100), "", null),
            ["iban"] = (false, T("آيبان أردني JO + 28 حرفاً ورقماً", "Jordanian IBAN: JO + 28 letters and digits"), "", null),
            ["cliqAlias"] = (false, Text(50), "", null),
            ["notes"] = (false, Text(1000), "", T("مثال — استبدل هذا الصف", "Example — replace this row")),
        };
        return Headers("partners").Select(h => new Col(h.Header, info[h.Key].Required, info[h.Key].Format, info[h.Key].Allowed, info[h.Key].Sample)).ToList();
    }

    /// <summary>Materials and sites: a unique name and one optional value (unit or area).</summary>
    private List<Col> Simple(string kind, string[] sample) =>
        Headers(kind).Select((h, i) => i == 0
            ? new Col(h.Header, true, Text(200), T("فريد في القائمة", "Unique in the list"), sample[0])
            : new Col(h.Header, false, Text(100), "", sample[1])).ToList();

    private List<Col> OwnershipTypes() =>
        Headers("ownership-types").Select((h, i) => i == 0
            ? new Col(h.Header, true, Text(100), T("فريد في القائمة", "Unique in the list"), T("مستأجرة", "Rented"))
            : new Col(h.Header, true, Text(20), T("خاص أو خارجي (Own, External)", "Own or External (خاص، خارجي)"), T("خارجي", "External"))).ToList();

    // ------------------------------------------------------------ tariffs (FR-019)

    private List<Col> Tariffs(DateOnly today)
    {
        var all = T("فارغ أو «الكل» = الكل", "Empty or “All” = all");
        var info = new Dictionary<string, (bool Required, string Format, string Allowed, object? Sample)>(StringComparer.Ordinal)
        {
            ["partyType"] = (true, Text(10), T("عميل أو شريك (Customer, Partner)", "Customer or Partner (عميل، شريك)"), T("عميل", "Customer")),
            ["party"] = (false, Text(200), T("اسم عميل أو شريك نشط بحسب نوع الطرف؛ ", "Name of an active customer or partner, by party type; ") + all, T("شركة البوتاس العربية", "Arab Potash")),
            ["material"] = (false, Text(200), ActiveName("مادة", "material") + "; " + all, T("بوتاس", "Potash")),
            ["loadingSite"] = (false, Text(200), ActiveName("موقع تحميل", "loading site") + "; " + all, T("غور الصافي", "Ghor Al-Safi")),
            ["unloadingSite"] = (false, Text(200), ActiveName("موقع تنزيل", "unloading site") + "; " + all, null),
            ["price"] = (true, Number(4), T("أكبر من صفر", "Above zero"), 6.000m),
            ["validFrom"] = (false, DateFormat, T("فارغ = بلا بداية", "Empty = no start"), today),
            ["validTo"] = (false, DateFormat, T("فارغ = بلا نهاية؛ ليس قبل «ساري من»", "Empty = open-ended; not before “Valid from”"), null),
            ["notes"] = (false, Text(1000), "", T("مثال — استبدل هذا الصف", "Example — replace this row")),
        };
        return PricingExchangeService.ImportColumns.Select(c => (c.Key, Header: En ? c.En : c.Ar))
            .Select(h => new Col(h.Header, info[h.Key].Required, info[h.Key].Format, info[h.Key].Allowed, info[h.Key].Sample)).ToList();
    }

    // ------------------------------------------------------------ chart of accounts with opening balances (FR-109, FR-110)

    private List<Col> Accounts() =>
    [
        new(T("رمز الحساب", "Code"), true, T("حروف لاتينية وأرقام و . _ -، حتى 30", "Latin letters, digits and . _ -, up to 30"), T("فريد؛ غير موجود في الدليل (الاستيراد يضيف حسابات جديدة فقط)", "Unique; not already in the chart (the import adds new accounts only)"), "1199"),
        new(T("الاسم بالعربية", "Arabic name"), true, Text(200), "", "صندوق فرعي"),
        new(T("الاسم بالإنجليزية", "English name"), false, Text(200), "", "Petty cash"),
        new(T("نوع الحساب", "Account type"), true, Text(20), T("أصول، التزامات، حقوق الملكية، إيرادات، مصروفات (Asset, Liability, Equity, Revenue, Expense)", "Asset, Liability, Equity, Revenue, Expense (أصول، التزامات، حقوق الملكية، إيرادات، مصروفات)"), T("أصول", "Asset")),
        new(T("رمز الحساب الأب", "Parent code"), false, T("رمز حساب في الدليل أو في الملف", "Code of an account in the chart or in the file"), T("من النوع نفسه؛ فارغ = حساب رئيسي", "Same type; empty = top-level account"), null),
        new(T("قابل للترحيل", "Postable"), false, T("نعم أو لا (Yes, No)", "Yes or No (نعم، لا)"), T("لا للحسابات التي لها فروع", "No for accounts with sub-accounts"), T("نعم", "Yes")),
        new(T("رصيد افتتاحي مدين", "Opening debit"), false, Number(3), T("رصيد افتتاحي للحسابات القابلة للترحيل؛ مدين أو دائن لا كلاهما", "Opening balance of postable accounts; debit or credit, not both"), 150.000m),
        new(T("رصيد افتتاحي دائن", "Opening credit"), false, Number(3), T("انظر «رصيد افتتاحي مدين»", "See “Opening debit”"), null),
        new(T("نوع طرف الرقابة", "Control party type"), false, Text(10), "Customer, Driver, Partner, Supplier", null),
    ];

    // ------------------------------------------------------------ journal entries (FR-114)

    /// <summary>One line of the sample entry, on postable accounts of the company's chart (an expense debited, an asset credited) when it has them.</summary>
    private async Task<List<Col>> JournalAsync(DateOnly today, bool debitLine, CancellationToken ct)
    {
        var type = debitLine ? "Expense" : "Asset";
        var code = await db.Set<DirectService.Accounting.Domain.Acc.Account>().AsNoTracking().Where(a => a.AccountType == type && a.IsPostable && !a.IsControlAccount && a.Status == "Active" && !a.Children.Any())
            .OrderBy(a => a.Code).Select(a => a.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? (debitLine ? "5101" : "1101");
        return Journal(today, debitLine, code);
    }

    private List<Col> Journal(DateOnly today, bool debitLine, string accountCode) =>
    [
        new(T("مرجع القيد", "Entry ref"), true, Text(30), T("الصفوف بالمرجع نفسه قيد واحد", "Rows with the same reference form one entry"), "J-1"),
        new(T("تاريخ القيد", "Entry date"), true, DateFormat, T("في فترة مفتوحة؛ واحد لكل صفوف القيد", "In an open period; the same on every row of the entry"), today),
        new(T("البيان", "Description"), true, Text(500), T("واحد لكل صفوف القيد", "The same on every row of the entry"), T("قيد مثال — استبدله", "Sample entry — replace it")),
        new(T("المرجع", "Reference"), false, Text(100), T("نص حر بجانب البيان (رقم فاتورة المورد مثلاً)؛ واحد لكل صفوف القيد", "Free text next to the description (a supplier invoice number, for example); the same on every row of the entry"), "REF-1001"),
        new(T("رمز الحساب", "Account code"), true, T("رمز حساب قابل للترحيل", "Code of a postable account"), T("حساب نشط بلا فروع", "Active account without sub-accounts"), accountCode),
        new(T("مدين", "Debit"), true, Number(3), T("مدين أو دائن في كل صف؛ مجموع المدين = مجموع الدائن لكل قيد", "Debit or credit on each row; total debit = total credit per entry"), debitLine ? 100.000m : null),
        new(T("دائن", "Credit"), true, Number(3), T("انظر «مدين»", "See “Debit”"), debitLine ? null : 100.000m),
        new(T("مركز الكلفة", "Cost centre"), false, T("رمز مركز كلفة نشط", "Code of an active cost centre"), "", null),
        new(T("نوع الطرف", "Party type"), false, Text(20), T("عميل، سائق، شريك، مورد، طرف محاسبي (Customer, Driver, Partner, Supplier, ApArParty)؛ مطلوب لحسابات الرقابة", "Customer, Driver, Partner, Supplier, ApArParty (عميل، سائق، شريك، مورد، طرف محاسبي); required on control accounts"), null),
        new(T("الطرف", "Party"), false, Text(200), T("اسم العميل أو الشريك أو الطرف المحاسبي، أو الرقم الوطني للسائق", "Customer, partner or accounting-party name, or the driver's national ID"), null),
        new(T("ملاحظة السطر", "Line note"), false, Text(500), "", null),
        // Owner request 25 Oct 2026 (DT-254, DT-255): line reference and tax; the debit / credit is the amount before tax.
        new(T("مرجع السطر", "Line reference"), false, Text(100), T("فارغ = مرجع القيد", "Empty = the entry's reference"), null),
        new(T("رمز الضريبة", "Tax code"), false, T("رمز ضريبة نشط (الإعدادات ← رموز الضريبة)", "Code of an active tax code (Settings → Tax codes)"),
            T("المدين أو الدائن مبلغ قبل الضريبة؛ تُضاف الضريبة آلياً في الجهة نفسها ويجب أن يتوازن القيد معها", "Debit or credit is the amount before tax; the tax is added on the same side and the entry must balance with it"), null),
        new(T("حساب الضريبة", "Tax account"), false, T("رمز حساب ضريبة", "Code of a tax account"), T("مطلوب لرمز بنسبة أكبر من 0%", "Required for a code with a rate above 0%"), null),
    ];

    // ------------------------------------------------------------ custom fields (FR-180)

    private async Task<IEnumerable<Col>> CustomAsync(string entity, DateOnly today, CancellationToken ct) =>
        (await customFields.DefinitionsAsync(entity, ct).ConfigureAwait(false)).Where(d => !d.IsHidden).Select(d =>
        {
            var options = CustomFieldValues.ReadOptions(d.Options);
            var (format, sample) = d.FieldType switch
            {
                CustomFieldTypes.Number => (Number(4), (object?)1m),
                CustomFieldTypes.Date => (DateFormat, today),
                CustomFieldTypes.Checkbox => (T("نعم أو لا (Yes, No)", "Yes or No (نعم، لا)"), T("نعم", "Yes")),
                CustomFieldTypes.List => (T("أحد الخيارات", "One of the options"), options.Count > 0 ? options[0] : null),
                _ => (Text(d.FieldType == CustomFieldTypes.LongText ? 2000 : 200), (object?)T("مثال", "Example")),
            };
            var allowed = d.FieldType == CustomFieldTypes.List ? string.Join(T("، ", ", "), options) : d.HelpText ?? "";
            return new Col(customFields.Label(d), d.IsRequired, format, allowed, d.IsRequired || d.FieldType == CustomFieldTypes.List ? sample : null);
        });
}
