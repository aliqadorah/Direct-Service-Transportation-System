using DirectService.Transportation.Application.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Platform.Domain;
using DirectService.Platform.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DirectService.Platform.Application.Notifications;
using DirectService.Platform.Application.Email;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// System email via Microsoft 365 (owner decision 22 Oct 2026, option A; DT-250…DT-253) against the fake Microsoft sign-in and Graph: the
/// console is staff-only, saving needs a fresh two-step code, the secret is never returned nor written to the audit log, new settings work
/// only after a successful test email, the outbox retries with back-off and Retry-After and never sends an email twice, fails after the
/// last attempt (wiping single-use links) and tells staff, warns 30 days before the secret expires, limits floods to one address, and
/// resends a failed email (the same message, or a new link).
/// DT-261 (GitHub CI 25 Oct 2026): every test starts from nothing kept in memory either — the Graph transport (a singleton) kept the access
/// token of the fake app, so a test that ran after an activation never met the refused sign-in; test emails now always ask for a new token.
/// </summary>
[Collection(SystemEmailCollection.Name)]
public sealed class SystemEmailTests(SystemEmailFactory f)
{
    private const string Secret = "fake-secret-0123456789~abcdef";
    private const string Path = "/api/v1/platform/system-email";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SaveSystemEmailRequest M365(string code, string? secret = Secret, string mailbox = "no-reply@directservicetech.com", string? rowVersion = null) =>
        new("Microsoft365", "11111111-2222-3333-4444-555555555555", "66666666-7777-8888-9999-000000000000", secret, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300),
            mailbox, "Direct Service", "support@directservicetech.com", code, rowVersion);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    /// <summary>
    /// A clean start: no settings (development folder), an empty outbox, no token kept by the Graph transport, the fake Graph answering 202
    /// and no queue pause — whatever test ran before (the order differs between machines: xUnit orders by IDs that include the assembly path).
    /// </summary>
    private async Task ResetAsync()
    {
        f.Graph.Reset();
        f.Services.GetServices<IEmailTransport>().OfType<GraphEmailTransport>().Single().ClearTokens();
        await f.DbAsync(db => db.Database.ExecuteSqlRawAsync("DELETE core.EmailOutbox; DELETE core.SystemEmailSettings;", Ct));
        using var scope = f.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<EmailDispatchState>().PausedUntil = DateTime.MinValue;
    }

    private async Task<int> DispatchAsync()
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().RunOnceAsync(Ct);
    }

    private async Task QueueAsync(EmailMessage message)
    {
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxEmailSender>().SendAsync(message, Ct);
    }

    /// <summary>Microsoft 365 made active: saved with a fresh code, then a successful test email.</summary>
    private async Task<SignedInUser> ActiveM365Async()
    {
        var staff = await PlatformTests.StaffAsync(f);
        await ReadAsync<SystemEmailResponse>(await staff.Client.PutAsJsonAsync(Path, M365(Totp.Code(staff.AuthenticatorKey)), Ct));
        var test = await ReadAsync<SystemEmailTestResponse>(await staff.Client.PostAsJsonAsync(Path + "/test", new SystemEmailTestRequest("staff-check@example.test"), Ct));
        Assert.True(test.Activated);
        f.Graph.Reset();
        return staff;
    }

    /// <summary>The text parts of a MIME message as Graph received it (base64), decoded.</summary>
    private static string Decoded(string mime) => string.Join("\n", mime.Split("Content-Transfer-Encoding: base64\r\n\r\n").Skip(1)
        .Select(p => p[..p.IndexOf("\r\n--", StringComparison.Ordinal)]).Select(b => Encoding.UTF8.GetString(Convert.FromBase64String(b.Replace("\r\n", "", StringComparison.Ordinal)))));

    private Task<int> MakeDueAsync() => f.DbAsync(db => db.Set<EmailOutbox>().Where(e => e.Status == EmailStatus.Pending)
        .ExecuteUpdateAsync(x => x.SetProperty(e => e.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1)), Ct));

    private Task<EmailOutbox> RowAsync(string to, string template) =>
        f.DbAsync(db => db.Set<EmailOutbox>().AsNoTracking().Where(e => e.Recipient == to && e.Template == template).OrderByDescending(e => e.Id).FirstAsync(Ct));

    [Fact]
    public async Task Staff_only_fresh_code_on_save_and_the_secret_is_never_returned_nor_audited()
    {
        await ResetAsync();
        var owner = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync(Path, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PutAsJsonAsync(Path, M365("123456"), Ct)).StatusCode);

        var staff = await PlatformTests.StaffAsync(f);
        var start = await ReadAsync<SystemEmailResponse>(await staff.Client.GetAsync(Path, Ct));
        Assert.Equal("Development", start.Active.Mode);
        Assert.Null(start.Pending);

        // No code, a wrong code: refused on the code field, nothing saved.
        var noCode = await staff.Client.PutAsJsonAsync(Path, M365(""), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noCode.StatusCode);
        Assert.Contains("\"field\":\"code\"", await noCode.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var wrong = await staff.Client.PutAsJsonAsync(Path, M365("000000"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Null((await ReadAsync<SystemEmailResponse>(await staff.Client.GetAsync(Path, Ct))).Pending);

        // A header injection attempt in the sender name is refused on its field.
        var injected = await staff.Client.PutAsJsonAsync(Path, M365(Totp.Code(staff.AuthenticatorKey)) with { DisplayName = "DS\r\nBcc: x@example.test" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, injected.StatusCode);
        Assert.Contains("\"field\":\"displayName\"", await injected.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        var saved = await staff.Client.PutAsJsonAsync(Path, M365(Totp.Code(staff.AuthenticatorKey)), Ct);
        var body = await saved.Content.ReadAsStringAsync(Ct);
        await saved.EnsureOkAsync();
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
        var response = (await saved.Content.ReadFromJsonAsync<SystemEmailResponse>(Ct))!;
        Assert.Equal("Development", response.Active.Mode);                       // still the active one
        Assert.Equal("Microsoft365", response.Pending!.Mode);
        Assert.True(response.Pending.HasSecret);
        Assert.NotNull(response.Pending.SecretSavedAt);
        Assert.DoesNotContain(Secret, await (await staff.Client.GetAsync(Path, Ct)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Stored encrypted; the audit log names the change but never the value.
        var row = await f.DbAsync(db => db.Set<SystemEmailSetting>().AsNoTracking().SingleAsync(s => s.State == "Pending", Ct));
        Assert.DoesNotContain(Secret, Encoding.UTF8.GetString(row.ClientSecretEncrypted!), StringComparison.Ordinal);
        var audit = await f.DbAsync(db => db.Set<AuditLog>().AsNoTracking().Where(a => a.EntityType == "CORE.SystemEmailSetting" && a.EntityId == row.Id).ToListAsync(Ct));
        var create = Assert.Single(audit, a => a.Action == "Create");
        Assert.Contains("\"ClientSecretEncrypted\":\"***\"", create.NewValues, StringComparison.Ordinal);
        Assert.All(audit, a => Assert.DoesNotContain(Secret, (a.NewValues ?? "") + a.OldValues, StringComparison.Ordinal));
        Assert.NotNull(create.UserId);                                            // the staff member who saved it

        // Without a new secret the saved one is kept for the same app; another app needs its own.
        var other = await staff.Client.PutAsJsonAsync(Path, M365(Totp.Code(staff.AuthenticatorKey), secret: null) with { EntraClientId = "99999999-7777-8888-9999-000000000000" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        Assert.Contains("\"field\":\"clientSecret\"", await other.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_settings_become_active_only_after_a_successful_test_email()
    {
        await ResetAsync();
        var staff = await PlatformTests.StaffAsync(f);
        await ReadAsync<SystemEmailResponse>(await staff.Client.PutAsJsonAsync(Path, M365(Totp.Code(staff.AuthenticatorKey)), Ct));

        // A wrong secret: the test says so in plain words; the pending settings stay pending, the development folder keeps sending.
        f.Graph.TokenRefusal = "7000215";
        var failed = await ReadAsync<SystemEmailTestResponse>(await staff.Client.PostAsJsonAsync(Path + "/test", new SystemEmailTestRequest("ops@example.test"), Ct));
        Assert.False(failed.Sent);
        Assert.Equal(EmailErrorKinds.WrongSecret, failed.ErrorKind);
        Assert.False(failed.Activated);
        Assert.Equal("Development", failed.Settings.Active.Mode);
        Assert.Equal(EmailErrorKinds.WrongSecret, failed.Settings.Pending!.LastTestErrorKind);
        Assert.DoesNotContain(Secret, failed.Detail ?? "", StringComparison.Ordinal);

        // A mailbox the app may not use: missing permission / outside its scope.
        f.Graph.TokenRefusal = null;
        f.Graph.Answer(HttpStatusCode.Forbidden, "ErrorAccessDenied");
        Assert.Equal(EmailErrorKinds.MissingPermission,
            (await ReadAsync<SystemEmailTestResponse>(await staff.Client.PostAsJsonAsync(Path + "/test", new SystemEmailTestRequest("ops@example.test"), Ct))).ErrorKind);

        // Fixed: the test is sent through Graph as the mailbox, and the new settings become the active ones.
        var ok = await ReadAsync<SystemEmailTestResponse>(await staff.Client.PostAsJsonAsync(Path + "/test", new SystemEmailTestRequest("ops@example.test"), Ct));
        Assert.True(ok.Sent);
        Assert.True(ok.Activated);
        Assert.Equal("Microsoft365", ok.Settings.Active.Mode);
        Assert.Null(ok.Settings.Pending);
        var (mailbox, mime) = Assert.Single(f.Graph.Sent);
        Assert.Equal("no-reply@directservicetech.com", mailbox);
        Assert.Contains("To: ops@example.test\r\n", mime, StringComparison.Ordinal);
        Assert.Contains("Reply-To: support@directservicetech.com\r\n", mime, StringComparison.Ordinal);

        // The tests are in the delivery log and the audit log (activation too).
        var log = await ReadAsync<PagedResult<EmailLogItem>>(await staff.Client.GetAsync(Path + "/log?template=TestEmail", Ct));
        Assert.Equal(3, log.TotalCount);
        var actions = await f.DbAsync(db => db.Set<AuditLog>().AsNoTracking().Where(a => a.EntityType == "CORE.SystemEmailSetting").Select(a => a.Action).ToListAsync(Ct));
        Assert.Contains("EmailTest", actions);
        Assert.Contains("Activate", actions);

        // Only the sender name: applied at once, nothing pending.
        var renamed = await ReadAsync<SystemEmailResponse>(await staff.Client.PutAsJsonAsync(Path,
            M365(Totp.Code(staff.AuthenticatorKey), secret: null, rowVersion: ok.Settings.Active.RowVersion) with { DisplayName = "Direct Service Support" }, Ct));
        Assert.Null(renamed.Pending);
        Assert.Equal("Direct Service Support", renamed.Active.DisplayName);
        Assert.Equal("Microsoft365", renamed.Active.Mode);
    }

    /// <summary>DT-261: a test email asks Microsoft sign-in for a new token, so a secret refused now is found even with a token kept from before.</summary>
    [Fact]
    public async Task A_test_email_asks_for_a_new_token_so_a_refused_secret_is_found()
    {
        await ResetAsync();
        var staff = await ActiveM365Async();   // a token is now kept for this app and secret
        await ReadAsync<SystemEmailResponse>(await staff.Client.PutAsJsonAsync(Path, M365(Totp.Code(staff.AuthenticatorKey)) with { SenderMailbox = "ops@directservicetech.com" }, Ct));
        f.Graph.TokenRefusal = "7000215";
        var before = f.Graph.TokenRequests;
        var failed = await ReadAsync<SystemEmailTestResponse>(await staff.Client.PostAsJsonAsync(Path + "/test", new SystemEmailTestRequest("ops@example.test"), Ct));
        Assert.Equal((false, EmailErrorKinds.WrongSecret, false), (failed.Sent, failed.ErrorKind, failed.Activated));
        Assert.Equal(before + 1, f.Graph.TokenRequests);
        Assert.Equal("no-reply@directservicetech.com", failed.Settings.Active.SenderMailbox);   // the active settings stay
        Assert.Equal("ops@directservicetech.com", failed.Settings.Pending!.SenderMailbox);
    }

    /// <summary>DT-261: settings changed while a test email is on its way are not made active by it (row version: 409, nothing activated).</summary>
    [Fact]
    public async Task A_test_email_never_activates_settings_changed_while_it_was_on_its_way()
    {
        await ResetAsync();
        var staff = await PlatformTests.StaffAsync(f);
        var first = await ReadAsync<SystemEmailResponse>(await staff.Client.PutAsJsonAsync(Path, M365(Totp.Code(staff.AuthenticatorKey)), Ct));
        f.Graph.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var test = staff.Client.PostAsJsonAsync(Path + "/test", new SystemEmailTestRequest("ops@example.test"), Ct);
        await WaitUntilAsync(() => f.Graph.Held == 1);
        // Meanwhile another staff member (a fresh code) saves other pending settings.
        var other = await PlatformTests.StaffAsync(f);
        var changed = await ReadAsync<SystemEmailResponse>(await other.Client.PutAsJsonAsync(Path,
            M365(Totp.Code(other.AuthenticatorKey), rowVersion: first.Pending!.RowVersion) with { SenderMailbox = "billing@directservicetech.com" }, Ct));
        Assert.Equal("billing@directservicetech.com", changed.Pending!.SenderMailbox);
        f.Graph.Hold.SetResult();

        var answer = await test;
        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
        var now = await ReadAsync<SystemEmailResponse>(await staff.Client.GetAsync(Path, Ct));
        Assert.Equal("Development", now.Active.Mode);                                    // nothing activated
        Assert.Equal("billing@directservicetech.com", now.Pending!.SenderMailbox);         // the changed settings still wait for their own test
        Assert.Null(now.Pending.LastTestOk);
    }

    /// <summary>DT-261: two test emails at the same moment make the pending settings active once; the other answer is a conflict or "not activated".</summary>
    [Fact]
    public async Task Two_test_emails_at_the_same_time_activate_the_settings_once()
    {
        await ResetAsync();
        var staff = await PlatformTests.StaffAsync(f);
        await ReadAsync<SystemEmailResponse>(await staff.Client.PutAsJsonAsync(Path, M365(Totp.Code(staff.AuthenticatorKey)), Ct));
        f.Graph.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var one = staff.Client.PostAsJsonAsync(Path + "/test", new SystemEmailTestRequest("one@example.test"), Ct);
        var two = staff.Client.PostAsJsonAsync(Path + "/test", new SystemEmailTestRequest("two@example.test"), Ct);
        await WaitUntilAsync(() => f.Graph.Held == 2);
        f.Graph.Hold.SetResult();
        var answers = await Task.WhenAll(one, two);

        var activated = 0;
        foreach (var a in answers)
        {
            if (a.StatusCode == HttpStatusCode.Conflict) continue;
            if ((await ReadAsync<SystemEmailTestResponse>(a)).Activated) activated++;
        }
        Assert.Equal(1, activated);
        var rows = await f.DbAsync(db => db.Set<SystemEmailSetting>().AsNoTracking().Where(s => s.State != "Retired").Select(s => new { s.State, s.Mode }).ToListAsync(Ct));
        var row = Assert.Single(rows);
        Assert.Equal(("Active", "Microsoft365"), (row.State, row.Mode));
    }

    private static async Task WaitUntilAsync(Func<bool> done)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!done()) await Task.Delay(20, limit.Token);
    }

    [Fact]
    public async Task The_outbox_retries_honours_retry_after_and_never_sends_an_email_twice()
    {
        await ResetAsync();
        await ActiveM365Async();
        var to = $"r-{Guid.NewGuid():N}@example.test";
        await QueueAsync(EmailTemplates.PasswordReset(to, "Rana", "https://app.test/reset-password?u=U1&t=TOKEN-RETRY", "en"));
        var row = await RowAsync(to, "PasswordReset");
        Assert.Equal(EmailStatus.Pending, row.Status);
        Assert.Equal("en", row.LanguageCode);
        Assert.True(row.HasSecretLink);
        // The link token is only in the encrypted message, never in a column of the log.
        Assert.DoesNotContain("TOKEN-RETRY", Encoding.UTF8.GetString(row.PayloadEncrypted!), StringComparison.Ordinal);

        // 429 with Retry-After: tried again only after the wait, and the whole queue waits.
        f.Graph.Answer((HttpStatusCode)429, "ApplicationThrottled", TimeSpan.FromSeconds(120));
        Assert.Equal(0, await DispatchAsync());
        row = await RowAsync(to, "PasswordReset");
        Assert.Equal((EmailStatus.Pending, 1, EmailErrorKinds.Throttled), (row.Status, row.Attempts, row.ErrorKind));
        Assert.True(row.NextAttemptAt >= DateTime.UtcNow.AddSeconds(100));
        using (var scope = f.Services.CreateScope())
        {
            var state = scope.ServiceProvider.GetRequiredService<EmailDispatchState>();
            Assert.True(state.PausedUntil > DateTime.UtcNow.AddSeconds(100));
            state.PausedUntil = DateTime.MinValue;
        }
        await MakeDueAsync();
        Assert.Equal(1, await DispatchAsync());
        row = await RowAsync(to, "PasswordReset");
        Assert.Equal((EmailStatus.Sent, 2), (row.Status, row.Attempts));
        Assert.Null(row.PayloadEncrypted);                                       // wiped once sent
        Assert.Contains("TOKEN-RETRY", Decoded(Assert.Single(f.Graph.Sent).Mime), StringComparison.Ordinal);

        // Further runs never send it again.
        await MakeDueAsync();
        Assert.Equal(0, await DispatchAsync());
        Assert.Single(f.Graph.Sent);

        // A send interrupted half-way (the host stopped): Failed as interrupted, not sent again on its own.
        await QueueAsync(EmailTemplates.TwoFactorChanged(to, "Rana", "تفعيل", "Turned on", DateTime.UtcNow, null, "https://app.test/sign-in", "ar"));
        await f.DbAsync(db => db.Set<EmailOutbox>().Where(e => e.Recipient == to && e.Template == "TwoFactorChanged")
            .ExecuteUpdateAsync(x => x.SetProperty(e => e.Status, EmailStatus.Sending).SetProperty(e => e.ClaimedUntil, DateTime.UtcNow.AddMinutes(-1)).SetProperty(e => e.Attempts, 1), Ct));
        await DispatchAsync();
        var interrupted = await RowAsync(to, "TwoFactorChanged");
        Assert.Equal((EmailStatus.Failed, EmailErrorKinds.Interrupted), (interrupted.Status, interrupted.ErrorKind));
        Assert.Single(f.Graph.Sent);
    }

    [Fact]
    public async Task After_the_last_attempt_it_fails_wipes_single_use_links_tells_staff_and_can_be_resent()
    {
        await ResetAsync();
        var staff = await ActiveM365Async();
        var owner = await f.SignUpOwnerAsync();
        await QueueAsync(EmailTemplates.PasswordReset(owner.Email, "Owner", "https://app.test/reset-password?u=U1&t=TOKEN-FAIL", "ar"));
        await QueueAsync(EmailTemplates.TwoFactorChanged(owner.Email, "Owner", "تفعيل", "Turned on", DateTime.UtcNow, null, "https://app.test/sign-in", "ar"));
        for (var i = 0; i < 12; i++) f.Graph.Answer(HttpStatusCode.Forbidden, "ErrorAccessDenied");
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            await MakeDueAsync();
            await DispatchAsync();
        }
        var reset = await RowAsync(owner.Email, "PasswordReset");
        Assert.Equal((EmailStatus.Failed, 6, EmailErrorKinds.MissingPermission), (reset.Status, reset.Attempts, reset.ErrorKind));
        Assert.Null(reset.PayloadEncrypted);                                     // the single-use link is not kept
        var notice = await RowAsync(owner.Email, "TwoFactorChanged");
        Assert.Equal(EmailStatus.Failed, notice.Status);
        Assert.NotNull(notice.PayloadEncrypted);                                 // a notice without a secret can be sent again as it was
        Assert.Empty(f.Graph.Sent);

        // Platform staff are told (once).
        Assert.Equal(1, f.Emails.CountOf(staff.Email, nameof(EmailTemplates.SystemEmailFailures)));
        var status = await ReadAsync<SystemEmailResponse>(await staff.Client.GetAsync(Path, Ct));
        Assert.False(status.Status.Working);
        Assert.Equal(2, status.Status.Failed);
        Assert.True((await ReadAsync<SystemEmailAlertResponse>(await staff.Client.GetAsync(Path + "/alert", Ct))).FailedLast24Hours >= 2);

        // The log shows how each can be resent: a new link, or the same message.
        var log = await ReadAsync<PagedResult<EmailLogItem>>(await staff.Client.GetAsync(Path + "/log?status=Failed&q=" + Uri.EscapeDataString(owner.Email), Ct));
        var resetItem = Assert.Single(log.Items, e => e.Template == "PasswordReset");
        var noticeItem = Assert.Single(log.Items, e => e.Template == "TwoFactorChanged");
        Assert.Equal(("newLink", "same"), (resetItem.Resend, noticeItem.Resend));
        Assert.DoesNotContain("TOKEN-FAIL", await (await staff.Client.GetAsync(Path + "/log", Ct)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        var before = f.Emails.CountOf(owner.Email, "PasswordReset");
        Assert.Equal("newLink", (await ReadAsync<EmailResendResponse>(await staff.Client.PostAsJsonAsync($"{Path}/log/{resetItem.Id}/resend", new { }, Ct))).How);
        Assert.Equal(before + 1, f.Emails.CountOf(owner.Email, "PasswordReset"));   // a new reset link from the flow itself
        Assert.Equal("same", (await ReadAsync<EmailResendResponse>(await staff.Client.PostAsJsonAsync($"{Path}/log/{noticeItem.Id}/resend", new { }, Ct))).How);
        await MakeDueAsync();
        Assert.Equal(1, await DispatchAsync());
        Assert.Contains("To: " + owner.Email, Assert.Single(f.Graph.Sent).Mime, StringComparison.Ordinal);
        Assert.NotNull((await RowAsync(owner.Email, "TwoFactorChanged")).ResentFromId);
        // Its message now belongs to the new row: the failed one cannot be resent a second time.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await staff.Client.PostAsJsonAsync($"{Path}/log/{noticeItem.Id}/resend", new { }, Ct)).StatusCode);
        var audited = await f.DbAsync(db => db.Set<AuditLog>().AsNoTracking().CountAsync(a => a.EntityType == "CORE.EmailOutbox" && a.Action == "Resend", Ct));
        Assert.Equal(2, audited);
    }

    [Fact]
    public async Task Staff_are_warned_30_days_before_the_secret_expires()
    {
        await ResetAsync();
        var staff = await ActiveM365Async();
        var expires = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20);
        await f.DbAsync(db => db.Set<SystemEmailSetting>().Where(s => s.State == "Active").ExecuteUpdateAsync(x => x.SetProperty(s => s.SecretExpiresOn, expires), Ct));
        await DispatchAsync();
        Assert.Equal(1, f.Emails.CountOf(staff.Email, nameof(EmailTemplates.SecretExpiring)));
        await DispatchAsync();
        Assert.Equal(1, f.Emails.CountOf(staff.Email, nameof(EmailTemplates.SecretExpiring)));       // once per step (30, then 7 days)
        Assert.Equal(30, await f.DbAsync(db => db.Set<SystemEmailSetting>().Where(s => s.State == "Active").Select(s => s.ExpiryWarnedDays).SingleAsync(Ct)));
        var alert = await ReadAsync<SystemEmailAlertResponse>(await staff.Client.GetAsync(Path + "/alert", Ct));
        Assert.Equal(20, alert.SecretDaysLeft);
        Assert.Equal(20, (await ReadAsync<SystemEmailResponse>(await staff.Client.GetAsync(Path, Ct))).Status.SecretDaysLeft);
    }

    [Fact]
    public async Task A_flood_to_one_address_is_suppressed_and_development_mode_writes_the_file()
    {
        await ResetAsync();
        var to = $"flood-{Guid.NewGuid():N}@example.test";
        for (var i = 0; i < 7; i++) await QueueAsync(EmailTemplates.PasswordReset(to, "Rana", "https://app.test/reset-password?u=U1&t=T" + i, "ar"));
        var rows = await f.DbAsync(db => db.Set<EmailOutbox>().AsNoTracking().Where(e => e.Recipient == to).ToListAsync(Ct));
        Assert.Equal(5, rows.Count(e => e.Status == EmailStatus.Pending));
        Assert.Equal(2, rows.Count(e => e.Status == EmailStatus.Suppressed && e.PayloadEncrypted == null));

        // Development (no settings): the files go to the drop folder, readable, with the To line the E2E helper reads.
        Assert.Equal(5, await DispatchAsync());
        var files = Directory.GetFiles(f.DropFolder, "*-PasswordReset-*.eml");
        Assert.Equal(5, files.Count(file => File.ReadAllText(file).Contains("To: " + to + "\r\n", StringComparison.Ordinal)));
        Assert.Contains(files, file => File.ReadAllText(file).Contains("href=\"https://app.test/reset-password?u=U1&amp;t=T0\"", StringComparison.Ordinal));
        Assert.Empty(f.Graph.Sent);
    }
}
