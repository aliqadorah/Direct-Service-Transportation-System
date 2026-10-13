using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner feedback 13 Oct 2026, item 8: provider staff without the terminal — the first-run setup (/provider/setup: one-time code, account,
/// mandatory two-step set-up, then 404 for good) and the console's staff screen (invite with a single-use 48-hour link, resend, cancel,
/// deactivate / reactivate, never oneself). The server-rendered pages are driven through their real forms.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed partial class StaffTests(ApiFactory f)
{
    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex Antiforgery();

    [GeneratedRegex("name=\"Details.Session\"[^>]*value=\"([0-9A-F]+)\"|value=\"([0-9A-F]{64})\"[^>]*name=\"Details.Session\"")]
    private static partial Regex SessionField();

    /// <summary>Posts a server-rendered form of the page at <paramref name="url"/> (its antiforgery token and handler name included).</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient c, string url, string page, string handler, params (string Name, string Value)[] fields)
    {
        var token = Antiforgery().Match(page).Groups[1].Value;
        var body = new List<KeyValuePair<string, string>> { new("_handler", handler), new("__RequestVerificationToken", WebUtility.HtmlDecode(token)) };
        body.AddRange(fields.Select(x => new KeyValuePair<string, string>(x.Name, x.Value)));
        return await c.PostAsync(url, new FormUrlEncodedContent(body));
    }

    /// <summary>Completes the mandatory two-step set-up of the partial sign-in on this client and returns the profile.</summary>
    private static async Task<MeResponse> CompleteTwoFactorAsync(HttpClient c)
    {
        var setup = await c.GetFromJsonAsync<TwoFactorSetupResponse>("/api/v1/auth/2fa/setup");
        await c.UseAntiforgeryAsync();
        var done = await ReadAsync<RecoveryCodesResponse>(await c.PostAsJsonAsync("/api/v1/auth/2fa/setup", new TwoFactorCodeRequest(Totp.Code(setup!.SharedKey), null)));
        await c.UseAntiforgeryAsync();
        return done.Me;
    }

    /// <summary>Hides every staff account (as if none existed yet) for the duration of a test; the returned action restores them.</summary>
    private async Task<Func<Task>> HideStaffAsync()
    {
        var ids = await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters()
            .Where(u => u.IsPlatformStaff && !u.IsDeleted && u.Email != TenantLifecycleService.SystemEmail).Select(u => u.Id).ToListAsync());
        await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.IsDeleted, true).SetProperty(x => x.DeletedAt, DateTime.UtcNow).SetProperty(x => x.DeletedBy, x => x.Id).SetProperty(x => x.DeleteReason, "test")));
        return () => f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.IsDeleted, false).SetProperty(x => x.DeletedAt, (DateTime?)null).SetProperty(x => x.DeletedBy, (long?)null).SetProperty(x => x.DeleteReason, (string?)null)));
    }

    [Fact]
    public async Task First_run_setup_needs_the_code_creates_the_first_staff_with_two_step_and_then_is_gone()
    {
        var restore = await HideStaffAsync();
        var email = $"first-staff-{Guid.NewGuid():N}@platform.test";
        try
        {
            // Start-up while no staff exists: the configured code is used and never written to the log.
            var log = new ListLogger();
            using (var scope = f.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ProviderSetupService>().AnnounceAsync(log, CancellationToken.None);
            Assert.Contains(log.Lines, l => l.Contains("/provider/setup", StringComparison.Ordinal) && l.Contains("Platform:SetupCode", StringComparison.Ordinal));
            Assert.DoesNotContain(log.Lines, l => l.Contains("SETUP", StringComparison.Ordinal) && l.Contains("0077", StringComparison.Ordinal));

            var c = f.NewClient();
            var page = await c.GetStringAsync("/provider/setup");
            Assert.Contains("setup-code", page, StringComparison.Ordinal);

            // A wrong code is refused (and audited); the right one (any case, spaces) opens step 2.
            var wrong = await (await PostFormAsync(c, "/provider/setup", page, "setup-code", ("Code.Code", "NOPE-NOPE"))).Content.ReadAsStringAsync();
            Assert.DoesNotContain("Details.Session", wrong, StringComparison.Ordinal);
            page = await (await PostFormAsync(c, "/provider/setup", wrong, "setup-code", ("Code.Code", " test-setup-code-0077 "))).Content.ReadAsStringAsync();
            var match = SessionField().Match(page);
            Assert.True(match.Success, "step 2 carries the setup session");
            var session = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;

            // The platform password policy applies; then the account is created and the two-step set-up follows at once.
            var weak = await (await PostFormAsync(c, "/provider/setup", page, "setup-account", ("Details.Session", session), ("Details.FullName", "First Staff"),
                ("Details.Email", email), ("Details.Password", "short"), ("Details.ConfirmPassword", "short"))).Content.ReadAsStringAsync();
            Assert.Equal(0, await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().CountAsync(u => u.Email == email)));
            var created = await PostFormAsync(c, "/provider/setup", weak, "setup-account", ("Details.Session", session), ("Details.FullName", "First Staff"),
                ("Details.Email", email), ("Details.Password", ApiHelpers.Password), ("Details.ConfirmPassword", ApiHelpers.Password));
            Assert.Contains("/two-factor/setup", created.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            await c.UseAntiforgeryAsync();
            var me = await CompleteTwoFactorAsync(c);
            Assert.True(me.IsPlatformStaff);
            await (await c.GetAsync("/api/v1/platform/staff")).EnsureOkAsync();

            // Gone for good: 404 now that a staff account exists, also for the old session and code.
            var anonymous = f.NewClient();
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/provider/setup")).StatusCode);
            var actions = await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().Where(a => a.EntityType == ProviderSetupService.AuditEntity).Select(a => a.Action).ToListAsync());
            Assert.Contains("SetupCodeRefused", actions);
            Assert.Contains("SetupCodeAccepted", actions);
            var user = await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Email == email));
            Assert.True(await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().AnyAsync(a => a.Action == "CreateFirstStaff" && a.EntityId == user.Id)));
            Assert.Null(user.TenantId);
        }
        finally
        {
            await restore();
        }
    }

    [Fact]
    public void Setup_code_is_checked_in_constant_time_locked_after_five_failures_and_used_once()
    {
        var state = new ProviderSetupState();
        var code = state.Initialise(null)!;
        Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}-[A-Z2-9]{5}$", code);
        Assert.Null(state.Initialise("OTHER"));   // set once
        var now = new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < ProviderSetupState.MaxFailures - 1; i++) Assert.False(state.Try("WRONG", now.AddSeconds(i)).Ok);
        var (ok, _, session) = state.Try(code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal), now.AddSeconds(10));
        Assert.True(ok);
        Assert.True(state.SessionValid(session, now.AddMinutes(19)));
        Assert.False(state.SessionValid(session, now.AddMinutes(21)));
        Assert.False(state.SessionValid("00", now));
        for (var i = 0; i < ProviderSetupState.MaxFailures; i++) state.Try("WRONG", now.AddMinutes(1));
        Assert.True(state.Try(code, now.AddMinutes(2)).Locked);          // even the right code waits out the lock…
        Assert.True(state.Try(code, now.AddMinutes(2), "10.0.0.9").Ok);   // …from that address only: nobody else is locked out
        Assert.True(state.Try(code, now.AddMinutes(17)).Ok);
        state.Consume();
        Assert.False(state.Try(code, now.AddMinutes(18)).Ok);

        // A configured code shorter than 16 characters is ignored (a code is generated); a strong one is used and not returned for the log.
        var weak = new ProviderSetupState();
        Assert.NotNull(weak.Initialise("1234"));
        Assert.True(weak.ConfiguredRefused);
        Assert.False(weak.Try("1234", now).Ok);
        var strong = new ProviderSetupState();
        Assert.Null(strong.Initialise("correct-horse-battery-staple"));
        Assert.True(strong.Try("CORRECT HORSE BATTERY STAPLE", now).Ok);

        // Many addresses together: above the global ceiling every attempt waits.
        var busy = new ProviderSetupState();
        var busyCode = busy.Initialise(null)!;
        for (var i = 0; i < ProviderSetupState.GlobalMaxFailures; i++) busy.Try("WRONG", now, "10.1." + i);
        Assert.True(busy.Try(busyCode, now.AddMinutes(1), "10.9.9.9").Locked);
    }

    [Fact]
    public async Task Staff_invite_a_colleague_who_sets_a_password_and_two_step_and_manage_the_list()
    {
        var staff = await PlatformTests.StaffAsync(f);
        var c = staff.Client;
        var email = $"colleague-{Guid.NewGuid():N}@platform.test";

        var invited = await ReadAsync<StaffResponse>(await c.PostAsJsonAsync("/api/v1/platform/staff/invitations", new StaffInviteRequest("Rana Haddad", email)));
        Assert.Equal(StaffStates.Invited, invited.State);
        Assert.InRange(invited.InvitationExpiresAt!.Value, DateTime.UtcNow.AddHours(47), DateTime.UtcNow.AddHours(49));
        Assert.Contains("user.emailInUse", await (await c.PostAsJsonAsync("/api/v1/platform/staff/invitations", new StaffInviteRequest("Again", email))).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var list = await ReadAsync<List<StaffResponse>>(await c.GetAsync("/api/v1/platform/staff"));
        Assert.Contains(list, s => s.Email == staff.Email && s.IsSelf && s.State == StaffStates.Active);
        Assert.DoesNotContain(list, s => s.Email == TenantLifecycleService.SystemEmail);

        // Resend: the first link stops working.
        var first = f.Emails.LinkParameters(email, "StaffInvitation");
        await (await c.PostAsJsonAsync($"/api/v1/platform/staff/{invited.Id}/resend", new { })).EnsureOkAsync();
        var second = f.Emails.LinkParameters(email, "StaffInvitation");
        Assert.NotEqual(first["t"], second["t"]);
        var guest = f.NewClient();
        Assert.Contains("ignore it", f.Emails.Last(email, "StaffInvitation").TextBody, StringComparison.Ordinal);
        var stale = await guest.GetStringAsync($"/accept-staff-invitation?u={Uri.EscapeDataString(first["u"])}&t={Uri.EscapeDataString(first["t"])}");
        Assert.Contains("invitation link is not valid", stale, StringComparison.Ordinal);

        // Accept: password, then the mandatory two-step set-up; the link works once.
        var url = $"/accept-staff-invitation?u={Uri.EscapeDataString(second["u"])}&t={Uri.EscapeDataString(second["t"])}";
        var page = await guest.GetStringAsync(url);
        Assert.Contains("Rana Haddad", WebUtility.HtmlDecode(page), StringComparison.Ordinal);
        var accepted = await PostFormAsync(guest, url, page, "accept-staff", ("Input.Password", ApiHelpers.Password), ("Input.ConfirmPassword", ApiHelpers.Password));
        Assert.Contains("/two-factor/setup", accepted.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        await guest.UseAntiforgeryAsync();
        Assert.True((await CompleteTwoFactorAsync(guest)).IsPlatformStaff);
        Assert.Contains("invitation link is not valid", await f.NewClient().GetStringAsync(url), StringComparison.Ordinal);
        var colleague = (await ReadAsync<List<StaffResponse>>(await c.GetAsync("/api/v1/platform/staff"))).Single(s => s.Email == email);
        Assert.Equal(StaffStates.Active, colleague.State);
        Assert.True(colleague.TwoFactorEnabled);

        // Never oneself; a colleague with a reason (sessions end at once), then back.
        var self = list.Single(s => s.IsSelf);
        Assert.Equal("staff.self", await (await c.PostAsJsonAsync($"/api/v1/platform/staff/{self.Id}/deactivate", new StaffStatusRequest("x"))).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/v1/platform/staff/{colleague.Id}/deactivate", new StaffStatusRequest(null))).StatusCode);
        Assert.Equal(StaffStates.Disabled, (await ReadAsync<StaffResponse>(await c.PostAsJsonAsync($"/api/v1/platform/staff/{colleague.Id}/deactivate", new StaffStatusRequest("Left the team")))).State);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/v1/platform/staff")).StatusCode);
        Assert.Equal(StaffStates.Active, (await ReadAsync<StaffResponse>(await c.PostAsJsonAsync($"/api/v1/platform/staff/{colleague.Id}/reactivate", new { }))).State);
        Assert.Equal("staff.notPending", await (await c.PostAsJsonAsync($"/api/v1/platform/staff/{colleague.Id}/cancel", new { })).ProblemCodeAsync());

        // Cancel a pending invitation: its link stops working and the address can be invited again.
        var other = $"other-{Guid.NewGuid():N}@platform.test";
        var pending = await ReadAsync<StaffResponse>(await c.PostAsJsonAsync("/api/v1/platform/staff/invitations", new StaffInviteRequest("Omar", other)));
        var link = f.Emails.LinkParameters(other, "StaffInvitation");
        (await c.PostAsJsonAsync($"/api/v1/platform/staff/{pending.Id}/cancel", new { })).EnsureSuccessStatusCode();
        Assert.Contains("invitation link is not valid", await f.NewClient().GetStringAsync($"/accept-staff-invitation?u={Uri.EscapeDataString(link["u"])}&t={Uri.EscapeDataString(link["t"])}"),
            StringComparison.Ordinal);
        await (await c.PostAsJsonAsync("/api/v1/platform/staff/invitations", new StaffInviteRequest("Omar", other))).EnsureOkAsync();

        // Audited; tenants' users cannot reach any of it.
        var audited = await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().Where(a => a.EntityType == "CORE.User").Select(a => a.Action).Distinct().ToListAsync());
        Assert.Contains("InviteStaff", audited);
        Assert.Contains("DeactivateStaff", audited);
        Assert.Contains("CancelStaffInvitation", audited);
        var owner = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync("/api/v1/platform/staff")).StatusCode);
    }

    /// <summary>Collects log lines.</summary>
    private sealed class ListLogger : ILogger
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
