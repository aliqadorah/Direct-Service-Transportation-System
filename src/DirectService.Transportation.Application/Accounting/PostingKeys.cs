namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// The posting keys of PRD 7.5.1 (names approved with the chart template, D-10) and the opening-difference key
/// (G-04). Cash boxes and bank accounts post to their own linked accounts and have no key. The TRN keys are the module's; the ACC keys are
/// defined by Accounting (<see cref="AccountingPostingKeys"/>) and named here under the same names, in the PRD order (Stage 3 session 4a).
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
    public const string SalesTaxOutput = AccountingPostingKeys.SalesTaxOutput;
    public const string SalesTaxInput = AccountingPostingKeys.SalesTaxInput;
    public const string DriverAdvances = "TRN.DriverAdvances";
    public const string DriverDeductions = "TRN.DriverDeductions";
    public const string OpeningBalanceDifference = AccountingPostingKeys.OpeningBalanceDifference;
    public const string SupplierDeductions = AccountingPostingKeys.SupplierDeductions;

    /// <summary>
    /// A posting key; an <paramref name="Optional"/> key is needed only by the documents that use it (never for "posting ready"). Its
    /// <paramref name="TemplateAccountCode"/> is the chart template account it is mapped to (draft §2; with the key since Stage 3 session 6c).
    /// </summary>
    public sealed record Definition(string Key, string NameAr, string NameEn, bool RequiredOnlyDuringSetup, bool Optional = false,
        string? TemplateAccountCode = null);

    /// <summary>All keys in the order of PRD 7.5.1.</summary>
    public static readonly IReadOnlyList<Definition> All =
    [
        new(DriverWageExpense, "مصروف أجور السائقين", "Driver wage expense", false, TemplateAccountCode: "5101"),
        new(DriverPayable, "ذمم السائقين الدائنة", "Driver payable", false, TemplateAccountCode: "2102"),
        new(PartnerCostExpense, "مصروف تكلفة الشركاء", "Partner cost expense", false, TemplateAccountCode: "5102"),
        new(PartnerPayable, "ذمم الشركاء الدائنة", "Partner payable", false, TemplateAccountCode: "2103"),
        new(TradeReceivables, "ذمم العملاء", "Trade receivables", false, TemplateAccountCode: "110301"),
        new(DiscountAllowed, "الخصم المسموح به", "Discount allowed", false, TemplateAccountCode: "4104"),
        new(TransportRevenue, "إيرادات النقل", "Transport revenue", false, TemplateAccountCode: "4101"),
        new(OtherRevenue, "إيرادات أخرى (رسوم إضافية)", "Other revenue (extra fees)", false, TemplateAccountCode: "4102"),
        Acc(SalesTaxOutput),
        Acc(SalesTaxInput),
        new(DriverAdvances, "سلف السائقين", "Driver advances", false, TemplateAccountCode: "1104"),
        new(DriverDeductions, "خصومات السائقين", "Driver deductions", false, TemplateAccountCode: "4103"),
        Acc(OpeningBalanceDifference),
        Acc(SupplierDeductions),
    ];

    /// <summary>A key owned by Accounting (<see cref="AccountingPostingKeys"/>, Stage 3 session 4a), at its place in the PRD order.</summary>
    private static Definition Acc(string key)
    {
        var d = AccountingPostingKeys.Get(key);
        return new(d.Key, d.NameAr, d.NameEn, d.RequiredOnlyDuringSetup, d.Optional, d.TemplateAccountCode);
    }

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

/// <summary>The engine's view of the posting keys (<see cref="IPostingKeyCatalog"/>): <see cref="PostingKeys"/> in the PRD order (Stage 3 session 4a).</summary>
public sealed class PostingKeyCatalog : IPostingKeyCatalog
{
    public IReadOnlyList<DirectService.Accounting.Contracts.Posting.PostingKeyDef> All { get; } = PostingKeys.All
        .Select(d => new DirectService.Accounting.Contracts.Posting.PostingKeyDef(d.Key, d.NameAr, d.NameEn, d.RequiredOnlyDuringSetup, d.Optional, d.TemplateAccountCode))
        .ToList();

    public IReadOnlyList<string> Names(IEnumerable<string> keys, string? language) => PostingKeys.Names(keys, language);

    public IEnumerable<string> Required(bool setupOpen) => PostingKeys.Required(setupOpen);

    public string NameList(IEnumerable<string> keys, string? language) => PostingKeys.NameList(keys, language);
}
