using DirectService.Accounting.Contracts.Posting;

namespace DirectService.Accounting.Application.Posting;

/// <summary>
/// The posting keys owned by Accounting (design c, E-12; Stage 3 session 4a): sales tax output / input, the opening-balance difference (G-04) and
/// the supplier-bill deductions (revision 6). The stored strings, names, flags and template accounts are those of PRD 7.5.1 and the chart
/// template, unchanged; the transport module's TRN keys stay with it.
/// </summary>
public static class AccountingPostingKeys
{
    public const string SalesTaxOutput = "ACC.SalesTaxOutput";
    public const string SalesTaxInput = "ACC.SalesTaxInput";
    public const string OpeningBalanceDifference = "ACC.OpeningBalanceDifference";
    /// <summary>Deductions on supplier-bill lines (revision 6), credited when a bill has deductions.</summary>
    public const string SupplierDeductions = "ACC.SupplierDeductions";

    public static readonly IReadOnlyList<PostingKeyDef> All =
    [
        new(SalesTaxOutput, "ضريبة المبيعات (مخرجات)", "Sales tax output", false, false, "2104"),
        new(SalesTaxInput, "ضريبة المبيعات (مدخلات)", "Sales tax input", false, false, "1105"),
        new(OpeningBalanceDifference, "فرق الأرصدة الافتتاحية", "Opening balance difference", true, false, "3103"),
        // Revision 6: optional so that companies created before it keep posting; a bill with a deduction needs it mapped.
        new(SupplierDeductions, "خصومات الموردين", "Supplier deductions", false, true, "4105"),
    ];

    public static PostingKeyDef Get(string key) => All.Single(d => d.Key == key);
}

/// <summary>Accounting's keys for the posting contract (<see cref="IPostingKeyProvider"/>, product ACC).</summary>
public sealed class AccountingPostingKeyProvider : IPostingKeyProvider
{
    public string ProductCode => AccountingCodes.ModuleAcc;

    public IReadOnlyList<PostingKeyDef> Keys => AccountingPostingKeys.All;
}
