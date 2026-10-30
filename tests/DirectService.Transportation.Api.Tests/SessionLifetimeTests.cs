using System.Net;
using System.Net.Http.Json;
using DirectService.Platform.Contracts.Identity;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DirectService.Platform.Application.Identity;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner testing 19 Oct 2026 (DT-211): Profile → Active sessions listed 15 "active" sessions back to 12 Oct. A session past its absolute
/// lifetime (12 h) or idle longer than the tenant's timeout is refused by the server, is not listed, and is ended by the clean-up job;
/// "Sign out of the other sessions" ends them at once.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SessionLifetimeTests(ApiFactory f)
{
    private static readonly string[] TenantFilter = ["Tenant"];

    /// <summary>The user's sessions, newest first (ids).</summary>
    private Task<List<long>> SessionIdsAsync(string email) => f.DbAsync(db => db.Set<UserSession>().IgnoreQueryFilters(TenantFilter)
        .Where(s => s.User.Email == email && s.RevokedAt == null).OrderByDescending(s => s.Id).Select(s => s.Id).ToListAsync());

    private async Task AgeAsync(long sessionId, TimeSpan? lastSeenAgo = null, bool expired = false)
    {
        if (lastSeenAgo is { } ago)
        {
            var lastSeen = DateTime.UtcNow - ago;
            await f.DbAsync(db => db.Set<UserSession>().IgnoreQueryFilters(TenantFilter).Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, lastSeen)));
        }
        if (expired)
        {
            var past = DateTime.UtcNow.AddMinutes(-1);
            await f.DbAsync(db => db.Set<UserSession>().IgnoreQueryFilters(TenantFilter).Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.ExpiresAt, past)));
        }
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SessionService>().ForgetAsync(sessionId, CancellationToken.None);   // drop the 30-s cache
    }

    [Fact]
    public async Task A_session_past_its_absolute_lifetime_is_refused_and_not_listed()
    {
        var o = await f.SignUpOwnerAsync();
        var second = await f.SignInAsync(o.Email, o.Password, o.AuthenticatorKey);
        var newest = (await SessionIdsAsync(o.Email))[0];
        Assert.Equal(2, (await o.Client.GetFromJsonAsync<List<SessionResponse>>("/api/v1/me/sessions"))!.Count);

        await AgeAsync(newest, expired: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/me/")).StatusCode);   // refused, not only hidden
        var list = (await o.Client.GetFromJsonAsync<List<SessionResponse>>("/api/v1/me/sessions"))!;
        Assert.True(Assert.Single(list).IsCurrent);
    }

    [Fact]
    public async Task A_session_idle_past_the_timeout_is_refused_and_not_listed()
    {
        var o = await f.SignUpOwnerAsync();
        var second = await f.SignInAsync(o.Email, o.Password, o.AuthenticatorKey);
        var newest = (await SessionIdsAsync(o.Email))[0];
        await AgeAsync(newest, lastSeenAgo: TimeSpan.FromMinutes(29));   // the tenant default is 30 minutes: still valid
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/v1/me/")).StatusCode);
        await AgeAsync(newest, lastSeenAgo: TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/me/")).StatusCode);
        Assert.Single((await o.Client.GetFromJsonAsync<List<SessionResponse>>("/api/v1/me/sessions"))!);
    }

    [Fact]
    public async Task The_clean_up_job_ends_expired_and_idle_sessions_and_keeps_valid_ones()
    {
        var o = await f.SignUpOwnerAsync();
        await f.SignInAsync(o.Email, o.Password, o.AuthenticatorKey);
        await f.SignInAsync(o.Email, o.Password, o.AuthenticatorKey);
        var ids = await SessionIdsAsync(o.Email);   // 3: two extra, then the sign-up one (current)
        await AgeAsync(ids[0], expired: true);
        await AgeAsync(ids[1], lastSeenAgo: TimeSpan.FromHours(3));

        using (var scope = f.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<SessionService>().EndExpiredAsync(1000, CancellationToken.None) >= 2);

        var rows = await f.DbAsync(db => db.Set<UserSession>().IgnoreQueryFilters(["Tenant", "SoftDelete"]).Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.RevokedAt, s.IsDeleted, s.DeleteReason }).ToListAsync());
        var expired = rows.Single(r => r.Id == ids[0]);
        var idle = rows.Single(r => r.Id == ids[1]);
        Assert.Equal((true, "Expired: absolute session lifetime"), (expired.RevokedAt is not null && expired.IsDeleted, expired.DeleteReason));
        Assert.Equal((true, "Expired: idle timeout"), (idle.RevokedAt is not null && idle.IsDeleted, idle.DeleteReason));
        Assert.Null(rows.Single(r => r.Id == ids[2]).RevokedAt);   // the valid one stays
        Assert.Equal(HttpStatusCode.OK, (await o.Client.GetAsync("/api/v1/me/")).StatusCode);
    }

    [Fact]
    public async Task Sign_out_of_other_sessions_ends_them_at_once()
    {
        var o = await f.SignUpOwnerAsync();
        var a = await f.SignInAsync(o.Email, o.Password, o.AuthenticatorKey);
        var b = await f.SignInAsync(o.Email, o.Password, o.AuthenticatorKey);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/v1/me/")).StatusCode);   // cached as valid
        var r = await o.Client.PostAsync("/api/v1/me/sessions/sign-out-others", null);
        await r.EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/v1/me/")).StatusCode);   // no 30-s grace from the cache
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/api/v1/me/")).StatusCode);
        var list = (await o.Client.GetFromJsonAsync<List<SessionResponse>>("/api/v1/me/sessions"))!;
        Assert.True(Assert.Single(list).IsCurrent);
    }
}
