using System.Security.Cryptography;
using System.Text;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// The one-time setup code of the first-run provider setup (owner feedback 13 Oct 2026, item 8a), kept in this process's memory: generated
/// at start-up while no platform staff exists (written to the log, never to a page or an e-mail) or taken from <c>Platform:SetupCode</c>
/// (never logged; refused when shorter than <see cref="MinConfiguredLength"/> characters). Wrong codes are counted per address: after
/// <see cref="MaxFailures"/> within <see cref="LockMinutes"/> minutes that address is refused for <see cref="LockMinutes"/> minutes, so one
/// caller cannot lock out the operator; above <see cref="GlobalMaxFailures"/> wrong codes in an hour from all addresses every attempt waits
/// <see cref="LockMinutes"/> minutes (security review 13 Oct 2026). A correct code opens one setup session (a random step key, 20 minutes);
/// creating the first staff account consumes the code.
/// </summary>
public sealed class ProviderSetupState
{
    public const int MaxFailures = 5;
    public const int GlobalMaxFailures = 100;
    public const int LockMinutes = 15;
    public const int MinConfiguredLength = 16;
    private static readonly TimeSpan SessionLife = TimeSpan.FromMinutes(20);
    private readonly Lock _gate = new();
    private string? _code;
    private string? _session;
    private DateTime _sessionEnds;
    private readonly Dictionary<string, (Queue<DateTime> Failures, DateTime LockedUntil)> _byAddress = new(StringComparer.Ordinal);
    private readonly Queue<DateTime> _all = new();
    private DateTime _globalLockedUntil;

    /// <summary>True when the code came from configuration (it is then never written to the log).</summary>
    public bool Configured { get; private set; }

    /// <summary>True when a configured code was refused as too short (the caller logs a warning without the value).</summary>
    public bool ConfiguredRefused { get; private set; }

    /// <summary>Sets the code: the configured one, else a new random one (returned so it can be logged once). Null when already set.</summary>
    public string? Initialise(string? configured)
    {
        lock (_gate)
        {
            if (_code is not null) return null;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var normalised = Normalise(configured);
                if (normalised.Length >= MinConfiguredLength)
                {
                    _code = normalised;
                    Configured = true;
                    return null;
                }
                ConfiguredRefused = true;
            }
            // 15 characters from an alphabet without look-alikes (≈ 74 bits), shown in groups of five.
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = Enumerable.Range(0, 15).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
            _code = new string(chars);
            return $"{_code[..5]}-{_code[5..10]}-{_code[10..]}";
        }
    }

    /// <summary>Checks a code from an address (case, spaces and dashes ignored; constant time). A correct code returns a new setup-session key.</summary>
    public (bool Ok, bool Locked, string? Session) Try(string? input, DateTime now, string? address = null)
    {
        lock (_gate)
        {
            var key = address ?? "unknown";
            var entry = _byAddress.TryGetValue(key, out var e) ? e : (Failures: new Queue<DateTime>(), LockedUntil: DateTime.MinValue);
            if (now < _globalLockedUntil || now < entry.LockedUntil) return (false, true, null);
            var ok = _code is not null && input is not null
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Normalise(input)), Encoding.UTF8.GetBytes(_code));
            if (!ok)
            {
                entry.Failures.Enqueue(now);
                while (entry.Failures.Count > 0 && entry.Failures.Peek() < now.AddMinutes(-LockMinutes)) entry.Failures.Dequeue();
                if (entry.Failures.Count >= MaxFailures)
                {
                    entry.LockedUntil = now.AddMinutes(LockMinutes);
                    entry.Failures.Clear();
                }
                _byAddress[key] = entry;
                _all.Enqueue(now);
                while (_all.Count > 0 && _all.Peek() < now.AddHours(-1)) _all.Dequeue();
                if (_all.Count >= GlobalMaxFailures)
                {
                    _globalLockedUntil = now.AddMinutes(LockMinutes);
                    _all.Clear();
                }
                return (false, now < entry.LockedUntil || now < _globalLockedUntil, null);
            }
            _byAddress.Remove(key);
            _session = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _sessionEnds = now + SessionLife;
            return (true, false, _session);
        }
    }

    /// <summary>The setup session opened by the code is still the current one and has not expired.</summary>
    public bool SessionValid(string? session, DateTime now)
    {
        lock (_gate)
        {
            return _session is not null && session is not null && now < _sessionEnds
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(session), Encoding.UTF8.GetBytes(_session));
        }
    }

    /// <summary>The first staff account exists: the code and its session can never be used again.</summary>
    public void Consume()
    {
        lock (_gate)
        {
            _code = null;
            _session = null;
        }
    }

    private static string Normalise(string s) =>
        new(s.Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).ToArray());
}

/// <summary>
/// First-run provider setup (owner feedback 13 Oct 2026, item 8a; replaces the <c>create-staff</c> command of DT-131 as the normal way,
/// which stays for recovery): while no platform staff account exists, <c>/provider/setup</c> asks for the one-time setup code, then
/// name, e-mail and password (platform policy), creates the first staff account and continues into the mandatory two-step set-up.
/// Once any staff account exists the setup is closed for good (404). Every attempt is audited.
/// </summary>
public sealed class ProviderSetupService(IAppDbContext db, IRequestContext ctx, IDataScope scope, UserManager<User> users, ProviderSetupState state,
    TenantLifecycleService lifecycle, IChangeContext changes, IPlatformLock locks, IOptions<PlatformOptions> options, TimeProvider clock)
{
    public const string AuditEntity = "CORE.ProviderSetup";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Staff accounts that count (the platform's system account is not a person).</summary>
    private IQueryable<User> Staff => db.Set<User>().IgnoreQueryFilters()
        .Where(u => u.IsPlatformStaff && !u.IsDeleted && u.Email != TenantLifecycleService.SystemEmail);

    /// <summary>The setup is open while no platform staff account exists.</summary>
    public async Task<bool> IsOpenAsync(CancellationToken ct) => !await Staff.AnyAsync(ct).ConfigureAwait(false);

    /// <summary>
    /// At start-up: while the setup is open, takes the configured code or generates one and writes the generated code to the log (a
    /// configured code is never logged). Nothing happens once staff exists.
    /// </summary>
    public async Task AnnounceAsync(ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (!await IsOpenAsync(ct).ConfigureAwait(false)) return;
        var generated = state.Initialise(options.Value.SetupCode);
#pragma warning disable CA1848, CA1873 // start-up message, written once
        if (state.ConfiguredRefused)
            logger.LogWarning("Platform:SetupCode is shorter than {MinLength} characters and was ignored; a one-time code was generated instead.", ProviderSetupState.MinConfiguredLength);
        if (generated is not null)
            logger.LogWarning("Provider first-run setup is open: no platform staff account exists. Open /provider/setup and enter this one-time setup code: {SetupCode}", generated);
        else if (state.Configured)
            logger.LogWarning("Provider first-run setup is open: no platform staff account exists. Open /provider/setup and enter the setup code configured in Platform:SetupCode.");
#pragma warning restore CA1848, CA1873
    }

    /// <summary>Step 1: the setup code. Returns the setup-session key for the next step; refusals are audited.</summary>
    public async Task<string> VerifyCodeAsync(string? code, CancellationToken ct)
    {
        if (!await IsOpenAsync(ct).ConfigureAwait(false)) throw Fail.NotFound();
        var (ok, locked, session) = state.Try(code, Now, ctx.IpAddress);
        await AuditAsync(ok ? "SetupCodeAccepted" : locked ? "SetupCodeLocked" : "SetupCodeRefused", ct).ConfigureAwait(false);
        if (locked) throw new AppException(ErrorCodes.SetupLocked, 429, "code", ProviderSetupState.LockMinutes);
        if (!ok) throw Fail.Field("code", ErrorCodes.SetupCodeInvalid);
        return session!;
    }

    /// <summary>The setup session of step 1 is still valid (the page keeps it in a hidden field).</summary>
    public bool SessionValid(string? session) => state.SessionValid(session, Now);

    /// <summary>
    /// Step 2: creates the first staff account (name, e-mail, password under the platform policy) — once, under a platform-wide lock — and
    /// consumes the code. The caller continues into the mandatory two-step set-up; the account cannot reach the console before it.
    /// </summary>
    public async Task<User> CreateFirstStaffAsync(string? session, string? fullName, string? email, string? password, CancellationToken ct)
    {
        if (!await IsOpenAsync(ct).ConfigureAwait(false)) throw Fail.NotFound();
        if (!state.SessionValid(session, Now))
        {
            await AuditAsync("SetupSessionRefused", ct).ConfigureAwait(false);
            throw Fail.Rule(ErrorCodes.SetupSessionExpired);
        }
        try
        {
            return await CreateAsync(fullName, email, password, ct).ConfigureAwait(false);
        }
        catch (AppException e) when (e.Status != 404)
        {
            await AuditAsync("SetupAccountRefused", ct, e.Code).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<User> CreateAsync(string? fullName, string? email, string? password, CancellationToken ct)
    {
        var name = TextNormalizer.CleanText(fullName);
        var mail = email?.Trim();
        new Validator().Require("fullName", name).MaxLength("fullName", name, 150).Require("email", mail).MaxLength("email", mail, 150)
            .Check(mail is null || AccountService.IsEmail(mail), "email", ErrorCodes.ValidationFormat).Require("password", password).ThrowIfInvalid();
        var policy = PasswordPolicy.Default;
        if (!policy.IsSatisfiedBy(password) || password!.Length > 64) throw AccountService.PolicyError(policy);

        await using var held = await locks.TryAcquireAsync("provider-setup", ct).ConfigureAwait(false) ?? throw Fail.Conflict(ErrorCodes.SetupBusy);
        if (!await IsOpenAsync(ct).ConfigureAwait(false)) throw Fail.NotFound();
        if (await users.FindByEmailAsync(mail!).ConfigureAwait(false) is not null) throw Fail.Conflict(ErrorCodes.EmailInUse);
        var system = await lifecycle.SystemUserAsync(ct).ConfigureAwait(false);
        var user = new User
        {
            FullName = name!, Email = mail!, LanguageCode = ctx.Language == "en" ? "en" : "ar", IsPlatformStaff = true, TenantId = null, EmailConfirmedAt = Now,
        };
        using (scope.Use(null, null, system))
        {
            changes.SetAction(user, "CreateFirstStaff", "Provider first-run setup");
            var created = await users.CreateAsync(user, password).ConfigureAwait(false);
            if (!created.Succeeded)
                throw created.Errors.Any(e => e.Code == TenantPasswordValidator.ErrorCode) ? AccountService.PolicyError(policy)
                    : created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName") ? Fail.Conflict(ErrorCodes.EmailInUse)
                    : Fail.Rule(ErrorCodes.ValidationFailed);
        }
        state.Consume();
        await AuditAsync("SetupCompleted", ct, user.Email).ConfigureAwait(false);
        return user;
    }

    /// <summary>An audit row of a setup attempt (anonymous; the address is recorded).</summary>
    private async Task AuditAsync(string action, CancellationToken ct, string? detail = null)
    {
        db.ChangeTracker.Clear();   // never save a half-built account with the audit row
        db.Set<AuditLog>().Add(new AuditLog
        {
            OccurredAt = Now, Action = action, EntityType = AuditEntity, IpAddress = ctx.IpAddress,
            NewValues = detail is null ? null : System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["_reason"] = detail }),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
