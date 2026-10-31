using DirectService.Accounting.Contracts.Accounting;

namespace DirectService.Accounting.Contracts.Modules;

// First form of the remaining contribution contracts of design c / j (Stage 3 session 2). Nothing implements or calls them yet; the session
// that switches each use (sessions 6, 7, 10, 13 of the Stage 3 plan) may refine its members and records the change as a DT row.

/// <summary>Something that keeps a period from closing (e.g. draft invoices), with how many there are.</summary>
public sealed record PeriodCloseIssue(string Code, int Count);

/// <summary>A module's pre-close check of a period (FR-117): the period service asks every registered check.</summary>
public interface IPeriodCloseCheck
{
    Task<IReadOnlyList<PeriodCloseIssue>> CheckAsync(string periodMonth, CancellationToken ct);
}

/// <summary>A movement of a cash box or bank account that a module's document made (receipt, advance, settlement, partner payment).</summary>
public sealed record CashMovementRow(long? CashBoxId, long? BankAccountId, DateOnly Date, string DocumentType, long DocumentId, string Number, decimal In, decimal Out);

/// <summary>A module's cash and bank movements for the cash & banks screens; a query composed in the caller's context.</summary>
public interface ICashMovementSource
{
    string Module { get; }

    IQueryable<CashMovementRow> Movements(DateOnly from, DateOnly to);
}

/// <summary>A document to report to the tax authority (lines as the e-invoice needs them).</summary>
public sealed record EInvoiceLine(string Description, decimal Quantity, decimal UnitPrice, decimal Discount, decimal TaxRate);

public sealed record EInvoiceDocument(string DocumentType, long DocumentId, string Number, DateOnly IssueDate, string? BuyerName, string? BuyerTaxNumber,
    string? OriginalNumber, IReadOnlyList<EInvoiceLine> Lines);

/// <summary>A module's documents that are e-invoiced (transport invoices and credit notes, K-10).</summary>
public interface IEInvoiceDocumentSource
{
    string DocumentType { get; }

    Task<EInvoiceDocument?> BuildAsync(long documentId, CancellationToken ct);
}

/// <summary>An account a product adds to the chart template of a new company.</summary>
public sealed record ChartTemplateAccount(string Code, string NameAr, string NameEn, string AccountType, string? ParentCode, string? ControlPartyType);

/// <summary>A product's extension of the general chart template (K-13): its accounts and the template account of each of its posting keys.</summary>
public interface IChartTemplateExtension
{
    string ProductCode { get; }

    IReadOnlyList<ChartTemplateAccount> Accounts { get; }

    IReadOnlyDictionary<string, string> Mapping { get; }
}

/// <summary>The chart of accounts as another module uses it (Stage 3 session 6c): the approved template for a new or demo company.</summary>
public interface IChartOfAccountsSetup
{
    /// <summary>Creates the template in an empty chart: accounts, main cash box, posting-key mapping (refused when the chart has accounts).</summary>
    Task<ChartTemplateResult> ApplyTemplateAsync(CancellationToken ct);
}

/// <summary>A record type that refers to an accounting record, and how many of them (the "in use" message of a delete, BR-040).</summary>
public sealed record RecordUse(string Type, int Count);

/// <summary>A module's documents that use a cash box or bank account (the delete check of cash boxes and bank accounts, Stage 3 session 6c).</summary>
public interface IMoneyAccountUseSource
{
    /// <summary>The module's uses of the cash box or bank account, in the module's order; only the record types with a count above 0.</summary>
    Task<IReadOnlyList<RecordUse>> UsesAsync(long? cashBoxId, long? bankAccountId, CancellationToken ct);
}
