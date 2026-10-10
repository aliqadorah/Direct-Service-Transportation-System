using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// Creates the per-company reference data the PRD defines when a company is created (step 3 seed
/// rule: per-company data is created by the application, not by a migration):
/// built-in ownership type "Company" (own fleet, FR-161), the TRN.* operation settings with their
/// defaults (7.9.1) and the market formats, the 12 number sequences (D-16), the e-invoicing settings
/// row (disabled, FR-059) and the two invoice layouts (FR-054). The chart of accounts is created
/// only when chosen in onboarding (template awaiting approval, D-10).
/// </summary>
public sealed class CompanyProvisioningService(IAppDbContext db, IDataScope scope)
{
    /// <summary>Name of the built-in ownership type. Fixed (cannot be renamed, C-08); the UI translates it.</summary>
    public const string BuiltInOwnershipType = "Company";

    public static readonly IReadOnlyList<string> DefaultSignatures = ["المحاسب", "المدير المالي"];

    /// <summary>Default annex columns (keys of <c>InvoiceLayoutService.BuiltInColumns</c>, FR-056).</summary>
    public static readonly IReadOnlyList<string> DefaultAnnexColumns =
        ["receiptNo", "truckPlate", "loadingDate", "unloadedWeight", "billingQty", "unitPrice", "amount"];

    /// <summary>Adds and saves the reference rows for an already saved <paramref name="company"/> (inside the caller's transaction).</summary>
    public async Task ProvisionAsync(Company company, long? actingUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(company);
        // Saved inside the company's own scope so the interceptor stamps and checks the right tenant/company.
        using var _ = scope.Use(company.TenantId, company.Id, actingUserId);
        var t = company.TenantId;
        var c = company.Id;

        db.Set<OwnershipType>().Add(new OwnershipType
        {
            TenantId = t, CompanyId = c, Name = BuiltInOwnershipType, FleetClass = "Own", IsBuiltIn = true,
        });

        foreach (var (key, value) in SettingKeys.Defaults)
            db.Set<CompanySetting>().Add(new CompanySetting { TenantId = t, CompanyId = c, SettingKey = key, SettingValue = value });

        foreach (var (type, prefix) in DocumentTypes.DefaultPrefixes)
            db.Set<NumberSequence>().Add(new NumberSequence
            {
                TenantId = t, CompanyId = c, DocumentType = type, Prefix = prefix, Digits = DocumentTypes.DefaultDigits, NextNumber = 1,
            });

        db.Set<EInvoiceSettings>().Add(new EInvoiceSettings { TenantId = t, CompanyId = c, IsEnabled = false });

        var columns = JsonSerializer.Serialize(DefaultAnnexColumns);
        var signatures = JsonSerializer.Serialize(DefaultSignatures);
        db.Set<InvoiceLayout>().Add(new InvoiceLayout
        {
            TenantId = t, CompanyId = c, LayoutType = "Claim", Title = "كشف مطالبة", Grouping = "LoadingSite",
            AnnexColumns = columns, Signatures = signatures,
        });
        db.Set<InvoiceLayout>().Add(new InvoiceLayout
        {
            TenantId = t, CompanyId = c, LayoutType = "TaxInvoice", Title = "فاتورة ضريبية", Grouping = "LoadingSite",
            AnnexColumns = columns, Signatures = signatures,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
