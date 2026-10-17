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

    public sealed record Definition(string Key, string NameAr, string NameEn, bool RequiredOnlyDuringSetup);

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
    ];

    public static Definition? Find(string? key) => All.FirstOrDefault(d => d.Key == key);

    /// <summary>Keys that must be mapped before automatic posting and invoicing (FR-094); the opening-difference key only during setup.</summary>
    public static IEnumerable<string> Required(bool setupOpen) =>
        All.Where(d => !d.RequiredOnlyDuringSetup || setupOpen).Select(d => d.Key);
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

/// <summary>Money rules (BR-020, BR-021): half-up rounding to the currency decimals.</summary>
public static class Money
{
    public static decimal Round(decimal value, int decimals = 3) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    /// <summary>Largest amount a DECIMAL(18,3) column holds (15 integer digits).</summary>
    public const decimal MaxAmount = 999_999_999_999_999.999m;

    /// <summary>True when the amount has no more decimals than the currency allows and fits DECIMAL(18,3) (SR-B2-01).</summary>
    public static bool IsValidAmount(decimal value, int decimals = 3) =>
        value >= 0 && value <= MaxAmount && Round(value, decimals) == value;

    public static string Format(decimal value) => value.ToString("#,##0.000", System.Globalization.CultureInfo.InvariantCulture);
}
