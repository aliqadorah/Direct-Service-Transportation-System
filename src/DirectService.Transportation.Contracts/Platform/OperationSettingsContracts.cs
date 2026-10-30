namespace DirectService.Transportation.Contracts.Platform;

// The operations section of the company settings (FR-170, FR-171): Transportation's part; the rest of the company settings DTOs are in
// Platform.Contracts (DirectService.Platform.Contracts.Tenants, Stage 2 session 13).

public sealed record OperationSettingsResponse(
    bool BillOnUnloadedWeight, bool PayDriverOnLoadedWeight, decimal ShortageTolerancePct, bool LockTariffs,
    bool InvoiceApproval, bool AccrueDriverWage, DateTime? MigrationCompletedAt);

/// <summary>FR-170: changing a weight-basis setting requires <see cref="ConfirmWeightBasisChange"/> = true.</summary>
public sealed record OperationSettingsRequest(
    bool BillOnUnloadedWeight, bool PayDriverOnLoadedWeight, decimal ShortageTolerancePct, bool LockTariffs,
    bool InvoiceApproval, bool AccrueDriverWage, bool ConfirmWeightBasisChange);

/// <summary>FR-171: irreversible; <see cref="Confirm"/> must be true.</summary>
public sealed record MigrationCompleteRequest(bool Confirm);
