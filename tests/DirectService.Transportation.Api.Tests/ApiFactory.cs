using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Infrastructure.Persistence;
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
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ConnectionString =
        "Server=localhost;Database=DirectServiceMaster_ApiTests;Integrated Security=true;TrustServerCertificate=true;Encrypt=true";

    public const string WebhookSecret = "test-webhook-secret";

    public CapturingEmailSender Emails { get; } = new();

    /// <summary>Live messages the application published (record changes, locks, notifications), recorded instead of SignalR.</summary>
    public RecordingNotifier Live { get; } = new();

    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), "dst-api-tests-keys-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:MasterDb", Environment.GetEnvironmentVariable("DST_API_TEST_DB") ?? ConnectionString);
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
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(Emails);
            s.RemoveAll<IRealtimeNotifier>();
            s.AddSingleton<IRealtimeNotifier>(Live);
        });
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

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

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
    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        var key = Base32Decode(base32Key);
        var counter = (long)((at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30);
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

    /// <summary>Password step + mandatory 2FA set-up (first sign-in).</summary>
    public static async Task<SignedInUser> SignInFirstTimeAsync(this ApiFactory f, HttpClient client, string email, string password)
    {
        _ = f;
        var signIn = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(email, password));
        await signIn.EnsureOkAsync();
        Assert.Equal("twoFactorSetup", (await signIn.Content.ReadFromJsonAsync<SignInResponse>())!.Next);
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
}
