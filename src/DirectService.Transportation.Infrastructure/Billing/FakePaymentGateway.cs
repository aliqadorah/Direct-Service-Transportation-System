using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DirectService.Transportation.Infrastructure.Billing;

/// <summary>
/// Development payment gateway (DEF-02 — the real gateway is chosen later behind <see cref="IPaymentGateway"/>): a payment page URL per
/// invoice, webhooks verified by HMAC-SHA256 of the body with <c>Billing:WebhookSecret</c> (no secret configured → every webhook refused),
/// renewals charged successfully unless <c>Billing:DeclineRenewals</c> is set. Card data is never handled (SEC-10).
/// </summary>
public sealed class FakePaymentGateway(IConfiguration config) : IPaymentGateway
{
    private sealed record WebhookBody(string? Reference, string? Status, string? Message, Dictionary<string, string>? Metadata, decimal? Amount, string? Currency);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<GatewayCheckout> CreateCheckoutAsync(string invoiceNumber, decimal total, string currency, string tenantName, IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct)
    {
        var reference = "chk_" + Guid.NewGuid().ToString("N");
        var url = (config["Billing:PaymentPageUrl"] ?? "https://pay.test/checkout/") + reference;
        return Task.FromResult(new GatewayCheckout(reference, url));
    }

    public GatewayEvent? VerifyWebhook(string body, string? signature)
    {
        var secret = config["Billing:WebhookSecret"];
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature) || body is null) return null;
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.Trim().ToUpperInvariant()))) return null;
        try
        {
            var b = JsonSerializer.Deserialize<WebhookBody>(body, Json);
            if (b?.Reference is not { Length: > 0 } reference) return null;
            return new GatewayEvent(reference, string.Equals(b.Status, "succeeded", StringComparison.OrdinalIgnoreCase), b.Message, b.Metadata ?? [], b.Amount, b.Currency);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Signature a test or the development page sends with a webhook body.</summary>
    public static string Sign(string secret, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    public Task<GatewayEvent> ChargeAsync(string tenantKey, string invoiceNumber, decimal total, string currency, CancellationToken ct)
    {
        // The invoice number is the idempotency key: the same invoice always gets the same charge reference.
        var declined = config.GetValue("Billing:DeclineRenewals", false);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tenantKey + "|" + invoiceNumber)))[..32].ToLowerInvariant();
        return Task.FromResult(new GatewayEvent("chg_" + key, !declined, declined ? "Card declined" : null, new Dictionary<string, string>(), total, currency));
    }
}

/// <summary>
/// Platform-wide lock (review F5, R5): SQL Server's sp_getapplock owned by a session on its own unpooled connection, so two application
/// instances never run the same job at once; released explicitly and by closing the physical connection.
/// </summary>
public sealed class SqlPlatformLock(MasterDbContext db) : IPlatformLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken ct)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(db.Database.GetConnectionString()) { Pooling = false };
        var connection = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
        var resource = "dst:" + name;
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @name, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;";
            command.Parameters.AddWithValue("@name", resource);
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (result >= 0) return new Held(connection, resource);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        await connection.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    private sealed class Held(Microsoft.Data.SqlClient.SqlConnection connection, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "EXEC sp_releaseapplock @Resource = @name, @LockOwner = 'Session';";
                command.Parameters.AddWithValue("@name", resource);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                // Closing the unpooled connection below ends the session and its lock anyway.
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// Platform-wide subscription invoice numbers SUB-000001 … (raw SQL, listed in BUILD_STATUS.md): the highest number read WITH (UPDLOCK,
/// HOLDLOCK) inside the invoice's transaction, so two invoices never take the same number and a rollback leaves no gap.
/// </summary>
public sealed class PlatformNumbers(MasterDbContext db) : IPlatformNumbers
{
    public async Task<string> NextSubscriptionInvoiceAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Subscription invoice numbers are taken inside a transaction.");
        var last = await db.Database.SqlQuery<long>($"""
            SELECT ISNULL(MAX(CAST(SUBSTRING(Number, 5, 20) AS BIGINT)), 0) AS Value
            FROM core.SubscriptionInvoices WITH (UPDLOCK, HOLDLOCK)
            WHERE Number LIKE 'SUB-%'
            """).SingleAsync(ct).ConfigureAwait(false);
        return "SUB-" + (last + 1).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
