using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Hosts the real application against a dedicated test database (recreated once per run from the
/// migrations). Email is captured in memory; the URL-token key is random per run. Tests isolate
/// themselves by signing up their own tenant (ADR-18).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// The test database. The shared API collection uses DirectServiceMaster_ApiTests; migration tests use their own database (DT-231), so the
    /// data other tests leave behind (e.g. driver repayments) never decides whether a migration can be rolled back.
    /// </summary>
    protected virtual string DatabaseName => "DirectServiceMaster_ApiTests";

    /// <summary>The connection string of this factory's database (CI gives the server and credentials in DST_API_TEST_DB).</summary>
    public string Database => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("DST_API_TEST_DB") ?? ConnectionString)
    {
        InitialCatalog = DatabaseName,
    }.ConnectionString;

    public const string ConnectionString =
        "Server=localhost;Database=DirectServiceMaster_ApiTests;Integrated Security=true;TrustServerCertificate=true;Encrypt=true";

    public const string WebhookSecret = "test-webhook-secret";

    /// <summary>Platform:SetupCode of the test host (first-run provider setup, owner feedback 13 Oct 2026).</summary>
    public const string SetupCode = "TEST-SETUP-CODE-0077";

    public CapturingEmailSender Emails { get; } = new();

    /// <summary>Microsoft sign-in and Graph for the system email tests (DT-250).</summary>
    public DirectService.Transportation.Tests.Email.FakeGraph Graph { get; } = new();

    /// <summary>Live messages the application published (record changes, locks, notifications), recorded instead of SignalR.</summary>
    public RecordingNotifier Live { get; } = new();

    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), "dst-api-tests-keys-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:MasterDb", Database);
        builder.UseSetting("UrlTokens:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("DataProtection:KeysPath", _keysPath);
        builder.UseSetting("Jobs:Enabled", "false");
        builder.UseSetting("RateLimits:AuthPerMinutePerIp", "100000");
        builder.UseSetting("RateLimits:ApiPerMinutePerUser", "100000");
        builder.UseSetting("RateLimits:ApiPerMinutePerTenant", "100000");
        builder.UseSetting("App:PublicBaseUrl", "https://app.test");
        builder.UseSetting("EInvoicing:AllowSimulator", "true");
        builder.UseSetting("EInvoicing:MaxAttempts", "2");
        builder.UseSetting("Billing:WebhookSecret", WebhookSecret);
        builder.UseSetting("Platform:SetupCode", SetupCode);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(Emails);
            // System email (DT-250): Microsoft sign-in and Graph are the test double in every API test — never a real credential or email.
            s.AddHttpClient(DirectService.Transportation.Infrastructure.Email.GraphEmailTransport.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Graph);
            s.RemoveAll<IRealtimeNotifier>();
            s.AddSingleton<IRealtimeNotifier>(Live);
            s.AddTransient<IStartupFilter, TestClientIpFilter>();
        });
    }

    /// <summary>Test host only: the "X-Test-Client-IP" header sets the connection's address (sign-in lockout per account + IP, DT-204).</summary>
    private sealed class TestClientIpFilter : IStartupFilter
    {
        public const string Header = "X-Test-Client-IP";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, nextMiddleware) =>
            {
                if (http.Request.Headers.TryGetValue(Header, out var ip) && System.Net.IPAddress.TryParse(ip.ToString(), out var address))
                    http.Connection.RemoteIpAddress = address;
                return nextMiddleware(http);
            });
            next(app);
        };
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
        try { Directory.Delete(_keysPath, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public HttpClient NewClient(string language = "en")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        return client;
    }

    /// <summary>Runs code against the database with an unscoped context (assertions only).</summary>
    public async Task<T> DbAsync<T>(Func<MasterDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
        return await query(db);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}

/// <summary>Captures outgoing emails so tests can follow verification, reset and invitation links.</summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    /// <summary>How many emails were sent so far.</summary>
    public int Count => _sent.Count;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>How many emails of a template were sent to an address.</summary>
    public int CountOf(string to, string template) => _sent.Count(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase) && m.Template == template);

    public EmailMessage Last(string to, string template) =>
        _sent.Reverse().First(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase) && m.Template == template);

    /// <summary>Query parameters of the link in the last email of a template sent to an address.</summary>
    public Dictionary<string, string> LinkParameters(string to, string template)
    {
        var text = Last(to, template).TextBody;
        var url = LinkRegex().Match(text).Value;
        var query = new Uri(url).Query.TrimStart('?');
        return query.Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
    }

    [GeneratedRegex(@"https://\S+")]
    private static partial Regex LinkRegex();
}

/// <summary>RFC 6238 TOTP (SHA-1, 30 s, 6 digits) as used by authenticator apps, for driving the real 2FA flow.</summary>
public static class Totp
{
    private static readonly Dictionary<string, long> LastStep = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    /// <summary>
    /// A fresh code, as the app shows it. The server accepts a code once only (DT-212) and the current step ±1, so each call uses a later
    /// step than the previous call for the same key — the previous, current or next one; when all are used it waits for the next 30-second
    /// step, like a person waiting for the app's next code. With <paramref name="at"/>: the code of that moment (no bookkeeping).
    /// </summary>
    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        if (at is { } moment) return CodeAt(base32Key, moment.ToUnixTimeSeconds() / 30);
        while (true)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            lock (Gate)
            {
                // The server accepts now ± 1; the previous step only while it cannot expire on the way (more than 3 s left in this step).
                var earliest = 30 - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30 > 3 ? now - 1 : now;
                var step = Math.Max(earliest, (LastStep.TryGetValue(base32Key, out var last) ? last : long.MinValue + 1) + 1);
                if (step <= now + 1)
                {
                    LastStep[base32Key] = step;
                    return CodeAt(base32Key, step);
                }
            }
            Thread.Sleep(TimeSpan.FromSeconds((now + 1) * 30 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0) + TimeSpan.FromMilliseconds(250));
        }
    }

    /// <summary>The code of one time step (Unix seconds / 30).</summary>
    public static string CodeAt(string base32Key, long step)
    {
        var key = Base32Decode(base32Key);
        var counter = step;
        var msg = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
#pragma warning disable CA5350 // RFC 6238 authenticator codes are defined over HMAC-SHA1 (what authenticator apps use).
        var hash = HMACSHA1.HashData(key, msg);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = input.Trim().TrimEnd('=').ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. bytes];
    }
}

/// <summary>A signed-in test user with its HTTP client.</summary>
public sealed record SignedInUser(HttpClient Client, MeResponse Me, string Email, string Password, string AuthenticatorKey, IReadOnlyList<string> RecoveryCodes)
{
    public string CompanyId => Me.Companies[0].Id;
}

public static class ApiHelpers
{
    public const string Password = "Passw0rd-Test";

    private static readonly string[] FirstSetupSteps = ["twoFactorSetup", "twoFactorOffer"];

    public static async Task UseAntiforgeryAsync(this HttpClient client)
    {
        var t = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/v1/auth/antiforgery");
        client.DefaultRequestHeaders.Remove(t!.HeaderName);
        client.DefaultRequestHeaders.Add(t.HeaderName, t.Token);
    }

    /// <summary>Signs up a new tenant (Owner), verifies the email, sets up 2FA and returns the signed-in Owner.</summary>
    public static async Task<SignedInUser> SignUpOwnerAsync(this ApiFactory f, string? organisation = null)
    {
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-up",
            new SignUpRequest("Test Owner", email, Password, organisation ?? "Org " + Guid.NewGuid().ToString("N")[..8], true, "en"));
        await r.EnsureOkAsync();
        var link = f.Emails.LinkParameters(email, "VerifyEmail");
        (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new LinkTokenRequest(link["u"], link["t"]))).EnsureSuccessStatusCode();
        return await f.SignInFirstTimeAsync(client, email, Password);
    }

    /// <summary>Password step + 2FA set-up at the first sign-in (required, or offered with "Skip for now" since revision 6).</summary>
    public static async Task<SignedInUser> SignInFirstTimeAsync(this ApiFactory f, HttpClient client, string email, string password)
    {
        _ = f;
        var signIn = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(email, password));
        await signIn.EnsureOkAsync();
        Assert.Contains((await signIn.Content.ReadFromJsonAsync<SignInResponse>())!.Next, FirstSetupSteps);
        var setup = await client.GetFromJsonAsync<TwoFactorSetupResponse>("/api/v1/auth/2fa/setup");
        var done = await client.PostAsJsonAsync("/api/v1/auth/2fa/setup", new TwoFactorCodeRequest(Totp.Code(setup!.SharedKey), null));
        await done.EnsureOkAsync();
        var codes = (await done.Content.ReadFromJsonAsync<RecoveryCodesResponse>())!;
        await client.UseAntiforgeryAsync(); // identity changed → new antiforgery token
        return new SignedInUser(client, codes.Me, email, password, setup.SharedKey, codes.RecoveryCodes);
    }

    /// <summary>Signs in an enrolled user (password + authenticator code) on a fresh client.</summary>
    public static async Task<HttpClient> SignInAsync(this ApiFactory f, string email, string password, string authenticatorKey)
    {
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var signIn = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(email, password));
        await signIn.EnsureOkAsync();
        var verify = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(Totp.Code(authenticatorKey), null));
        await verify.EnsureOkAsync();
        await client.UseAntiforgeryAsync();
        return client;
    }

    public static async Task EnsureOkAsync(this HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode)
            Assert.Fail($"{(int)r.StatusCode} {r.ReasonPhrase}: {await r.Content.ReadAsStringAsync()}");
    }

    /// <summary>The problem code, or the first field error's code for a validation problem (field errors).</summary>
    public static async Task<string> ErrorCodeAsync(this HttpResponseMessage r)
    {
        var doc = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        if (doc.TryGetProperty("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Array && errors.GetArrayLength() > 0)
            return errors[0].GetProperty("code").GetString()!;
        return doc.TryGetProperty("code", out var c) ? c.GetString()! : string.Empty;
    }

    public static async Task<string> ProblemCodeAsync(this HttpResponseMessage r)
    {
        var doc = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return doc.TryGetProperty("code", out var c) ? c.GetString()! : string.Empty;
    }
}

/// <summary>Records what would be pushed to connected users (FR-185, FR-190).</summary>
public sealed class RecordingNotifier : IRealtimeNotifier
{
    public ConcurrentQueue<(long CompanyId, string EntityType, string? Token, string Action)> Records { get; } = new();

    public ConcurrentQueue<(Guid User, object Payload)> Notifications { get; } = new();

    public Task RecordChangedAsync(long companyId, string entityType, string? token, string action, CancellationToken ct)
    {
        Records.Enqueue((companyId, entityType, token, action));
        return Task.CompletedTask;
    }

    public Task LockChangedAsync(long companyId, string entityType, string token, string? holderName, CancellationToken ct) => Task.CompletedTask;

    public Task NotifyUserAsync(Guid userPublicId, object notification, CancellationToken ct)
    {
        Notifications.Enqueue((userPublicId, notification));
        return Task.CompletedTask;
    }

    public ConcurrentQueue<(Guid Staff, object Payload)> SupportEnds { get; } = new();

    public Task SupportEndedAsync(Guid staffPublicId, object payload, CancellationToken ct)
    {
        SupportEnds.Enqueue((staffPublicId, payload));
        return Task.CompletedTask;
    }
}

/// <summary>DT-231: the trips-list migration test on its own database (no other test's data in it).</summary>
public sealed class TripListMigrationFactory : ApiFactory
{
    protected override string DatabaseName => "DirectServiceMaster_MigrationTests";
}

/// <summary>DT-259: the journal line tax upgrade / rollback test on its own database.</summary>
public sealed class JournalTaxUpgradeFactory : ApiFactory
{
    protected override string DatabaseName => "DirectServiceMaster_TaxUpgradeTests";
}

/// <summary>DT-231: the settlement upgrade / rollback test on its own database.</summary>
public sealed class SettlementUpgradeFactory : ApiFactory
{
    protected override string DatabaseName => "DirectServiceMaster_UpgradeTests";
}

/// <summary>
/// System email (DT-250…DT-252) on its own database: the settings are platform-wide, so these tests never meet another class's settings or
/// outbox. Flows still go to the captured emails (sign-up helpers); the tests use the outbox writer and the dispatcher directly.
/// </summary>
public sealed class SystemEmailFactory : ApiFactory
{
    protected override string DatabaseName => "DirectServiceMaster_EmailTests";

    /// <summary>The development mode's folder of this run.</summary>
    public string DropFolder { get; } = Path.Combine(Path.GetTempPath(), "dst-email-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Email:DropFolder", DropFolder);
        builder.ConfigureServices(s => s.AddScoped<DirectService.Transportation.Infrastructure.Email.OutboxEmailSender>());
    }
}

[CollectionDefinition(Name)]
public sealed class SystemEmailCollection : ICollectionFixture<SystemEmailFactory>
{
    public const string Name = "SystemEmail";
}
