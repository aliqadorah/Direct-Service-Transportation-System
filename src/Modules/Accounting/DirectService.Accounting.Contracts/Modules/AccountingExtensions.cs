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
