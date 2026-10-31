using DirectService.Accounting.Application.Posting;
namespace DirectService.Accounting.Application.Books;

/// <summary>
/// Built-in transport chart of accounts (FR-107), approved by the owner on 8 Oct 2026 as drafted in
/// docs/step3/03_Chart_Of_Accounts_Template_DRAFT.md (D-10), including: discount allowed as contra-revenue (4104),
/// driver deductions credited to revenue (4103), accumulated depreciation as an Asset-type account (1203).
/// Codes by level: 1 digit class · 2 group · 4 account · 6 sub-account. Only leaf accounts are postable.
/// Owner request 25 Oct 2026 (DT-256): the template's sales-tax accounts 1105 (input VAT) and 2104 (output VAT) are marked as tax accounts and
/// 5207 "VAT expenses (non-recoverable)" is added and marked; existing companies get them only through "Add the default tax accounts".
/// Owner request 26 Oct 2026: 5207 is named "ضريبة مبيعات غير قابلة للخصم" in Arabic (DT-263), and the direct transport costs (51) are the
/// cost of sales of the profit and loss statement (DT-264).
/// </summary>
public static class ChartTemplate
{
    public sealed record TemplateAccount(string Code, string NameAr, string NameEn, string Type, string? ParentCode, string? ControlPartyType, bool IsTax = false,
        string? ProfitLossGroup = null);

    /// <summary>A default tax account (DT-256): its type, the template code (marked when it exists), its group and names.</summary>
    public sealed record DefaultTaxAccount(string Type, string TemplateCode, string GroupCode, string NameAr, string NameEn);

    /// <summary>VAT on sales (output, liability), VAT on purchases (input, asset), VAT expenses (non-recoverable, expense).</summary>
    public static readonly IReadOnlyList<DefaultTaxAccount> TaxAccounts =
    [
        new("Liability", "2104", "21", "ضريبة المبيعات على المبيعات (مخرجات)", "VAT on sales (output VAT)"),
        new("Asset", "1105", "11", "ضريبة المبيعات على المشتريات (مدخلات)", "VAT on purchases (input VAT)"),
        new("Expense", "5207", "52", VatExpensesNameAr, "VAT expenses (non-recoverable)"),
    ];

    /// <summary>Arabic name of the VAT expenses account (owner decision 26 Oct 2026, DT-263).</summary>
    public const string VatExpensesNameAr = "ضريبة مبيعات غير قابلة للخصم";

    /// <summary>Earlier default Arabic names of the tax accounts (still recognised as the template's own account; renamed by Rev6_BudgetProfitLoss).</summary>
    public static readonly IReadOnlyList<string> FormerNamesAr = ["مصروف ضريبة المبيعات غير القابلة للاسترداد"];

    public const string MainCashBoxNameAr = "الصندوق الرئيسي";
    public const string MainCashBoxAccountCode = "110101";

    public static readonly IReadOnlyList<TemplateAccount> Accounts =
    [
        new("1", "الأصول", "Assets", "Asset", null, null),
        new("11", "الأصول المتداولة", "Current assets", "Asset", "1", null),
        new("1101", "النقدية في الصناديق", "Cash on hand", "Asset", "11", null),
        new("110101", "الصندوق الرئيسي", "Main cash box", "Asset", "1101", null),
        new("1102", "النقدية لدى البنوك", "Cash at banks", "Asset", "11", null),
        new("110201", "البنك – الحساب الجاري", "Bank – current account", "Asset", "1102", null),
        new("1103", "الذمم المدينة", "Receivables", "Asset", "11", null),
        new("110301", "ذمم عملاء النقل", "Trade receivables – transport customers", "Asset", "1103", "Customer"),
        new("110302", "ذمم مدينة أخرى", "Other receivables", "Asset", "1103", "Customer"),
        new("1104", "سلف السائقين", "Driver advances", "Asset", "11", "Driver"),
        new("1105", "ضريبة المبيعات على المشتريات (مدخلات)", "Sales tax input", "Asset", "11", null, IsTax: true),
        new("1106", "مصاريف مدفوعة مقدماً", "Prepaid expenses", "Asset", "11", null),
        new("12", "الأصول غير المتداولة", "Non-current assets", "Asset", "1", null),
        new("1201", "الشاحنات والمركبات", "Trucks and vehicles", "Asset", "12", null),
        new("1202", "الأثاث والأجهزة والمعدات", "Furniture and equipment", "Asset", "12", null),
        new("1203", "مجمع الإهلاك", "Accumulated depreciation", "Asset", "12", null),
        new("2", "الالتزامات", "Liabilities", "Liability", null, null),
        new("21", "الالتزامات المتداولة", "Current liabilities", "Liability", "2", null),
        new("2101", "ذمم الموردين", "Suppliers payable", "Liability", "21", "Supplier"),
        new("2102", "ذمم السائقين الدائنة", "Driver payable", "Liability", "21", "Driver"),
        new("2103", "ذمم شركات النقل الشريكة", "Partner payable", "Liability", "21", "Partner"),
        new("2104", "ضريبة المبيعات المستحقة (مخرجات)", "Sales tax output", "Liability", "21", null, IsTax: true),
        new("2105", "مصاريف مستحقة", "Accrued expenses", "Liability", "21", null),
        new("3", "حقوق الملكية", "Equity", "Equity", null, null),
        new("3101", "رأس المال", "Capital", "Equity", "3", null),
        new("3102", "الأرباح المحتجزة", "Retained earnings", "Equity", "3", null),
        new("3103", "فرق الأرصدة الافتتاحية", "Opening balance difference", "Equity", "3", null),
        new("4", "الإيرادات", "Revenue", "Revenue", null, null),
        new("4101", "إيرادات النقل", "Transport revenue", "Revenue", "4", null),
        new("4102", "إيرادات أخرى (رسوم إضافية)", "Other revenue (extra fees)", "Revenue", "4", null),
        new("4103", "إيرادات خصومات السائقين", "Driver deductions income", "Revenue", "4", null),
        new("4104", "الخصم المسموح به", "Discount allowed", "Revenue", "4", null),
        new("4105", "إيرادات خصومات الموردين", "Supplier deductions income", "Revenue", "4", null),
        new("5", "المصروفات", "Expenses", "Expense", null, null),
        new("51", "تكاليف النقل المباشرة", "Direct transport costs", "Expense", "5", null, ProfitLossGroup: "CostOfSales"),
        new("5101", "أجور السائقين", "Driver wage expense", "Expense", "51", null),
        new("5102", "تكلفة النقل لدى الشركاء", "Partner (subcontracted haulage) cost", "Expense", "51", null),
        new("5103", "الوقود", "Fuel", "Expense", "51", null),
        new("5104", "صيانة الشاحنات وقطع الغيار", "Truck maintenance and spare parts", "Expense", "51", null),
        new("5105", "تأمين وترخيص الشاحنات", "Truck insurance and licensing", "Expense", "51", null),
        new("52", "المصاريف الإدارية والعمومية", "General and administrative expenses", "Expense", "5", null),
        new("5201", "الرواتب والأجور الإدارية", "Administrative salaries and wages", "Expense", "52", null),
        new("5202", "الإيجارات", "Rent", "Expense", "52", null),
        new("5203", "الاتصالات والإنترنت", "Communications and internet", "Expense", "52", null),
        new("5204", "الرسوم والاشتراكات", "Fees and subscriptions", "Expense", "52", null),
        new("5205", "مصروف الإهلاك", "Depreciation expense", "Expense", "52", null),
        new("5206", "العمولات والمصاريف البنكية", "Bank charges", "Expense", "52", null),
        new("5207", VatExpensesNameAr, "VAT expenses (non-recoverable)", "Expense", "52", null, IsTax: true),
        new("5299", "مصاريف أخرى", "Other expenses", "Expense", "52", null),
    ];

    /// <summary>
    /// Default posting-key mapping (draft §2): every key's template account (<c>PostingKeyDef.TemplateAccountCode</c>, given with the key by its
    /// product since Stage 3 session 6c), in the PRD order of the catalogue.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Mapping(IPostingKeyCatalog catalog) =>
        catalog.All.Where(k => k.TemplateAccountCode is not null).ToDictionary(k => k.Key, k => k.TemplateAccountCode!, StringComparer.Ordinal);

    /// <summary>An account is postable when no other template account names it as parent.</summary>
    public static bool IsLeaf(string code) => Accounts.All(a => a.ParentCode != code);
}
