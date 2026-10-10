using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Cfg;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Application.EInvoicing;

/// <summary>E-invoicing options: the simulator connector is offered only where allowed (development, tests); attempts before Rejected.</summary>
public sealed class EInvoicingOptions
{
    public const string Section = "EInvoicing";

    /// <summary>Offer the simulator connector (never in production).</summary>
    public bool AllowSimulator { get; set; }

    /// <summary>Technical failures retried automatically before the document becomes Rejected ("Failed", D-04).</summary>
    public int MaxAttempts { get; set; } = 5;
}

/// <summary>
/// E-invoicing settings per company (FR-059, FR-066, SEC-09): switch, connector, seller tax number, registration type, income-source
/// sequence, environment, and the API credentials encrypted with Data Protection — write-only, never returned or logged (audit
/// writes "***"). Enabling requires every value the submission needs.
/// </summary>
public sealed class EInvoiceSettingsService(IAppDbContext db, ISecretProtector protector, IEnumerable<IEInvoiceConnector> connectors,
    IOptions<EInvoicingOptions> options)
{
    private static readonly string[] RegistrationTypes = [EInvoiceCodes.Income, EInvoiceCodes.Sales, EInvoiceCodes.Special];
    private static readonly string[] Environments = [EInvoiceCodes.Test, EInvoiceCodes.Production];

    /// <summary>Connectors offered in this environment (FR-066).</summary>
    public IReadOnlyList<string> Available => connectors.Select(c => c.Name).Where(n => n != EInvoiceCodes.Simulator || options.Value.AllowSimulator).Order(StringComparer.Ordinal).ToList();

    public async Task<EInvoiceSettingsResponse> GetAsync(CancellationToken ct)
    {
        var row = await db.Set<EInvoiceSettings>().AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return ToResponse(row);
    }

    public async Task<EInvoiceSettingsResponse> SaveAsync(EInvoiceSettingsRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var row = await db.Set<EInvoiceSettings>().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is not null)
        {
            if (string.IsNullOrEmpty(r.RowVersion)) throw Fail.Field("rowVersion", ErrorCodes.ValidationRequired, "rowVersion");
            db.ExpectRowVersion(row, r.RowVersion);
        }
        var connector = string.IsNullOrWhiteSpace(r.Connector) ? row?.Connector ?? EInvoiceCodes.JoFotara : r.Connector.Trim();
        var registration = string.IsNullOrWhiteSpace(r.RegistrationType) ? row?.RegistrationType ?? EInvoiceCodes.Sales : r.RegistrationType.Trim();
        var environment = string.IsNullOrWhiteSpace(r.Environment) ? row?.Environment ?? EInvoiceCodes.Test : r.Environment.Trim();
        var seller = TextNormalizer.CleanCode(r.SellerTaxNumber);
        var source = TextNormalizer.CleanCode(r.IncomeSourceSequence);
        var clientId = r.ClientId?.Trim();
        var secret = r.SecretKey?.Trim();
        var hasClient = !string.IsNullOrEmpty(clientId) || row?.ClientIdEncrypted is { Length: > 0 };
        var hasSecret = !string.IsNullOrEmpty(secret) || row?.SecretKeyEncrypted is { Length: > 0 };
        new Validator()
            .Check(Available.Contains(connector, StringComparer.Ordinal), "connector", ErrorCodes.ValidationFormat)
            .Check(RegistrationTypes.Contains(registration, StringComparer.Ordinal), "registrationType", ErrorCodes.ValidationFormat)
            .Check(Environments.Contains(environment, StringComparer.Ordinal), "environment", ErrorCodes.ValidationFormat)
            .Check(!(connector == EInvoiceCodes.Simulator && environment == EInvoiceCodes.Production), "environment", ErrorCodes.ValidationFormat)
            .Check(seller is null || (seller.Length <= 30 && seller.All(char.IsAsciiDigit)), "sellerTaxNumber", ErrorCodes.ValidationFormat)
            .Check(source is null || (source.Length <= 30 && source.All(char.IsAsciiDigit)), "incomeSourceSequence", ErrorCodes.ValidationFormat)
            .Check(clientId is null || clientId.Length <= 200, "clientId", ErrorCodes.ValidationRange)
            .Check(secret is null || secret.Length <= 200, "secretKey", ErrorCodes.ValidationRange)
            // Sent as HTTP headers: visible ASCII only (review M5).
            .Check(clientId is null || clientId.All(ch => ch is > ' ' and <= '~'), "clientId", ErrorCodes.ValidationFormat)
            .Check(secret is null || secret.All(ch => ch is > ' ' and <= '~'), "secretKey", ErrorCodes.ValidationFormat)
            // Switching on needs everything a submission needs (FR-059, design: required when enabled).
            .Check(!r.IsEnabled || seller is not null, "sellerTaxNumber", ErrorCodes.ValidationRequired)
            .Check(!r.IsEnabled || source is not null, "incomeSourceSequence", ErrorCodes.ValidationRequired)
            .Check(!r.IsEnabled || hasClient, "clientId", ErrorCodes.ValidationRequired)
            .Check(!r.IsEnabled || hasSecret, "secretKey", ErrorCodes.ValidationRequired)
            .ThrowIfInvalid();
        // Documents waiting or rejected were built for the current connector and environment: finish or cancel them first (review M2).
        if (row is not null && (row.Environment != environment || row.Connector != connector)
            && await db.Set<Domain.Trn.EInvoiceSubmission>().AnyAsync(x => x.Status == EInvoiceCodes.Queued || x.Status == EInvoiceCodes.Rejected, ct).ConfigureAwait(false))
            throw Fail.Rule(ErrorCodes.EInvoicePendingDocuments);
        if (row is null)
        {
            row = new EInvoiceSettings();
            db.Set<EInvoiceSettings>().Add(row);
        }
        row.IsEnabled = r.IsEnabled;
        row.Connector = connector;
        row.RegistrationType = registration;
        row.Environment = environment;
        row.SellerTaxNumber = seller;
        row.IncomeSourceSequence = source;
        if (!string.IsNullOrEmpty(clientId)) row.ClientIdEncrypted = protector.Protect(clientId);
        if (!string.IsNullOrEmpty(secret)) row.SecretKeyEncrypted = protector.Protect(secret);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(row);
    }

    private EInvoiceSettingsResponse ToResponse(EInvoiceSettings? row) => row is null
        ? new(false, EInvoiceCodes.JoFotara, null, EInvoiceCodes.Sales, null, false, false, EInvoiceCodes.Test, 0, Available, null)
        : new(row.IsEnabled, row.Connector, row.SellerTaxNumber, row.RegistrationType ?? EInvoiceCodes.Sales, row.IncomeSourceSequence,
            row.ClientIdEncrypted is { Length: > 0 }, row.SecretKeyEncrypted is { Length: > 0 }, row.Environment, row.IcvCounter, Available,
            Convert.ToBase64String(row.RowVersion));

    /// <summary>The enabled settings of the current company, or null (documents are then not queued).</summary>
    internal async Task<EInvoiceSettings?> EnabledAsync(CancellationToken ct) =>
        await db.Set<EInvoiceSettings>().AsNoTracking().SingleOrDefaultAsync(s => s.IsEnabled, ct).ConfigureAwait(false);

    internal EInvoiceCredentials Credentials(EInvoiceSettings s) =>
        new(protector.Unprotect(s.ClientIdEncrypted!), protector.Unprotect(s.SecretKeyEncrypted!), s.Environment);

    internal IEInvoiceConnector? Connector(string name) =>
        Available.Contains(name, StringComparer.Ordinal) ? connectors.FirstOrDefault(c => c.Name == name) : null;
}
