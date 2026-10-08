using System.Security.Claims;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Domain.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Web.Realtime;

/// <summary>
/// Live updates (FR-185, RT-01/02, ADR-11): a connection joins the group of a company only after the same
/// check as the API (role in a company of the user's tenant). Messages go to that company's users only.
/// Hub invocations run in their own DI scope, so the caller is identified from the connection's
/// authenticated principal (validated against its session when the connection was opened).
/// </summary>
[Authorize]
public sealed class LiveHub(AccessService access, IUrlTokenService tokens, IAppDbContext db) : Hub
{
    public const string Path = "/hubs/live";

    public static string Group(long companyId) => "co:" + companyId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Joins the company's group; returns false when the caller has no role in that company.</summary>
    public async Task<bool> JoinCompany(string companyToken)
    {
        var companyId = await CompanyIdAsync(companyToken).ConfigureAwait(false);
        if (companyId is null) return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(companyId.Value), Context.ConnectionAborted).ConfigureAwait(false);
        return true;
    }

    public async Task LeaveCompany(string companyToken)
    {
        var companyId = await CompanyIdAsync(companyToken).ConfigureAwait(false);
        if (companyId is not null)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(companyId.Value), Context.ConnectionAborted).ConfigureAwait(false);
    }

    private async Task<long?> CompanyIdAsync(string companyToken)
    {
        if (!Guid.TryParseExact(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), "N", out var userPublicId)) return null;
        var companyPublicId = tokens.Decode<Company>(companyToken);
        if (companyPublicId is null) return null;
        var userId = await db.Set<User>().IgnoreQueryFilters(["Tenant"]).AsNoTracking()
            .Where(u => u.PublicId == userPublicId && u.Status == "Active").Select(u => (long?)u.Id)
            .SingleOrDefaultAsync(Context.ConnectionAborted).ConfigureAwait(false);
        if (userId is null) return null;
        var grant = (await access.GrantsAsync(userId.Value, Context.ConnectionAborted).ConfigureAwait(false))
            .FirstOrDefault(g => g.CompanyPublicId == companyPublicId && g.CompanyStatus == "Active");
        return grant?.CompanyId;
    }
}

/// <summary>Publishes record and lock changes to the company group (no internal ids in messages).</summary>
public sealed class SignalRNotifier(IHubContext<LiveHub> hub, IRequestContext ctx) : IRealtimeNotifier
{
    public Task RecordChangedAsync(long companyId, string entityType, string token, string action, CancellationToken ct) =>
        hub.Clients.Group(LiveHub.Group(companyId)).SendAsync("recordChanged", new { entityType, id = token, action, by = ctx.UserName }, ct);

    public Task LockChangedAsync(long companyId, string entityType, string token, string? holderName, CancellationToken ct) =>
        hub.Clients.Group(LiveHub.Group(companyId)).SendAsync("lockChanged", new { entityType, id = token, holder = holderName }, ct);
}
