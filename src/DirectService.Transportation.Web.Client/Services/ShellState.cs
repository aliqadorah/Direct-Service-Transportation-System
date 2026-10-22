using DirectService.Transportation.Contracts.Identity;

namespace DirectService.Transportation.Web.Client.Services;

/// <summary>
/// What the shell shows for the signed-in user: profile (language, date format), companies with their roles and permissions, the
/// current company, theme (per user on this device, DT-244), unread notifications and whether the setup checklist is still open. Filled from
/// <c>GET /api/v1/me</c> and the company endpoints (step 8); components re-render on <see cref="Changed"/>.
/// </summary>
public sealed class ShellState : IDisposable
{
    public MeResponse? Me { get; private set; }

    public CompanyAccess? Company { get; private set; }

    /// <summary>"light", "dark" or "system" (follow the device, the default).</summary>
    public string Theme { get; private set; } = "system";

    public int UnreadNotifications { get; private set; }

    public bool SetupIncomplete { get; private set; }

    public string? PlanText { get; private set; }

    public IReadOnlyCollection<string> Permissions => Company?.Permissions ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    public string DateFormat => Me?.DateFormat ?? Ui.Fmt.DefaultDateFormat;

    public event Action? Changed;

    private CancellationTokenSource _companyScope = new();

    /// <summary>
    /// Cancelled when the user leaves the company (switch, DT-187): <see cref="Api"/> links every request of the current company to it, so
    /// the old company's in-flight requests stop and their answers are never rendered.
    /// </summary>
    public CancellationToken CompanyToken => _companyScope.Token;

    /// <summary>Ends the current company's requests (the start of a company switch); a new scope begins at once.</summary>
    public void LeaveCompany()
    {
        var old = _companyScope;
        _companyScope = new CancellationTokenSource();
        old.Cancel();   // not disposed: requests may still hold its token
    }

    public void Dispose() => _companyScope.Dispose();

    public void SetMe(MeResponse? me)
    {
        Me = me;
        if (Company is not null && me is not null) Company = me.Companies.FirstOrDefault(c => c.Id == Company.Id);
        Changed?.Invoke();
    }

    /// <summary>Selects the company of the URL; false when the user has no access to it (the page shows "not found").</summary>
    public bool SelectCompany(string? token)
    {
        var c = Me?.Companies.FirstOrDefault(x => x.Id == token);
        if (c is null) return false;
        SupportEnded = null;
        if (!ReferenceEquals(c, Company))
        {
            if (Company?.Id != c.Id) LeaveCompany();
            Company = c;
            Changed?.Invoke();
        }
        return true;
    }

    /// <summary>Why the staff member's support session for the company on screen ended (DT-220): code, when (UTC) and who ended it.</summary>
    public sealed record SupportEnd(string Reason, DateTime? At, string? By);

    /// <summary>Set when the support window closed: the shell then shows only the "support ended" message — no banner, no company data.</summary>
    public SupportEnd? SupportEnded { get; private set; }

    /// <summary>
    /// The support window closed (a 403 with its reason, or the live message): the company is left at once — its requests in flight are
    /// cancelled and nothing of it stays selected — and the shell shows the reason.
    /// </summary>
    public void EndSupport(SupportEnd end)
    {
        ArgumentNullException.ThrowIfNull(end);
        if (SupportEnded is not null) return;
        SupportEnded = end;
        LeaveCompany();
        Company = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// A change was refused for its antiforgery token even after a fresh one (DT-221): the page is out of date, e.g. the user signed in or
    /// out in another tab. The shell then says so with a "Reload" button instead of a raw error.
    /// </summary>
    public bool PageExpired { get; private set; }

    public void MarkPageExpired()
    {
        if (PageExpired) return;
        PageExpired = true;
        Changed?.Invoke();
    }

    /// <summary>Any other value (unknown, unreadable) means "system".</summary>
    public void SetTheme(string? theme)
    {
        Theme = theme is "light" or "dark" ? theme : "system";
        Changed?.Invoke();
    }

    /// <summary>Device preference key of the user's theme (DT-244).</summary>
    public string ThemeKey => "theme." + (Me?.Id ?? "-");

    public void SetNotifications(int unread)
    {
        UnreadNotifications = Math.Max(0, unread);
        Changed?.Invoke();
    }

    public void SetSetup(bool incomplete, string? planText)
    {
        SetupIncomplete = incomplete;
        PlanText = planText;
        Changed?.Invoke();
    }

    public bool Can(string permission) => Permissions.Contains(permission);
}
