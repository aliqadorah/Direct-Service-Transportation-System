using DirectService.Transportation.Contracts.Common;

namespace DirectService.Transportation.Contracts.Accounting;

// Revision 6 part B (owner change request 15 Oct 2026): journal entry templates (نماذج القيود).

/// <summary>A template in the list.</summary>
public sealed record JournalTemplateSummary(string Id, string Name, string? Description, string? Reference, int Lines, bool HasAmounts, DateTime UpdatedAt, string? UpdatedByName,
    string RowVersion);

/// <summary>A template with its lines (accounts, cost centres, line descriptions, optional amounts).</summary>
public sealed record JournalTemplateResponse(string Id, string Name, string? Description, string? Reference, IReadOnlyList<JournalTemplateLineResponse> Lines,
    DateTime UpdatedAt, string RowVersion);

/// <summary>A template line; <c>Account</c> is null only if the account no longer exists.</summary>
public sealed record JournalTemplateLineResponse(AccountRef? Account, decimal Debit, decimal Credit, Ref? CostCentre, string? Note);

/// <summary>Creates or saves a template (rename = save with a new name). Lines: account required, amounts optional (0 = none).</summary>
public sealed record JournalTemplateRequest(string? Name, string? Description, string? Reference, IReadOnlyList<JournalTemplateLineRequest>? Lines, string? RowVersion = null);

public sealed record JournalTemplateLineRequest(string? AccountId, decimal Debit, decimal Credit, string? CostCentreId, string? Note);
