using System.Text.Json;
using Microsoft.AspNetCore.Components;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Platform.Contracts.Collaboration;
using Microsoft.AspNetCore.SignalR.Client;

namespace DirectService.Transportation.Web.Client.Services;

/// <summary>A record changed in the company (from the server's live hub): entity type ("TRN.Trip"), its token (null for bulk changes), action and who.</summary>
public sealed record LiveChange(string EntityType, string? Id, string Action, string? By);

/// <summary>A record lock taken or released (BR-039): holder null = free.</summary>
public sealed record LockChange(string EntityType, string Id, string? Holder);

/// <summary>
/// Live updates (FR-185, NFR-16, RT-01/02): one SignalR connection per browser tab to <c>/hubs/live</c> (same-origin cookie), joined to the
/// group of the company on screen. Screens subscribe to record changes (lists and record views reload, grid rows highlight a few seconds),
/// lock changes (a read-only form becomes editable when the holder leaves) and personal notifications. The connection reconnects by
/// itself; after a reconnect the company group is joined again and screens reload once, so nothing missed while offline stays stale (RT-02).
/// Not started in component tests (no hub): the events then simply never fire.
/// </summary>
public sealed class LiveUpdates(NavigationManager nav) : IAsyncDisposable
{
    private static readonly TimeSpan Highlight = TimeSpan.FromSeconds(4);
    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.Ordinal);
    private HubConnection? _hub;
    private string? _company;

    public event Action<LiveChange>? RecordChanged;
    public event Action<LockChange>? LockChanged;
    public event Action<JsonElement>? Notification;

    /// <summary>The staff member's support window ended (DT-220): reason, when, who, and the tenant's company tokens.</summary>
    public event Action<JsonElement>? SupportEnded;

    /// <summary>Raised after the connection came back: screens reload what they show (RT-02).</summary>
    public event Action? Reconnected;

    public bool Connected => _hub?.State == HubConnectionState.Connected;

    /// <summary>Connects (once) and joins the company's group, leaving the previous one.</summary>
    public async Task EnterCompanyAsync(string company)
    {
        // One switch at a time (DT-187): a quick A → B → C must not leave the tab in two companies' groups.
        await _switch.WaitAsync().ConfigureAwait(false);
        try
        {
            await JoinAsync(company).ConfigureAwait(false);
        }
        finally
        {
            _switch.Release();
        }
    }

    private readonly SemaphoreSlim _switch = new(1, 1);

    private async Task JoinAsync(string company)
    {
        try
        {
            if (_hub is null)
            {
                _hub = new HubConnectionBuilder().WithUrl(nav.ToAbsoluteUri(LiveHubNames.Path)).WithAutomaticReconnect().Build();
                _hub.On<JsonElement>(LiveHubNames.RecordChanged, OnRecord);
                _hub.On<JsonElement>(LiveHubNames.LockChanged, OnLock);
                _hub.On<JsonElement>(LiveHubNames.Notification, n => Notification?.Invoke(n));
                _hub.On<JsonElement>(LiveHubNames.SupportEnded, e => SupportEnded?.Invoke(e));
                _hub.Reconnected += async _ =>
                {
                    try { if (_company is not null) await _hub.InvokeAsync<bool>(LiveHubNames.JoinCompany, _company).ConfigureAwait(false); }
                    catch (Exception e) when (e is HttpRequestException or InvalidOperationException or TaskCanceledException or Microsoft.AspNetCore.SignalR.HubException) { }
                    Reconnected?.Invoke();   // screens reload either way
                };
            }
            // First start, or a start that failed / a connection that gave up reconnecting: start again (automatic reconnect covers
            // only connections that were up). The group is joined afresh.
            if (_hub.State == HubConnectionState.Disconnected)
            {
                _company = null;
                await _hub.StartAsync().ConfigureAwait(false);
            }
            if (_company == company) return;
            if (_company is not null) await _hub.InvokeAsync(LiveHubNames.LeaveCompany, _company).ConfigureAwait(false);
            _company = company;
            await _hub.InvokeAsync<bool>(LiveHubNames.JoinCompany, company).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or TaskCanceledException or Microsoft.AspNetCore.SignalR.HubException)
        {
            // Live updates are a convenience: the screens still work (and reload on navigation) without them.
        }
    }

    /// <summary>Whether a record changed in the last few seconds (grid row highlight, UX-20).</summary>
    public bool IsRecent(object? key)
    {
        if (key is not string id || !_recent.TryGetValue(id, out var at)) return false;
        if (DateTime.UtcNow - at < Highlight) return true;
        _recent.Remove(id);
        return false;
    }

    /// <summary>For tests and for screens that announce their own change: as if the hub had sent it.</summary>
    public void Raise(LiveChange change)
    {
        if (change.Id is not null)
        {
            if (_recent.Count > 500)
            {
                var now = DateTime.UtcNow;
                foreach (var old in _recent.Where(r => now - r.Value >= Highlight).Select(r => r.Key).ToList()) _recent.Remove(old);
            }
            _recent[change.Id] = DateTime.UtcNow;
        }
        RecordChanged?.Invoke(change);
    }

    public void RaiseLock(LockChange change) => LockChanged?.Invoke(change);

    /// <summary>For tests: as if the hub had sent "supportEnded".</summary>
    public void RaiseSupportEnded(JsonElement payload) => SupportEnded?.Invoke(payload);

    private void OnRecord(JsonElement e) =>
        Raise(new LiveChange(Text(e, "entityType") ?? "", Text(e, "id"), Text(e, "action") ?? "", Text(e, "by")));

    private void OnLock(JsonElement e) => RaiseLock(new LockChange(Text(e, "entityType") ?? "", Text(e, "id") ?? "", Text(e, "holder")));

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync().ConfigureAwait(false);
    }
}
