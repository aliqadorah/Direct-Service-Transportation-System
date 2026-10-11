using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Transportation.Web.Client.Localization;
using Microsoft.AspNetCore.Components;

namespace DirectService.Transportation.Web.Client.Services;

/// <summary>
/// The signed-in user's session in the browser: loads the profile once (<c>GET /me</c>), keeps the shell state for the company of the URL
/// (setup progress, unread notifications), and carries the user-menu actions (language, sign-out) and the command-bar search.
/// </summary>
public sealed class AppSession(Api api, ShellState shell, Loc loc, NavigationManager nav)
{
    private Task? _loading;
    private string? _companyLoaded;

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    /// <summary>Loads the profile again (a company created, archived or restored), so the switcher and the menu follow.</summary>
    public Task ReloadAsync()
    {
        _loading = null;
        return EnsureLoadedAsync();
    }

    private async Task LoadAsync()
    {
        var me = await api.GetAsync<MeResponse>("api/v1/me").ConfigureAwait(false);
        loc.SetLanguage(me.LanguageCode);
        shell.SetMe(me);
    }

    /// <summary>Selects the company of the URL and loads its shell figures; false when the user has no access (shown as not found).</summary>
    public async Task<bool> EnterCompanyAsync(string company)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        // Staff: the window list is read again on every company change, so an ended window shows "not found" (review L3).
        var staff = shell.Me?.IsPlatformStaff == true;
        if (staff && company != _companyLoaded && !await LoadSupportCompaniesAsync().ConfigureAwait(false)) return false;
        if (!shell.SelectCompany(company)) return false;
        if (_companyLoaded == company) return true;
        _companyLoaded = company;
        try
        {
            var setup = await api.GetAsync<SetupChecklistResponse>(api.C("setup-checklist")).ConfigureAwait(false);
            var notes = await api.GetAsync<NotificationPage>(api.C("notifications?unread=true")).ConfigureAwait(false);
            shell.SetSetup(setup.Done < setup.Total, shell.PlanText);
            shell.SetNotifications(notes.Unread);
        }
        catch (ApiException)
        {
            // The shell still works without the figures (e.g. a read-only or suspended company).
        }
        return true;
    }

    private Task<IReadOnlyList<AnnouncementResponse>>? _announcements;
    private DateTime _announcementsAt;

    /// <summary>Announcements the user hid in this browser session.</summary>
    public HashSet<string> HiddenAnnouncements { get; } = new(StringComparer.Ordinal);

    /// <summary>Current service announcements (P-14), read again after five minutes so new ones appear without a reload; none when unreadable.</summary>
    public Task<IReadOnlyList<AnnouncementResponse>> AnnouncementsAsync()
    {
        if (_announcements is null || DateTime.UtcNow - _announcementsAt > TimeSpan.FromMinutes(5))
        {
            _announcementsAt = DateTime.UtcNow;
            _announcements = LoadAnnouncementsAsync();
        }
        return _announcements;
    }

    private async Task<IReadOnlyList<AnnouncementResponse>> LoadAnnouncementsAsync()
    {
        try { return await api.GetAsync<IReadOnlyList<AnnouncementResponse>>("api/v1/announcements").ConfigureAwait(false); }
        catch (ApiException) { return []; }
    }

    /// <summary>
    /// Platform staff have no companies of their own: inside an approved, current support window the API lists the tenant's active companies
    /// with read-only view permissions (BR-014, DT-152); every request there is still checked and written to the tenant's audit log.
    /// </summary>
    private async Task<bool> LoadSupportCompaniesAsync()
    {
        try
        {
            var companies = await api.GetAsync<IReadOnlyList<CompanyAccess>>("api/v1/platform/support-access/companies").ConfigureAwait(false);
            if (shell.Me is { } me) shell.SetMe(me with { Companies = companies });
            return companies.Count > 0;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    /// <summary>The company to open after sign-in:the profile's default, else the first one.</summary>
    public string? HomeCompany => shell.Me?.DefaultCompany is { } d && shell.Me.Companies.Any(c => c.Id == d) ? d : (shell.Me is { Companies.Count: > 0 } m ? m.Companies[0].Id : null);

    /// <summary>Saves the interface language in the profile (FR-178) and reloads, so the server renders the page in that language.</summary>
    public async Task SetLanguageAsync(string language)
    {
        var me = shell.Me;
        if (me is null) return;
        await api.PutAsync("api/v1/me/preferences", new PreferencesRequest(language, me.DefaultCompany, me.DateFormat)).ConfigureAwait(false);
        nav.Refresh(forceReload: true);
    }

    public async Task SignOutAsync()
    {
        try { await api.PostAsync("api/v1/auth/sign-out", null).ConfigureAwait(false); }
        catch (ApiException) { }
        nav.NavigateTo("/sign-in", forceLoad: true);
    }

    /// <summary>Global search (FR-187) as command-bar results linked to the record screens.</summary>
    public async Task<IReadOnlyList<CommandResult>> SearchAsync(string text, CancellationToken ct)
    {
        if (shell.Company is null) return [];
        var r = await api.GetAsync<SearchResponse>(api.C("search?q=" + Uri.EscapeDataString(text)), ct).ConfigureAwait(false);
        return r.Hits.Select(h => (Hit: h, Href: Links.ForSearchHit(shell.Company.Id, h.Type, h.Id)))
            .Where(x => x.Href is not null)
            .Select(x => new CommandResult(x.Hit.Detail is null ? x.Hit.Title : $"{x.Hit.Title} · {x.Hit.Detail}", loc["search." + x.Hit.Type], x.Href!)).ToList();
    }
}
