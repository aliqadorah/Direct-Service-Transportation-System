namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// The posting keys of PRD 7.5.1 (names approved with the chart template, D-10) and the opening-difference key
/// (G-04). Cash boxes and bank accounts post to their own linked accounts and have no key.
/// </summary>
public static class PostingKeys
{
    public const string DriverWageExpense = "TRN.DriverWageExpense";
    public const string DriverPayable = "TRN.DriverPayable";
    public const string PartnerCostExpense = "TRN.PartnerCostExpense";
    public const string PartnerPayable = "TRN.PartnerPayable";
    public const string TradeReceivables = "TRN.TradeReceivables";
    public const string DiscountAllowed = "TRN.DiscountAllowed";
    public const string TransportRevenue = "TRN.TransportRevenue";
    public const string OtherRevenue = "TRN.OtherRevenue";
    public const string SalesTaxOutput = "ACC.SalesTaxOutput";
    public const string SalesTaxInput = "ACC.SalesTaxInput";
    public const string DriverAdvances = "TRN.DriverAdvances";
    public const string DriverDeductions = "TRN.DriverDeductions";
    public const string OpeningBalanceDifference = "ACC.OpeningBalanceDifference";
    /// <summary>Deductions on supplier-bill lines (revision 6), credited when a bill has deductions.</summary>
    public const string SupplierDeductions = "ACC.SupplierDeductions";

    /// <summary>A posting key; an <paramref name="Optional"/> key is needed only by the documents that use it (never for "posting ready").</summary>
    public sealed record Definition(string Key, string NameAr, string NameEn, bool RequiredOnlyDuringSetup, bool Optional = false);

    /// <summary>All keys in the order of PRD 7.5.1.</summary>
    public static readonly IReadOnlyList<Definition> All =
    [
        new(DriverWageExpense, "مصروف أجور السائقين", "Driver wage expense", false),
        new(DriverPayable, "ذمم السائقين الدائنة", "Driver payable", false),
        new(PartnerCostExpense, "مصروف تكلفة الشركاء", "Partner cost expense", false),
        new(PartnerPayable, "ذمم الشركاء الدائنة", "Partner payable", false),
        new(TradeReceivables, "ذمم العملاء", "Trade receivables", false),
        new(DiscountAllowed, "الخصم المسموح به", "Discount allowed", false),
        new(TransportRevenue, "إيرادات النقل", "Transport revenue", false),
        new(OtherRevenue, "إيرادات أخرى (رسوم إضافية)", "Other revenue (extra fees)", false),
        new(SalesTaxOutput, "ضريبة المبيعات (مخرجات)", "Sales tax output", false),
        new(SalesTaxInput, "ضريبة المبيعات (مدخلات)", "Sales tax input", false),
        new(DriverAdvances, "سلف السائقين", "Driver advances", false),
        new(DriverDeductions, "خصومات السائقين", "Driver deductions", false),
        new(OpeningBalanceDifference, "فرق الأرصدة الافتتاحية", "Opening balance difference", true),
        // Revision 6: optional so that companies created before it keep posting; a bill with a deduction needs it mapped.
        new(SupplierDeductions, "خصومات الموردين", "Supplier deductions", false, true),
    ];

    public static Definition? Find(string? key) => All.FirstOrDefault(d => d.Key == key);

    /// <summary>
    /// The names users see for posting keys (the Account mapping page's names) in a language — never the internal codes (owner testing
    /// 20 Oct 2026, DT-224). An unknown key is kept as it is.
    /// </summary>
    public static IReadOnlyList<string> Names(IEnumerable<string> keys, string? language) =>
        keys.Select(k => Find(k) is { } d ? (language == "en" ? d.NameEn : d.NameAr) : k).ToList();

    /// <summary><see cref="Names"/> joined for a message ("، " in Arabic).</summary>
    public static string NameList(IEnumerable<string> keys, string? language) => string.Join(language == "en" ? ", " : "، ", Names(keys, language));

    /// <summary>Keys that must be mapped before automatic posting and invoicing (FR-094); the opening-difference key only during setup.</summary>
    public static IEnumerable<string> Required(bool setupOpen) =>
        All.Where(d => !d.Optional && (!d.RequiredOnlyDuringSetup || setupOpen)).Select(d => d.Key);
}

/// <summary>Stored codes used by the accounting tables (design CHECK lists).</summary>
public static class AccountingCodes
{
    public static readonly IReadOnlyList<string> AccountTypes = ["Asset", "Liability", "Equity", "Revenue", "Expense"];

    /// <summary>Account.ControlPartyType values.</summary>
    public static readonly IReadOnlyList<string> ControlPartyTypes = ["Customer", "Driver", "Partner", "Supplier"];

    public const string Active = "Active";
    public const string Archived = "Archived";

    public const string Posted = "Posted";
    public const string Reversed = "Reversed";
    public const string Void = "Void";
    /// <summary>A journal draft in the journal list (revision 6; acc.JournalDrafts, never an entry status).</summary>
    public const string Draft = "Draft";

    public const string ModuleTrn = "TRN";
    public const string ModuleAcc = "ACC";

    public const string SourceManual = "Manual";
    public const string SourceOpening = "Opening";

    /// <summary>EntryLine.PartyType values: TRN Customer/Driver/Partner, ACC ApArParty.</summary>
    public const string PartyCustomer = "Customer";
    public const string PartyDriver = "Driver";
    public const string PartyPartner = "Partner";
    public const string PartyApAr = "ApArParty";

    /// <summary>Calendar month code YYYY-MM of a date (design convention CHAR(7)).</summary>
    public static string Month(DateOnly date) => date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsMonth(string? value) =>
        value is { Length: 7 } && DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

    public static DateOnly FirstDay(string month) =>
        DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

