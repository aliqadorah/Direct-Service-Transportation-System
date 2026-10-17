namespace DirectService.Transportation.Application.Configuration;

/// <summary>cfg.CompanySettings keys (module-prefixed, design §3.2) with their PRD defaults (7.9.1).</summary>
public static class SettingKeys
{
    public const string BillOnUnloadedWeight = "TRN.BillOnUnloadedWeight";
    public const string PayDriverOnLoadedWeight = "TRN.PayDriverOnLoadedWeight";
    public const string ShortageTolerancePct = "TRN.ShortageTolerancePct";
    public const string LockTariffs = "TRN.LockTariffs";
    public const string InvoiceApproval = "TRN.InvoiceApproval";
    public const string AccrueDriverWage = "TRN.AccrueDriverWage";

    /// <summary>Market validation formats (7.9.1 "phone and IBAN validation formats"; launch market Jordan).</summary>
    public const string PhonePattern = "CORE.PhonePattern";
    public const string IbanPattern = "CORE.IbanPattern";

    /// <summary>Demo company flag (G-11) and setup checklist marks (G-10).</summary>
    public const string IsDemo = "CORE.IsDemo";
    public const string SetupPrefix = "CORE.Setup.";

    /// <summary>
    /// Last date (company time, yyyy-MM-dd) a supplier bill with a deduction was refused because "Supplier deductions" is not mapped: the
    /// accounting dashboard reminds to map it while such bills are being entered (revision 6 part B).
    /// </summary>
    public const string SupplierDeductionAttempted = "ACC.SupplierDeductionAttempted";

    public const string JordanPhonePattern = "^(07[789][0-9]{7}|06[0-9]{7})$";
    public const string JordanIbanPattern = "^JO[0-9]{2}[A-Z]{4}[0-9]{4}[A-Z0-9]{18}$";

    /// <summary>Defaults written when a company is created (cfg.CompanySettings business rules).</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [BillOnUnloadedWeight] = "true",
        [PayDriverOnLoadedWeight] = "false",
        [ShortageTolerancePct] = "0.5",
        [LockTariffs] = "false",
        [InvoiceApproval] = "true",
        [AccrueDriverWage] = "true",
        [PhonePattern] = JordanPhonePattern,
        [IbanPattern] = JordanIbanPattern,
    };
}

/// <summary>cfg.NumberSequences document types with the approved prefixes (D-16, BR-037), 5 digits.</summary>
public static class DocumentTypes
{
    public const string Invoice = "TRN.Invoice";
    public const string CreditNote = "TRN.CreditNote";
    public const string Receipt = "TRN.Receipt";
    public const string Advance = "TRN.Advance";
    public const string Settlement = "TRN.Settlement";
    public const string PartnerPayment = "TRN.PartnerPayment";
    public const string JournalEntry = "ACC.JournalEntry";
    public const string SupplierBill = "ACC.SupplierBill";
    public const string Receivable = "ACC.Receivable";
    public const string ApArPayment = "ACC.ApArPayment";
    public const string ApArReturn = "ACC.ApArReturn";
    public const string JournalExport = "ACC.JournalExport";

    public static readonly IReadOnlyDictionary<string, string> DefaultPrefixes = new Dictionary<string, string>
    {
        [Invoice] = "INV-",
        [CreditNote] = "CRN-",
        [Receipt] = "RCV-",
        [Advance] = "ADV-",
        [Settlement] = "STL-",
        [PartnerPayment] = "PPY-",
        [JournalEntry] = "JV-",
        [SupplierBill] = "BIL-",
        [Receivable] = "ARD-",
        [ApArPayment] = "PAY-",
        [ApArReturn] = "RTN-",
        [JournalExport] = "JEX-",
    };

    public const byte DefaultDigits = 5;

    /// <summary>Formats a number: prefix + zero-padded counter, e.g. INV-00045.</summary>
    public static string Format(string prefix, byte digits, long number) =>
        prefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0');
}
