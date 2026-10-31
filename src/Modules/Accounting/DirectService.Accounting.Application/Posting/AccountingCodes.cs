using DirectService.Accounting.Contracts.Posting;

namespace DirectService.Accounting.Application.Posting;

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

    public const string ModuleTrn = PartyCodes.ModuleTrn;
    public const string ModuleAcc = PartyCodes.ModuleAcc;

    public const string SourceManual = "Manual";
    public const string SourceOpening = "Opening";

    /// <summary>EntryLine.PartyType values: TRN Customer/Driver/Partner, ACC ApArParty.</summary>
    public const string PartyCustomer = PartyCodes.Customer;
    public const string PartyDriver = PartyCodes.Driver;
    public const string PartyPartner = PartyCodes.Partner;
    public const string PartyApAr = PartyCodes.ApArParty;

    /// <summary>Calendar month code YYYY-MM of a date (design convention CHAR(7)).</summary>
    public static string Month(DateOnly date) => date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsMonth(string? value) =>
        value is { Length: 7 } && DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

    public static DateOnly FirstDay(string month) =>
        DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The cfg.NumberSequences document types owned by Accounting (stored values, design e.2; Stage 3 session 4a).</summary>
public static class AccountingDocumentTypes
{
    public const string JournalEntry = "ACC.JournalEntry";
    public const string JournalExport = "ACC.JournalExport";
}

/// <summary>Refuses activity dated in a closed period (BR-036, FR-133).</summary>
public interface IPeriodGuard
{
    Task EnsureOpenAsync(DateOnly date, CancellationToken ct);
}

/// <summary>
/// The posting keys of every product in the order users see them (PRD 7.5.1), for the engine's readiness check and its messages (names, never
/// codes, DT-224). Given by the transport module until the per-product catalogue of Stage 5 (Stage 3 session 4a).
/// </summary>
public interface IPostingKeyCatalog
{
    /// <summary>Every key in the PRD order (the account-mapping screen, Stage 3 session 6).</summary>
    IReadOnlyList<PostingKeyDef> All { get; }

    /// <summary>The users' names of the keys in a language; an unknown key is kept as it is.</summary>
    IReadOnlyList<string> Names(IEnumerable<string> keys, string? language);

    /// <summary>Keys that must be mapped before automatic posting and invoicing (FR-094); the opening-difference key only during setup.</summary>
    IEnumerable<string> Required(bool setupOpen);

    /// <summary>The users' names of the keys joined for a message ("، " in Arabic); an unknown key is kept as it is.</summary>
    string NameList(IEnumerable<string> keys, string? language);
}
